using System.Globalization;
using Florisa.Api.Contratos;
using Florisa.Api.Dados;
using Florisa.Api.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Florisa.Api.Servicos;

public class ServicoFrete
{
    readonly FlorisaDb _db;
    public ServicoFrete(FlorisaDb db) => _db = db;

    public const string Prazo = "No dia escolhido";

    /// <summary>Taxa do bairro. Erro amigável se a loja não entrega lá.</summary>
    public async Task<TaxaEntrega> TaxaAsync(string? bairro, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bairro))
            throw new RegraException("Escolha o bairro da entrega.");
        var chave = Texto.Chave(bairro);
        var taxas = await _db.TaxasEntrega.Where(t => t.Ativo).ToListAsync(ct);
        return taxas.FirstOrDefault(t => Texto.Chave(t.Bairro) == chave)
            ?? throw new RegraException($"Ainda não entregamos em {bairro.Trim()} pelo site. Chame a gente no WhatsApp que a gente combina.");
    }
}

/// <summary>Cria, consulta e anda com os pedidos. Toda a regra de preço fica aqui.</summary>
public class ServicoPedidos
{
    readonly FlorisaDb _db;
    readonly ServicoFrete _frete;
    readonly IGatewayPagamento _gateway;
    readonly IEnvioMensagens _mensagens;
    readonly HorarioLoja _horario;
    readonly IConfiguration _config;
    readonly ILogger<ServicoPedidos> _log;

    public ServicoPedidos(FlorisaDb db, ServicoFrete frete, IGatewayPagamento gateway, IEnvioMensagens mensagens,
        HorarioLoja horario, IConfiguration config, ILogger<ServicoPedidos> log)
    {
        _db = db; _frete = frete; _gateway = gateway; _mensagens = mensagens; _horario = horario; _config = config; _log = log;
    }

    static readonly string[] Origens = { "site", "whatsapp", "balcao" };

