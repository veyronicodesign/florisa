using System.Security.Claims;
using Florisa.Api.Contratos;
using Florisa.Api.Dados;
using Florisa.Api.Dominio;
using Florisa.Api.Servicos;
using Microsoft.EntityFrameworkCore;

namespace Florisa.Api.Endpoints;

/// <summary>Endpoints do painel da equipe. Todos exigem login da equipe, com permissões por papel.</summary>
public static class PainelEndpoints
{
    public static void MapPainel(this IEndpointRouteBuilder app)
    {
        var painel = app.MapGroup("/api/painel");

        painel.MapPost("/login", async (LoginEquipeReq req, FlorisaDb db, ServicoTokens tokens, CancellationToken ct) =>
        {
            var usuario = req.Usuario?.Trim().ToLowerInvariant() ?? "";
            var m = await db.Equipe.FirstOrDefaultAsync(x => x.Usuario == usuario && x.Ativo, ct);
            if (m is null || string.IsNullOrEmpty(req.Senha) || !Senhas.Conferir(req.Senha, m.SenhaHash))
                return Results.Json(new ErroResp("Usuário ou senha incorretos."), statusCode: StatusCodes.Status401Unauthorized);
            return Results.Ok(new LoginEquipeResp(tokens.Equipe(m), m.Nome, m.Papel));
        }).RequireRateLimiting("codigos");

        var equipe = painel.MapGroup("").RequireAuthorization("Equipe");

        // ---------------- Pedidos ----------------

        equipe.MapGet("/pedidos", async (string? dia, string? modo, string? busca, FlorisaDb db, HorarioLoja horario, CancellationToken ct) =>
        {
            var q = db.Pedidos.Include(p => p.Itens).Where(p => p.PagamentoStatus != StatusPagamento.Recusado);
            var hoje = horario.Hoje;
            var amanha = hoje.AddDays(1);
            var limiteConcluidos = horario.AgoraUtc.AddHours(-18);
            q = (dia ?? "hoje") switch
            {
                // Hoje: tudo que está aberto para hoje ou atrasado, mais o que foi concluído nas últimas horas.
                "hoje" => q.Where(p =>
                    (p.Etapa < Etapas.Concluido && (p.DataEntrega == null || p.DataEntrega <= hoje)) ||
                    (p.Etapa == Etapas.Concluido && p.AtualizadoEm >= limiteConcluidos)),
                "amanha" => q.Where(p => p.DataEntrega == amanha),
                "abertos" => q.Where(p => p.Etapa < Etapas.Concluido),
                _ => q
            };
            if (modo is "entrega" or "retirada") q = q.Where(p => p.Modo == modo);
            if (!string.IsNullOrWhiteSpace(busca))
            {
                var b = busca.Trim().TrimStart('#');
                var tel = Telefones.Normalizar(b);
                q = q.Where(p => p.Numero == b || p.ClienteNome.Contains(b) || (tel != null && p.ClienteTelefone == tel));
            }
            var lista = await q.OrderBy(p => p.Etapa).ThenBy(p => p.Id).Take(300).ToListAsync(ct);
            return lista.Select(Mapeamento.Resumo);
        });

        equipe.MapGet("/pedidos/{numero}", async (string numero, ServicoPedidos pedidos, CancellationToken ct) =>
        {
            var p = await pedidos.BuscarAsync(numero, ct);
            return p is null ? Results.NotFound(new ErroResp("Pedido não encontrado.")) : Results.Ok(Mapeamento.Painel(p));
        });

        equipe.MapPost("/pedidos/{numero}/avancar", async (string numero, ClaimsPrincipal user, ServicoPedidos pedidos, CancellationToken ct) =>
        {
            var p = await pedidos.BuscarAsync(numero, ct);
            if (p is null) return Results.NotFound(new ErroResp("Pedido não encontrado."));
            await pedidos.AvancarAsync(p, user.NomeEquipe(), user.Papel(), ct);
            return Results.Ok(Mapeamento.Painel(p));
        });

        equipe.MapPost("/pedidos/{numero}/confirmar-pagamento", async (string numero, ClaimsPrincipal user, ServicoPedidos pedidos, CancellationToken ct) =>
        {
            var p = await pedidos.BuscarAsync(numero, ct);
            if (p is null) return Results.NotFound(new ErroResp("Pedido não encontrado."));
            await pedidos.ConfirmarManualAsync(p, user.NomeEquipe(), ct);
            return Results.Ok(Mapeamento.Painel(p));
        }).RequireAuthorization("Atendimento");

        equipe.MapPatch("/pedidos/{numero}", async (string numero, AtualizarPedidoReq req, ClaimsPrincipal user,
            ServicoPedidos pedidos, FlorisaDb db, HorarioLoja horario, CancellationToken ct) =>
        {
            var p = await pedidos.BuscarAsync(numero, ct);
            if (p is null) return Results.NotFound(new ErroResp("Pedido não encontrado."));
            if (req.Entregador is not null)
            {
                p.Entregador = Texto.Limpo(req.Entregador, 80);
                p.Eventos.Add(new PedidoEvento { Quando = horario.AgoraUtc, Autor = user.NomeEquipe(), Descricao = $"Entregador: {p.Entregador ?? "nenhum"}" });
            }
            if (req.NotaInterna is not null) p.NotaInterna = Texto.Limpo(req.NotaInterna, 1000);
            p.AtualizadoEm = horario.AgoraUtc;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Mapeamento.Painel(p));
        }).RequireAuthorization("Atendimento");

