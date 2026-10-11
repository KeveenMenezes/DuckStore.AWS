# Spec: cart-points-redemption

> Módulo 5 de 5 do mapa em [SPEC.md](./SPEC.md). Depende de: `points-ledger` (tabela, GSI1 `OrderId`, status de resgate).
> Status: **AGUARDANDO APROVAÇÃO.**
> Substitui o modelo de cupom da ADR-0046 (decisão registrada na ADR-0048, escrita em `points-ledger`).

## Objetivo

No carrinho, o cliente informa quantos pontos quer usar. O desconto é de 100 pontos = R$ 1,00, limitado a 20% do total do carrinho após campanhas (sem frete e sem juros) e ao saldo do cliente.

Os pontos ficam reservados no checkout, viram `Used` quando o pagamento é autorizado e voltam ao saldo se o pedido for cancelado (`Released` antes do uso, `Refunded` depois).

### Critérios de aceite

1. No carrinho, logado, vejo meu saldo e um campo "Usar pontos", com máximo = `min(saldo, maxRedeemablePoints)`. Ao lado aparece a conversão "N pontos = R$ X".
2. Carrinho de R$ 200,00 após campanha: o máximo pelo teto é 4.000 pontos (R$ 40,00). Com saldo de 2.500, o máximo do campo é 2.500.
3. Informo 1.000 pontos: o total mostra −R$ 10,00, e as parcelas e o preço à vista são recalculados sobre o total líquido.
4. Se eu burlar o front e mandar pontos acima do teto, tanto `basketInstallmentPlan` quanto `checkoutBasket` rejeitam com erro explícito. O valor não é cortado em silêncio.
5. Se eu burlar o front e mandar pontos acima do saldo, o checkout é aceito, a reserva falha (`Failed`), o pagamento é recusado ("pontos insuficientes") e o pedido é cancelado. Nenhum ponto é debitado.
6. Checkout com pontos: o saldo cai imediatamente e o histórico mostra "Resgate −1.000" com status "Aguardando pagamento" (`Reserved`).
7. Pagamento autorizado: a linha vira "Usado" (`Used`), sem mudar o saldo.
8. Pagamento recusado: o pedido é cancelado, a linha vira "Estornado" (`Released`) e os 1.000 pontos voltam ao saldo.
9. Pedido cancelado depois de pago: a linha vira "Reembolsado" (`Refunded`) e os pontos voltam. Na v1 não existe ação para cancelar um pedido pago, então o critério é validado só por teste unitário do consumer.
10. Visitante não vê o campo de pontos, e `pointsToUse` sem token é rejeitado.
11. Eventos duplicados (checkout, pagamento, cancelamento) não debitam nem devolvem duas vezes.
12. O fluxo de cupom antigo (`redeemChallengePoints`, `myRewards`, `discountId`) não existe mais no schema nem na SPA.
13. **Total recalculado no servidor (todo checkout, com ou sem pontos):** o pedido e o pagamento usam o total calculado pelo Pricing, nunca o `totalPrice` enviado pelo cliente.
14. Se o `totalPrice` enviado for diferente do total do servidor (preço alterado, campanha encerrada ou front manipulado), o checkout é **rejeitado** com o erro `PriceChanged`. Nenhum pedido é criado e nenhum ponto é reservado. A SPA mostra "O valor do seu carrinho mudou. Confira o novo total antes de finalizar.", refaz a cotação e exibe o novo total para o cliente confirmar.

## Desenho

### 1. Cotação: `basketInstallmentPlan(items, pointsToUse: Int)`

`discountId` sai e entra `pointsToUse`. Só Cognito quando `> 0`, mantendo a regra atual de rejeitar guest.

