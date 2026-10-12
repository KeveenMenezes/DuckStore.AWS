# Capability Map: Checkout e Pedido Orquestrados (Checkout Sagas v1)

> Status: **Mapa APROVADO** (revisão 1, 2026-10-12). Cada spec de módulo passa pela própria aprovação:
>
> | Ordem | Módulo | Spec |
> |---|---|---|
> | 1 | `checkout-saga` | [`SPEC-checkout-saga.md`](./SPEC-checkout-saga.md) (aprovado, revisão 4) |
> | 2 | `checkout-points-reservation` | [`SPEC-checkout-points-reservation.md`](./SPEC-checkout-points-reservation.md) (aprovado, revisão 2) |
> | 3 | `order-saga` | [`SPEC-order-saga.md`](./SPEC-order-saga.md) (aprovado, revisão 2) |
>
> Plano: [`tasks/plan.md`](./tasks/plan.md) · Tarefas: [`tasks/todo.md`](./tasks/todo.md)

## 1. Objetivo

Deixar o checkout e o ciclo do pedido **fechados**: sem pedido duplicado, sem pedido que nasce para ser cancelado por falta de pontos, sem valor vindo do cliente, sem endpoint público e com um lugar explícito para os próximos passos do pedido.

Duas sagas Step Functions:

```
           ┌───────── EXPRESS síncrona (o cliente espera) ───────────────┐
 SPA ────▶ │ LoadCart → CatalogLines → Quote → ReservePoints →           │ ──▶ { orderId }
           │ RegisterPayment → Checkout   (falhou ─▶ VoidPayment, ReleasePoints)
           │ "posso aceitar este pedido?"                                │
           └─────────────────────────────────────────────────────────────┘
                                   │ carrinho fechado → BasketCheckoutEvent (CDC, sem cartão)
                                   ▼  starter: cria o pedido + StartExecution(name = orderId)
           ┌───────── STANDARD assíncrona ───────────────────────────────┐
           │ AuthorizePayment ─┬─ autorizado → CompleteOrder + UsePoints │
           │ "conduz o pedido" └─ recusado  → CancelOrder + ReleasePoints│
           └─────────────────────────────────────────────────────────────┘
```

## 2. Decisões tomadas (diálogo de 2026-10-12)

| Tema | Decisão |
|---|---|
| Papel de cada saga | A **Express valida** (o cliente recebe a recusa na hora, antes do pedido existir). A **Standard conduz** o pedido depois de aceito |
| Pontos | Reservados **de forma síncrona** na Express. Um pedido nunca nasce para ser cancelado por falta de saldo |
| Início da Standard | Um starter consome o `BasketCheckoutEvent` (CDC) e chama `StartExecution` com `name = orderId`. Todo carrinho fechado vira pedido, mesmo que a Express caia depois de fechar, e o nome deduplica retries |
| Gateway | A saga chama o **Payment**, que chama o **PaymentGateway por Invoke síncrono**. Sai o hop `PaymentRequestedEvent` |
| Linhas do pedido | Preço unitário vem do **Pricing**, e nome e imagem vêm do **Catalog**. Nada vem do carrinho salvo pelo cliente além de `ProductId` e `Quantity` |
| Pagamento em dinheiro | Como hoje: o gateway simulado decide na hora |
| Checkout de visitante | Fora. Continua exigindo login (ADR-0016) |
| Function URL pública do checkout | Removida nesta iniciativa (módulo 1), sem hotfix separado |
| Dados do cartão | Validados e registrados pelo Payment na Express (`RegisterPayment`, módulo 1). Saem do `BasketCheckoutEvent` e do Basket, e nunca entram na Standard |
| Total do pedido | O pedido grava o `ChargedTotal` e os `PointsUsed` do checkout, e "Meus pedidos" mostra o valor cobrado (módulo 3) |

## 3. Estado atual → alvo

O pedido hoje atravessa **12 consumidores de eventos**. A orquestração substitui todos eles e mantém como evento só o que avisa outros contextos.

| Hoje (coreografia) | Alvo | Módulo |
|---|---|---|
| `challenges-basket-checkout-consumer` (reserva assíncrona) | Passo `ReservePoints` da Express | 2 |
| `payment-points-reserved-consumer`, `payment-points-reservation-failed-consumer`, estado `AwaitingPoints`, regras `PointsReservedRule`/`PointsReservationFailedRule`/`PaymentDeclinedInsufficientPointsRule` | Somem: a reserva acontece antes do pedido | 2 |
| `ordering-basket-checkout-consumer` | `ordering-order-saga-starter`: cria o pedido e inicia a Standard | 3 |
| `payment-basket-checkout-consumer` (criação do pagamento) | Passo `RegisterPayment` da Express. O consumer vira promotor `Registered → Pending` e sai no módulo 3 | 1 → 3 |
| `PaymentRequestedRule` + `paymentgateway-payment-requested-consumer` | Passo `AuthorizePayment`: o Payment chama o gateway por Invoke | 3 |
| `ordering-payment-authorized-consumer`, `ordering-payment-declined-consumer` | Passos `CompleteOrder`/`CancelOrder` | 3 |
| `payment-result-authorized-consumer`, `payment-result-declined-consumer` | O Payment grava o resultado na mesma chamada de `AuthorizePayment` | 3 |
| `challenges-payment-authorized-consumer`, `challenges-order-cancelled-consumer` | Passos `UsePoints`/`RefundPoints` | 3 |
| `review-order-completed-consumer` (`OrderCompletedEvent`) | **Fica.** É aviso para outro contexto (fan-out) | — |

