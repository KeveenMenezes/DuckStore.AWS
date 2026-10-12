# Spec: Checkout como Saga em Step Functions (Checkout Saga v1)

> Status: **APROVADO** (revisão 4, 2026-10-12: `RegisterPayment` incorporado). Módulo 1 de 3 do mapa [`SPEC-checkout.md`](./SPEC-checkout.md).

## 1. Objetivo

Trocar o pipeline AppSync `checkoutBasket` (`loadCart → quote → checkout`, ADR-0048 §4) por uma **saga Step Functions Express síncrona**, no mesmo padrão do `createProductWithPrice` (ADR-0032), e corrigir os defeitos de robustez e de segurança do fluxo atual.

O cliente não percebe diferença no caminho feliz: continua chamando `checkoutBasket` e recebendo `{ isSuccess, orderId }`. O que muda:

1. **Não existe pedido duplicado.** Duplo clique, retry de rede ou queda de Lambda no meio levam ao **mesmo** `orderId`.
2. **O checkout fecha o carrinho que foi cotado.** Se o carrinho mudou entre a cotação e o fechamento, o checkout recusa (`CartChanged`).
3. **Nenhum dado do pedido vem do cliente além de `ProductId` e `Quantity`.** O total e o preço unitário vêm do Pricing, e o nome e a imagem, do Catalog.
4. **O cartão é validado e registrado pelo Payment no checkout.** Cartão inválido é recusado na tela (`InvalidCard`), e os dados do cartão não circulam mais no evento nem ficam gravados no Basket.
5. **O Lambda de checkout deixa de ser público.**

## 2. Problemas do desenho atual

Todos confirmados no código.

| # | Severidade | Problema | Evidência | Efeito |
|---|---|---|---|---|
| P1 | **Crítico (segurança)** | O `basket-checkout-basket` tem **Function URL pública com `authType: NONE`**, que nada usa | `infra/constructs/basket-lambdas.ts:109-111`, export em `infra/stacks/basket-stack.ts:24` | Qualquer pessoa fecha o carrinho de **qualquer** `OwnerId` com **qualquer** `TotalPrice`, sem passar pelo Pricing |
| P2 | Alto | Marcar o checkout e apagar o carrinho são **duas escritas soltas**, sem condição | `DynamoShoppingCartRepository.cs:38-61`, `CheckoutBasket/Handler.cs:33-37` | Uma queda entre as duas publica o evento e mantém o carrinho, e o retry gera **outro** pedido |
| P3 | Alto | **Sem chave de idempotência.** O `OrderId` nasce de `Guid.NewGuid()` no handler | `CheckoutBasket/Handler.cs:21` | Duplo clique ou retry concorrente geram dois pedidos, dois pagamentos e duas reservas de pontos |
| P4 | Alto (segurança) | **Número do cartão e CVV no `BasketCheckoutEvent`** (EventBridge) e gravados no `CheckoutData` do Basket | `Mutation.checkoutBasket.checkout.js` (bloco `Payment`), `CheckoutBasket/Handler.cs:31` | Dado de cartão espalhado por dois contextos que não precisam dele |
| P5 | Médio | **Cartão não validado.** O consumer do Payment cria o pagamento sem passar pelo `CreatePaymentCommandValidator` (Luhn) | `Payment.Function/.../Consumers/BasketCheckout/Endpoint.cs` chama `CreatePaymentHandler.CreateNewPayment` direto | Cartão inválido vira pedido, e a recusa só chega depois, de forma assíncrona |
| P6 | Médio | **Janela entre cotar e fechar.** O Basket relê o carrinho depois da cotação | `Mutation.checkoutBasket.loadCart.js` vs. `CheckoutBasket/Handler.cs:11` | O total cobrado corresponde a um carrinho, e as linhas do pedido, a outro |
| P7 | Médio | **Preço, nome e imagem das linhas vêm do cliente.** O `storeBasket` grava o que o SPA manda | `Mutation.storeBasket.js:25-32`, `CheckoutBasket/Handler.cs:25-28` | O pedido registra preços unitários e nomes forjáveis |
| P8 | Baixo | Ordem dos passos e tratamento de erro em 4 arquivos APPSYNC_JS **sem teste**, sem como compensar um passo que já escreveu | `graphql/resolvers/basket/mutations/Mutation.checkoutBasket*.js` | Qualquer passo novo com escrita fica sem rollback |

