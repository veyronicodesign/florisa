using System.Security.Cryptography;
using System.Text;
using Florisa.Api.Dominio;

namespace Florisa.Api.Servicos;

// ---------------------------------------------------------------------------------
// Pagamento: uma interface, um provedor por implementação.
// Hoje existe só o simulado. Quando a Florisa escolher (Mercado Pago, Asaas...),
// entra uma nova classe e a configuração "Pagamento:Provedor" passa a apontar para ela.
// ---------------------------------------------------------------------------------

public record CobrancaPix(string GatewayId, string CopiaECola, string? Qr, DateTime ExpiraEm);
public record CobrancaCartao(string GatewayId, string Status);
/// <summary>Aviso recebido do provedor: qual cobrança e o novo status.</summary>
public record AvisoPagamento(string GatewayId, string Status);

public interface IGatewayPagamento
{
    string Nome { get; }
    Task<CobrancaPix> CriarPixAsync(Pedido pedido, CancellationToken ct);
    /// <summary>Cobra o cartão a partir do token gerado no navegador. Nunca recebe o número do cartão.</summary>
    Task<CobrancaCartao> CobrarCartaoAsync(Pedido pedido, string token, int parcelas, CancellationToken ct);
    /// <summary>Lê o aviso (webhook) do provedor. Devolve null se não for válido ou não interessar.</summary>
    Task<AvisoPagamento?> LerAvisoAsync(HttpRequest request, CancellationToken ct);
}

/// <summary>
/// Pagamento de mentira, para desenvolver e testar sem provedor.
/// Pix: fica pendente até alguém confirmar no painel (ou chamar o webhook simulado).
/// Cartão: aprova qualquer token, menos os que começam com "recusar".
/// </summary>
public class GatewaySimulado : IGatewayPagamento
{
    readonly HorarioLoja _horario;
    readonly IConfiguration _config;

    public GatewaySimulado(HorarioLoja horario, IConfiguration config)
    {
        _horario = horario;
        _config = config;
    }

    public string Nome => "simulado";

    public Task<CobrancaPix> CriarPixAsync(Pedido pedido, CancellationToken ct)
    {
        var minutos = _config.GetValue("Pagamento:PixValidadeMinutos", 30);
        var id = "sim-pix-" + Guid.NewGuid().ToString("N")[..12];
        // Não é um Pix de verdade: o texto deixa isso claro para ninguém tentar pagar.
        var copia = $"PIX-SIMULADO-FLORISA-{pedido.Numero}-{pedido.Total:0.00}-NAO-PAGAR";
        return Task.FromResult(new CobrancaPix(id, copia, null, _horario.AgoraUtc.AddMinutes(minutos)));
    }

    public Task<CobrancaCartao> CobrarCartaoAsync(Pedido pedido, string token, int parcelas, CancellationToken ct)
    {
        var status = token.StartsWith("recusar", StringComparison.OrdinalIgnoreCase)
            ? StatusPagamento.Recusado
            : StatusPagamento.Aprovado;
        return Task.FromResult(new CobrancaCartao("sim-cartao-" + Guid.NewGuid().ToString("N")[..12], status));
    }

    public async Task<AvisoPagamento?> LerAvisoAsync(HttpRequest request, CancellationToken ct)
    {
        // Só aceita com o segredo configurado, para ninguém "pagar" pedidos chamando a URL.
        var segredo = _config["Pagamento:SegredoWebhook"];
        if (string.IsNullOrEmpty(segredo) || request.Headers["X-Florisa-Segredo"] != segredo) return null;
        var corpo = await request.ReadFromJsonAsync<AvisoPagamento>(ct);
        return corpo is { GatewayId.Length: > 0 } ? corpo : null;
    }
}

// ---------------------------------------------------------------------------------
// Mensagens (WhatsApp). Hoje só escreve no log; o provedor real entra depois.
// ---------------------------------------------------------------------------------

public interface IEnvioMensagens
{
    Task EnviarAsync(string telefone, string texto, CancellationToken ct);
}

public class EnvioMensagensLog : IEnvioMensagens
{
    readonly ILogger<EnvioMensagensLog> _log;
    public EnvioMensagensLog(ILogger<EnvioMensagensLog> log) => _log = log;

    public Task EnviarAsync(string telefone, string texto, CancellationToken ct)
    {
        _log.LogInformation("WhatsApp para {Telefone}: {Texto}", telefone, texto);
        return Task.CompletedTask;
    }
}

public static class Hash
{
    public static string Sha256(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
}
