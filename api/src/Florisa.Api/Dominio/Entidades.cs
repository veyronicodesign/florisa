namespace Florisa.Api.Dominio;

/// <summary>Produto do catálogo. O Id é o mesmo código usado no site (ex.: "rn21").</summary>
public class Produto
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Categoria { get; set; } = "";
    public decimal Preco { get; set; }
    /// <summary>Preço "de" (riscado) quando está em promoção.</summary>
    public decimal? PrecoDe { get; set; }
    public string? Selo { get; set; }
    public string? Descricao { get; set; }
    public string? Imagem { get; set; }
    /// <summary>Se aceita extras (bombom, pelúcia, balão). Chocolates e coroas não aceitam.</summary>
    public bool PermiteExtras { get; set; } = true;
    /// <summary>Dias de antecedência mínima. Ex.: cestas de café da manhã = 1.</summary>
    public int AntecedenciaDias { get; set; }
    /// <summary>Aparece no site.</summary>
    public bool Ativo { get; set; } = true;
    /// <summary>Pode ser comprado agora. Falso = aparece como indisponível.</summary>
    public bool Disponivel { get; set; } = true;
    /// <summary>Produto de exemplo (nome e preço provisórios, a confirmar com a loja).</summary>
    public bool Exemplo { get; set; }
    public int Ordem { get; set; }
}

public class Extra
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public decimal Preco { get; set; }
    public bool Ativo { get; set; } = true;
}

/// <summary>Taxa de entrega por bairro ou cidade.</summary>
public class TaxaEntrega
{
    public string Bairro { get; set; } = "";
    public decimal Valor { get; set; }
    public bool Ativo { get; set; } = true;
    public int Ordem { get; set; }
}

public class Cliente
{
    public int Id { get; set; }
    /// <summary>Só dígitos, com DDD e sem o 55. Ex.: 47999999999.</summary>
    public string Telefone { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Email { get; set; }
    public DateTime CriadoEm { get; set; }
}

public static class Etapas
{
    public const int AguardandoPagamento = -1;
    public const int Recebido = 0;
    public const int EmPreparo = 1;
    public const int SaiuOuPronto = 2;
    public const int Concluido = 3;

    public static string Nome(int etapa, string modo) => (etapa, modo) switch
    {
        (-1, _) => "Aguardando pagamento",
        (0, _) => "Pedido recebido",
        (1, _) => "Em preparo",
        (2, "retirada") => "Pronto para retirada",
        (2, _) => "Saiu para entrega",
        (3, "retirada") => "Retirado",
        (3, _) => "Entregue",
        _ => "?"
    };
}

public static class StatusPagamento
{
    public const string Pendente = "pendente";
    public const string Aprovado = "aprovado";
    public const string Recusado = "recusado";
    public const string NaRetirada = "na_retirada";
}

public class Pedido
{
    public int Id { get; set; }
    public string Numero { get; set; } = "";
    /// <summary>"site", "whatsapp" ou "balcao".</summary>
    public string Origem { get; set; } = "site";

    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    // Cópia dos dados do cliente no momento do pedido.
    public string ClienteNome { get; set; } = "";
    public string ClienteTelefone { get; set; } = "";
    public string? ClienteEmail { get; set; }

    public string? DestinatarioNome { get; set; }
    public string? DestinatarioTelefone { get; set; }

    /// <summary>"entrega" ou "retirada".</summary>
    public string Modo { get; set; } = "entrega";
    public DateOnly? DataEntrega { get; set; }
    public string? Cep { get; set; }
    public string? Rua { get; set; }
    public string? NumeroEndereco { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }

    public string? MensagemCartao { get; set; }
    public string? Observacoes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Frete { get; set; }
    public decimal Total { get; set; }

    public int Etapa { get; set; } = Etapas.AguardandoPagamento;
    public string? Entregador { get; set; }
    public string? NotaInterna { get; set; }

    // Pagamento
    public string PagamentoMetodo { get; set; } = "pix";
    public int PagamentoParcelas { get; set; } = 1;
    public string PagamentoStatus { get; set; } = StatusPagamento.Pendente;
    public string? PagamentoGatewayId { get; set; }
    public string? PixCopiaECola { get; set; }
    public string? PixQr { get; set; }
    public DateTime? PixExpiraEm { get; set; }
    public string? CartaoFinal { get; set; }
    public DateTime? PagoEm { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }

    public List<PedidoItem> Itens { get; set; } = new();
    public List<PedidoEvento> Eventos { get; set; } = new();
}

public class PedidoItem
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public string ProdutoId { get; set; } = "";
    public string Nome { get; set; } = "";
    public int Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    /// <summary>Ids dos extras separados por vírgula (ex.: "bombom,balao").</summary>
    public string Extras { get; set; } = "";
    /// <summary>Soma dos extras de uma unidade.</summary>
    public decimal ValorExtras { get; set; }
    public decimal Total { get; set; }

    public string[] ListaExtras() =>
        string.IsNullOrEmpty(Extras) ? Array.Empty<string>() : Extras.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Histórico do pedido: quem fez o quê e quando.</summary>
public class PedidoEvento
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public DateTime Quando { get; set; }
    public string Descricao { get; set; } = "";
    public string Autor { get; set; } = "";
}

/// <summary>Código de 4 dígitos enviado pelo WhatsApp para o cliente entrar.</summary>
public class CodigoAcesso
{
    public int Id { get; set; }
    public string Telefone { get; set; } = "";
    public string CodigoHash { get; set; } = "";
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public int Tentativas { get; set; }
    public bool Usado { get; set; }
}

public static class Papeis
{
    public const string Admin = "admin";
    public const string Atendente = "atendente";
    public const string Estoque = "estoque";
    public const string Entregador = "entregador";
    public static readonly string[] Todos = { Admin, Atendente, Estoque, Entregador };
}

/// <summary>Pessoa da equipe que usa o painel.</summary>
public class MembroEquipe
{
    public int Id { get; set; }
    public string Usuario { get; set; } = "";
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public string Papel { get; set; } = Papeis.Atendente;
    public bool Ativo { get; set; } = true;
}