O Pricing (`GetBasketInstallmentPlan`) aplica, nesta ordem:
1. Alocação de campanha (`CartDiscountAllocation`, ADR-0043), que resulta no `baseTotal`.
2. `maxRedeemablePoints = floor(baseTotal × MaxDiscountPercent / 100 × PointsPerUnit / CurrencyPerUnit)`. Com 20%, 100 e 1, isso dá `floor(baseTotal × 20)`.
3. Se `pointsToUse > maxRedeemablePoints`, lança `PointsAboveCapException` (BadRequest).
4. `pointsDiscount = pointsToUse × CurrencyPerUnit / PointsPerUnit`, arredondado para baixo em centavos.
5. As parcelas e o preço à vista são calculados sobre `baseTotal − pointsDiscount`, com piso em zero.

A resposta ganha `maxRedeemablePoints: Int!` e `pointsDiscount: Float!`. O Plan confirma qual campo do resultado atual é o "pós-campanha" e em que ponto do cálculo de custo e parcelas o desconto entra.

**Total cobrado (usado pelo checkout, §2):** se a chamada também trouxer `expectedTotal`, `paymentMethod` e `installments`, o Pricing escolhe o total daquela forma de pagamento (à vista → `cashPrice`; cartão em N parcelas → `installments[N].totalValue`; N inexistente → `BadRequest`), compara com `expectedTotal` em centavos e, se for diferente, lança `PriceChangedException` (`errorType = PriceChanged`, com `serverTotal`). Se bater, devolve `chargedTotal`. Escolher o total e decidir que ele divergiu é regra de preço, então fica no Pricing e não no JS do resolver (ADR-0009, ADR-0048 §3).

O Pricing **não conhece o saldo** (ADR-0046 §1). O saldo é checado no front (UX) e na reserva (autoridade).

### 2. Checkout: `checkoutBasket` vira pipeline (o total é sempre do servidor)

Hoje o resolver repassa o `totalPrice` do cliente direto ao Basket. Com isso, o valor cobrado é o que o front envia, e o teto checado só na cotação pode ser contornado. O `checkoutBasket` passa a ser um **pipeline AppSync** que roda em **todo** checkout. Classificação ADR-0009: critério 2 (orquestração entre serviços). O JS só mapeia argumentos, resultados e erros; toda regra fica em Lambda (ADR-0048 §4). Isso emenda a ADR-0026 §3: os Lambdas do Basket continuam sem chamar o Pricing, mas a mutation passa a depender de uma leitura síncrona do Pricing, orquestrada pelo AppSync.

| Função (`graphql/resolvers/basket/mutations/`) | Data source | Operação |
|---|---|---|
| `Mutation.checkoutBasket.loadCart.js` | DynamoDB `shopping-carts` | `GetItem` no carrinho (`USER#<sub>`) + `JSON.parse(Data)`. Carrinho vazio ou inexistente → `BadRequest` |
| `Mutation.checkoutBasket.quote.js` | Lambda Pricing `GetBasketInstallmentPlan` | Itens **do carrinho salvo** (não do input), `pointsToUse` (0 quando ausente), `expectedTotal = input.totalPrice`, `paymentMethod`, `installments` → `chargedTotal`, ou erro `PointsAboveCap` / `PriceChanged` repassado com `errorType` e `serverTotal` |
| `Mutation.checkoutBasket.checkout.js` | Lambda Basket | Como hoje, com `TotalPrice = chargedTotal` e `PointsToUse`. O valor do cliente nunca segue adiante |

O `pipelineResolver()` de `appsync-api.ts` hoje usa um único data source para todas as funções e ganha uma variante com um data source por função.

- **Por que rejeitar em vez de só substituir:** cobrar um valor diferente do que o cliente viu na tela, mesmo que menor, quebra a confiança. O cliente sempre confirma o valor que será cobrado.
- **Comparação exata em centavos:** os dois lados vêm do mesmo cálculo do Pricing (a SPA exibe a cotação). Uma diferença significa preço mudado ou input adulterado, nunca arredondamento.
- **Erro tipado:** o `errorType = PriceChanged` e o `errorInfo.serverTotal` deixam a SPA tratar o caso sem interpretar a mensagem de texto.
- O `input.totalPrice` continua no schema, agora com o papel de "valor que o cliente confirmou".

