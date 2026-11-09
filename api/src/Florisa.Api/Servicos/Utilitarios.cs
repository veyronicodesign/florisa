using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Florisa.Api.Servicos;

/// <summary>Erro de regra de negócio. Vira {"mensagem": "..."} com o status HTTP indicado.</summary>
public class RegraException : Exception
{
    public int Status { get; }
    public RegraException(string mensagem, int status = StatusCodes.Status400BadRequest) : base(mensagem) => Status = status;
}

public static class Telefones
{
    /// <summary>Deixa só os dígitos, tira o 55 do Brasil e devolve DDD + número (10 ou 11 dígitos). Null se inválido.</summary>
    public static string? Normalizar(string? tel)
    {
        if (string.IsNullOrWhiteSpace(tel)) return null;
        var d = new string(tel.Where(char.IsDigit).ToArray());
        if (d.Length is 12 or 13 && d.StartsWith("55")) d = d[2..];
        if (d.Length is 11 && d.StartsWith('0')) d = d[1..];
        return d.Length is 10 or 11 ? d : null;
    }

    public static string Obrigatorio(string? tel, string campo = "WhatsApp")
        => Normalizar(tel) ?? throw new RegraException($"Confira o {campo}: use DDD + número, como (47) 99999-9999.");
}

public static class Texto
{
    /// <summary>Minúsculas e sem acento, para comparar nomes de bairro.</summary>
    public static string Chave(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var c in n)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string? Limpo(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }
}

/// <summary>Senhas da equipe: PBKDF2-SHA256, 100 mil iterações, sal aleatório.</summary>
public static class Senhas
{
    const int Iteracoes = 100_000;

    public static string Gerar(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, Iteracoes, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iteracoes}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Conferir(string senha, string guardada)
    {
        var p = guardada.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out var it)) return false;
        var sal = Convert.FromBase64String(p[2]);
        var esperado = Convert.FromBase64String(p[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, it, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }
}

/// <summary>Data e hora no fuso da loja (Jaraguá do Sul).</summary>
public class HorarioLoja
{
    readonly TimeProvider _relogio;
    readonly TimeZoneInfo _fuso;

    public HorarioLoja(TimeProvider relogio, IConfiguration config)
    {
        _relogio = relogio;
        _fuso = TimeZoneInfo.FindSystemTimeZoneById(config["Loja:Fuso"] ?? "America/Sao_Paulo");
    }

    public DateTime AgoraUtc => _relogio.GetUtcNow().UtcDateTime;
    public DateTime AgoraLocal => TimeZoneInfo.ConvertTimeFromUtc(AgoraUtc, _fuso);
    public DateOnly Hoje => DateOnly.FromDateTime(AgoraLocal);
}
