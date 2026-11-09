using System.Net;
using System.Net.Http.Json;
using Florisa.Api.Contratos;

namespace Florisa.Api.Tests;

public class PainelTests : IClassFixture<FabricaApi>
{
    readonly FabricaApi _f;
    public PainelTests(FabricaApi f) => _f = f;

    async Task<PedidoCriadoResp> CriarAsync(string metodo = "pix", string tel = "47912345678")
    {
        var r = await _f.CreateClient().PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(metodo: metodo, tel: tel));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<PedidoCriadoResp>())!;
    }

    [Fact]
    public async Task Painel_exige_login()
    {
        var r = await _f.CreateClient().GetAsync("/api/painel/pedidos");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Senha_errada_nao_entra()
    {
        var r = await _f.CreateClient().PostAsJsonAsync("/api/painel/login", new { usuario = "admin", senha = "errada" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Fluxo_completo_do_pedido_no_painel()
    {
        var equipe = await _f.EquipeAsync();
        var criado = await CriarAsync();

        // Aguardando pagamento: não dá para avançar.
        var antes = await equipe.PostAsync($"/api/painel/pedidos/{criado.Numero}/avancar", null);
        Assert.Equal(HttpStatusCode.BadRequest, antes.StatusCode);

        // Pix confirmado na mão (modo simulado ou Pix que caiu na conta).
        var pago = await (await equipe.PostAsync($"/api/painel/pedidos/{criado.Numero}/confirmar-pagamento", null))
            .Content.ReadFromJsonAsync<PedidoPainelResp>();
        Assert.Equal(0, pago!.Pedido.Etapa);

        // Recebido -> Em preparo -> Saiu para entrega -> Entregue
        PedidoPainelResp? atual = null;
        foreach (var esperado in new[] { "Em preparo", "Saiu para entrega", "Entregue" })
        {
            var r = await equipe.PostAsync($"/api/painel/pedidos/{criado.Numero}/avancar", null);
            r.EnsureSuccessStatusCode();
            atual = await r.Content.ReadFromJsonAsync<PedidoPainelResp>();
            Assert.Equal(esperado, atual!.Pedido.EtapaNome);
        }
        Assert.Equal(3, atual!.Pedido.Etapa);
        Assert.Contains(atual.Historico, h => h.Descricao.Contains("na mão"));

        var depois = await equipe.PostAsync($"/api/painel/pedidos/{criado.Numero}/avancar", null);
        Assert.Equal(HttpStatusCode.BadRequest, depois.StatusCode);

        // O cliente recebeu aviso a cada etapa.
        Assert.Contains(_f.Mensagens.Enviadas, m => m.Tel == "47912345678" && m.Texto.Contains("saiu para entrega"));
    }

    [Fact]
    public async Task Fila_de_hoje_lista_pedidos_e_esconde_recusados()
    {
        var equipe = await _f.EquipeAsync();
        var ok = await CriarAsync(tel: "47922223333");
        var r = await _f.CreateClient().PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(metodo: "cartao", token: "recusar", tel: "47922223333"));
        var recusado = await r.Content.ReadFromJsonAsync<PedidoCriadoResp>();

        var fila = await equipe.GetFromJsonAsync<List<PedidoResumoDto>>("/api/painel/pedidos?dia=abertos");
        Assert.Contains(fila!, p => p.Numero == ok.Numero && p.TemCartao && p.Itens.Contains("rosas"));
        Assert.DoesNotContain(fila!, p => p.Numero == recusado!.Numero);
    }

    [Fact]
    public async Task Admin_cria_entregador_que_so_marca_entrega()
    {
        var admin = await _f.EquipeAsync();
        var novo = await admin.PostAsJsonAsync("/api/painel/equipe", new { usuario = "adilson", nome = "Adilson", senha = "entregas-123", papel = "entregador" });
        novo.EnsureSuccessStatusCode();
        var entregador = await _f.EquipeAsync("adilson", "entregas-123");

        var criado = await CriarAsync(metodo: "cartao", tel: "47944445555"); // já entra pago, etapa 0
        var naoPode = await entregador.PostAsync($"/api/painel/pedidos/{criado.Numero}/avancar", null);
        Assert.Equal(HttpStatusCode.Forbidden, naoPode.StatusCode);

        (await admin.PostAsync($"/api/painel/pedidos/{criado.Numero}/avancar", null)).EnsureSuccessStatusCode(); // em preparo
        (await entregador.PostAsync($"/api/painel/pedidos/{criado.Numero}/avancar", null)).EnsureSuccessStatusCode(); // saiu

        var semPermissao = await entregador.PutAsJsonAsync("/api/painel/produtos/rn21", new { preco = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, semPermissao.StatusCode);
    }

    [Fact]
    public async Task Produto_esgotado_nao_pode_ser_comprado()
    {
        var admin = await _f.EquipeAsync();
        (await admin.PutAsJsonAsync("/api/painel/produtos/fm03", new { disponivel = false })).EnsureSuccessStatusCode();

        var r = await _f.CreateClient().PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "fm03", total: 185 + 35 + 12));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

        (await admin.PutAsJsonAsync("/api/painel/produtos/fm03", new { disponivel = true })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Mudar_preco_no_painel_muda_o_preco_cobrado()
    {
        var admin = await _f.EquipeAsync();
        (await admin.PutAsJsonAsync("/api/painel/produtos/fm17", new { preco = 300 })).EnsureSuccessStatusCode();

        var antigo = await _f.CreateClient().PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "fm17", total: 280 + 35 + 12));
        Assert.Equal(HttpStatusCode.Conflict, antigo.StatusCode);
        var novo = await _f.CreateClient().PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "fm17", total: 300 + 35 + 12));
        novo.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Pedido_do_whatsapp_entra_pelo_painel_ja_pago()
    {
        var admin = await _f.EquipeAsync();
        var r = await admin.PostAsJsonAsync("/api/painel/pedidos", new
        {
            origem = "whatsapp",
            jaPago = true,
            pedido = Pedidos.Exemplo(metodo: "pix", tel: "47966667777", total: 0) // total do painel não é conferido
        });
        r.EnsureSuccessStatusCode();
        var p = await r.Content.ReadFromJsonAsync<PedidoPainelResp>();
        Assert.Equal("whatsapp", p!.Pedido.Origem);
        Assert.Equal(0, p.Pedido.Etapa);
        Assert.Equal(237, p.Pedido.Total);
    }
}
