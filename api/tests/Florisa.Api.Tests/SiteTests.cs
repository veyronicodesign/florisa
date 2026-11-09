using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Florisa.Api.Tests;

/// <summary>Servidor abrindo a loja junto (como em desenvolvimento).</summary>
public class FabricaComSite : FabricaApi
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Site:Pasta", "../../..");
    }
}

public class SiteTests : IClassFixture<FabricaComSite>
{
    readonly HttpClient _c;
    public SiteTests(FabricaComSite f) => _c = f.CreateClient();

    [Fact]
    public async Task Abre_a_loja_ligada_ao_servidor()
    {
        var html = await _c.GetStringAsync("/");
        Assert.Contains("Florisa", html);
        Assert.Contains("msgCartao", html);             // campo da mensagem do cartão
        Assert.Contains("API.cartaoSimulado = true", html); // liga sozinho em localhost
    }

    [Fact]
    public async Task Serve_fotos_e_script_das_imagens()
    {
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/fotos/rn03.webp")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/image-slot.js")).StatusCode);
    }

    [Fact]
    public async Task Nao_expoe_o_codigo_do_servidor()
    {
        var r = await _c.GetAsync("/api/src/Florisa.Api/appsettings.Development.json");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }
}

public class PainelPaginaTests : IClassFixture<FabricaApi>
{
    readonly HttpClient _c;
    public PainelPaginaTests(FabricaApi f) => _c = f.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Painel_abre_em_barra_painel()
    {
        var redireciona = await _c.GetAsync("/painel");
        Assert.Equal(HttpStatusCode.Redirect, redireciona.StatusCode);
        var html = await _c.GetStringAsync("/painel/");
        Assert.Contains("Painel Florisa", html);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/painel/painel.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/painel/fotos/f-rn17.webp")).StatusCode);
    }

    [Fact]
    public async Task Entregadores_exige_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _c.GetAsync("/api/painel/entregadores")).StatusCode);
    }
}
