using System.Net.Http.Headers;
using System.Net.Http.Json;
using Florisa.Api.Servicos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Florisa.Api.Tests;

/// <summary>Relógio parado numa data conhecida. Começa numa segunda, 09/11/2026, 10h em Jaraguá (13h UTC).</summary>
public class RelogioFixo : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = new(2026, 11, 9, 13, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Agora;
}

/// <summary>Guarda as mensagens de WhatsApp em vez de enviar.</summary>
public class MensagensFalsas : IEnvioMensagens
{
    public List<(string Tel, string Texto)> Enviadas { get; } = new();
    public Task EnviarAsync(string telefone, string texto, CancellationToken ct)
    {
        lock (Enviadas) Enviadas.Add((telefone, texto));
        return Task.CompletedTask;
    }
    public string UltimoCodigo(string tel)
    {
        lock (Enviadas)
        {
            var t = Enviadas.Last(m => m.Tel == tel && m.Texto.Contains("código")).Texto;
            return System.Text.RegularExpressions.Regex.Match(t, @"\b\d{4}\b").Value;
        }
    }
}

public class FabricaApi : WebApplicationFactory<Program>
{
    readonly string _banco = Path.Combine(Path.GetTempPath(), $"florisa-teste-{Guid.NewGuid():N}.db");
    public RelogioFixo Relogio { get; } = new();
    public MensagensFalsas Mensagens { get; } = new();

    public const string SenhaAdmin = "senha-de-teste-123";
    public const string Segredo = "segredo-teste";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("BancoDeDados", "sqlite");
        builder.UseSetting("ConnectionStrings:Florisa", $"Data Source={_banco}");
        builder.UseSetting("Jwt:Chave", "chave-de-teste-com-mais-de-32-caracteres-123456");
        builder.UseSetting("Pagamento:Provedor", "simulado");
        builder.UseSetting("Pagamento:SegredoWebhook", Segredo);
        builder.UseSetting("Equipe:AdminSenha", SenhaAdmin);
        builder.UseSetting("Limites:CodigosPorMinuto", "1000");
        builder.UseSetting("Limites:PedidosPorMinuto", "1000");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Relogio);
            s.RemoveAll<IEnvioMensagens>();
            s.AddSingleton<IEnvioMensagens>(Mensagens);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_banco); } catch { /* sem problema */ }
    }

    public async Task<HttpClient> EquipeAsync(string usuario = "admin", string senha = SenhaAdmin)
    {
        var c = CreateClient();
        var r = await c.PostAsJsonAsync("/api/painel/login", new { usuario, senha });
        r.EnsureSuccessStatusCode();
        var login = await r.Content.ReadFromJsonAsync<Contratos.LoginEquipeResp>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }
}

public static class Pedidos
{
    /// <summary>Pedido igual ao que o site envia (exemplo do INTEGRACAO.md), com data numa terça.</summary>
    public static object Exemplo(string metodo = "pix", string modo = "entrega", decimal total = 237, string data = "2026-11-10",
        string produto = "rn21", string[]? extras = null, string tel = "47999999999", string bairro = "Centro", string? token = null)
    {
        var entrega = modo == "entrega"
            ? new { modo, data = (string?)data, taxa = 12m, endereco = (object?)new { cep = "89250000", rua = "Rua A", numero = "10", complemento = "", bairro } }
            : new { modo, data = (string?)null, taxa = 0m, endereco = (object?)null };
        return new
        {
            origem = "site",
            cliente = new { nome = "Ana", tel, email = "ana@email.com" },
            destinatario = new { nome = "Maria", tel = "47988887777" },
            entrega,
            itens = new[] { new { id = produto, nome = "Bouquet", quantidade = 1, precoUnitario = 190, extras = extras ?? new[] { "bombom" }, total = 225 } },
            subtotal = 225,
            frete = modo == "entrega" ? 12 : 0,
            total,
            pagamento = new { metodo, parcelas = 1, cartaoToken = token ?? (metodo == "cartao" ? "tok_teste" : null), cartaoFinal = metodo == "cartao" ? "4242" : null },
            mensagemCartao = "Feliz aniversário!"
        };
    }
}
