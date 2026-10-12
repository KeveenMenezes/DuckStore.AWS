# Spec: Saga Standard do Pedido

> Status: **APROVADO** (revisão 2, 2026-10-12: decisões 3.1 e 3.2 aceitas, `RegisterPayment` no módulo 1). Módulo 3 de 3 do mapa [`SPEC-checkout.md`](./SPEC-checkout.md). Depende de [`checkout-saga`](./SPEC-checkout-saga.md) e [`checkout-points-reservation`](./SPEC-checkout-points-reservation.md).

## 1. Objetivo

Depois de aceito pelo checkout, o pedido é **conduzido por uma saga Step Functions Standard** (`order-saga`): pagamento, conclusão ou cancelamento, e liquidação dos pontos. Isso substitui a coreografia entre Ordering, Payment, PaymentGateway e Challenges.

Ganhos:

- O caminho do pedido fica numa definição só, com histórico visual por pedido.
- Cada falha tem compensação explícita, e uma execução falha pode ser retomada (redrive).
- Futuros passos pós-pedido entram como estados novos, sem nova rede de eventos.
- "Meus pedidos" passa a mostrar o **valor cobrado**.

## 2. Desenho

```
BasketCheckoutEvent ─▶ ordering-order-saga-starter
                         1. cria o pedido Pending (condicional por OrderId; ChargedTotal e PointsUsed gravados)
                         2. StartExecution(name = orderId, input = { orderId, customerId, pointsUsed })
                                                   │
┌──────────────────────────── order-saga (Step Functions STANDARD) ─────────────────────────┐
│                                                                                           │
│  AuthorizePayment ─▶ autorizado? ─sim─▶ CompleteOrder ─▶ pontos? ─▶ UsePoints             │
│  (Payment → gateway             └não─▶ CancelOrder ──▶ pontos? ─▶ ReleasePoints           │
│   por Invoke)                                                                             │
│                                                                                           │
│  Todo passo: idempotente por orderId, retry com backoff. Esgotou → FAILED → alarme.       │
└───────────────────────────────────────────────────────────────────────────────────────────┘
       │ CDC das tabelas (fatos, sem consumidor interno):
       └─▶ OrderCreated / OrderCompleted (→ Review) / OrderCancelled, PaymentAuthorized / PaymentDeclined
```

### 2.1 Requisitos

| Peça | Requisito |
|---|---|
| `ordering-order-saga-starter` (Lambda do Ordering, regra EventBridge em `BasketCheckoutEvent`) | (1) Cria o pedido `Pending` a partir do evento, condicionado a `attribute_not_exists` pelo `OrderId`, no lugar do inbox por `evt.Id`. O pedido grava **`ChargedTotal`** (o `TotalPrice` do evento) e **`PointsUsed`**. (2) `StartExecution` com `name = orderId` e input **só com ids e a contagem de pontos**. Pedido já existente e `ExecutionAlreadyExists` são sucesso (redelivery). Outros erros fazem retry e vão para a DLQ |
| `AuthorizePayment` (`payment-authorize`, Invoke) | Carrega o pagamento `Registered` do pedido (criado no checkout, módulo 1), chama o `paymentgateway-authorize` por **Invoke síncrono** com o cartão que **o próprio Payment guardou**, aplica o resultado e grava. Pagamento já decidido devolve o resultado gravado (replay). Saída `{ authorized, detail }`, sem cartão |
| `paymentgateway-authorize` (Lambda nova, Invoke) | O `SimulatedGatewayDecision` atual, devolvido como retorno. Não publica eventos |
| `CompleteOrder` / `CancelOrder` (Ordering, Invoke) | `Order.ApplyPaymentResult`, que já ignora transição repetida. O `OrderCompletedRule`/`OrderCancelledRule` continuam publicando por CDC |
| `UsePoints` / `ReleasePoints` (Challenges, Invoke) | `Reserved → Used` e `Reserved → Released`. Só rodam com `pointsUsed > 0`. Idempotentes (`SaveSettlementAsync`). O `ReleasePoints` é a Lambda do módulo 2 |
| Falha esgotada | Execução `FAILED` → regra EventBridge em *Step Functions Execution Status Change* → `duckstore-alerts`. Recuperação por **redrive** da execução |
| Eventos de fato | O `PaymentAuthorizedEvent`/`PaymentDeclinedEvent` passam a sair por CDC da tabela `payments` (regras novas no Payment), não mais do gateway. Os `Order*` continuam como hoje |
| Reconciliadores (a cada 15 min, EventBridge Scheduler) | `challenges-redemption-reconciler`: `REDEMPTION#` `Reserved` com mais de 15 min e sem `order-saga` com `name = orderId` → `Released`. `payment-registration-reconciler`: pagamento `Registered` com mais de 15 min e sem `order-saga` → `Voided`. Cobrem os checkouts abortados entre o registro e o fechamento |
| GraphQL `Order` | Campos novos `chargedTotal: Float` e `pointsUsed: Int` (nulos em pedidos antigos). "Meus pedidos" mostra o `chargedTotal` quando existe, ou a soma das linhas nos pedidos antigos |