Classificação validada pelo `adr-guardian` (ADR-0048 §4). Confirmar com a skill `resolver-selection` ao implementar.

### 3. Eventos

```csharp
// BasketCheckoutEvent: DiscountId sai; entra só a quantidade de pontos.
// Nenhum valor em moeda derivado de pontos: o desconto já está dentro do TotalPrice (que agora é o
// chargedTotal do Pricing), e o Challenges também consome este evento e não pode receber moeda
// (ADR-0046 §1, ADR-0048 §4).
public int PointsToUse { get; set; }            // 0 = sem pontos

// novos (Challenges → EventBridge, via stream de points-transactions)
public record PointsReservedEvent : IntegrationEvent { Guid OrderId; string OwnerId; int Points; }
public record PointsReservationFailedEvent : IntegrationEvent { Guid OrderId; string OwnerId; int Points; }

// novo (Ordering → EventBridge, regra MODIFY → Cancelled no publisher que já existe)
public record OrderCancelledEvent : IntegrationEvent { Guid OrderId; Guid CustomerId; }

// PaymentAuthorizedEvent: DiscountId sai (o restante fica)
```

O Basket só repassa (passthrough): não valida nem interpreta pontos, igual ao `DiscountId` de hoje (ADR-0026, "zero discount responsibility").

### 4. Máquina de estados (tabela `points-transactions`, `TransactionId = REDEMPTION#<orderId>`)

```
                BasketCheckout(PointsToUse>0)
                         │
          ┌──────────────┴──────────────┐
   Score >= pts                    Score < pts
          │                             │
       Reserved ── PointsReserved ──> Payment autoriza
          │                             │
          │                         Failed ── PointsReservationFailed ──> Payment recusa
          │                                                                  │
          ├── PaymentAuthorized ──> Used ── OrderCancelled ──> Refunded (+pts)
          │
          └── OrderCancelled ──> Released (+pts)
```

| Consumer (Challenges) | Evento | Escrita |
|---|---|---|
| `challenges-basket-checkout-consumer` | `BasketCheckoutEvent` com `PointsToUse > 0` | `TransactWriteItems`: Update PROFILE `ADD Score -pts` com condição `Score >= pts` + Put `REDEMPTION#<orderId>` `Reserved` com `attribute_not_exists`. Se a transação falhar só pela condição do saldo: Put `Failed` com `attribute_not_exists`. Se falhar pelo Put (duplicado): no-op |
| `challenges-payment-authorized-consumer` | `PaymentAuthorizedEvent` | Query GSI1 por `OrderId`; Update `Status = Used` com condição `Status = Reserved` |
| `challenges-order-cancelled-consumer` | `OrderCancelledEvent` | Query GSI1; se `Reserved`: `TransactWriteItems` com Update `Released` (cond `Status = Reserved`) + PROFILE `ADD Score +pts`. Se `Used`: o mesmo, com `Refunded` (cond `Status = Used`). Se `Failed` ou terminal: no-op |

**Alvo ausente ≠ condição falhada (ADR-0048 §5):** um consumer que **não encontra** a linha alvo (GSI1 ainda não propagado, por ser eventualmente consistente) **lança exceção**, para o EventBridge tentar de novo e, esgotadas as tentativas, mandar para a DLQ. No-op só quando a linha existe e a condição de status falha (evento duplicado ou já liquidado).

Publisher novo: `challenges-points-transactions-stream-publisher`, na stream de `points-transactions`, com as regras `PointsReservedRule` (INSERT `Reserved`) e `PointsReservationFailedRule` (INSERT `Failed`), e DLQ `onFailure` no `DynamoEventSource` (ADR-0015 §1).