**Fica como está:** o total recalculado pelo servidor (`PriceChanged`, `PointsAboveCap`), as regras de preço no Pricing, o CDC do Basket (`CheckoutedRule` → `BasketCheckoutEvent`) e o dono vindo do token Cognito.

## 3. Desenho

### 3.1 Fluxo

```
SPA ──checkoutBasket(input + checkoutId)──▶ AppSync (HTTP data source, StartSyncExecution)
                                              │
┌──────────────────────────── checkout-saga (Step Functions EXPRESS, síncrona) ───────────────────────────┐
│                                                                                                         │
│ DeriveOrderId ─▶ LoadCart ─▶ CatalogLines ─▶ Quote ─▶ RegisterPayment ─▶ Checkout ─▶ Output              │
│                  │            │               │         (Payment)          (Basket)                     │
│                  ├ vazio ─▶   └ produto  ─▶   ├ Rejection ─▶ ├ inválido ─▶  ├ versão mudou ─┐           │
│                  │ BadRequest   ausente ─▶    │ PriceChanged │ InvalidCard  │  ou erro       ▼           │
│                  │              Product       │ PointsAboveCap              │          VoidPayment ─▶  │
│                  │              Unavailable   └ retry transitório           │          Fail(erro       │
│                  └ já fechado com este checkoutId ─▶ orderId (replay)       └ já fechado ─▶  original)  │
│                                                                                orderId (replay)         │
└─────────────────────────────────────────────────────────────────────────────────────────────────────────┘
                                              │
                                              ▼
                     DynamoDB Streams ─▶ CheckoutedRule ─▶ EventBridge (BasketCheckoutEvent, sem cartão)
```

### 3.2 Requisitos por passo

| Passo | Requisito |
|---|---|
| Entrada (resolver) | Exige Cognito. `ownerId = USER#<sub>` e `customerId = sub` **sempre do token**. Repassa o `checkoutId` |
| `DeriveOrderId` | `OrderId` = GUID derivado de forma determinística de `(ownerId, checkoutId)`. Duas execuções da mesma tentativa chegam ao mesmo id. Como o dono entra na derivação, um `checkoutId` escolhido pelo cliente nunca colide com o pedido de outra pessoa |
| `LoadCart` | Leitura consistente. Saída: `ProductId`, `Quantity` e a **versão** do carrinho. Vazio → `Fail(BadRequest)`. Já fechado com o mesmo `checkoutId` → devolve o `orderId` gravado (**replay**) |
| `CatalogLines` | `BatchGetItem` direto na tabela `products` (integração SDK, sem Lambda). Leva `Name` e a imagem principal. Produto inexistente → `Fail(ProductUnavailable)` com os ids em `cause` |
| `Quote` | Mesmo Lambda do Pricing. A resposta passa a incluir o **preço unitário cobrado de cada linha** (pós-campanha, ADR-0043). `Rejection` → `Fail` com `error = ErrorType` e `cause` = JSON `{ message, serverTotal, maxRedeemablePoints }`. Retry em erro transitório |
| `RegisterPayment` | Lambda nova `payment-register` (Invoke). Entrada: `orderId`, `customerId`, `amount = chargedTotal`, forma de pagamento, parcelas e dados do cartão. Valida o cartão nas formas com cartão: Luhn, validade `MM/AAAA` não vencida e CVV de 3 ou 4 dígitos. Inválido → `Fail(InvalidCard)` com `cause` `{ message, field }`. Válido → grava o pagamento com o **novo status `Registered`**, que não aciona o gateway. **Um pagamento por `orderId`**: a repetição devolve o mesmo |
| `Checkout` | Lambda do Basket com dono, `checkoutId`, `orderId`, versão cotada, **linhas montadas pela saga**, `chargedTotal`, endereço, forma de pagamento e parcelas, **sem cartão**. Não relê itens nem preços, e não gera o `OrderId`. Grava numa **única operação atômica e condicional** (seção 3.4) |
| `VoidPayment` (compensação) | `Catch` em `Checkout` → Lambda nova `payment-void` (`Registered → Voided`, idempotente) → `Fail` com o **erro original**. Retry com backoff; se esgotar, `Fail(CompensationFailed)` e alarme. O replay "já fechado" é sucesso e não anula nada |
| Timeouts | Cada `Task` com timeout próprio, e a soma abaixo do timeout da execução, para toda falha de passo cair no `Catch` |
| Saída (resolver) | `SUCCEEDED` → `{ isSuccess: true, orderId }`. `FAILED` → erro GraphQL com `errorType` = `error` e `errorInfo` = `cause` parseado. **O SPA lê `PriceChanged`/`PointsAboveCap` e `errorInfo.serverTotal` como hoje** |

