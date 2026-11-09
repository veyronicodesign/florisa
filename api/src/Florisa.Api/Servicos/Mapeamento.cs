using System.Globalization;
using Florisa.Api.Contratos;
using Florisa.Api.Dominio;

namespace Florisa.Api.Servicos;

public static class Mapeamento
{
    public static PagamentoResp Pagamento(Pedido p) => new(
        p.PagamentoGatewayId,
        p.PagamentoMetodo,
        p.PagamentoStatus,
        p.PagamentoMetodo == "pix" && p.PixCopiaECola is not null ? new PixDto(p.PixCopiaECola, p.PixQr, p.PixExpiraEm) : null,
        p.PagamentoParcelas,
        p.CartaoFinal);

    public static PedidoCriadoResp Criado(Pedido p) => new(p.Numero, p.Etapa, Pagamento(p));

    public static PedidoResp Completo(Pedido p) => new(
        p.Numero,
        p.Etapa,
        Etapas.Nome(p.Etapa, p.Modo),
        p.CriadoEm,
        p.Origem,
        new ClienteDto(p.ClienteNome, p.ClienteTelefone, p.ClienteEmail),
        p.DestinatarioNome is null ? null : new DestinatarioDto(p.DestinatarioNome, p.DestinatarioTelefone),
        new EntregaDto(
            p.Modo,
            p.DataEntrega?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            p.Frete,
            p.Modo == "entrega" ? new EnderecoDto(p.Cep, p.Rua, p.NumeroEndereco, p.Complemento, p.Bairro) : null),
        p.Itens.OrderBy(i => i.Id).Select(i => new ItemDto(i.ProdutoId, i.Nome, i.Quantidade, i.PrecoUnitario, i.ListaExtras(), i.Total)).ToList(),
        p.Subtotal,
        p.Frete,
        p.Total,
        Pagamento(p),
        p.MensagemCartao,
        p.Observacoes);

    public static PedidoResumoDto Resumo(Pedido p) => new(
        p.Numero,
        p.Etapa,
        Etapas.Nome(p.Etapa, p.Modo),
        p.Modo,
        p.DataEntrega?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        p.CriadoEm,
        p.Origem,
        p.ClienteNome,
        p.ClienteTelefone,
        p.DestinatarioNome,
        p.Bairro,
        string.Join(" + ", p.Itens.OrderBy(i => i.Id).Select(i => i.Quantidade > 1 ? $"{i.Quantidade}× {i.Nome}" : i.Nome)),
        !string.IsNullOrEmpty(p.MensagemCartao),
        p.PagamentoMetodo,
        p.PagamentoStatus,
        p.Total,
        p.Entregador);

    public static PedidoPainelResp Painel(Pedido p) => new(
        Completo(p),
        p.Entregador,
        p.NotaInterna,
        p.Eventos.OrderBy(e => e.Quando).ThenBy(e => e.Id).Select(e => new EventoDto(e.Quando, e.Descricao, e.Autor)).ToList());

    public static ProdutoPainelDto Produto(Produto p) => new(p.Id, p.Nome, p.Categoria, p.Preco, p.PrecoDe, p.Selo, p.Descricao,
        p.Imagem, p.PermiteExtras, p.AntecedenciaDias, p.Ativo, p.Disponivel, p.Exemplo, p.Ordem);
}
