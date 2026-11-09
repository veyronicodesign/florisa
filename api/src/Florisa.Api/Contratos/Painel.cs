namespace Florisa.Api.Contratos;

public record LoginEquipeReq(string? Usuario, string? Senha);
public record LoginEquipeResp(string Token, string Nome, string Papel);

public record PedidoResumoDto(
    string Numero,
    int Etapa,
    string EtapaNome,
    string Modo,
    string? Data,
    DateTime CriadoEm,
    string Origem,
    string ClienteNome,
    string ClienteTel,
    string? DestinatarioNome,
    string? Bairro,
    string Itens,
    bool TemCartao,
    string PagamentoMetodo,
    string PagamentoStatus,
    decimal Total,
    string? Entregador,
    string? MensagemCartao = null,
    string? Foto = null);

public record EventoDto(DateTime Quando, string Descricao, string Autor);

public record PedidoPainelResp(PedidoResp Pedido, string? Entregador, string? NotaInterna, List<EventoDto> Historico);

public record AtualizarPedidoReq(string? Entregador, string? NotaInterna);

public record NovoPedidoEquipeReq(string? Origem, NovoPedidoReq? Pedido, bool JaPago = false);

public record ProdutoPainelDto(string Id, string Nome, string Categoria, decimal Preco, decimal? PrecoDe, string? Selo,
    string? Descricao, string? Imagem, bool PermiteExtras, int AntecedenciaDias, bool Ativo, bool Disponivel, bool Exemplo, int Ordem);

public record SalvarProdutoReq(string? Nome, string? Categoria, decimal? Preco, decimal? PrecoDe, string? Selo,
    string? Descricao, string? Imagem, bool? PermiteExtras, int? AntecedenciaDias, bool? Ativo, bool? Disponivel, bool? Exemplo, int? Ordem);

public record TaxaDto(string Bairro, decimal Valor, bool Ativo);

public record MembroDto(int Id, string Usuario, string Nome, string Papel, bool Ativo);
public record NovoMembroReq(string? Usuario, string? Nome, string? Senha, string? Papel);