### 3.3 Contratos

- **GraphQL:** `CheckoutInput` ganha `checkoutId: ID`, gerado pelo SPA ao abrir o checkout e mantido nos retries da mesma tentativa. Ele é renovado depois de sucesso ou de **qualquer** recusa. Nasce opcional (sem ele, a saga gera um id e não há proteção de replay) e vira obrigatório na última tarefa do módulo. Novos `errorType`: `CartChanged`, `ProductUnavailable` e `InvalidCard`. O SPA recota e pede nova confirmação nos dois primeiros, e no `InvalidCard` marca o campo do cartão.
- **Pricing:** `GetBasketInstallmentPlanResponse` ganha `Lines: [{ ProductId, Quantity, UnitPrice }]` (aditivo).
- **Payment:** Lambdas novas `payment-register` e `payment-void`, e status novos `Registered` e `Voided`. O `payment-basket-checkout-consumer` deixa de criar o pagamento: ele **promove** o pagamento `Registered` do pedido para `Pending`, ou para `AwaitingPoints` quando `PointsToUse > 0`, até o módulo 2. O `PaymentRequestedRule` passa a casar a transição `Registered → Pending`, como já casa `AwaitingPoints → Pending`. Um pedido sem pagamento registrado lança erro, e o retry leva à DLQ.
- **Basket:** `CheckoutBasketRequest` recebe `CheckoutId`, `OrderId`, `CartVersion` e as linhas montadas pela saga, sem cartão.
- **`BasketCheckoutEvent`/`BasketCheckoutDto`:** **perdem os campos de cartão** (`CardName`, `CardNumber`, `Expiration`, `Cvv`). `PaymentMethod` e `Installments` ficam. O resto não muda.
- **Carrinho:** ganha `Version` (token novo a cada escrita), gravado pelo `storeBasket` e pelo `MergeAsync`. Um carrinho antigo sem `Version` continua fechável.

### 3.4 Checkout atômico (mecanismo recomendado; o plano confirma)

`TransactWriteItems` na tabela `shopping-carts`:

1. `Put` de `OwnerId = CHECKOUT#<owner>#<checkoutId>` com `Type = Checkout`, `CheckoutData`, `OrderId` e `ExpiresAt` em 24 h, condicionado a `attribute_not_exists(OwnerId)`.
2. `Delete` do carrinho, condicionado a `Version = :cotada` (ou `attribute_not_exists(Version)`).

Falha no item 1 significa replay: o Basket lê o registro e devolve o `OrderId`. Falha no item 2 significa `CartChanged`. O `CheckoutedRule` passa a casar em **INSERT** de `Type = Checkout` e publica o evento.

## 4. Fora deste módulo

A reserva síncrona de pontos (módulo 2) e a saga Standard do pedido (módulo 3) estão no mapa. Depois do `BasketCheckoutEvent`, a coreografia atual continua cuidando do pós-checkout até o módulo 3, com uma única adaptação: o Payment promove um pagamento já registrado em vez de criá-lo.

## 5. Riscos