### 2.2 O que sai

| Serviço | Remoção |
|---|---|
| Ordering | `ordering-basket-checkout-consumer` (substituído pelo starter), `ordering-payment-authorized-consumer`, `ordering-payment-declined-consumer`, e o inbox `ordering-processed-events`, se ficar sem uso |
| Payment | `payment-basket-checkout-consumer` (o promotor do módulo 1), `payment-result-authorized-consumer`, `payment-result-declined-consumer`, `PaymentRequestedRule`, o status `Pending` como etapa intermediária (o `AuthorizePayment` decide direto de `Registered`), e o inbox `payment-processed-events`, se ficar sem uso |
| PaymentGateway | `paymentgateway-payment-requested-consumer`, substituído pelo `paymentgateway-authorize` |
| Challenges | `challenges-payment-authorized-consumer`, `challenges-order-cancelled-consumer` |
| BuildingBlocks.Messaging | `PaymentRequestedEvent` |

**Fica:** `review-order-completed-consumer` e todos os eventos de fato.

## 3. ADR

**ADR-0051 (nova):** orquestração Standard para a transação do pedido e coreografia (CDC) para avisos entre contextos. Emenda as ADRs 0004/0005 (o evento deixa de ser o caminho entre os passos do pedido), a 0025 (gateway síncrono pelo Payment), a 0038 (o Payment não consome mais o `BasketCheckoutEvent`) e a 0048 §5 (liquidação de pontos pela saga). Registra também o `ChargedTotal`/`PointsUsed` no pedido. Escrita na primeira tarefa do módulo, com a skill `adr`. O `adr-guardian` revisa o diff.

## 4. Riscos

| Risco | Mitigação |
|---|---|
| Troca da coreografia pela saga com pedidos em andamento | Premissa do mapa: dados de dev em andamento são descartáveis. O starter e a saga sobem antes de os consumers antigos saírem |
| Os dois caminhos ativos ao mesmo tempo durante o deploy (consumer antigo e starter) | A criação do pedido é condicional por `OrderId`, então o segundo vira no-op. O gateway só decide uma vez, porque a transição a partir de `Registered`/`Pending` é única |
| Dev local sem Step Functions | O `local.ts` reproduz o starter e a saga em processo depois do checkout, invocando as mesmas Lambdas pelo emulador. Pelo `CLAUDE.md`, o EventBridge local não entrega eventos, então hoje o pós-checkout provavelmente não roda localmente. O plano confirma |
| PII (nome, endereço, e-mail) | O endereço não entra no input da saga: só ids e a contagem de pontos. O pedido guarda o endereço, como hoje |

## 5. Testes

| Nível | O que cobre |
|---|---|
| Unit (Ordering) | Starter: cria o pedido com `ChargedTotal`/`PointsUsed`; redelivery é no-op; `ExecutionAlreadyExists` é sucesso. `CompleteOrder`/`CancelOrder` idempotentes |
| Unit (Payment, PaymentGateway) | `AuthorizePayment`: `Registered` → decidido; replay devolve o resultado gravado. Gateway: mesma decisão de hoje. Reconciliador de pagamento |
| Unit (Challenges) | `UsePoints` idempotente. Reconciliador libera só reservas órfãs antigas |
| Functional (`Ordering.FunctionalTests`) | Revisado se o fixture depender de algum consumer removido |
| CDK | `cdk diff` com a state machine, o starter, as Lambdas de passo, os reconciliadores, o alarme e a remoção da seção 2.2 |
| Dev AWS | Critérios abaixo, mais a inspeção visual de uma execução autorizada e de uma recusada no console |

## 6. Critérios de sucesso

1. Um checkout autorizado gera uma execução `order-saga` com nome igual ao `orderId`, e o pedido termina `Completed` (pontos `Used`, quando houver).
2. Um checkout recusado pelo gateway termina com o pedido `Cancelled` e os pontos `Released`.
3. Uma redelivery do `BasketCheckoutEvent` não cria segunda execução nem segundo pedido.
4. Um passo que falha até esgotar deixa a execução `FAILED`, dispara o alarme, e o redrive termina o pedido.
5. O Review continua liberando a avaliação quando o pedido completa.
6. Os componentes da seção 2.2 não existem mais, e nenhum consumer interno participa do caminho do pedido.
7. Uma reserva de pontos e um pagamento `Registered` órfãos, com mais de 15 min, são liberados ou anulados pelos reconciliadores.
8. O cartão não aparece no input, no output nem no histórico de nenhuma execução `order-saga`.
9. "Meus pedidos" mostra o total cobrado (com desconto de pontos e preço à vista) nos pedidos novos.
10. No dev local, um checkout gera o pedido e o leva a `Completed` ou `Cancelled`.
11. Testes verdes, `cdk synth` limpo, `pnpm build` verde.
