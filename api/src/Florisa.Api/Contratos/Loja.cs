namespace Florisa.Api.Contratos;

// Formatos exatamente como em INTEGRACAO.md (o JSON sai em camelCase).

public record FreteReq(string? Bairro, string? Cep, decimal? Subtotal);
public record FreteResp(decimal Valor, string Prazo);

public record ClienteDto(string? Nome, string? Tel, string? Email);
public record DestinatarioDto(string? Nome, string? Tel);
public record EnderecoDto(string? Cep, string? Rua, string? Numero, string? Complemento, string? Bairro);
public record EntregaDto(string? Modo, string? Data, decimal? Taxa, EnderecoDto? Endereco);
public record ItemDto(string? Id, string? Nome, int Quantidade, decimal? PrecoUnitario, string[]? Extras, decimal? Total);
public record PagamentoReq(string? Metodo, int? Parcelas, string? CartaoToken, string? CartaoFinal);

public record NovoPedidoReq(
    string? Origem,
    ClienteDto? Cliente,
    DestinatarioDto? Destinatario,
    EntregaDto? Entrega,
    List<ItemDto>? Itens,
    decimal? Subtotal,
    decimal? Frete,
    decimal? Total,
    PagamentoReq? Pagamento,
    // Campos a mais (opcionais) que o site ainda não envia:
    string? MensagemCartao = null,
    string? Observacoes = null);

public record PixDto(string CopiaECola, string? Qr, DateTime? ExpiraEm);
public record PagamentoResp(string? Id, string Metodo, string Status, PixDto? Pix, int Parcelas, string? CartaoFinal);

public record PedidoCriadoResp(string Numero, int Etapa, PagamentoResp Pagamento);

/// <summary>Pedido completo: o formato de entrada do POST /pedidos + numero, etapa, criadoEm e pagamento com status.</summary>
public record PedidoResp(
    string Numero,
    int Etapa,
    string EtapaNome,
    DateTime CriadoEm,
    string Origem,
    ClienteDto Cliente,
    DestinatarioDto? Destinatario,
    EntregaDto Entrega,
    List<ItemDto> Itens,
    decimal Subtotal,
    decimal Frete,
    decimal Total,
    PagamentoResp Pagamento,
    string? MensagemCartao,
    string? Observacoes);

public record CodigoReq(string? Tel);
public record CodigoResp(bool Enviado);
public record VerificarReq(string? Tel, string? Codigo);
public record ClienteLogadoDto(string Nome, string? Email);
public record VerificarResp(string Token, ClienteLogadoDto Cliente);

public record ErroResp(string Mensagem);

public record CatalogoProdutoDto(string Id, string Nome, string Categoria, decimal Preco, decimal? De, string? Selo,
    string? Desc, string? Img, bool SemExtras, int AntecedenciaDias, bool Disponivel, bool Exemplo);
public record CatalogoExtraDto(string Id, string Nome, decimal Preco);
public record CatalogoResp(List<CatalogoProdutoDto> Produtos, List<CatalogoExtraDto> Extras, Dictionary<string, decimal> Frete);