| Risco | Mitigação |
|---|---|
| Latência extra do Step Functions e de um passo a mais (`RegisterPayment`) | Express síncrona com `lambda:invoke` otimizado. Medir antes e depois (critério 10) |
| `StartSyncExecution` é *at-most-once*, e um timeout não diz se o checkout aconteceu | O cliente repete com o mesmo `checkoutId` e recebe o mesmo `orderId` |
| Pagamento `Registered` órfão (a execução abortou entre `RegisterPayment` e `Checkout` sem passar pelo `Catch`) | Inofensivo: o gateway nunca é chamado para `Registered`. O módulo 3 o anula no reconciliador |
| Mudanças de forma em dois pontos durante o deploy: `CheckoutedRule` (MODIFY → INSERT) e o consumer do Payment (cria → promove) | As duas peças aceitam as formas antiga e nova durante a transição: a regra casa as duas, e o consumer cria quando não acha pagamento registrado. A forma antiga sai numa tarefa seguinte, depois do deploy da saga |
| Sem Step Functions no dev local | O `local.ts` imita a saga passo a passo, como no `createProductWithPrice` |

## 6. ADR

**ADR-0050:** checkout como saga Step Functions Express, com idempotência por `checkoutId`, fechamento atômico com versão do carrinho, linhas do Pricing e do Catalog, e registro do pagamento no checkout (o cartão sai do evento). Emenda a ADR-0048 §4, a ADR-0025 (o pagamento nasce no checkout, não no consumer) e a ADR-0038 (campos de cartão fora do `BasketCheckoutEvent`). Cita a ADR-0032 como precedente. Escrita na primeira tarefa, com a skill `adr`.

## 7. Comandos e estrutura

```bash
dotnet build DuckStore.slnx
dotnet test tests/Services/Basket/Basket.UnitTests/Basket.UnitTests.csproj
dotnet test tests/Services/Pricing/Pricing.UnitTests/Pricing.UnitTests.csproj
dotnet test tests/Services/Payment/Payment.UnitTests/Payment.UnitTests.csproj
dotnet format DuckStore.slnx
cd infra && npm run build && npx cdk synth AppSyncStack && npx cdk diff BasketStack PaymentStack PricingStack AppSyncStack
cd src/WebApps/Shopping.Web.SPA.React && pnpm lint && pnpm typecheck && pnpm build
```

| Onde | O que muda |
|---|---|
| `infra/constructs/checkout-saga.ts` (novo) | State machine `checkout-saga`, irmã de `product-create-saga.ts`. Lê `shopping-carts` e `products` (importadas por nome) e invoca Pricing, Payment e Basket |
| `infra/stacks/appsync-stack.ts`, `infra/constructs/appsync-api.ts` | Instancia a saga e troca o pipeline pelo resolver HTTP |
| `infra/constructs/basket-lambdas.ts`, `infra/stacks/basket-stack.ts` | Remove a Function URL e o export `CheckoutBasketUrl` |
| `infra/constructs/payment-lambdas.ts` | `payment-register`, `payment-void` |
| `graphql/schema.graphql` | `checkoutId` no `CheckoutInput` |
| `graphql/resolvers/basket/mutations/` | `Mutation.checkoutBasket.js` vira o resolver HTTP, os 3 arquivos de passo são apagados, e o `Mutation.storeBasket.js` grava `Version` |
| `src/Services/Basket/...` | Checkout atômico e idempotente, `Version` no `MergeAsync`, `CheckoutedRule` em INSERT, DTO sem cartão |
| `src/Services/Payment/...` | Features `RegisterPayment`/`VoidPayment`, status `Registered`/`Voided` e o consumer passando a promover |
| `src/Services/Pricing/.../GetBasketInstallmentPlan/*` | `Lines` na resposta |
| `src/BuildingBlocks/BuildingBlocks.Messaging/Events/BasketCheckoutEvent.cs` | Sem campos de cartão |
| `src/AppHost/PaymentExtensions.cs` | Registro local de `payment-register`/`payment-void` |
| `src/WebApps/Shopping.Web.SPA.React/features/checkout/*`, `app/api/graphql/local.ts` | `checkoutId`, novos `errorType` e o `local.ts` imitando a saga |
| `docs/adr/0050-*.md`, `CLAUDE.md`, READMEs/diagramas afetados | Documentação |

## 8. Estilo de código

Handler fino com a regra no domínio/repositório (`thin-handlers-rich-domain`), comentários em inglês só para regra não óbvia (`comments`) e construct CDK espelhando `product-create-saga.ts`:

