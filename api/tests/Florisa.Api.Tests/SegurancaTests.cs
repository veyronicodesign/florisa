using System.Net;
using System.Net.Http.Json;

namespace Florisa.Api.Tests;

public class SegurancaTests : IClassFixture<FabricaApi>
{
    readonly FabricaApi _f;
    readonly HttpClient _c;
    public SegurancaTests(FabricaApi f) { _f = f; _c = f.CreateClient(); }

    void AvancaRelogio(int segundos) => _f.Relogio.Agora = _f.Relogio.Agora.AddSeconds(segundos);

    [Fact]
    public async Task No_maximo_5_codigos_por_telefone_por_dia()
    {
        const string tel = "47911223344";
        for (var i = 0; i < 5; i++)
        {
            (await _c.PostAsJsonAsync("/api/auth/codigo", new { tel })).EnsureSuccessStatusCode();
            AvancaRelogio(61);
        }
        var sexto = await _c.PostAsJsonAsync("/api/auth/codigo", new { tel });
        Assert.Equal(HttpStatusCode.TooManyRequests, sexto.StatusCode);
    }

    [Fact]
    public async Task Depois_de_10_erros_nem_o_codigo_certo_entra()
    {
        const string tel = "47955443322";
        AvancaRelogio(3600);
        string codigo = "";
        for (var rodada = 0; rodada < 2; rodada++)
        {
            (await _c.PostAsJsonAsync("/api/auth/codigo", new { tel })).EnsureSuccessStatusCode();
            codigo = _f.Mensagens.UltimoCodigo(tel);
            var errado = codigo == "0000" ? "1111" : "0000";
            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo = errado })).StatusCode);
            AvancaRelogio(61);
        }
        (await _c.PostAsJsonAsync("/api/auth/codigo", new { tel })).EnsureSuccessStatusCode();
        codigo = _f.Mensagens.UltimoCodigo(tel);
        var certo = await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo });
        Assert.Equal(HttpStatusCode.TooManyRequests, certo.StatusCode);
    }

    [Fact]
    public async Task Codigo_errado_algumas_vezes_ainda_deixa_entrar_com_o_certo()
    {
        const string tel = "47966554433";
        AvancaRelogio(7200);
        (await _c.PostAsJsonAsync("/api/auth/codigo", new { tel })).EnsureSuccessStatusCode();
        var codigo = _f.Mensagens.UltimoCodigo(tel);
        var errado = codigo == "0000" ? "1111" : "0000";
        await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo = errado });
        await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo = errado });
        (await _c.PostAsJsonAsync("/api/auth/verificar", new { tel, codigo })).EnsureSuccessStatusCode();
    }
}
