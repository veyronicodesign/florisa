using System.Security.Claims;
using Florisa.Api.Contratos;
using Florisa.Api.Dados;
using Florisa.Api.Servicos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Florisa.Api.Endpoints;

/// <summary>Endpoints que o site (index.html, objeto API) chama. Contrato em INTEGRACAO.md.</summary>
public static class LojaEndpoints
{
    public static void MapLoja(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/catalogo", async (FlorisaDb db, CancellationToken ct) =>
        {
            var produtos = await db.Produtos.Where(p => p.Ativo).OrderBy(p => p.Ordem).ToListAsync(ct);
            var extras = await db.Extras.Where(e => e.Ativo).OrderBy(e => e.Nome).ToListAsync(ct);
            var taxas = await db.TaxasEntrega.Where(t => t.Ativo).OrderBy(t => t.Ordem).ToListAsync(ct);
            return new CatalogoResp(
                produtos.Select(p => new CatalogoProdutoDto(p.Id, p.Nome, p.Categoria, p.Preco, p.PrecoDe, p.Selo, p.Descricao,
                    p.Imagem, !p.PermiteExtras, p.AntecedenciaDias, p.Disponivel, p.Exemplo)).ToList(),
                extras.Select(e => new CatalogoExtraDto(e.Id, e.Nome, e.Preco)).ToList(),
                taxas.ToDictionary(t => t.Bairro, t => t.Valor));
        });

        api.MapPost("/frete", async (FreteReq req, ServicoFrete frete, CancellationToken ct) =>
        {
            var taxa = await frete.TaxaAsync(req.Bairro, ct);
            return new FreteResp(taxa.Valor, ServicoFrete.Prazo);
        });

        api.MapPost("/pedidos", async (NovoPedidoReq req, ServicoPedidos pedidos, CancellationToken ct) =>
        {
            // O site sempre cria com origem "site". Pedidos de WhatsApp e balcão entram pelo painel.
            var p = await pedidos.CriarAsync(req, "site", "cliente", conferirTotal: true, ct);
            return Results.Ok(Mapeamento.Criado(p));
        }).RequireRateLimiting("pedidos");

        api.MapGet("/pedidos/{numero}", async (string numero, string? tel, ClaimsPrincipal user, HttpContext http,
            ServicoPedidos pedidos, CancellationToken ct) =>
        {
            var p = await pedidos.BuscarAsync(numero.Trim().TrimStart('#'), ct);
            if (p is null) return Results.NotFound(new ErroResp("Pedido não encontrado."));

            // Só mostra para quem sabe o telefone de quem comprou (ou está logado com ele).
            var telInformado = Telefones.Normalizar(tel);
            var logado = await TelefoneLogadoAsync(http);
            var podeVer = (telInformado is not null && telInformado == p.ClienteTelefone) || (logado is not null && logado == p.ClienteTelefone);
            return podeVer ? Results.Ok(Mapeamento.Completo(p)) : Results.NotFound(new ErroResp("Pedido não encontrado."));
        });

        api.MapGet("/pedidos", async (int? meus, ClaimsPrincipal user, FlorisaDb db, CancellationToken ct) =>
        {
            if (meus != 1) return Results.BadRequest(new ErroResp("Use /pedidos?meus=1."));
            var tel = user.Telefone();
            if (!user.EhCliente() || tel is null) return Results.Unauthorized();
            var lista = await db.Pedidos.Include(p => p.Itens)
                .Where(p => p.ClienteTelefone == tel)
                .OrderByDescending(p => p.Id).Take(50).ToListAsync(ct);
            return Results.Ok(lista.Select(Mapeamento.Completo));
        }).RequireAuthorization("Cliente");

        api.MapPost("/auth/codigo", async (CodigoReq req, ServicoCodigos codigos, CancellationToken ct) =>
        {
            await codigos.EnviarAsync(req.Tel, ct);
            return new CodigoResp(true);
        }).RequireRateLimiting("codigos");

        api.MapPost("/auth/verificar", async (VerificarReq req, ServicoCodigos codigos, CancellationToken ct) =>
            await codigos.VerificarAsync(req.Tel, req.Codigo, ct)
        ).RequireRateLimiting("codigos");

        // Aviso do provedor de pagamento (Pix pago, cartão recusado...).
        api.MapPost("/pagamentos/webhook", async (HttpRequest request, IGatewayPagamento gateway, ServicoPedidos pedidos, CancellationToken ct) =>
        {
            var aviso = await gateway.LerAvisoAsync(request, ct);
            if (aviso is null) return Results.Unauthorized();
            await pedidos.ConfirmarPeloGatewayAsync(aviso, ct);
            return Results.Ok(new { recebido = true });
        });
    }

    /// <summary>Lê o token do cliente, se veio, sem exigir login.</summary>
    static async Task<string?> TelefoneLogadoAsync(HttpContext http)
    {
        var r = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        return r.Succeeded && r.Principal!.EhCliente() ? r.Principal.Telefone() : null;
    }
}
