using Florisa.Api.Dominio;
using Florisa.Api.Servicos;
using Microsoft.EntityFrameworkCore;

namespace Florisa.Api.Dados;

/// <summary>
/// Dados iniciais copiados da loja v8 (index.html: PRODUTOS, EXTRAS e FRETE).
/// Só entram quando o banco está vazio. Depois disso, a equipe muda tudo pelo painel.
/// Preços de extras e taxas de entrega ainda são EXEMPLOS [CONFIRMAR com a loja].
/// </summary>
public static class Semente
{
    public static async Task AplicarAsync(FlorisaDb db, IConfiguration config, TimeProvider relogio)
    {
        if (!await db.Produtos.AnyAsync())
        {
            var ordem = 0;
            foreach (var p in Produtos())
            {
                p.Ordem = ordem++;
                db.Produtos.Add(p);
            }
        }

        if (!await db.Extras.AnyAsync())
        {
            db.Extras.AddRange(
                new Extra { Id = "bombom", Nome = "Caixa de bombons", Preco = 35 },
                new Extra { Id = "pelucia", Nome = "Urso de pelúcia", Preco = 59 },
                new Extra { Id = "balao", Nome = "Balão metalizado", Preco = 25 });
        }

        if (!await db.TaxasEntrega.AnyAsync())
        {
            var taxas = new (string Bairro, decimal Valor)[]
            {
                ("Vila Lalau", 10), ("Centro", 12), ("Baependi", 12), ("Czerniewicz", 12),
                ("Jaraguá Esquerdo", 14), ("Vila Nova", 14), ("Amizade", 15), ("Ilha da Figueira", 15),
                ("Barra do Rio Cerro", 18), ("Nova Brasília", 14), ("Outro bairro de Jaraguá do Sul", 20),
                ("Guaramirim", 25), ("Schroeder", 30), ("Corupá", 35), ("Massaranduba", 35)
            };
            var i = 0;
            foreach (var (bairro, valor) in taxas)
                db.TaxasEntrega.Add(new TaxaEntrega { Bairro = bairro, Valor = valor, Ordem = i++ });
        }

        // Primeiro acesso ao painel: cria o administrador com a senha da configuração (Equipe:AdminSenha).
        var senhaAdmin = config["Equipe:AdminSenha"];
        if (!string.IsNullOrWhiteSpace(senhaAdmin) && !await db.Equipe.AnyAsync())
        {
            db.Equipe.Add(new MembroEquipe
            {
                Usuario = config["Equipe:AdminUsuario"] ?? "admin",
                Nome = config["Equipe:AdminNome"] ?? "Administrador",
                SenhaHash = Senhas.Gerar(senhaAdmin),
                Papel = Papeis.Admin
            });
        }

        await db.SaveChangesAsync();
    }