```csharp
// One conditional transaction closes the cart exactly once per checkoutId: the CHECKOUT# record
// makes a retry return the first OrderId, and the Version condition refuses a cart that changed
// after Pricing quoted it (ADR-0050).
```

## 9. Testes

| Nível | O que cobre |
|---|---|
| Unit (`Basket.UnitTests`) | Checkout feliz; replay devolve o mesmo `OrderId`; versão diferente → `CartChanged`; linhas e `OrderId` vêm do request; `CheckoutedRule` casa o INSERT do registro de checkout |
| Unit (`Payment.UnitTests`) | Registro válido grava `Registered`; Luhn, validade e CVV inválidos → `InvalidCard`; dinheiro dispensa cartão; registro repetido devolve o mesmo pagamento; void idempotente; o consumer promove `Registered → Pending/AwaitingPoints` |
| Unit (`Pricing.UnitTests`) | `Lines` soma ao preço pós-campanha, coerente com a ADR-0043 |
| Derivação do `OrderId` | Mesmo `(dono, checkoutId)` → mesmo id; donos diferentes → ids diferentes |
| CDK | `cdk synth` limpo, e o `cdk diff` mostra só a saga, as Lambdas de pagamento, a remoção da Function URL e a troca do resolver |
| Local (`local.ts`) | Checkout, replay, `CartChanged` e `InvalidCard` pelo SPA local |
| Dev AWS | Critérios abaixo, via GraphQL e SPA |

Nenhum teste existente é removido sem substituto. Os do handler e do consumer atuais são reescritos para o comportamento novo.

## 10. Limites

- **Sempre:** build e testes verdes antes de cada commit; o `local.ts` acompanha cada mudança de contrato.
- **Perguntar antes:** deploy no dev AWS; push e PR; mudança no `BasketCheckoutEvent` além da remoção dos campos de cartão.
- **Nunca:** expor o Lambda de checkout fora do AppSync/Step Functions; aceitar preço ou total do cliente; gravar ou publicar dado de cartão fora do Payment.

## 11. Critérios de sucesso

1. A Function URL do `basket-checkout-basket` não existe mais.
2. Um checkout pelo SPA cria **um** pedido, **um** pagamento e, com pontos, **uma** reserva.
3. Duas chamadas com o mesmo `checkoutId`, em sequência ou concorrentes, devolvem o **mesmo** `orderId`, com **um** `BasketCheckoutEvent` e **um** pagamento.
4. Carrinho alterado entre cotação e fechamento → `errorType = CartChanged`, sem evento, o carrinho continua lá e o pagamento registrado fica `Voided`.
5. Carrinho salvo com `price` e `productName` forjados gera pedido com o preço do Pricing e o nome do Catalog nas linhas.
6. Carrinho com produto apagado no Catalog → `ProductUnavailable`, com os ids, e nenhum pedido.
7. Cartão com Luhn inválido → `InvalidCard` na tela e nenhum pedido. Dinheiro funciona sem cartão.
8. O cartão não aparece no `BasketCheckoutEvent`, no registro de checkout do Basket, no output da execução Express nem nos logs dela. O input da Express carrega o cartão até o `RegisterPayment`, e a Express não guarda histórico.
9. `PriceChanged` e `PointsAboveCap` chegam ao SPA com o mesmo `errorType` e `errorInfo` de hoje. Os arquivos `Mutation.checkoutBasket.{loadCart,quote,checkout}.js` não existem mais, e a execução aparece no Step Functions e no X-Ray.
10. Latência do `checkoutBasket` no dev medida antes e depois (10 execuções aquecidas), com aumento de p50 de no máximo 200 ms.
11. Testes verdes, `cdk synth` limpo, `pnpm build` verde.

## 12. Premissas

1. **Express síncrona**, como na ADR-0032.
2. O fechamento fica no **Lambda do Basket** e o registro do pagamento no **Lambda do Payment**: idempotência, versão e validação são regra de domínio, não ASL.
3. Janela de replay de 24 h.
4. O `ProductName` gravado no pedido é o nome do Catalog no momento do checkout (snapshot), como o preço.
5. Os dados do cartão passam pelo input da Express (que só registra erros em log, `LogLevel.ERROR`, sem dados de execução). O plano garante que o log não inclua o input.