Os eventos de fato do pedido e do pagamento (`OrderCreated`/`OrderCompleted`/`OrderCancelled`, `PaymentAuthorized`/`PaymentDeclined`) **continuam publicados por CDC** para quem quiser ouvir, mesmo sem consumidor interno. Saem só os que eram comandos disfarçados de evento: `PaymentRequestedEvent`, `PointsReservedEvent` e `PointsReservationFailedEvent`.

## 4. Módulos

| Module id | Responsabilidade | Serviços | Depende de |
|---|---|---|---|
| `checkout-saga` | Express síncrona no lugar do pipeline AppSync. Remove a Function URL. `checkoutId` e `OrderId` derivado. Fechamento atômico com versão do carrinho. Linhas com preço do Pricing e nome/imagem do Catalog. `RegisterPayment`/`VoidPayment` (cartão validado e fora do evento). `local.ts` imitando a saga | Basket, Pricing, Payment, AppSync, SPA | — |
| `checkout-points-reservation` | `ReservePoints`/`ReleasePoints` síncronos (Lambdas do Challenges) na Express, com compensação quando o `Checkout` falha. Remove `AwaitingPoints`, os consumers e as regras de reserva assíncrona. `InsufficientPoints` passa a ser recusa síncrona, e o SPA a trata como `PointsAboveCap` | Challenges, Payment, AppSync (saga), SPA | `checkout-saga` |
| `order-saga` | Starter que cria o pedido (com `ChargedTotal`/`PointsUsed`) e inicia a Standard `order-saga`: `AuthorizePayment` (Payment → gateway síncrono) → `CompleteOrder` + `UsePoints` ou `CancelOrder` + `ReleasePoints`. Cada passo idempotente pelo `orderId`. Reconciliadores de órfãos. Remove os consumers da coreografia (seção 3). Alarme de execução falha | Ordering, Payment, PaymentGateway, Challenges, infra | `checkout-saga`, `checkout-points-reservation` |

Ordem de build (linear): `checkout-saga` → `checkout-points-reservation` → `order-saga`.

- O módulo 2 vem antes do 3 porque a Standard parte do princípio de que os pontos já estão reservados. Sem o módulo 2, ela teria de esperar a reserva assíncrona, que é justamente o que sai.
- Cada módulo termina com o sistema funcionando ponta a ponta. Entre o 1 e o 3, a coreografia atual continua cuidando do pós-checkout.

## 5. ADRs

| ADR | Módulo | Conteúdo |
|---|---|---|
| **0050** (nova) | 1 | Checkout como saga Express. Idempotência por `checkoutId`, fechamento atômico, linhas do Pricing e do Catalog. Emenda a ADR-0048 §4 e cita a ADR-0032 |
| **0048 §6** (emenda) | 2 | Reserva de pontos síncrona e fim do `AwaitingPoints` |
| **0051** (nova) | 3 | Orquestração para a transação do pedido, coreografia para avisos. Emenda as ADRs 0004/0005 (o evento deixa de ser o caminho entre os passos do pedido), 0025 (gateway síncrono pelo Payment) e 0048 §5 (liquidação de pontos pela saga) |

O `SPEC-inventory.md` usa 0050 e 0051 no rascunho. Ele é renumerado quando for retomado.

## 6. Fora do escopo

- Confirmação posterior do pagamento em dinheiro (QR). A Standard deixa isso simples depois: um estado de espera com task token.
- Checkout de visitante.
- Cancelamento ou estorno de pedido já pago (não existe hoje).

## 7. Premissas

1. **Dev é o único ambiente com dados.** Pedidos em andamento durante o deploy do módulo 3 podem ser descartados, então não há migração de execuções em voo.
2. **Payment e PaymentGateway continuam serviços separados.** O gateway segue simulando um processador externo e decide na hora, inclusive para dinheiro.
3. **O custo da Standard é desprezível** neste projeto, cobrado por transição de estado, e a retenção do histórico é o padrão do serviço (90 dias).
4. **O `OrderId` é derivado de `(dono, checkoutId)`.** Duas execuções concorrentes da mesma tentativa produzem o mesmo id, e por isso a reserva de pontos (`REDEMPTION#<orderId>`) e o `name` da Standard deduplicam sozinhos.