    public async Task<Pedido> CriarAsync(NovoPedidoReq req, string origem, string autor, bool conferirTotal, CancellationToken ct)
    {
        if (!Origens.Contains(origem)) throw new RegraException("Origem do pedido inválida.");

        // ---- Cliente
        var nome = Texto.Limpo(req.Cliente?.Nome, 120) ?? throw new RegraException("Informe o seu nome.");
        var tel = Telefones.Obrigatorio(req.Cliente?.Tel);
        var email = Texto.Limpo(req.Cliente?.Email, 160);
        if (email is not null && !email.Contains('@')) throw new RegraException("Confira o e-mail.");

        // ---- Destinatário (presente)
        string? destNome = null, destTel = null;
        if (req.Destinatario is not null)
        {
            destNome = Texto.Limpo(req.Destinatario.Nome, 120) ?? throw new RegraException("Informe o nome de quem vai receber.");
            destTel = req.Destinatario.Tel is { Length: > 0 } ? Telefones.Obrigatorio(req.Destinatario.Tel, "telefone de quem recebe") : null;
        }

        // ---- Itens: preço sempre do banco, nunca do navegador
        if (req.Itens is not { Count: > 0 }) throw new RegraException("O carrinho está vazio.");
        if (req.Itens.Count > 30) throw new RegraException("Pedido com itens demais. Chame a gente no WhatsApp.");

        var ids = req.Itens.Select(i => i.Id ?? "").Distinct().ToList();
        var produtos = await _db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var extras = await _db.Extras.Where(e => e.Ativo).ToDictionaryAsync(e => e.Id, ct);

        var itens = new List<PedidoItem>();
        foreach (var i in req.Itens)
        {
            if (i.Id is null || !produtos.TryGetValue(i.Id, out var p) || !p.Ativo)
                throw new RegraException($"O produto \"{i.Nome ?? i.Id}\" não está mais no catálogo. Tire do carrinho e tente de novo.");
            if (!p.Disponivel)
                throw new RegraException($"\"{p.Nome}\" acabou por hoje. Tire do carrinho ou escolha outra data.", StatusCodes.Status409Conflict);
            if (i.Quantidade is < 1 or > 20)
                throw new RegraException($"Quantidade inválida em \"{p.Nome}\".");

            var idsExtras = (i.Extras ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
            if (idsExtras.Length > 0 && !p.PermiteExtras)
                throw new RegraException($"\"{p.Nome}\" não aceita extras.");
            decimal valorExtras = 0;
            foreach (var ex in idsExtras)
            {
                if (!extras.TryGetValue(ex, out var extra)) throw new RegraException("Um dos extras escolhidos não está mais disponível.");
                valorExtras += extra.Preco;
            }

            itens.Add(new PedidoItem
            {
                ProdutoId = p.Id,
                Nome = p.Nome,
                Quantidade = i.Quantidade,
                PrecoUnitario = p.Preco,
                Extras = string.Join(',', idsExtras),
                ValorExtras = valorExtras,
                Total = (p.Preco + valorExtras) * i.Quantidade
            });
        }
        var subtotal = itens.Sum(i => i.Total);

        // ---- Entrega ou retirada
        var modo = req.Entrega?.Modo?.Trim().ToLowerInvariant();
        if (modo is not ("entrega" or "retirada")) throw new RegraException("Escolha entrega ou retirada na loja.");

        DateOnly? data = null;
        if (!string.IsNullOrWhiteSpace(req.Entrega!.Data))
        {
            if (!DateOnly.TryParseExact(req.Entrega.Data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                throw new RegraException("Data inválida.");
            data = d;
        }

        var antecedencia = itens.Max(i => produtos[i.ProdutoId].AntecedenciaDias);
        decimal frete = 0;
        string? cep = null, rua = null, numero = null, complemento = null, bairro = null;
        if (modo == "entrega")
        {
            var end = req.Entrega.Endereco ?? throw new RegraException("Informe o endereço da entrega.");
            cep = new string((end.Cep ?? "").Where(char.IsDigit).ToArray());
            if (cep.Length != 8) throw new RegraException("Confira o CEP.");
            rua = Texto.Limpo(end.Rua, 160) ?? throw new RegraException("Informe a rua.");
            numero = Texto.Limpo(end.Numero, 20) ?? throw new RegraException("Informe o número.");
            complemento = Texto.Limpo(end.Complemento, 120);
            var taxa = await _frete.TaxaAsync(end.Bairro, ct);
            bairro = taxa.Bairro;
            frete = taxa.Valor;
            if (data is null) throw new RegraException("Escolha a data da entrega.");
        }
        if (data is { } dia) ValidarData(dia, antecedencia);

        var total = subtotal + frete;
        if (conferirTotal && req.Total is { } totalTela && Math.Abs(totalTela - total) >= 0.01m)
            throw new RegraException(
                $"Os valores mudaram desde que você montou o carrinho. O total agora é {total.ToString("C", new CultureInfo("pt-BR"))}. Confira e tente de novo.",
                StatusCodes.Status409Conflict);

        // ---- Pagamento
        var metodo = req.Pagamento?.Metodo?.Trim().ToLowerInvariant();
        if (metodo is not ("pix" or "cartao" or "loja")) throw new RegraException("Escolha a forma de pagamento.");
        if (metodo == "loja" && modo != "retirada") throw new RegraException("Pagar na loja só vale para retirada.");
        var parcelas = 1;
        if (metodo == "cartao")
        {
            var max = _config.GetValue("Loja:ParcelasMax", 3);
            parcelas = req.Pagamento!.Parcelas ?? 1;
            if (parcelas < 1 || parcelas > max) throw new RegraException($"Parcele em até {max}x.");
            if (string.IsNullOrWhiteSpace(req.Pagamento.CartaoToken)) throw new RegraException("Não recebemos os dados do cartão. Tente de novo.");
        }

        // ---- Grava
        var agora = _horario.AgoraUtc;
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Telefone == tel, ct);
        if (cliente is null)
        {
            cliente = new Cliente { Telefone = tel, Nome = nome, Email = email, CriadoEm = agora };
            _db.Clientes.Add(cliente);
        }
        else
        {
            cliente.Nome = nome;
            if (email is not null) cliente.Email = email;
        }

        var pedido = new Pedido
        {
            Origem = origem,
            Cliente = cliente,
            ClienteNome = nome,
            ClienteTelefone = tel,
            ClienteEmail = email,
            DestinatarioNome = destNome,
            DestinatarioTelefone = destTel,
            Modo = modo,
            DataEntrega = data,
            Cep = cep, Rua = rua, NumeroEndereco = numero, Complemento = complemento, Bairro = bairro,
            MensagemCartao = Texto.Limpo(req.MensagemCartao, 400),
            Observacoes = Texto.Limpo(req.Observacoes, 400),
            Subtotal = subtotal,
            Frete = frete,
            Total = total,
            Itens = itens,
            PagamentoMetodo = metodo,
            PagamentoParcelas = parcelas,
            PagamentoStatus = metodo == "loja" ? StatusPagamento.NaRetirada : StatusPagamento.Pendente,
            CartaoFinal = metodo == "cartao" ? Texto.Limpo(req.Pagamento!.CartaoFinal, 4) : null,
            Etapa = metodo == "loja" ? Etapas.Recebido : Etapas.AguardandoPagamento,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
        pedido.Eventos.Add(new PedidoEvento { Quando = agora, Autor = autor, Descricao = $"Pedido criado ({origem})" });

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync(ct);
        // Número sequencial a partir do Id (sem disputa entre dois pedidos ao mesmo tempo).
        pedido.Numero = (_config.GetValue("Loja:NumeroInicial", 4800) + pedido.Id).ToString(CultureInfo.InvariantCulture);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // ---- Cobrança (depois de gravar, para o provedor receber o número do pedido)
        if (metodo == "pix")
        {
            var pix = await _gateway.CriarPixAsync(pedido, ct);
            pedido.PagamentoGatewayId = pix.GatewayId;
            pedido.PixCopiaECola = pix.CopiaECola;
            pedido.PixQr = pix.Qr;
            pedido.PixExpiraEm = pix.ExpiraEm;
        }
        else if (metodo == "cartao")
        {
            var cobranca = await _gateway.CobrarCartaoAsync(pedido, req.Pagamento!.CartaoToken!, parcelas, ct);
            pedido.PagamentoGatewayId = cobranca.GatewayId;
            if (cobranca.Status == StatusPagamento.Aprovado) MarcarPago(pedido, "Cartão aprovado", "sistema");
            else
            {
                pedido.PagamentoStatus = StatusPagamento.Recusado;
                pedido.Eventos.Add(new PedidoEvento { Quando = _horario.AgoraUtc, Autor = "sistema", Descricao = "Cartão recusado" });
            }
        }
        await _db.SaveChangesAsync(ct);

        if (pedido.PagamentoStatus != StatusPagamento.Recusado)
            await AvisarAsync(pedido, pedido.Etapa == Etapas.AguardandoPagamento
                ? $"Florisa: recebemos o seu pedido #{pedido.Numero}. Assim que o Pix for confirmado, ele entra na fila."
                : $"Florisa: pedido #{pedido.Numero} recebido! Avisamos por aqui a cada etapa.", ct);

        return pedido;
    }

    void ValidarData(DateOnly dia, int antecedencia)
    {
        var hoje = _horario.Hoje;
        if (dia < hoje) throw new RegraException("Escolha uma data a partir de hoje.");
        if (dia > hoje.AddDays(_config.GetValue("Loja:DiasMaximosAgendamento", 60)))
            throw new RegraException("Para datas tão adiante, chame a gente no WhatsApp.");
        if (dia.DayOfWeek == DayOfWeek.Sunday && !_config.GetValue("Loja:EntregaDomingo", false))
            throw new RegraException("Aos domingos a loja fica fechada. Escolha outro dia.");
        if (antecedencia > 0 && dia < hoje.AddDays(antecedencia))
            throw new RegraException(antecedencia == 1
                ? "Um dos itens precisa ser pedido com 1 dia de antecedência. Escolha a partir de amanhã."
                : $"Um dos itens precisa ser pedido com {antecedencia} dias de antecedência.");
    }

    void MarcarPago(Pedido p, string descricao, string autor)
    {
        p.PagamentoStatus = StatusPagamento.Aprovado;
        p.PagoEm = _horario.AgoraUtc;
        if (p.Etapa == Etapas.AguardandoPagamento) p.Etapa = Etapas.Recebido;
        p.AtualizadoEm = _horario.AgoraUtc;
        p.Eventos.Add(new PedidoEvento { Quando = _horario.AgoraUtc, Autor = autor, Descricao = descricao });
    }

    public Task<Pedido?> BuscarAsync(string numero, CancellationToken ct) =>
        _db.Pedidos.Include(p => p.Itens).Include(p => p.Eventos).FirstOrDefaultAsync(p => p.Numero == numero, ct);

    /// <summary>Pix confirmado pelo provedor (webhook). Repetir o aviso não faz nada.</summary>
    public async Task<bool> ConfirmarPeloGatewayAsync(AvisoPagamento aviso, CancellationToken ct)
    {
        var p = await _db.Pedidos.Include(x => x.Eventos).FirstOrDefaultAsync(x => x.PagamentoGatewayId == aviso.GatewayId, ct);
        if (p is null) return false;
        if (aviso.Status == StatusPagamento.Aprovado && p.PagamentoStatus != StatusPagamento.Aprovado)
        {
            MarcarPago(p, "Pagamento confirmado pelo banco", "sistema");
            await _db.SaveChangesAsync(ct);
            await AvisarAsync(p, $"Florisa: pagamento do pedido #{p.Numero} confirmado. Ele já entrou na fila!", ct);
        }
        else if (aviso.Status == StatusPagamento.Recusado && p.PagamentoStatus == StatusPagamento.Pendente)
        {
            p.PagamentoStatus = StatusPagamento.Recusado;
            p.Eventos.Add(new PedidoEvento { Quando = _horario.AgoraUtc, Autor = "sistema", Descricao = "Pagamento recusado pelo banco" });
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    /// <summary>A equipe confirma à mão (Pix caiu na conta, pagou no balcão, ou modo simulado).</summary>
    public async Task ConfirmarManualAsync(Pedido p, string autor, CancellationToken ct)
    {
        if (p.PagamentoStatus == StatusPagamento.Aprovado) throw new RegraException("Este pedido já está pago.");
        MarcarPago(p, $"Pagamento confirmado na mão por {autor}", autor);
        await _db.SaveChangesAsync(ct);
        await AvisarAsync(p, $"Florisa: pagamento do pedido #{p.Numero} confirmado. Ele já entrou na fila!", ct);
    }

    /// <summary>Passa o pedido para a próxima etapa da linha do tempo.</summary>
    public async Task AvancarAsync(Pedido p, string autor, string papel, CancellationToken ct)
    {
        if (p.PagamentoStatus == StatusPagamento.Recusado) throw new RegraException("O pagamento deste pedido foi recusado.");
        if (p.Etapa == Etapas.AguardandoPagamento) throw new RegraException("Este pedido ainda está aguardando pagamento.");
        if (p.Etapa >= Etapas.Concluido) throw new RegraException("Este pedido já foi concluído.");
        if (papel == Papeis.Entregador && (p.Modo != "entrega" || p.Etapa < Etapas.EmPreparo))
            throw new RegraException("Entregador só marca saída e entrega.", StatusCodes.Status403Forbidden);

        p.Etapa++;
        p.AtualizadoEm = _horario.AgoraUtc;
        // Quem paga na retirada: ao retirar, considera pago.
        if (p.Etapa == Etapas.Concluido && p.PagamentoStatus == StatusPagamento.NaRetirada)
        {
            p.PagamentoStatus = StatusPagamento.Aprovado;
            p.PagoEm = _horario.AgoraUtc;
        }
        var nome = Etapas.Nome(p.Etapa, p.Modo);
        p.Eventos.Add(new PedidoEvento { Quando = _horario.AgoraUtc, Autor = autor, Descricao = nome });
        await _db.SaveChangesAsync(ct);

        var texto = (p.Etapa, p.Modo) switch
        {
            (Etapas.EmPreparo, _) => $"Florisa: o pedido #{p.Numero} está sendo montado agora.",
            (Etapas.SaiuOuPronto, "retirada") => $"Florisa: o pedido #{p.Numero} está pronto! Pode retirar na R. Bernardo Dornbusch, 2433.",
            (Etapas.SaiuOuPronto, _) => $"Florisa: o pedido #{p.Numero} saiu para entrega.",
            (Etapas.Concluido, "retirada") => $"Florisa: pedido #{p.Numero} retirado. Obrigado!",
            (Etapas.Concluido, _) => $"Florisa: pedido #{p.Numero} entregue. Obrigado por comprar com a gente!",
            _ => null
        };
        if (texto is not null) await AvisarAsync(p, texto, ct);
    }

    async Task AvisarAsync(Pedido p, string texto, CancellationToken ct)
    {
        try { await _mensagens.EnviarAsync(p.ClienteTelefone, texto, ct); }
        catch (Exception e) { _log.LogWarning(e, "Falha ao avisar o pedido {Numero}", p.Numero); }
    }
}
