using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Florisa.Api.Contratos;

namespace Florisa.Api.Tests;

public class LojaTests : IClassFixture<FabricaApi>
{
    readonly FabricaApi _f;
    readonly HttpClient _c;

    public LojaTests(FabricaApi f)
    {
        _f = f;
        _c = f.CreateClient();
    }

    [Fact]
    public async Task Frete_devolve_a_taxa_do_bairro_sem_ligar_para_acento()
    {
        var r = await _c.PostAsJsonAsync("/api/frete", new { bairro = "jaragua esquerdo", cep = "89250000", subtotal = 190 });
        r.EnsureSuccessStatusCode();
        var frete = await r.Content.ReadFromJsonAsync<FreteResp>();
        Assert.Equal(14, frete!.Valor);
        Assert.Equal("No dia escolhido", frete.Prazo);
    }

    [Fact]
    public async Task Frete_de_bairro_que_nao_atende_explica_o_que_fazer()
    {
        var r = await _c.PostAsJsonAsync("/api/frete", new { bairro = "Florianópolis", cep = "88000000", subtotal = 190 });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var erro = await r.Content.ReadFromJsonAsync<ErroResp>();
        Assert.Contains("WhatsApp", erro!.Mensagem);
    }

    [Fact]
    public async Task Pedido_com_pix_fica_aguardando_pagamento_com_numero_sequencial()
    {
        var r = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo());
        r.EnsureSuccessStatusCode();
        var p = await r.Content.ReadFromJsonAsync<PedidoCriadoResp>();
        Assert.Equal(-1, p!.Etapa);
        Assert.Equal("pendente", p.Pagamento.Status);
        Assert.NotNull(p.Pagamento.Pix);
        Assert.NotNull(p.Pagamento.Pix!.ExpiraEm);
        Assert.True(int.Parse(p.Numero) > 4800);
    }

    [Fact]
    public async Task Servidor_recalcula_o_total_e_recusa_total_errado()
    {
        // 190 (rn21) + 35 (bombom) + 12 (Centro) = 237. O navegador mandou 200.
        var r = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(total: 200));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var erro = await r.Content.ReadFromJsonAsync<ErroResp>();
        Assert.Contains("237", erro!.Mensagem);
    }

    [Fact]
    public async Task Cartao_aprovado_entra_direto_na_fila()
    {
        var r = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(metodo: "cartao"));
        r.EnsureSuccessStatusCode();
        var p = await r.Content.ReadFromJsonAsync<PedidoCriadoResp>();
        Assert.Equal(0, p!.Etapa);
        Assert.Equal("aprovado", p.Pagamento.Status);
    }

    [Fact]
    public async Task Cartao_recusado_devolve_status_recusado()
    {
        var r = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(metodo: "cartao", token: "recusar-teste"));
        r.EnsureSuccessStatusCode();
        var p = await r.Content.ReadFromJsonAsync<PedidoCriadoResp>();
        Assert.Equal("recusado", p!.Pagamento.Status);
        Assert.Equal(-1, p.Etapa);
    }

    [Fact]
    public async Task Pagar_na_loja_so_vale_para_retirada()
    {
        var naEntrega = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(metodo: "loja"));
        Assert.Equal(HttpStatusCode.BadRequest, naEntrega.StatusCode);

        var naRetirada = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(metodo: "loja", modo: "retirada", total: 225));
        naRetirada.EnsureSuccessStatusCode();
        var p = await naRetirada.Content.ReadFromJsonAsync<PedidoCriadoResp>();
        Assert.Equal("na_retirada", p!.Pagamento.Status);
        Assert.Equal(0, p.Etapa);
    }

    [Fact]
    public async Task Domingo_e_data_passada_sao_recusados()
    {
        var domingo = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(data: "2026-11-15"));
        Assert.Equal(HttpStatusCode.BadRequest, domingo.StatusCode);
        var ontem = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(data: "2026-11-08"));
        Assert.Equal(HttpStatusCode.BadRequest, ontem.StatusCode);
    }

    [Fact]
    public async Task Cesta_precisa_de_um_dia_de_antecedencia()
    {
        // Hoje é 09/11. Cesta Manhã Florida (380) para hoje: não pode. Para amanhã: pode.
        var hoje = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "cesta3", data: "2026-11-09", total: 380 + 35 + 12));
        Assert.Equal(HttpStatusCode.BadRequest, hoje.StatusCode);
        var amanha = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "cesta3", data: "2026-11-10", total: 380 + 35 + 12));
        amanha.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Chocolate_nao_aceita_extras()
    {
        var r = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "lindt75", total: 30 + 35 + 12));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Produto_inexistente_e_recusado()
    {
        var r = await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(produto: "nao-existe"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Consulta_so_mostra_o_pedido_para_o_telefone_certo()
    {
        var criado = await (await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(tel: "47911112222")))
            .Content.ReadFromJsonAsync<PedidoCriadoResp>();

        var certo = await _c.GetAsync($"/api/pedidos/{criado!.Numero}?tel=5547911112222");
        certo.EnsureSuccessStatusCode();
        var p = await certo.Content.ReadFromJsonAsync<PedidoResp>();
        Assert.Equal(237, p!.Total);
        Assert.Equal("Centro", p.Entrega.Endereco!.Bairro);
        Assert.Equal(new[] { "bombom" }, p.Itens[0].Extras);
        Assert.Equal("Feliz aniversário!", p.MensagemCartao);

        var errado = await _c.GetAsync($"/api/pedidos/{criado.Numero}?tel=47900000000");
        Assert.Equal(HttpStatusCode.NotFound, errado.StatusCode);
        var semTel = await _c.GetAsync($"/api/pedidos/{criado.Numero}");
        Assert.Equal(HttpStatusCode.NotFound, semTel.StatusCode);
    }

    [Fact]
    public async Task Webhook_confirma_o_pix_e_o_pedido_entra_na_fila()
    {
        var criado = await (await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(tel: "47933334444")))
            .Content.ReadFromJsonAsync<PedidoCriadoResp>();

        var semSegredo = await _c.PostAsJsonAsync("/api/pagamentos/webhook", new { gatewayId = criado!.Pagamento.Id, status = "aprovado" });
        Assert.Equal(HttpStatusCode.Unauthorized, semSegredo.StatusCode);

        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/pagamentos/webhook")
        {
            Content = JsonContent.Create(new { gatewayId = criado.Pagamento.Id, status = "aprovado" })
        };
        msg.Headers.Add("X-Florisa-Segredo", FabricaApi.Segredo);
        (await _c.SendAsync(msg)).EnsureSuccessStatusCode();

        var p = await _c.GetFromJsonAsync<PedidoResp>($"/api/pedidos/{criado.Numero}?tel=47933334444");
        Assert.Equal(0, p!.Etapa);
        Assert.Equal("aprovado", p.Pagamento.Status);
    }

    [Fact]
    public async Task Login_por_codigo_e_meus_pedidos()
    {
        const string tel = "47955556666";
        await _c.PostAsJsonAsync("/api/pedidos", Pedidos.Exemplo(tel: tel));

        (await _c.PostAsJsonAsync("/api/auth/codigo", new { tel })).EnsureSuccessStatusCode();
        var codigo = _f.Mensagens.UltimoCodigo(tel);

        var errado = await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo = codigo == "0000" ? "1111" : "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, errado.StatusCode);

        var certo = await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo });
        certo.EnsureSuccessStatusCode();
        var login = await certo.Content.ReadFromJsonAsync<VerificarResp>();
        Assert.Equal("Ana", login!.Cliente.Nome);

        var logado = _f.CreateClient();
        logado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var meus = await logado.GetFromJsonAsync<List<PedidoResp>>("/api/pedidos?meus=1");
        Assert.Single(meus!);

        var semLogin = await _c.GetAsync("/api/pedidos?meus=1");
        Assert.Equal(HttpStatusCode.Unauthorized, semLogin.StatusCode);
    }

    [Fact]
    public async Task Codigo_nao_pode_ser_pedido_duas_vezes_seguidas()
    {
        const string tel = "47977778888";
        (await _c.PostAsJsonAsync("/api/auth/codigo", new { tel })).EnsureSuccessStatusCode();
        var denovo = await _c.PostAsJsonAsync("/api/auth/codigo", new { tel });
        Assert.Equal(HttpStatusCode.TooManyRequests, denovo.StatusCode);
    }

    [Fact]
    public async Task Catalogo_traz_produtos_extras_e_taxas()
    {
        var cat = await _c.GetFromJsonAsync<CatalogoResp>("/api/catalogo");
        Assert.Contains(cat!.Produtos, p => p.Id == "rn21" && p.Preco == 190 && p.De == 250);
        Assert.Contains(cat.Extras, e => e.Id == "bombom");
        Assert.Equal(12, cat.Frete["Centro"]);
    }
}