Por que o cancelamento usa `OrderCancelledEvent` e não `PaymentDeclinedEvent`: um pagamento recusado já cancela o pedido (o Ordering faz isso hoje). Reagir ao cancelamento cobre recusa e cancelamento pós-pagamento com um único consumer, e o `PaymentDeclinedEvent` nem traz o owner.

### 5. Payment espera a reserva (fecha a corrida)

Sem isso, o pagamento poderia ser autorizado em paralelo a uma reserva que falha, e o cliente pagaria com desconto sem ter os pontos.

- O consumer de `BasketCheckout` do Payment: com `PointsToUse > 0`, cria o `Payment` em status novo `AwaitingPoints` e **não** chama o gateway.
- Novo slice `Consumers/PointsReservation/` (Lambda `payment-points-reservation-consumer`), com dois `[LambdaFunction]` e uma regra EventBridge por detail-type, no mesmo formato do `PaymentResult/` (a ADR-0040 só vale para o CatalogView):
  - `PointsReservedEvent`: `AwaitingPoints → Pending`. A `PaymentRequestedRule` passa a casar também nesse MODIFY, e o fluxo do gateway roda como hoje.
  - `PointsReservationFailedEvent`: `AwaitingPoints → Declined` (motivo `InsufficientPoints`).
- Como o `PaymentDeclinedEvent` hoje só é publicado pelo PaymentGateway, uma regra nova `PaymentDeclinedInsufficientPointsRule` no `payment-payments-stream-publisher` o publica na transição `AwaitingPoints → Declined`, e o Ordering cancela o pedido pelo consumer que já existe. O contrato do evento não muda. O consumer `PaymentResult` do próprio Payment também recebe esse evento e faz no-op pelo guard `Status != Pending`, e isso é intencional.
- **Pagamento ainda não criado:** se o `PointsReservedEvent` chegar antes do `AwaitingPoints` existir (o Payment e o Challenges consomem o `BasketCheckout` em paralelo), o handler **lança exceção** para o EventBridge tentar de novo. Ele não descarta o evento como o `PaymentResult` faz hoje.
- Sem pontos, o fluxo do Payment fica idêntico ao atual.

### 6. Remoção do modelo de cupom

- **Challenges:** mutation `redeemChallengePoints` e Lambda `challenges-redeem-points`, feature `RedeemPoints`, `Redemption`/`REDEMPTION#` em `challenge-progress`, `PointsRedeemedRule`, `PointsRedeemedEvent`, `InsufficientPointsException` (se ficar sem uso; a reserva usa a sua própria) e `PlayerProgress.Redeem`/`MinimumRedeemablePoints`.
- **Pricing:** módulo `CustomerDiscounts` inteiro (tabela `customer-discounts`, `pricing-points-redeemed-consumer`, `pricing-payment-authorized-consumer`), `myRewards` e o `DiscountId` no `GetBasketInstallmentPlan`.
- **Schema/SPA:** `CustomerDiscount`, `myRewards`, `discountId` no checkout e na cotação, e `GET_MY_REWARDS`.
- **Mantém:** `rewardConversion`, só sem `expiryDays`. O `reward-config.ts` passa a `REWARD_POINTS_PER_UNIT = 100`, `REWARD_CURRENCY_PER_UNIT = 1` e `REWARD_MAX_DISCOUNT_PERCENT = 20` (env `Rewards__MaxDiscountPercent` no Lambda do Pricing). `REWARD_EXPIRY_DAYS` é removido.

### 7. SPA

