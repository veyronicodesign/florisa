using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Florisa.Api.Contratos;
using Florisa.Api.Dados;
using Florisa.Api.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Florisa.Api.Servicos;

public static class Claims
{
    public const string Tipo = "tipo";          // "cliente" ou "equipe"
    public const string Telefone = "tel";
    public const string Papel = "papel";        // admin, atendente, estoque, entregador
    public const string Nome = "nome";
}

/// <summary>Gera os tokens (JWT) do cliente e da equipe.</summary>
public class ServicoTokens
{
    readonly IConfiguration _config;
    readonly HorarioLoja _horario;

    public ServicoTokens(IConfiguration config, HorarioLoja horario)
    {
        _config = config;
        _horario = horario;
    }

    public static SymmetricSecurityKey Chave(IConfiguration config)
    {
        var chave = config["Jwt:Chave"];
        if (string.IsNullOrWhiteSpace(chave) || chave.Length < 32)
            throw new InvalidOperationException("Configure Jwt:Chave com pelo menos 32 caracteres.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave));
    }

    public static string Emissor(IConfiguration config) => config["Jwt:Emissor"] ?? "florisa-api";

    string Gerar(IEnumerable<Claim> claims, TimeSpan validade)
    {
        var agora = _horario.AgoraUtc;
        var descritor = new SecurityTokenDescriptor
        {
            Issuer = Emissor(_config),
            Audience = Emissor(_config),
            Subject = new ClaimsIdentity(claims),
            IssuedAt = agora,
            NotBefore = agora.AddMinutes(-1),
            Expires = agora.Add(validade),
            SigningCredentials = new SigningCredentials(Chave(_config), SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descritor);
    }

    public string Cliente(Cliente c) => Gerar(new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, c.Id.ToString()),
        new Claim(Claims.Tipo, "cliente"),
        new Claim(Claims.Telefone, c.Telefone)
    }, TimeSpan.FromDays(_config.GetValue("Jwt:DiasCliente", 30)));

    public string Equipe(MembroEquipe m) => Gerar(new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, m.Id.ToString()),
        new Claim(Claims.Tipo, "equipe"),
        new Claim(Claims.Papel, m.Papel),
        new Claim(Claims.Nome, m.Nome)
    }, TimeSpan.FromHours(_config.GetValue("Jwt:HorasEquipe", 12)));
}

/// <summary>Login do cliente com código de 4 dígitos no WhatsApp.</summary>
public class ServicoCodigos
{
    readonly FlorisaDb _db;
    readonly IEnvioMensagens _mensagens;
    readonly HorarioLoja _horario;
    readonly ServicoTokens _tokens;
    readonly IConfiguration _config;

    const int MaxTentativas = 5;

    public ServicoCodigos(FlorisaDb db, IEnvioMensagens mensagens, HorarioLoja horario, ServicoTokens tokens, IConfiguration config)
    {
        _db = db; _mensagens = mensagens; _horario = horario; _tokens = tokens; _config = config;
    }

    string HashDe(string tel, string codigo) => Hash.Sha256($"{tel}:{codigo}:{_config["Jwt:Chave"]}");

    public async Task EnviarAsync(string? telBruto, CancellationToken ct)
    {
        var tel = Telefones.Obrigatorio(telBruto);
        var agora = _horario.AgoraUtc;

        var ultimo = await _db.CodigosAcesso.Where(c => c.Telefone == tel)
            .OrderByDescending(c => c.CriadoEm).FirstOrDefaultAsync(ct);
        if (ultimo is not null && ultimo.CriadoEm > agora.AddSeconds(-60))
            throw new RegraException("Já enviamos um código agora há pouco. Espere um minuto para pedir outro.", StatusCodes.Status429TooManyRequests);

        var codigo = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");
        _db.CodigosAcesso.Add(new CodigoAcesso
        {
            Telefone = tel,
            CodigoHash = HashDe(tel, codigo),
            CriadoEm = agora,
            ExpiraEm = agora.AddMinutes(10)
        });
        await _db.SaveChangesAsync(ct);
        await _mensagens.EnviarAsync(tel, $"Florisa: seu código de acesso é {codigo}. Vale por 10 minutos.", ct);
    }

    public async Task<VerificarResp> VerificarAsync(string? telBruto, string? codigo, CancellationToken ct)
    {
        var tel = Telefones.Obrigatorio(telBruto);
        var agora = _horario.AgoraUtc;
        var errado = new RegraException("Código incorreto ou vencido. Peça um novo.", StatusCodes.Status401Unauthorized);

        var registro = await _db.CodigosAcesso
            .Where(c => c.Telefone == tel && !c.Usado && c.ExpiraEm > agora)
            .OrderByDescending(c => c.CriadoEm).FirstOrDefaultAsync(ct);
        if (registro is null || registro.Tentativas >= MaxTentativas || string.IsNullOrWhiteSpace(codigo)) throw errado;

        registro.Tentativas++;
        var bate = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashDe(tel, codigo.Trim())), Encoding.UTF8.GetBytes(registro.CodigoHash));
        if (!bate)
        {
            await _db.SaveChangesAsync(ct);
            throw errado;
        }

        registro.Usado = true;
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Telefone == tel, ct);
        if (cliente is null)
        {
            cliente = new Cliente { Telefone = tel, Nome = "", CriadoEm = agora };
            _db.Clientes.Add(cliente);
        }
        await _db.SaveChangesAsync(ct);
        return new VerificarResp(_tokens.Cliente(cliente), new ClienteLogadoDto(cliente.Nome, cliente.Email));
    }
}

public static class UsuarioAtual
{
    public static bool EhCliente(this ClaimsPrincipal u) => u.FindFirstValue(Claims.Tipo) == "cliente";
    public static bool EhEquipe(this ClaimsPrincipal u) => u.FindFirstValue(Claims.Tipo) == "equipe";
    public static string? Telefone(this ClaimsPrincipal u) => u.FindFirstValue(Claims.Telefone);
    public static string Papel(this ClaimsPrincipal u) => u.FindFirstValue(Claims.Papel) ?? "";
    public static string NomeEquipe(this ClaimsPrincipal u) => u.FindFirstValue(Claims.Nome) ?? "equipe";
    public static int? ClienteId(this ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