        // Pedido que chegou pelo WhatsApp ou no balcão.
        equipe.MapPost("/pedidos", async (NovoPedidoEquipeReq req, ClaimsPrincipal user, ServicoPedidos pedidos, CancellationToken ct) =>
        {
            if (req.Pedido is null) return Results.BadRequest(new ErroResp("Envie os dados do pedido."));
            var origem = req.Origem is "balcao" ? "balcao" : "whatsapp";
            var p = await pedidos.CriarAsync(req.Pedido, origem, user.NomeEquipe(), conferirTotal: false, ct);
            if (req.JaPago && p.PagamentoStatus is StatusPagamento.Pendente or StatusPagamento.NaRetirada)
                await pedidos.ConfirmarManualAsync(p, user.NomeEquipe(), ct);
            return Results.Ok(Mapeamento.Painel(p));
        }).RequireAuthorization("Atendimento");

        // ---------------- Produtos ----------------

        equipe.MapGet("/produtos", async (FlorisaDb db, CancellationToken ct) =>
            (await db.Produtos.OrderBy(p => p.Ordem).ToListAsync(ct)).Select(Mapeamento.Produto));

        equipe.MapPut("/produtos/{id}", async (string id, SalvarProdutoReq req, FlorisaDb db, CancellationToken ct) =>
        {
            var p = await db.Produtos.FindAsync(new object[] { id }, ct);
            var novo = p is null;
            if (novo)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9-]{2,40}$"))
                    return Results.BadRequest(new ErroResp("Código do produto: use letras minúsculas, números e hífen."));
                p = new Produto { Id = id, Ordem = await db.Produtos.CountAsync(ct) };
                db.Produtos.Add(p);
            }
            if (req.Nome is not null) p!.Nome = Texto.Limpo(req.Nome, 160) ?? p.Nome;
            if (req.Categoria is not null) p!.Categoria = Texto.Limpo(req.Categoria, 40) ?? p.Categoria;
            if (req.Preco is { } preco)
            {
                if (preco <= 0) return Results.BadRequest(new ErroResp("O preço precisa ser maior que zero."));
                p!.Preco = preco;
            }
            if (req.PrecoDe is not null) p!.PrecoDe = req.PrecoDe > 0 ? req.PrecoDe : null;
            if (req.Selo is not null) p!.Selo = Texto.Limpo(req.Selo, 40);
            if (req.Descricao is not null) p!.Descricao = Texto.Limpo(req.Descricao, 600);
            if (req.Imagem is not null) p!.Imagem = Texto.Limpo(req.Imagem, 300);
            if (req.PermiteExtras is { } pe) p!.PermiteExtras = pe;
            if (req.AntecedenciaDias is { } ad) p!.AntecedenciaDias = Math.Clamp(ad, 0, 30);
            if (req.Ativo is { } at) p!.Ativo = at;
            if (req.Disponivel is { } di) p!.Disponivel = di;
            if (req.Exemplo is { } ex) p!.Exemplo = ex;
            if (req.Ordem is { } o) p!.Ordem = o;
            if (string.IsNullOrWhiteSpace(p!.Nome) || p.Preco <= 0 || string.IsNullOrWhiteSpace(p.Categoria))
                return Results.BadRequest(new ErroResp("Produto novo precisa de nome, categoria e preço."));
            await db.SaveChangesAsync(ct);
            return Results.Ok(Mapeamento.Produto(p));
        }).RequireAuthorization("Estoque");

        // ---------------- Taxas de entrega ----------------

        equipe.MapGet("/taxas", async (FlorisaDb db, CancellationToken ct) =>
            (await db.TaxasEntrega.OrderBy(t => t.Ordem).ToListAsync(ct)).Select(t => new TaxaDto(t.Bairro, t.Valor, t.Ativo)));

        equipe.MapPut("/taxas", async (List<TaxaDto> taxas, FlorisaDb db, CancellationToken ct) =>
        {
            if (taxas.Any(t => string.IsNullOrWhiteSpace(t.Bairro) || t.Valor < 0))
                return Results.BadRequest(new ErroResp("Cada bairro precisa de nome e de uma taxa de zero para cima."));
            if (taxas.Select(t => Texto.Chave(t.Bairro)).Distinct().Count() != taxas.Count)
                return Results.BadRequest(new ErroResp("Tem bairro repetido na lista."));
            var atuais = await db.TaxasEntrega.ToListAsync(ct);
            db.TaxasEntrega.RemoveRange(atuais);
            await db.SaveChangesAsync(ct);
            var i = 0;
            foreach (var t in taxas)
                db.TaxasEntrega.Add(new TaxaEntrega { Bairro = t.Bairro.Trim(), Valor = t.Valor, Ativo = t.Ativo, Ordem = i++ });
            await db.SaveChangesAsync(ct);
            return Results.Ok(taxas);
        }).RequireAuthorization("Admin");

        // ---------------- Equipe ----------------

        equipe.MapGet("/equipe", async (FlorisaDb db, CancellationToken ct) =>
            (await db.Equipe.OrderBy(m => m.Nome).ToListAsync(ct)).Select(m => new MembroDto(m.Id, m.Usuario, m.Nome, m.Papel, m.Ativo))
        ).RequireAuthorization("Admin");

        equipe.MapPost("/equipe", async (NovoMembroReq req, FlorisaDb db, CancellationToken ct) =>
        {
            var usuario = req.Usuario?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(req.Nome))
                return Results.BadRequest(new ErroResp("Informe usuário e nome."));
            if (req.Senha is not { Length: >= 8 }) return Results.BadRequest(new ErroResp("A senha precisa ter pelo menos 8 caracteres."));
            if (!Papeis.Todos.Contains(req.Papel)) return Results.BadRequest(new ErroResp("Papel inválido."));
            if (await db.Equipe.AnyAsync(m => m.Usuario == usuario, ct)) return Results.Conflict(new ErroResp("Esse usuário já existe."));
            var m = new MembroEquipe { Usuario = usuario, Nome = req.Nome.Trim(), Papel = req.Papel!, SenhaHash = Senhas.Gerar(req.Senha) };
            db.Equipe.Add(m);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new MembroDto(m.Id, m.Usuario, m.Nome, m.Papel, m.Ativo));
        }).RequireAuthorization("Admin");
    }
}