- `features/cart`: componente `points-redeem-input` com o saldo, um campo numérico com máximo `min(saldo, maxRedeemablePoints)` e a conversão. A cotação é refeita com debounce ao mudar o valor.
- `features/checkout`: envia `pointsToUse` e o `totalPrice` exibido. O resumo mostra a linha "Pontos −R$ X".
- Ao receber `PriceChanged`: mostra um alerta inline (não um toast genérico) com "O valor do seu carrinho mudou. Confira o novo total antes de finalizar.", refaz a cotação, destaca o novo total (o `serverTotal`) e reabilita o botão de finalizar. Não reenvia o checkout sozinho.
- Se a cotação refeita reduzir o `maxRedeemablePoints` abaixo dos pontos informados, o campo é ajustado para o novo máximo e o cliente é avisado.
- O histórico (`features/points`) exibe os status de resgate: Aguardando pagamento, Usado, Estornado, Reembolsado e Falhou.
- `local.ts`: cotação com teto, e o checkout local também recalcula e compara o total (`PriceChanged`). O checkout local simula a reserva e o débito de forma síncrona (o EventBridge não existe localmente) e marca a linha como `Used`.

### Fora do escopo

- Ação de cancelar um pedido já pago (o `Refunded` fica pronto e sem gatilho de UI).
- Usar pontos para pagar frete ou juros.
- Pagamento 100% em pontos (o teto de 20% já impede).

## Comandos

```bash
dotnet build DuckStore.slnx
dotnet test tests/Services/Challenges/Challenges.UnitTests/Challenges.UnitTests.csproj
dotnet test tests/Services/Pricing/Pricing.UnitTests/Pricing.UnitTests.csproj
dotnet test tests/Services/Payment/Payment.UnitTests/Payment.UnitTests.csproj
dotnet test tests/Services/Ordering/Ordering.UnitTests/Ordering.UnitTests.csproj
dotnet test tests/Services/Basket/Basket.UnitTests/Basket.UnitTests.csproj
dotnet test                                   # suíte inteira antes do PR (muitos contratos mudam)
dotnet run --project src/AppHost/AppHost.csproj
cd infra && for s in ChallengesStack PricingStack PaymentStack OrderingStack BasketStack AppSyncStack; do npx cdk synth $s && npx cdk diff $s; done
cd src/WebApps/Shopping.Web.SPA.React && pnpm lint && pnpm build
```

## Estrutura

```
src/BuildingBlocks/BuildingBlocks.Messaging/Events/
  BasketCheckoutEvent.cs        − DiscountId, + PointsToUse
  PaymentAuthorizedEvent.cs     − DiscountId
  PointsReservedEvent.cs, PointsReservationFailedEvent.cs, OrderCancelledEvent.cs   novos
  PointsRedeemedEvent.cs        removido
src/Services/Challenges/Challenges.Function/Modules/Progress/
  Domain/Entities/PointsTransaction.cs           + Reserve/Use/Release/Refund/Fail
  EventsIntegration/Consumers/{BasketCheckout,PaymentAuthorized,OrderCancelled}/
  EventsIntegration/Publishers/PointsTransactions/{Endpoint,Rules/*}
  Features/RedeemPoints/                         removido
src/Services/Pricing/Pricing.Function/
  Modules/Prices/Features/GetBasketInstallmentPlan/   teto + pointsDiscount + chargedTotal/PriceChanged
  Modules/CustomerDiscounts/                          removido
  Shared/Configuration/RewardOptions.cs               + MaxDiscountPercent, − ExpiryDays
src/Services/Payment/Payment.Function/...           AwaitingPoints + 2 consumers
src/Services/Ordering/.../Publishers/Rules/OrderCancelledRule.cs
src/Services/Basket/Basket.Function/Features/CheckoutBasket/   passthrough PointsToUse
src/AppHost/{Challenges,Pricing,Payment}Extensions.cs
infra/constructs/{challenges,pricing,payment,ordering}-{dynamodb,lambdas}.ts, reward-config.ts, appsync-api.ts
graphql/schema.graphql
graphql/resolvers/basket/mutations/Mutation.checkoutBasket{,.loadCart,.quotePoints,.checkout}.js
graphql/resolvers/pricing/queries/Query.basketInstallmentPlan.js
graphql/resolvers/challenges/mutations/Mutation.redeemChallengePoints.js   removido
src/WebApps/Shopping.Web.SPA.React/features/{cart,checkout,points}/**, api/queries/pricing.ts, app/api/graphql/local.ts
tests/Services/{Challenges,Pricing,Payment,Ordering,Basket}/...
```

