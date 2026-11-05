# Florisa v8 — integração da loja com o sistema de pedidos e pagamento

A loja (catálogo, carrinho, checkout, pagamento, confirmação, acompanhamento e login) está completa no front.
Todo contato com o servidor passa pelo objeto `API` dentro de `Florisa v8.html`. Nada mais precisa mudar.

## Como ligar

```js
API.modo = "real";                                   // hoje: "simulado"
API.base = "https://pedidos.floresflorisa.com.br/api"; // seu servidor
```

E troque o corpo de `API.tokenizarCartao` pelo SDK do gateway escolhido (Mercado Pago, PagBank, Asaas, Stripe...).

Regras de segurança:
- A chave secreta do gateway fica só no servidor.
- O número do cartão nunca vai para o seu servidor: o SDK gera um token no navegador e só o token é enviado.
- O servidor recalcula preços, frete e total a partir dos `id` dos itens. Nunca confie no `total` que veio do navegador.
- Se o site e a API ficarem em domínios diferentes, libere CORS para o domínio do site.

## Endpoints

Todas as respostas em JSON. Erro: status HTTP 4xx/5xx com `{ "mensagem": "texto para o cliente" }`.
Pedido não encontrado: `404`. Com login, o front envia `Authorization: Bearer <token>`.

### POST /frete
Entrada: `{ "bairro": "Centro", "cep": "89250000", "subtotal": 190 }`
Saída: `{ "valor": 12, "prazo": "No dia escolhido" }`

### POST /pedidos
Entrada:
```json
{
  "origem": "site",
  "cliente": { "nome": "Ana", "tel": "47999999999", "email": "ana@email.com" },
  "destinatario": { "nome": "Maria", "tel": "47988887777" },
  "entrega": {
    "modo": "entrega",
    "data": "2026-11-07",
    "taxa": 12,
    "endereco": { "cep": "89250000", "rua": "Rua A", "numero": "10", "complemento": "", "bairro": "Centro" }
  },
  "itens": [
    { "id": "rn21", "nome": "Bouquet com 12 rosas", "quantidade": 1, "precoUnitario": 190, "extras": ["bombom"], "total": 225 }
  ],
  "subtotal": 225, "frete": 12, "total": 237,
  "pagamento": { "metodo": "pix", "parcelas": 1, "cartaoToken": null, "cartaoFinal": null }
}
```
- `destinatario` é `null` quando não é presente.
- `entrega.modo` é `"entrega"` ou `"retirada"`. Na retirada, `endereco` e `data` são `null`.
- `pagamento.metodo` é `"pix"`, `"cartao"` ou `"loja"` (pagar na retirada).

Saída:
```json
{
  "numero": "4821",
  "etapa": -1,
  "pagamento": {
    "id": "id-no-gateway",
    "metodo": "pix",
    "status": "pendente",
    "pix": { "copiaECola": "000201...", "qr": "<png em base64 ou URL>", "expiraEm": "2026-11-05T21:00:00Z" }
  }
}
```
- `status`: `"pendente"` (aguardando Pix), `"aprovado"`, `"recusado"` (o front mostra erro e deixa tentar de novo) ou `"na_retirada"`.
- `etapa`: `-1` = aguardando pagamento. A partir de `0`, segue a linha do tempo (abaixo).

### GET /pedidos/:numero?tel=47999999999
Devolve o pedido completo: o mesmo formato da entrada de `POST /pedidos`, mais `numero`, `etapa`, `criadoEm` (ISO) e `pagamento` com `status`.
Use o `tel` para não mostrar o pedido de uma pessoa para outra.
Na tela do pedido, o front consulta a cada 5 s enquanto o Pix está pendente e a cada 30 s depois disso, até a última etapa.
O botão "Já paguei" também consulta. A confirmação real do Pix deve vir do webhook do gateway no servidor.

### POST /auth/codigo
Entrada: `{ "tel": "47999999999" }`. Envia um código de 4 dígitos pelo WhatsApp. Saída: `{ "enviado": true }`

### POST /auth/verificar
Entrada: `{ "tel": "47999999999", "codigo": "1234" }`
Saída: `{ "token": "jwt...", "cliente": { "nome": "Ana", "email": "ana@email.com" } }`. Código errado: `401`.

### GET /pedidos?meus=1 (com token)
Devolve `[pedido, ...]` do cliente logado, no mesmo formato do `GET /pedidos/:numero`.

## Linha do tempo (`etapa`)

| etapa | Entrega            | Retirada             |
|-------|--------------------|----------------------|
| -1    | Aguardando pagamento | Aguardando pagamento |
| 0     | Pedido recebido    | Pedido recebido      |
| 1     | Em preparo         | Em preparo           |
| 2     | Saiu para entrega  | Pronto para retirada |
| 3     | Entregue           | Retirado             |

Os textos ficam em `ETAPAS_E` e `ETAPAS_R` no arquivo. A equipe da loja avança a etapa no seu painel.

## O que muda sozinho no modo real
- Some o aviso "Ambiente simulado" e o botão "Simular próxima etapa".
- O login deixa de aceitar qualquer código.
- O acompanhamento busca no servidor qualquer número digitado, inclusive de pedidos feitos em outro aparelho.

## Pendências [CONFIRMAR]
- Taxas de entrega: os valores na tabela `FRETE` são exemplos. No modo real, quem calcula é o `POST /frete`.
- `LOJA_CFG.parcelasMax` e `LOJA_CFG.pixChave`.
- Preços dos extras (`EXTRAS`).
