/* Painel Florisa — telas da equipe. Fala só com /api/painel (ver api/README.md). */
(function () {
  "use strict";

  // ---------------------------------------------------------------- utilidades
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var app = $("#app");
  var FUSO = "America/Sao_Paulo";

  function esc(v) {
    return String(v == null ? "" : v).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
  function brl(v) { return "R$ " + Number(v || 0).toLocaleString("pt-BR", { minimumFractionDigits: Number(v) % 1 ? 2 : 0, maximumFractionDigits: 2 }); }
  function hora(iso) { return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone: FUSO }); }
  function dataHora(iso) { return new Date(iso).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit", timeZone: FUSO }); }
  function hojeISO(somaDias) {
    var d = new Date(Date.now() + (somaDias || 0) * 864e5);
    return d.toLocaleDateString("en-CA", { timeZone: FUSO }); // AAAA-MM-DD
  }
  function dataCurta(iso) {
    if (!iso) return "";
    if (iso === hojeISO(0)) return "Hoje";
    if (iso === hojeISO(1)) return "Amanhã";
    var p = iso.split("-"); var d = new Date(+p[0], +p[1] - 1, +p[2]);
    return d.toLocaleDateString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit" }).replace(".", "");
  }
  function telFmt(t) { t = String(t || ""); return t.length === 11 ? "(" + t.slice(0, 2) + ") " + t.slice(2, 7) + "-" + t.slice(7) : t.length === 10 ? "(" + t.slice(0, 2) + ") " + t.slice(2, 6) + "-" + t.slice(6) : t; }
  function foto(img) { return img ? "fotos/" + encodeURIComponent(img) : ""; }
  function fotoTag(img, cls) {
    return img ? '<img class="' + (cls || "foto") + '" src="' + foto(img) + '" alt="" loading="lazy">'
      : '<span class="' + (cls || "foto") + '" aria-hidden="true"></span>';
  }

  var ICONES = {
    pedidos: '<path d="M8 9h8M8 13h8M8 17h5"/><rect x="4" y="4" width="16" height="16" rx="3"/>',
    entregas: '<path d="M3 7h11v9H3zM14 10h4l3 3v3h-7"/><circle cx="7" cy="17" r="2"/><circle cx="17" cy="17" r="2"/>',
    produtos: '<path d="M12 21c-4-3-7-6-7-10a7 7 0 0 1 14 0c0 4-3 7-7 10z"/><circle cx="12" cy="10" r="2.5"/>',
    equipe: '<circle cx="9" cy="8" r="3.5"/><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6M16 4a3.5 3.5 0 0 1 0 7M18 14c2 .6 3 3 3 6"/>',
    busca: '<circle cx="11" cy="11" r="7"/><path d="M20 20l-4-4"/>',
    mais: '<path d="M12 5v14M5 12h14"/>',
    zap: '<path d="M4 20l1.3-4A8 8 0 1 1 8 18.7z"/>',
    imprimir: '<path d="M6 9V3h12v6M6 18H4v-7h16v7h-2M8 14h8v7H8z"/>',
    seta: '<path d="M5 12h14M13 6l6 6-6 6"/>',
    voltar: '<path d="M15 5l-7 7 7 7"/>',
    check: '<path d="M5 12l5 5L20 7"/>',
    mapa: '<path d="M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z"/><circle cx="12" cy="10" r="2.5"/>'
  };
  function ic(n) { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + ICONES[n] + "</svg>"; }

  var toastT;
  function aviso(txt, erro) {
    var t = $("#toast"); t.textContent = txt; t.className = "on" + (erro ? " erro" : "");
    clearTimeout(toastT); toastT = setTimeout(function () { t.className = erro ? "erro" : ""; }, 3600);
  }

  // ---------------------------------------------------------------- sessão e API
  var CHAVE = "florisa-painel";
  var sessao = null;
  try { sessao = JSON.parse(localStorage.getItem(CHAVE) || "null"); } catch (e) { sessao = null; }
  function salvaSessao(s) { sessao = s; try { s ? localStorage.setItem(CHAVE, JSON.stringify(s)) : localStorage.removeItem(CHAVE); } catch (e) { /* sem armazenamento */ } }
  function pode(papeis) { return !!sessao && papeis.indexOf(sessao.papel) >= 0; }
  var ATENDE = ["admin", "atendente"], ESTOQUE = ["admin", "estoque"], ADMIN = ["admin"];

  function api(metodo, caminho, corpo) {
    var h = { Accept: "application/json" };
    if (corpo !== undefined) h["Content-Type"] = "application/json";
    if (sessao) h.Authorization = "Bearer " + sessao.token;
    return fetch("/api/painel" + caminho, { method: metodo, headers: h, body: corpo !== undefined ? JSON.stringify(corpo) : undefined })
      .then(function (r) {
        if (r.status === 401 && caminho !== "/login") { salvaSessao(null); rota(); throw new Error("Sua sessão expirou. Entre de novo."); }
        return r.json().catch(function () { return null; }).then(function (j) {
          if (!r.ok) throw new Error((j && j.mensagem) || (r.status === 403 ? "Você não tem permissão para isso." : "Erro " + r.status));
          return j;
        });
      });
  }

  // ---------------------------------------------------------------- textos das etapas
  var ETAPAS = {
    entrega: ["Pedido recebido", "Em preparo", "Saiu para entrega", "Entregue"],
    retirada: ["Pedido recebido", "Em preparo", "Pronto para retirada", "Retirado"]
  };
  var COLUNAS = [
    { etapa: -1, nome: "Aguardando pagamento", cor: "#C9A46A" },
    { etapa: 0, nome: "Recebido", cor: "#8A9A6B" },
    { etapa: 1, nome: "Em preparo", cor: "#6E4B33" },
    { etapa: 2, nome: "Saiu / pronto", cor: "#56623F" },
    { etapa: 3, nome: "Concluído", cor: "#3F4A2E" }
  ];
  function acaoAvancar(etapa, modo) {
    if (etapa === 0) return "Começar preparo";
    if (etapa === 1) return modo === "retirada" ? "Pronto p/ retirada" : "Saiu para entrega";
    if (etapa === 2) return modo === "retirada" ? "Confirmar retirada" : "Confirmar entrega";
    return "";
  }
  function pagInfo(metodo, status) {
    if (status === "pendente") return ["Pix pendente", "alerta"];
    if (status === "recusado") return ["Pagamento recusado", "terra"];
    if (status === "na_retirada") return ["Paga na retirada", "terra"];
    return [(metodo === "cartao" ? "Cartão" : metodo === "loja" ? "Pago na loja" : "Pix") + " · pago", "ok"];
  }
  function podeAvancar(etapa, modo) {
    if (etapa < 0 || etapa >= 3) return false;
    if (sessao.papel === "entregador") return modo === "entrega" && etapa >= 1;
    return true;
  }

  // ---------------------------------------------------------------- casca (menu lateral)
  var abertos = 0;
  function casca(atual, conteudo) {
    var itens = [["pedidos", "Pedidos", "#/pedidos"], ["entregas", "Entregas", "#/entregas"]];
    if (sessao.papel !== "entregador") itens.push(["produtos", "Produtos e estoque", "#/produtos"]);
    if (pode(ADMIN)) itens.push(["equipe", "Equipe", "#/equipe"]);
    app.innerHTML = '<div class="casca"><aside class="lado"><img class="marca" src="logo-h-dark.webp" alt="Florisa Floricultura"><nav aria-label="Painel">' +
      itens.map(function (i) {
        return '<a href="' + i[2] + '"' + (i[0] === atual ? ' aria-current="page"' : "") + ">" + ic(i[0]) + i[1] +
          (i[0] === "pedidos" && abertos ? '<span class="cont">' + abertos + "</span>" : "") + "</a>";
      }).join("") + '</nav><div class="quem"><b>' + esc(sessao.nome) + "</b>" + esc(NOME_PAPEL[sessao.papel] || sessao.papel) +
      ' · <button type="button" id="sair">Sair</button></div></aside><main id="main">' + conteudo + "</main></div><div id=\"comanda\"></div>";
    $("#sair").onclick = function () { salvaSessao(null); location.hash = "#/"; rota(); };
  }
  var NOME_PAPEL = { admin: "Administração", atendente: "Atendimento", estoque: "Estoque", entregador: "Entregas" };

  // ---------------------------------------------------------------- login
  function telaLogin() {
    app.innerHTML = '<div class="login"><form id="fLogin" novalidate><img src="logo-h.webp" alt="Florisa Floricultura"><h1>Painel da loja</h1>' +
      '<label class="campo">Usuário<input id="lUsu" autocomplete="username" autocapitalize="none" required></label>' +
      '<label class="campo">Senha<input id="lSen" type="password" autocomplete="current-password" required></label>' +
      '<p class="erro-msg" id="lErro"></p><button class="btn btn-mata" type="submit">Entrar</button></form></div>';
    $("#lUsu").focus();
    $("#fLogin").onsubmit = function (e) {
      e.preventDefault();
      var b = this.querySelector("button"); b.disabled = true; $("#lErro").textContent = "";
      api("POST", "/login", { usuario: $("#lUsu").value.trim(), senha: $("#lSen").value })
        .then(function (r) { salvaSessao({ token: r.token, nome: r.nome, papel: r.papel }); api("GET", "/produtos").then(function (l) { produtos = l; }).catch(function () {}); location.hash = "#/pedidos"; rota(); })
        .catch(function (err) { $("#lErro").textContent = err.message; b.disabled = false; });
    };
  }

  // ---------------------------------------------------------------- fila de pedidos
  var filtro = { dia: "hoje", modo: "", busca: "" };
  var recarga;
  function telaPedidos() {
    if (sessao.papel === "entregador") return telaEntregador();
    var hoje = new Date().toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long", timeZone: FUSO });
    casca("pedidos",
      '<div class="topo"><div><h1>Pedidos</h1><p>' + esc(hoje.charAt(0).toUpperCase() + hoje.slice(1)) + "</p></div>" +
      '<div class="acoes"><label class="busca">' + ic("busca") + '<span class="oculto">Buscar pedido</span><input id="busca" placeholder="Nº do pedido, nome ou telefone" value="' + esc(filtro.busca) + '"></label>' +
      (pode(ATENDE) ? '<button class="btn btn-mata" type="button" id="novo">' + ic("mais") + "Pedido do balcão ou WhatsApp</button>" : "") + "</div></div>" +
      '<div class="kpis" id="kpis"></div>' +
      '<div class="chips" role="group" aria-label="Filtros">' +
      [["hoje", "Hoje"], ["amanha", "Amanhã"], ["abertos", "Todos em aberto"], ["todos", "Histórico"]].map(function (f) {
        return '<button type="button" class="chip" data-dia="' + f[0] + '" aria-pressed="' + (filtro.dia === f[0]) + '">' + f[1] + "</button>";
      }).join("") + '<span style="width:12px"></span>' +
      [["", "Entrega e retirada"], ["entrega", "Só entregas"], ["retirada", "Só retiradas"]].map(function (f) {
        return '<button type="button" class="chip" data-modo="' + f[0] + '" aria-pressed="' + (filtro.modo === f[0]) + '">' + f[1] + "</button>";
      }).join("") + "</div>" +
      '<div class="fila-caixa"><div class="fila" id="fila"><p class="vazio">Carregando…</p></div></div>');
    $$("[data-dia]").forEach(function (b) { b.onclick = function () { filtro.dia = b.dataset.dia; telaPedidos(); }; });
    $$("[data-modo]").forEach(function (b) { b.onclick = function () { filtro.modo = b.dataset.modo; telaPedidos(); }; });
    var tb; $("#busca").oninput = function () { var v = this.value; clearTimeout(tb); tb = setTimeout(function () { filtro.busca = v; carregaFila(); }, 350); };
    if ($("#novo")) $("#novo").onclick = novoPedido;
    carregaFila();
  }

  function carregaFila() {
    clearTimeout(recarga);
    var q = "?dia=" + filtro.dia + (filtro.modo ? "&modo=" + filtro.modo : "") + (filtro.busca ? "&busca=" + encodeURIComponent(filtro.busca) : "");
    return Promise.all([api("GET", "/pedidos" + q), filtro.dia === "abertos" ? null : api("GET", "/pedidos?dia=abertos")]).then(function (r) {
      var lista = r[0], todosAbertos = r[1] || lista;
      abertos = todosAbertos.filter(function (p) { return p.etapa < 3; }).length;
      var cont = $(".lado .cont"); if (cont) cont.textContent = abertos;
      desenhaKpis(lista, todosAbertos);
      desenhaFila(lista);
    }).catch(function (e) { var f = $("#fila"); if (f) f.innerHTML = '<p class="vazio">' + esc(e.message) + "</p>"; })
      .then(function () { if (location.hash.indexOf("#/pedidos") === 0 && !modalAberto()) recarga = setTimeout(carregaFila, 30000); });
  }

  function desenhaKpis(lista, abertosLista) {
    var hoje = hojeISO(0);
    var deHoje = abertosLista.filter(function (p) { return !p.data || p.data <= hoje; });
    var vendido = lista.filter(function (p) { return p.pagamentoStatus === "aprovado" && p.criadoEm && new Date(p.criadoEm).toLocaleDateString("en-CA", { timeZone: FUSO }) === hoje; })
      .reduce(function (a, p) { return a + p.total; }, 0);
    var k = [
      ["Aguardando Pix", abertosLista.filter(function (p) { return p.etapa === -1; }).length, "some sozinho quando o banco confirma"],
      ["Para preparar", deHoje.filter(function (p) { return p.etapa === 0 || p.etapa === 1; }).length, "recebidos e em preparo"],
      ["Entregas hoje", deHoje.filter(function (p) { return p.modo === "entrega" && p.etapa >= 0; }).length, "ainda não entregues"],
      ["Vendido hoje", brl(vendido), filtro.dia === "hoje" ? "pagamentos confirmados" : "na lista abaixo"]
    ];
    $("#kpis").innerHTML = k.map(function (x) { return '<div class="kpi"><span class="rotulo">' + x[0] + '</span><b class="num">' + x[1] + "</b><span>" + x[2] + "</span></div>"; }).join("");
  }

  function cardPedido(p) {
    var pg = pagInfo(p.pagamentoMetodo, p.pagamentoStatus);
    var para = p.modo === "retirada" ? esc(p.destinatarioNome ? "Para " + p.destinatarioNome + " · " : "") + esc(p.clienteNome) + " retira na loja"
      : "Para " + esc(p.destinatarioNome || p.clienteNome) + (p.bairro ? " · " + esc(p.bairro) : "");
    var botao = "";
    if (p.etapa === -1 && pode(ATENDE)) botao = '<button class="btn btn-pq" type="button" data-pagar="' + esc(p.numero) + '">Confirmar Pix</button>';
    else if (podeAvancar(p.etapa, p.modo)) botao = '<button class="btn btn-mata btn-pq" type="button" data-avancar="' + esc(p.numero) + '">' + acaoAvancar(p.etapa, p.modo) + "</button>";
    return '<article class="ped"><div class="l1"><a href="#/pedido/' + encodeURIComponent(p.numero) + '">#' + esc(p.numero) + '</a><span class="hora">' + hora(p.criadoEm) + "</span>" +
      '<span class="etq ' + (p.modo === "retirada" ? "retirada" : "") + '">' + (p.modo === "retirada" ? "Retirada" : "Entrega") + "</span></div>" +
      '<div class="l2">' + fotoTag(p.foto) + '<div><div class="itens">' + esc(p.itens) + '</div><div class="para">' + para + "</div></div></div>" +
      (p.mensagemCartao ? '<div class="bilhete">Cartão: “' + esc(p.mensagemCartao.length > 90 ? p.mensagemCartao.slice(0, 90) + "…" : p.mensagemCartao) + "”</div>" : "") +
      '<div class="l3"><span class="pilula ' + pg[1] + '">' + pg[0] + "</span>" + (p.data ? '<span class="pilula borda">' + esc(dataCurta(p.data)) + "</span>" : "") +
      (p.origem !== "site" ? '<span class="pilula borda">via ' + (p.origem === "balcao" ? "balcão" : "WhatsApp") + "</span>" : "") +
      (p.entregador ? '<span class="pilula borda">' + esc(p.entregador) + "</span>" : "") + "</div>" +
      '<div class="l4"><b class="num">' + brl(p.total) + "</b>" + botao + "</div></article>";
  }

  function desenhaFila(lista) {
    var f = $("#fila"); if (!f) return;
    f.innerHTML = COLUNAS.map(function (c) {
      var ps = lista.filter(function (p) { return p.etapa === c.etapa; });
      return '<section class="coluna" aria-label="' + c.nome + '"><h2><span class="bola" style="background:' + c.cor + '"></span>' + c.nome + "<span>" + ps.length + "</span></h2>" +
        (ps.length ? ps.map(cardPedido).join("") : '<p class="vazio">Nada aqui agora</p>') + "</section>";
    }).join("");
    $$("[data-avancar]", f).forEach(function (b) { b.onclick = function () { avancar(b.dataset.avancar, b).then(carregaFila); }; });
    $$("[data-pagar]", f).forEach(function (b) { b.onclick = function () { confirmarPagamento(b.dataset.pagar, b).then(carregaFila); }; });
  }

  function avancar(numero, botao) {
    if (botao) botao.disabled = true;
    return api("POST", "/pedidos/" + encodeURIComponent(numero) + "/avancar").then(function (r) {
      aviso("#" + numero + ": " + r.pedido.etapaNome + ". O cliente foi avisado.");
      return r;
    }).catch(function (e) { aviso(e.message, true); if (botao) botao.disabled = false; });
  }
  function confirmarPagamento(numero, botao) {
    return confirmar("Confirmar o pagamento do pedido #" + numero + "?",
      "Use quando o Pix caiu na conta e o sistema ainda não marcou, ou quando o cliente pagou na loja.", "Confirmar pagamento")
      .then(function (ok) {
        if (!ok) return null;
        if (botao) botao.disabled = true;
        return api("POST", "/pedidos/" + encodeURIComponent(numero) + "/confirmar-pagamento").then(function (r) { aviso("Pagamento do #" + numero + " confirmado."); return r; })
          .catch(function (e) { aviso(e.message, true); if (botao) botao.disabled = false; });
      });
  }

  // ---------------------------------------------------------------- detalhe do pedido
  var entregadores = null;
  function telaPedido(numero) {
    casca("pedidos", '<p>Carregando…</p>');
    Promise.all([api("GET", "/pedidos/" + encodeURIComponent(numero)), entregadores ? entregadores : api("GET", "/entregadores").catch(function () { return []; })])
      .then(function (r) { entregadores = r[1]; desenhaPedido(r[0]); })
      .catch(function (e) { $("#main").innerHTML = '<a class="btn" href="#/pedidos">' + ic("voltar") + "Voltar</a><p>" + esc(e.message) + "</p>"; });
  }

  function desenhaPedido(d) {
    var p = d.pedido, e = p.entrega || {}, end = e.endereco, pg = pagInfo(p.pagamento.metodo, p.pagamento.status);
    var nomes = ETAPAS[e.modo] || ETAPAS.entrega;
    var quando = {};
    (d.historico || []).forEach(function (h) { nomes.forEach(function (n, i) { if (h.descricao === n) quando[i] = h; }); });
    var pagoEv = (d.historico || []).filter(function (h) { return /Pagamento confirmado|Cartão aprovado/.test(h.descricao); })[0];
    if (pagoEv && !quando[0]) quando[0] = pagoEv;
    if (!quando[0] && p.etapa >= 0) quando[0] = d.historico[0];

    var linha = '<ol class="linha-tempo">' + [["Aguardando pagamento", -1]].concat(nomes.map(function (n, i) { return [n, i]; })).map(function (x) {
      var i = x[1], estado = p.etapa > i ? "feita" : p.etapa === i ? (i === 3 ? "feita" : "atual") : "falta";
      if (i === -1 && p.etapa === -1) estado = "atual";
      var info = i === -1 ? (p.etapa === -1 ? "Esperando o banco confirmar" : "Pago") : quando[i] ? hora(quando[i].quando) + " · " + esc(quando[i].autor) : estado === "falta" && i === p.etapa + 1 ? "Próxima etapa" : "";
      return '<li class="' + estado + '"><span class="marco">' + (estado === "feita" ? ic("check") : "") + "</span><span><strong>" + x[0] + "</strong><small>" + info + "</small></span></li>";
    }).join("") + "</ol>";

    var botaoPrincipal = p.etapa === -1 && pode(ATENDE) ? '<button class="btn btn-mata" type="button" id="dPagar">Confirmar pagamento</button>'
      : podeAvancar(p.etapa, e.modo) ? '<button class="btn btn-mata" type="button" id="dAvancar">' + acaoAvancar(p.etapa, e.modo) + ic("seta") + "</button>" : "";
    var whats = "https://wa.me/55" + p.cliente.tel;
    var mapa = end ? "https://www.google.com/maps/search/?api=1&query=" + encodeURIComponent(end.rua + ", " + end.numero + " - " + end.bairro + ", Jaraguá do Sul - SC") : "";

    $("#main").innerHTML =
      '<a class="link" href="#/pedidos" style="align-self:flex-start;text-decoration:none;display:inline-flex;gap:6px;align-items:center;min-height:36px">' + ic("voltar") + "Voltar para a fila</a>" +
      '<div class="topo"><div><div class="acoes"><h1>Pedido #' + esc(p.numero) + '</h1><span class="pilula terra">' + esc(p.etapaNome) + '</span><span class="etq ' + (e.modo === "retirada" ? "retirada" : "") + '">' + (e.modo === "retirada" ? "Retirada" : "Entrega") + "</span></div>" +
      "<p>Feito " + (p.origem === "site" ? "no site" : p.origem === "balcao" ? "no balcão" : "pelo WhatsApp") + " em " + dataHora(p.criadoEm) + (e.data ? " · " + (e.modo === "retirada" ? "retirada" : "entrega") + " <strong>" + esc(dataCurta(e.data)) + "</strong>" : "") + "</p></div>" +
      '<div class="acoes"><button class="btn" type="button" id="dImprimir">' + ic("imprimir") + 'Imprimir comanda</button><a class="btn" href="' + whats + '" target="_blank" rel="noopener">' + ic("zap") + "WhatsApp do cliente</a>" + botaoPrincipal + "</div></div>" +
      '<div class="det"><div class="principal">' +
      '<section class="cartao"><h2>Itens</h2>' + p.itens.map(function (i) {
        var pr = produtos ? produtos.filter(function (x) { return x.id === i.id; })[0] : null;
        return '<div class="item">' + fotoTag(pr && pr.imagem) + "<div><strong>" + esc(i.nome) + "</strong><br><small>" + i.quantidade + " × " + brl(i.precoUnitario) +
          (i.extras && i.extras.length ? " · com " + esc(i.extras.map(nomeExtra).join(", ")) : "") + " · código " + esc(i.id) + '</small></div><strong class="num">' + brl(i.total) + "</strong></div>";
      }).join("") +
      '<div class="soma num"><div><span>Subtotal</span><span>' + brl(p.subtotal) + "</span></div>" +
      (e.modo === "entrega" ? "<div><span>Entrega" + (end ? " · " + esc(end.bairro) : "") + "</span><span>" + brl(p.frete) + "</span></div>" : "") +
      '<div class="total"><span>Total</span><span>' + brl(p.total) + "</span></div></div></section>" +
      (p.mensagemCartao || p.observacoes ? '<section class="cartao msg-cartao"><div class="acoes" style="justify-content:space-between"><h2>Mensagem do cartão</h2><span class="rotulo" style="color:var(--terra)">Escrever à mão</span></div>' +
        (p.mensagemCartao ? "<p>“" + esc(p.mensagemCartao) + "”</p>" : '<span>Sem mensagem.</span>') +
        (p.observacoes ? '<span style="display:block;margin-top:10px">Instruções do cliente: ' + esc(p.observacoes) + "</span>" : "") + "</section>" : "") +
      '<div class="duas"><section class="cartao"><h2>' + (e.modo === "retirada" ? "Retirada" : "Entrega") + "</h2>" +
      (p.destinatario ? "<p style=\"margin:0 0 8px\"><strong>" + esc(p.destinatario.nome) + "</strong> (é presente)" + (p.destinatario.tel ? "<br>" + esc(telFmt(p.destinatario.tel)) : "") + "</p>" : "") +
      (end ? '<p style="margin:0 0 8px">' + esc(end.rua) + ", " + esc(end.numero) + (end.complemento ? " · " + esc(end.complemento) : "") + "<br>" + esc(end.bairro) + " · " + esc(end.cep) + '</p><a href="' + mapa + '" target="_blank" rel="noopener">Abrir rota no mapa</a>'
        : "<p style=\"margin:0\">Retira na loja: R. Bernardo Dornbusch, 2433.</p>") +
      (e.modo === "entrega" && pode(ATENDE) ? '<label class="campo" style="margin-top:12px">Entregador<input id="dEntregador" list="listaEntregadores" value="' + esc(d.entregador || "") + '" placeholder="Quem leva"><datalist id="listaEntregadores">' +
        (entregadores || []).map(function (n) { return '<option value="' + esc(n) + '">'; }).join("") + "</datalist></label>" : (d.entregador ? "<p>Entregador: " + esc(d.entregador) + "</p>" : "")) +
      "</section>" +
      '<section class="cartao"><h2>Cliente e pagamento</h2><p style="margin:0 0 8px"><strong>' + esc(p.cliente.nome) + "</strong><br>" + esc(telFmt(p.cliente.tel)) + (p.cliente.email ? " · " + esc(p.cliente.email) : "") + "</p>" +
      '<p style="margin:0"><span class="pilula ' + pg[1] + '">' + pg[0] + "</span> " + (p.pagamento.metodo === "cartao" ? p.pagamento.parcelas + "x" + (p.pagamento.cartaoFinal ? " · final " + esc(p.pagamento.cartaoFinal) : "") : "") + "</p>" +
      (p.pagamento.id ? '<p style="font-size:13px;color:var(--suave)">Código no provedor: ' + esc(p.pagamento.id) + "</p>" : "") + "</section></div></div>" +
      '<div class="lateral"><section class="cartao"><h2>Andamento</h2>' + linha +
      '<p style="margin:0;font-size:13px;background:var(--creme);border-radius:10px;padding:10px 12px">Cada etapa avisa o cliente pelo WhatsApp e aparece em “Meus pedidos” no site.</p></section>' +
      (pode(ATENDE) ? '<section class="cartao"><h2>Anotação da equipe</h2><label class="campo"><span class="oculto">Anotação interna</span><textarea id="dNota" placeholder="Só a equipe vê. Ex.: portaria fecha às 17h">' + esc(d.notaInterna || "") + '</textarea></label><button class="btn btn-pq" type="button" id="dSalvarNota" style="margin-top:10px">Salvar anotação</button></section>' : "") +
      '<section class="cartao"><h2>Histórico</h2><div class="historico">' + (d.historico || []).slice().reverse().map(function (h) {
        return "<div><strong>" + dataHora(h.quando) + "</strong> · " + esc(h.descricao) + ' <span style="color:var(--suave)">(' + esc(h.autor) + ")</span></div>";
      }).join("") + "</div></section></div></div>";

    $("#comanda").innerHTML = "<h1>Florisa · Pedido #" + esc(p.numero) + "</h1><p>" + (e.modo === "retirada" ? "RETIRADA" : "ENTREGA") + (e.data ? " · " + esc(dataCurta(e.data)) : "") + "</p>" +
      "<ul>" + p.itens.map(function (i) { return "<li>" + i.quantidade + "× " + esc(i.nome) + (i.extras && i.extras.length ? " + " + esc(i.extras.map(nomeExtra).join(", ")) : "") + "</li>"; }).join("") + "</ul>" +
      (p.mensagemCartao ? '<div class="bilhete-impresso">' + esc(p.mensagemCartao) + "</div>" : "") +
      (p.observacoes ? "<p>Obs.: " + esc(p.observacoes) + "</p>" : "") +
      (end ? "<p>Para: " + esc(p.destinatario ? p.destinatario.nome : p.cliente.nome) + "<br>" + esc(end.rua) + ", " + esc(end.numero) + " " + esc(end.complemento || "") + " · " + esc(end.bairro) + "<br>Tel.: " + esc(telFmt(p.destinatario && p.destinatario.tel || p.cliente.tel)) + "</p>" : "<p>Cliente: " + esc(p.cliente.nome) + " · " + esc(telFmt(p.cliente.tel)) + "</p>") +
      (d.notaInterna ? "<p>Equipe: " + esc(d.notaInterna) + "</p>" : "");

    $("#dImprimir").onclick = function () { window.print(); };
    if ($("#dAvancar")) $("#dAvancar").onclick = function () { avancar(p.numero, this).then(function (r) { if (r) desenhaPedido(r); }); };
    if ($("#dPagar")) $("#dPagar").onclick = function () { confirmarPagamento(p.numero, this).then(function (r) { if (r) desenhaPedido(r); }); };
    if ($("#dSalvarNota")) $("#dSalvarNota").onclick = function () {
      api("PATCH", "/pedidos/" + encodeURIComponent(p.numero), { notaInterna: $("#dNota").value }).then(function () { aviso("Anotação salva."); }).catch(function (er) { aviso(er.message, true); });
    };
    var ent = $("#dEntregador");
    if (ent) ent.onchange = function () {
      api("PATCH", "/pedidos/" + encodeURIComponent(p.numero), { entregador: ent.value }).then(function (r) { aviso("Entregador: " + (ent.value || "nenhum")); desenhaPedido(r); }).catch(function (er) { aviso(er.message, true); });
    };
  }

  var EXTRAS = { bombom: "caixa de bombons", pelucia: "urso de pelúcia", balao: "balão" };
  function nomeExtra(id) { return EXTRAS[id] || id; }

  // ---------------------------------------------------------------- entregas
  var taxas = null;
  function telaEntregas() {
    if (sessao.papel === "entregador") return telaEntregador();
    casca("entregas", '<div class="topo"><div><h1>Entregas</h1><p id="eResumo">Carregando…</p></div></div><div class="det"><div class="principal" id="eLista"></div><div class="lateral" id="eTaxas"></div></div>');
    Promise.all([api("GET", "/pedidos?dia=abertos&modo=entrega"), api("GET", "/taxas"), api("GET", "/entregadores").catch(function () { return []; })]).then(function (r) {
      entregadores = r[2]; taxas = r[1];
      var hoje = hojeISO(0), amanha = hojeISO(1);
      var lista = r[0].filter(function (p) { return p.etapa >= 0; });
      var deHoje = lista.filter(function (p) { return !p.data || p.data <= hoje; });
      var deAmanha = lista.filter(function (p) { return p.data === amanha; }).length;
      $("#eResumo").textContent = deHoje.length + (deHoje.length === 1 ? " entrega para hoje" : " entregas para hoje") + (deAmanha ? " · " + deAmanha + " amanhã" : "");
      var grupos = {};
      deHoje.forEach(function (p) { (grupos[p.bairro || "Sem bairro"] = grupos[p.bairro || "Sem bairro"] || []).push(p); });
      var nomes = Object.keys(grupos).sort();
      $("#eLista").innerHTML = nomes.length ? nomes.map(function (b) {
        return '<section class="cartao"><h2>' + esc(b) + ' <small style="font:500 14px var(--sans);color:var(--suave)">' + grupos[b].length + "</small></h2>" + grupos[b].map(function (p) {
          return '<div class="item" style="flex-wrap:wrap"><a href="#/pedido/' + encodeURIComponent(p.numero) + '" style="font:600 17px var(--serif);text-decoration:none;min-width:56px">#' + esc(p.numero) + "</a>" +
            '<div style="flex:1 1 200px"><strong>' + esc(p.destinatarioNome || p.clienteNome) + "</strong><br><small>" + esc(p.itens) + "</small></div>" +
            '<span class="pilula ' + (p.etapa === 2 ? "terra" : "alerta") + '">' + esc(p.etapaNome) + "</span>" +
            (pode(ATENDE) ? '<label><span class="oculto">Entregador do pedido ' + esc(p.numero) + '</span><input type="text" list="listaEnt" data-entregador="' + esc(p.numero) + '" value="' + esc(p.entregador || "") + '" placeholder="Entregador" style="min-height:40px;border-radius:10px;border:1px solid var(--linha-2);padding:0 10px;width:150px"></label>' : "") + "</div>";
        }).join("") + "</section>";
      }).join("") + '<datalist id="listaEnt">' + (entregadores || []).map(function (n) { return '<option value="' + esc(n) + '">'; }).join("") + "</datalist>"
        : '<section class="cartao"><p style="margin:0">Nenhuma entrega aberta para hoje.</p></section>';
      $$("[data-entregador]").forEach(function (i) {
        i.onchange = function () { api("PATCH", "/pedidos/" + encodeURIComponent(i.dataset.entregador), { entregador: i.value }).then(function () { aviso("Entregador salvo."); }).catch(function (er) { aviso(er.message, true); }); };
      });
      desenhaTaxas();
    }).catch(function (e) { $("#eResumo").textContent = e.message; });
  }

  function desenhaTaxas() {
    var edita = pode(ADMIN);
    $("#eTaxas").innerHTML = '<section class="cartao"><h2>Taxa por bairro</h2>' +
      '<p class="aviso-alerta">Valores de exemplo do site: confirmar com a loja</p>' +
      '<div class="tabela-caixa"><table><thead><tr><th>Bairro ou cidade</th><th>Taxa (R$)</th>' + (edita ? "<th>Ativo</th>" : "") + "</tr></thead><tbody>" +
      taxas.map(function (t, i) {
        return "<tr><td>" + (edita ? '<input type="text" data-tb="' + i + '" value="' + esc(t.bairro) + '" style="width:100%;text-align:left" aria-label="Nome do bairro">' : esc(t.bairro)) + "</td>" +
          '<td>' + (edita ? '<input type="number" min="0" step="1" data-tv="' + i + '" value="' + t.valor + '" aria-label="Taxa ' + esc(t.bairro) + '">' : brl(t.valor)) + "</td>" +
          (edita ? '<td><input type="checkbox" data-ta="' + i + '"' + (t.ativo ? " checked" : "") + ' aria-label="Entregar em ' + esc(t.bairro) + '"></td>' : "") + "</tr>";
      }).join("") + "</tbody></table></div>" +
      (edita ? '<div class="acoes" style="margin-top:12px"><button class="btn btn-pq" type="button" id="tNovo">' + ic("mais") + 'Bairro</button><button class="btn btn-mata btn-pq" type="button" id="tSalvar">Salvar taxas</button></div>' : "") + "</section>";
    if (!edita) return;
    $("#tNovo").onclick = function () { lerTaxas(); taxas.push({ bairro: "", valor: 0, ativo: true }); desenhaTaxas(); var u = $$("[data-tb]"); u[u.length - 1].focus(); };
    $("#tSalvar").onclick = function () {
      lerTaxas(); var b = this; b.disabled = true;
      api("PUT", "/taxas", taxas.filter(function (t) { return t.bairro.trim(); })).then(function (r) { taxas = r; aviso("Taxas salvas. O site já usa os valores novos."); desenhaTaxas(); })
        .catch(function (e) { aviso(e.message, true); b.disabled = false; });
    };
  }
  function lerTaxas() {
    taxas = taxas.map(function (t, i) {
      return { bairro: $('[data-tb="' + i + '"]').value.trim(), valor: Number($('[data-tv="' + i + '"]').value || 0), ativo: $('[data-ta="' + i + '"]').checked };
    });
  }

  // ---------------------------------------------------------------- entregador (celular)
  function telaEntregador() {
    casca(location.hash.indexOf("entregas") >= 0 ? "entregas" : "pedidos", '<div class="topo"><div><h1>Suas entregas</h1><p id="enResumo">Carregando…</p></div></div><div class="entregas-lista" id="enLista"></div>');
    api("GET", "/pedidos?dia=hoje&modo=entrega").then(function (lista) {
      var minhas = lista.filter(function (p) { return p.etapa >= 1 && (!p.entregador || p.entregador === sessao.nome); });
      var feitas = minhas.filter(function (p) { return p.etapa === 3; }).length;
      $("#enResumo").textContent = feitas + " de " + minhas.length + " entregues hoje";
      $("#enLista").innerHTML = minhas.length ? minhas.map(function (p) {
        var estado = p.etapa === 1 ? ["Em preparo na loja", "alerta"] : p.etapa === 2 ? ["A caminho", "terra"] : ["Entregue", "ok"];
        return '<article class="cartao entrega"' + (p.etapa === 3 ? ' style="opacity:.7"' : "") + '><div class="acoes" style="justify-content:space-between"><h2 style="margin:0">#' + esc(p.numero) + '</h2><span class="pilula ' + estado[1] + '">' + estado[0] + "</span></div>" +
          "<div><strong>" + esc(p.destinatarioNome || p.clienteNome) + "</strong><br>" + esc(p.bairro || "") + "<br><small>" + esc(p.itens) + "</small></div>" +
          (p.etapa < 3 ? '<div class="acoes"><a class="btn" href="#/pedido/' + encodeURIComponent(p.numero) + '">' + ic("mapa") + "Endereço</a>" +
            '<button class="btn btn-mata" type="button" data-avancar="' + esc(p.numero) + '">' + (p.etapa === 1 ? "Saí com o pedido" : "Entreguei") + "</button></div>" : "") + "</article>";
      }).join("") : '<section class="cartao"><p style="margin:0">Nenhuma entrega com você agora.</p></section>';
      $$("[data-avancar]").forEach(function (b) { b.onclick = function () { avancar(b.dataset.avancar, b).then(telaEntregador); }; });
    }).catch(function (e) { $("#enResumo").textContent = e.message; });
  }

  // ---------------------------------------------------------------- produtos
  var produtos = null, catProd = "", buscaProd = "";
  var CATS = { rosas: "Rosas e amor", lirios: "Lírios", aniversario: "Aniversário", cafe: "Café da manhã", plantas: "Orquídeas e plantas", mimos: "Mimos e presentes", condolencias: "Condolências" };
  function telaProdutos() {
    casca("produtos", '<div class="topo"><div><h1>Produtos e estoque</h1><p>O que você muda aqui vale na loja na hora.</p></div><div class="acoes">' +
      '<label class="busca">' + ic("busca") + '<span class="oculto">Buscar produto</span><input id="pBusca" placeholder="Buscar produto ou código" value="' + esc(buscaProd) + '"></label>' +
      (pode(ESTOQUE) ? '<button class="btn btn-mata" type="button" id="pNovo">' + ic("mais") + "Novo produto</button>" : "") + '</div></div><div class="chips" id="pCats"></div>' +
      '<section class="cartao" style="padding:0;overflow:hidden"><div class="tabela-caixa"><table style="min-width:860px"><thead><tr><th style="padding-left:16px">Produto</th><th>Ocasião</th><th>Preço (R$)</th><th>Preço “de”</th><th>Selo</th><th>Tem hoje</th><th>No site</th></tr></thead><tbody id="pLinhas"><tr><td colspan="7" style="padding:16px">Carregando…</td></tr></tbody></table></div></section>');
    var tb; $("#pBusca").oninput = function () { var v = this.value; clearTimeout(tb); tb = setTimeout(function () { buscaProd = v; desenhaProdutos(); }, 200); };
    if ($("#pNovo")) $("#pNovo").onclick = novoProduto;
    api("GET", "/produtos").then(function (l) { produtos = l; desenhaProdutos(); }).catch(function (e) { $("#pLinhas").innerHTML = '<tr><td colspan="7">' + esc(e.message) + "</td></tr>"; });
  }
  function desenhaProdutos() {
    var edita = pode(ESTOQUE);
    var cats = [""].concat(Object.keys(CATS));
    $("#pCats").innerHTML = cats.map(function (c) {
      var n = produtos.filter(function (p) { return !c || p.categoria === c; }).length;
      return '<button type="button" class="chip" data-cat="' + c + '" aria-pressed="' + (catProd === c) + '">' + (c ? CATS[c] : "Todas") + ' <span style="opacity:.7">' + n + "</span></button>";
    }).join("");
    $$("[data-cat]").forEach(function (b) { b.onclick = function () { catProd = b.dataset.cat; desenhaProdutos(); }; });
    var q = buscaProd.trim().toLowerCase();
    var lista = produtos.filter(function (p) { return (!catProd || p.categoria === catProd) && (!q || p.nome.toLowerCase().indexOf(q) >= 0 || p.id.indexOf(q) >= 0); });
    $("#pLinhas").innerHTML = lista.map(function (p) {
      var dis = edita ? "" : " disabled";
      return '<tr data-id="' + esc(p.id) + '"><td style="padding-left:16px"><div class="prod-nome">' + fotoTag(p.imagem) + "<div>" + esc(p.nome) + "<small>" + esc(p.id) + (p.exemplo ? " · exemplo, confirmar" : "") + (p.antecedenciaDias ? " · pedir " + p.antecedenciaDias + " dia antes" : "") + "</small></div></div></td>" +
        "<td>" + esc(CATS[p.categoria] || p.categoria) + "</td>" +
        '<td><input type="number" min="1" step="1" data-campo="preco" value="' + p.preco + '" aria-label="Preço de ' + esc(p.nome) + '"' + dis + "></td>" +
        '<td><input type="number" min="0" step="1" data-campo="precoDe" value="' + (p.precoDe || "") + '" placeholder="—" aria-label="Preço antigo de ' + esc(p.nome) + '"' + dis + "></td>" +
        '<td><input type="text" data-campo="selo" value="' + esc(p.selo || "") + '" placeholder="—" aria-label="Selo de ' + esc(p.nome) + '"' + dis + "></td>" +
        '<td><label class="marca"><input type="checkbox" data-campo="disponivel"' + (p.disponivel ? " checked" : "") + dis + '><span>' + (p.disponivel ? "Sim" : "Esgotado") + "</span></label></td>" +
        '<td><label class="marca"><input type="checkbox" data-campo="ativo"' + (p.ativo ? " checked" : "") + dis + '><span>' + (p.ativo ? "Visível" : "Oculto") + "</span></label></td></tr>";
    }).join("") || '<tr><td colspan="7" style="padding:16px">Nenhum produto encontrado.</td></tr>';
    if (!edita) return;
    $$("#pLinhas [data-campo]").forEach(function (i) {
      i.onchange = function () {
        var tr = i.closest("tr"), id = tr.dataset.id, campo = i.dataset.campo, corpo = {};
        corpo[campo] = i.type === "checkbox" ? i.checked : campo === "selo" ? i.value : Number(i.value || 0);
        tr.classList.add("salvando");
        api("PUT", "/produtos/" + encodeURIComponent(id), corpo).then(function (p) {
          produtos = produtos.map(function (x) { return x.id === id ? p : x; });
          aviso(p.nome + ": salvo."); desenhaProdutos();
        }).catch(function (e) { aviso(e.message, true); tr.classList.remove("salvando"); });
      };
    });
  }
  function novoProduto() {
    abreModal('<h2>Novo produto</h2><form id="fProd" class="grade-campos" novalidate>' +
      '<label class="campo">Código<input id="npId" placeholder="ex.: rn30" required pattern="[a-z0-9-]{2,40}"></label>' +
      '<label class="campo" style="grid-column:span 2">Nome<input id="npNome" required></label>' +
      '<label class="campo">Ocasião<select id="npCat">' + Object.keys(CATS).map(function (c) { return '<option value="' + c + '">' + CATS[c] + "</option>"; }).join("") + "</select></label>" +
      '<label class="campo">Preço (R$)<input id="npPreco" type="number" min="1" required></label>' +
      '<label class="campo" style="grid-column:1/-1">Descrição<textarea id="npDesc"></textarea></label>' +
      '<p class="erro-msg" id="npErro" style="grid-column:1/-1"></p><div class="acoes" style="grid-column:1/-1;justify-content:flex-end"><button class="btn" type="button" data-fechar>Cancelar</button><button class="btn btn-mata" type="submit">Criar produto</button></div></form>');
    $("#fProd").onsubmit = function (e) {
      e.preventDefault();
      var id = $("#npId").value.trim().toLowerCase();
      api("PUT", "/produtos/" + encodeURIComponent(id), { nome: $("#npNome").value, categoria: $("#npCat").value, preco: Number($("#npPreco").value || 0), descricao: $("#npDesc").value })
        .then(function (p) { fechaModal(); aviso(p.nome + " criado. A foto entra pelo site."); telaProdutos(); })
        .catch(function (er) { $("#npErro").textContent = er.message; });
    };
  }

  // ---------------------------------------------------------------- equipe
  function telaEquipe() {
    casca("equipe", '<div class="topo"><div><h1>Equipe</h1><p>Quem entra no painel e o que cada um pode fazer.</p></div></div>' +
      '<div class="det"><div class="principal"><section class="cartao"><div class="tabela-caixa"><table><thead><tr><th>Nome</th><th>Usuário</th><th>Papel</th></tr></thead><tbody id="eqLinhas"><tr><td colspan="3">Carregando…</td></tr></tbody></table></div></section></div>' +
      '<div class="lateral"><section class="cartao"><h2>Adicionar pessoa</h2><form id="fEq" style="display:flex;flex-direction:column;gap:12px" novalidate>' +
      '<label class="campo">Nome<input id="eqNome" required></label><label class="campo">Usuário (para entrar)<input id="eqUsu" autocapitalize="none" required></label>' +
      '<label class="campo">Senha (mínimo 8 caracteres)<input id="eqSen" type="password" autocomplete="new-password" required></label>' +
      '<label class="campo">Papel<select id="eqPapel"><option value="atendente">Atendimento: pedidos e pagamentos</option><option value="estoque">Estoque: produtos e preços</option><option value="entregador">Entregas: só marca saída e entrega</option><option value="admin">Administração: tudo</option></select></label>' +
      '<p class="erro-msg" id="eqErro"></p><button class="btn btn-mata" type="submit">Adicionar</button></form></section></div></div>');
    var carrega = function () {
      api("GET", "/equipe").then(function (l) {
        $("#eqLinhas").innerHTML = l.map(function (m) { return "<tr><td>" + esc(m.nome) + "</td><td>" + esc(m.usuario) + "</td><td>" + esc(NOME_PAPEL[m.papel] || m.papel) + (m.ativo ? "" : " · inativo") + "</td></tr>"; }).join("");
      }).catch(function (e) { $("#eqLinhas").innerHTML = '<tr><td colspan="3">' + esc(e.message) + "</td></tr>"; });
    };
    carrega();
    $("#fEq").onsubmit = function (e) {
      e.preventDefault(); $("#eqErro").textContent = "";
      api("POST", "/equipe", { nome: $("#eqNome").value, usuario: $("#eqUsu").value, senha: $("#eqSen").value, papel: $("#eqPapel").value })
        .then(function (m) { aviso(m.nome + " já pode entrar no painel."); this.reset(); entregadores = null; carrega(); }.bind(this))
        .catch(function (er) { $("#eqErro").textContent = er.message; });
    };
  }

  // ---------------------------------------------------------------- pedido do balcão / WhatsApp
  function novoPedido() {
    Promise.all([produtos ? produtos : api("GET", "/produtos"), taxas ? taxas : api("GET", "/taxas")]).then(function (r) {
      produtos = r[0]; taxas = r[1];
      var opcoes = produtos.filter(function (p) { return p.ativo; }).map(function (p) { return '<option value="' + esc(p.id) + '">' + esc(p.nome) + " · " + brl(p.preco) + (p.disponivel ? "" : " (esgotado)") + "</option>"; }).join("");
      abreModal('<h2>Pedido do balcão ou WhatsApp</h2><form id="fNovo" novalidate style="display:flex;flex-direction:column;gap:14px">' +
        '<div class="chips"><label class="marca"><input type="radio" name="origem" value="whatsapp" checked> WhatsApp</label><label class="marca"><input type="radio" name="origem" value="balcao"> Balcão</label></div>' +
        '<div class="grade-campos"><label class="campo">Nome do cliente<input id="nCli" required></label><label class="campo">WhatsApp do cliente<input id="nTel" type="tel" inputmode="tel" required placeholder="(47) 9 0000-0000"></label></div>' +
        '<div class="grade-campos"><label class="campo" style="grid-column:span 2">Produto<select id="nProd">' + opcoes + '</select></label><label class="campo">Quantidade<input id="nQtd" type="number" min="1" max="20" value="1"></label></div>' +
        '<div class="chips"><label class="marca"><input type="radio" name="nModo" value="retirada" checked> Retira na loja</label><label class="marca"><input type="radio" name="nModo" value="entrega"> Entrega</label></div>' +
        '<div id="nEntrega" hidden><div class="grade-campos"><label class="campo">Quem recebe<input id="nDest"></label><label class="campo">Bairro<select id="nBairro">' +
        taxas.filter(function (t) { return t.ativo; }).map(function (t) { return '<option value="' + esc(t.bairro) + '">' + esc(t.bairro) + " · " + brl(t.valor) + "</option>"; }).join("") + "</select></label>" +
        '<label class="campo">Rua<input id="nRua"></label><label class="campo">Número<input id="nNum"></label><label class="campo">Complemento<input id="nCompl"></label><label class="campo">CEP<input id="nCep" inputmode="numeric" placeholder="89250-000"></label></div></div>' +
        '<label class="campo">Data<input id="nData" type="date" min="' + hojeISO(0) + '" value="' + hojeISO(0) + '"></label>' +
        '<label class="campo">Mensagem do cartão<textarea id="nMsg" placeholder="Opcional"></textarea></label>' +
        '<div class="chips"><label class="marca"><input type="radio" name="nPag" value="pago" checked> Já pagou (Pix ou na loja)</label><label class="marca"><input type="radio" name="nPag" value="pix"> Mandar Pix para pagar</label><label class="marca"><input type="radio" name="nPag" value="loja"> Paga na retirada</label></div>' +
        '<p class="erro-msg" id="nErro"></p><div class="acoes" style="justify-content:flex-end"><button class="btn" type="button" data-fechar>Cancelar</button><button class="btn btn-mata" type="submit">Lançar pedido</button></div></form>');
      var modoSel = function () { return $("[name=nModo]:checked").value; };
      $$("[name=nModo]").forEach(function (r) { r.onchange = function () { $("#nEntrega").hidden = modoSel() !== "entrega"; }; });
      $("#fNovo").onsubmit = function (e) {
        e.preventDefault(); $("#nErro").textContent = "";
        var modo = modoSel(), pag = $("[name=nPag]:checked").value;
        if (pag === "loja" && modo !== "retirada") { $("#nErro").textContent = "Pagar na retirada só vale para retirada."; return; }
        var corpo = {
          origem: $("[name=origem]:checked").value,
          jaPago: pag === "pago",
          pedido: {
            cliente: { nome: $("#nCli").value, tel: $("#nTel").value },
            destinatario: modo === "entrega" && $("#nDest").value.trim() ? { nome: $("#nDest").value, tel: null } : null,
            entrega: { modo: modo, data: $("#nData").value || null, endereco: modo === "entrega" ? { cep: $("#nCep").value, rua: $("#nRua").value, numero: $("#nNum").value, complemento: $("#nCompl").value, bairro: $("#nBairro").value } : null },
            itens: [{ id: $("#nProd").value, quantidade: Number($("#nQtd").value || 1), extras: [] }],
            pagamento: { metodo: pag === "pix" ? "pix" : pag === "loja" ? "loja" : (modo === "retirada" ? "loja" : "pix") },
            mensagemCartao: $("#nMsg").value
          }
        };
        var b = this.querySelector("[type=submit]"); b.disabled = true;
        api("POST", "/pedidos", corpo).then(function (r) { fechaModal(); aviso("Pedido #" + r.pedido.numero + " lançado."); location.hash = "#/pedido/" + r.pedido.numero; })
          .catch(function (er) { $("#nErro").textContent = er.message; b.disabled = false; });
      };
    }).catch(function (e) { aviso(e.message, true); });
  }

  // ---------------------------------------------------------------- modal e confirmação
  var modal = $("#modal"), caixa = $("#modalCaixa"), focoAntes = null;
  function modalAberto() { return !modal.hidden; }
  function abreModal(html) {
    focoAntes = document.activeElement; caixa.innerHTML = html; modal.hidden = false;
    $$("[data-fechar]", caixa).forEach(function (b) { b.onclick = fechaModal; });
    var f = caixa.querySelector("input, select, textarea, button"); if (f) f.focus();
  }
  function fechaModal() { modal.hidden = true; caixa.innerHTML = ""; if (focoAntes && focoAntes.focus) focoAntes.focus(); if (location.hash.indexOf("#/pedidos") === 0) carregaFila(); }
  modal.addEventListener("click", function (e) { if (e.target === modal) fechaModal(); });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape" && modalAberto()) fechaModal(); });
  function confirmar(titulo, texto, botao) {
    return new Promise(function (ok) {
      abreModal("<h2>" + esc(titulo) + "</h2><p style=\"margin:0\">" + esc(texto) + '</p><div class="acoes" style="justify-content:flex-end"><button class="btn" type="button" id="cNao">Cancelar</button><button class="btn btn-mata" type="button" id="cSim">' + esc(botao) + "</button></div>");
      $("#cNao").onclick = function () { modal.hidden = true; ok(false); };
      $("#cSim").onclick = function () { modal.hidden = true; ok(true); };
    });
  }

  // ---------------------------------------------------------------- rotas
  function rota() {
    clearTimeout(recarga);
    if (!sessao) return telaLogin();
    var h = location.hash || "#/pedidos";
    var m = /^#\/pedido\/(.+)$/.exec(h);
    if (m) return telaPedido(decodeURIComponent(m[1]));
    if (h.indexOf("#/entregas") === 0) return telaEntregas();
    if (h.indexOf("#/produtos") === 0 && sessao.papel !== "entregador") return telaProdutos();
    if (h.indexOf("#/equipe") === 0 && pode(ADMIN)) return telaEquipe();
    return telaPedidos();
  }
  window.addEventListener("hashchange", rota);
  // Carrega os produtos em segundo plano (fotos no detalhe do pedido).
  if (sessao) api("GET", "/produtos").then(function (l) { produtos = l; }).catch(function () {});
  rota();
})();