## Estilo de código

```csharp
// Toda transição de resgate é condicional pelo status de origem: um evento duplicado ou fora de
// ordem falha a condição e vira no-op, nunca um segundo débito ou estorno (SPEC-cart-points-redemption §4).
public PointsTransaction Release(DateTime at) => Transition(PointsTransactionStatus.Reserved, PointsTransactionStatus.Released, at);
public PointsTransaction Refund(DateTime at)  => Transition(PointsTransactionStatus.Used,     PointsTransactionStatus.Refunded, at);

// Estorno devolve exatamente o que saiu: o valor vem da própria linha, nunca do evento.
public int PointsToReturn => Status is PointsTransactionStatus.Released or PointsTransactionStatus.Refunded ? -Points : 0;
```

```csharp
// Pricing — o teto é regra do Pricing (ADR-0046 §1/§5); o saldo não é (o Pricing não o conhece).
public int MaxRedeemablePoints(decimal baseTotal) =>
    (int)Math.Floor(baseTotal * MaxDiscountPercent / 100m * PointsPerUnit / CurrencyPerUnit);
```

## Testes

- **Unit:**
  - **Pricing:** `MaxRedeemablePoints` (R$200 → 4000; R$0,99 → 19; carrinho vazio → 0); a cotação rejeita acima do teto; o desconto entra depois da campanha; as parcelas são calculadas sobre o líquido.
  - **Challenges:**
    - Reserva com saldo suficiente gera `Reserved` com débito.
    - Saldo insuficiente gera `Failed` sem débito.
    - Checkout duplicado é no-op.
    - Cada transição respeita o status de origem.
    - `Released` e `Refunded` devolvem `|Points|`.
    - `OrderCancelled` sobre `Failed` é no-op.
  - **Payment:** com pontos fica `AwaitingPoints` e não chama o gateway; `PointsReserved` autoriza; `PointsReservationFailed` recusa com `InsufficientPoints`; sem pontos o comportamento não muda.
  - **Ordering:** `OrderCancelledRule` casa só na transição para `Cancelled`.
  - **Basket:** `PointsToUse` e `TotalPrice` chegam inalterados ao evento.
- **Functional (`Ordering.FunctionalTests`):** checkout sem pontos continua funcionando, agora passando pelo pipeline (regressão).
- **Manual (AWS dev, por causa do EventBridge):** critérios 1 a 8, 10, 11, 13 e 14. O critério 9 é coberto só pelo teste unitário. Para o 14, alterar o preço de um produto entre a cotação e o checkout e também mandar um `totalPrice` adulterado via GraphQL.

## Limites

- **Sempre:** o total cobrado vem do Pricing e o checkout é rejeitado com `PriceChanged` quando o total do cliente diverge; o teto é aplicado no Pricing tanto na cotação quanto no pipeline do checkout; toda transição é condicional; os valores de estorno vêm da linha do ledger; `local.ts` é atualizado.
- **Perguntar antes:**
  - Remover recursos com estado do CDK (a tabela `customer-discounts`). Confirmar a `RemovalPolicy` e se os dados de dev podem ser descartados.
  - Mudar o contrato do `PaymentDeclinedEvent`.
- **Nunca:**
  - Pôr valor em moeda em evento do Challenges.
  - O Basket interpretar pontos.
  - Invoke síncrono entre Lambdas fora do Payment → PaymentGateway que já existe.
  - Cortar o valor de pontos em silêncio em vez de rejeitar.
  - Usar o `input.totalPrice` como valor cobrado, ou substituir o total sem avisar o cliente.

## Perguntas abertas

Nenhuma. O `TotalPrice` vindo do cliente foi decidido em 2026-10-09: o servidor sempre usa o total do Pricing e rejeita com `PriceChanged` quando o total do cliente diverge (§2, critérios 13 e 14).
