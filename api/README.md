# Florisa API

Servidor da loja Florisa: pedidos, frete, login por código no WhatsApp e o painel da equipe.
Segue o contrato do [`INTEGRACAO.md`](../INTEGRACAO.md), que é o que o site (`index.html`, objeto `API`) espera.

ASP.NET Core (.NET 10) + Entity Framework Core. SQLite para desenvolver, PostgreSQL em produção.

## Rodar no computador

Precisa do [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd api/src/Florisa.Api
dotnet run
```

Em desenvolvimento ele cria o arquivo `florisa.db` com os produtos, extras e taxas da v8 e um usuário do painel:
`admin` / `florisa-dev-123` (definido em `appsettings.Development.json`).

Com o servidor ligado:

- **Loja:** http://localhost:5000 (o site da v8, já ligado a este servidor)
- **Painel da equipe:** http://localhost:5000/painel

Testes:

```bash
cd api
dotnet test Florisa.slnx
```

A cada envio para o GitHub, o workflow `.github/workflows/api.yml` compila e roda os testes.

## Endpoints

### Loja (o site chama)

| Método | Caminho | O que faz |
|---|---|---|
| GET | `/api/catalogo` | Produtos, extras e taxas de entrega |
| POST | `/api/frete` | Taxa de entrega do bairro |
| POST | `/api/pedidos` | Cria o pedido e a cobrança (Pix, cartão ou pagar na loja) |
| GET | `/api/pedidos/{numero}?tel=` | Consulta o pedido (só com o telefone de quem comprou) |
| GET | `/api/pedidos?meus=1` | Pedidos do cliente logado |
| POST | `/api/auth/codigo` | Envia o código de 4 dígitos pelo WhatsApp |
| POST | `/api/auth/verificar` | Confere o código e devolve o token |
| POST | `/api/pagamentos/webhook` | Aviso do provedor de pagamento |

### Painel (equipe, com login)

As telas do painel ficam em `src/Florisa.Api/wwwroot/painel` (HTML, CSS e JS, sem build) e abrem em `/painel`.

| Método | Caminho | Quem pode |
|---|---|---|
| POST | `/api/painel/login` | todos |
| GET | `/api/painel/pedidos?dia=hoje\|amanha\|abertos\|todos&modo=&busca=` | equipe |
| GET | `/api/painel/pedidos/{numero}` | equipe |
| GET | `/api/painel/entregadores` | equipe |
| POST | `/api/painel/pedidos/{numero}/avancar` | equipe (entregador só saída e entrega) |
| POST | `/api/painel/pedidos/{numero}/confirmar-pagamento` | admin, atendente |
| PATCH | `/api/painel/pedidos/{numero}` (entregador, nota interna) | admin, atendente |
| POST | `/api/painel/pedidos` (pedido do WhatsApp ou balcão) | admin, atendente |
| GET / PUT | `/api/painel/produtos[/{id}]` | ver: equipe · mudar: admin, estoque |
| GET / PUT | `/api/painel/taxas` | ver: equipe · mudar: admin |
| GET / POST | `/api/painel/equipe` | admin |

Erros sempre voltam como `{ "mensagem": "texto para mostrar" }`.

## Regras que o servidor garante

- **Preço vem do banco, nunca do navegador.** Se o total que o site mostrou for diferente do calculado, o pedido volta com `409` e a mensagem com o total certo.
- Produto esgotado (`disponivel = false`) não pode ser comprado.
- Data a partir de hoje, sem domingo (configurável), e respeitando a antecedência do produto (cestas: 1 dia).
- "Pagar na loja" só vale para retirada.
- Pedido só anda na fila depois de pago. A equipe pode confirmar o pagamento à mão.
- O cliente recebe aviso no WhatsApp a cada etapa.
- Número do pedido sequencial a partir de 4801.

## O que ainda falta ligar

| Peça | Hoje | Para ligar |
|---|---|---|
| Pagamento | `GatewaySimulado`: Pix fica pendente até alguém confirmar no painel; cartão aprova qualquer token (menos os que começam com `recusar`) | Implementar `IGatewayPagamento` para o provedor escolhido e trocar `Pagamento:Provedor` |
| WhatsApp | `EnvioMensagensLog`: só escreve no log | Implementar `IEnvioMensagens` com o provedor de WhatsApp |
| Banco | Tabelas criadas com `EnsureCreated` | Trocar por migrações do EF Core antes da produção |
| Site | `API.modo = "simulado"` | `API.modo = "real"` e `API.base` apontando para este servidor |
| Mensagem do cartão | O servidor aceita `mensagemCartao` e `observacoes` no pedido | O checkout do site ainda não tem esses campos |

## Configuração de produção

Defina por variável de ambiente (nunca no repositório):

```
BancoDeDados=postgres
ConnectionStrings__Florisa=Host=...;Database=florisa;Username=...;Password=...
Jwt__Chave=<texto aleatório com 32+ caracteres>
Equipe__AdminSenha=<senha inicial do admin>
Pagamento__SegredoWebhook=<segredo>
Cors__Origens__0=https://floresflorisa.com.br
```

O `Dockerfile` desta pasta gera a imagem para hospedar (Railway, Fly.io, Azure, servidor próprio).