    static IEnumerable<Produto> Produtos()
    {
        Produto P(string id, string nome, decimal preco, string cat, string desc, decimal? de = null,
            string? selo = null, bool exemplo = false, bool semExtras = false, int antecedencia = 0, string? img = null) =>
            new()
            {
                Id = id, Nome = nome, Preco = preco, PrecoDe = de, Categoria = cat, Descricao = desc, Selo = selo,
                Exemplo = exemplo, PermiteExtras = !semExtras, AntecedenciaDias = antecedencia, Imagem = img
            };

        yield return P("rn21", "Bouquet com 12 rosas nacionais vermelhas", 190, "rosas", "12 rosas nacionais vermelhas com complementos. Tamanho G. Cores, flores, tons e embalagem podem variar.", de: 250, selo: "Promoção");
        yield return P("lindt25", "Lindt chocolate ao leite 25 g", 14, "mimos", "Chocolate ao leite com recheio cremoso, 25 g.", de: 17, selo: "Promoção", semExtras: true);
        yield return P("rn03", "Bouquet com 6 rosas nacionais", 120, "rosas", "6 rosas nacionais com complementos verdes. Embalagem tradicional e laço. Tamanho M.", de: 170, selo: "Promoção");
        yield return P("ri04", "Bouquet com 12 rosas vermelhas", 250, "rosas", "12 rosas vermelhas importadas com complementos verdes, em embalagem transparente.", de: 280, selo: "Promoção");
        yield return P("lindt75", "Bombons Lindt ao leite 75 g", 30, "mimos", "Bombons de chocolate ao leite Lindt, 75 g.", de: 49, selo: "Promoção", semExtras: true);
        yield return P("lr1", "Buquê de lírios brancos", 189, "lirios", "Lírios brancos com folhagens, em papel kraft.", exemplo: true);
        yield return P("lr2", "Lírios cor-de-rosa com rosas", 239, "lirios", "Lírios cor-de-rosa e rosas, com laço.", exemplo: true);
        yield return P("lr3", "Arranjo de lírios no vaso", 210, "lirios", "Lírios em vaso de vidro, prontos para a mesa.", exemplo: true);
        yield return P("lr4", "Lírio avulso", 28, "lirios", "Haste de lírio embalada individualmente.", exemplo: true);
        yield return P("rn17", "15 rosas coloridas com complementos", 305, "rosas", "Buquê com 15 rosas vermelhas, amarelas e cor-de-rosa, com eucalipto e complementos.", selo: "Tamanho G", img: "f-rn17.webp");
        yield return P("fm09", "Rosas, gérberas e astromélias", 270, "aniversario", "Buquê com rosas, gérberas e astromélias em papel rosa.", img: "f-gerbera.webp");
        yield return P("cesta3", "Cesta Manhã Florida", 380, "cafe", "Cesta de café da manhã com flores. Pedidos com um dia de antecedência.", selo: "Peça 1 dia antes", antecedencia: 1, img: "f-cesta.webp");
        yield return P("fm03", "Flores do campo coloridas", 185, "aniversario", "Buquê de flores do campo em cores variadas.", img: "f-campo.webp");
        yield return P("fm08", "Rosas e flores mistas", 290, "aniversario", "Rosas com flores mistas em embalagem kraft.", selo: "Embalagem kraft", img: "f-fm08.webp");
        yield return P("fm17", "Rosas e boca-de-leão", 280, "aniversario", "Buquê de rosas com boca-de-leão.", img: "f-boca.webp");
        yield return P("ex1", "Rosa vermelha avulsa", 14, "rosas", "Rosa vermelha embalada individualmente.", exemplo: true, img: "p-amor.webp");
        yield return P("ex2", "Buquê de rosas em tons pastel", 220, "rosas", "Rosas cor-de-rosa e flores em tons pastel, com laço.", exemplo: true, img: "p-hero.webp");
        yield return P("ex3", "Cesta de café da manhã", 185, "cafe", "Cesta de café da manhã com flores.", exemplo: true, selo: "Peça 1 dia antes", antecedencia: 1, img: "p-cesta.webp");
        yield return P("ex4", "Orquídea no vaso", 120, "plantas", "Orquídea em vaso, pronta para presentear.", exemplo: true, img: "p-orquidea.webp");
        yield return P("ex5", "Vaso com flores", 89, "mimos", "Arranjo de flores em vaso decorativo.", exemplo: true, img: "p-vaso.webp");
        yield return P("ex6", "Buquê de flores mistas", 160, "mimos", "Buquê com flores mistas da estação.", exemplo: true, img: "p-mistas.webp");
        yield return P("ex7", "Coroa de flores brancas", 280, "condolencias", "Coroa com crisântemos e lisiantos brancos. Faixa com a sua mensagem inclusa.", exemplo: true, semExtras: true, img: "p-coroa.webp");
        yield return P("ex8", "Ramalhete de condolências", 80, "condolencias", "Ramalhete em tons claros para homenagens.", exemplo: true, semExtras: true, img: "p-coroa.webp");
    }
}
