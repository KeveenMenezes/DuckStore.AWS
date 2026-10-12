# Spec: Reserva de Pontos Síncrona no Checkout

> Status: **APROVADO** (revisão 2, 2026-10-12: compensação encadeada com o `RegisterPayment` do módulo 1). Módulo 2 de 3 do mapa [`SPEC-checkout.md`](./SPEC-checkout.md). Depende de [`checkout-saga`](./SPEC-checkout-saga.md).

## 1. Objetivo

Um checkout que usa pontos **reserva o saldo antes de o pedido existir**. Sem saldo, o cliente recebe a recusa na tela (`InsufficientPoints`), e nenhum pedido, pagamento ou evento é criado.

Hoje (ADR-0048 §5/§6), o pedido nasce, o Payment espera em `AwaitingPoints`, o Challenges reserva de forma assíncrona e, quando o saldo não basta, o pagamento é recusado e o pedido cancelado. O cliente viu "pedido feito" e depois o pedido some. Além disso, o `local.ts:1045-1051` já recusa na hora, então local e AWS se comportam diferente.

## 2. Desenho

Dois passos novos na `checkout-saga` (Express), entre `Quote` e `RegisterPayment`. Como os pontos são reservados antes do pagamento, a compensação desfaz na ordem inversa:

```
… → Quote ─▶ pontos? ─não──────────────────────▶ RegisterPayment ─▶ Checkout ─▶ Output
               │ sim                                │                  │
               ▼                                    │ InvalidCard      │ CartChanged / erro
         ReservePoints ─▶ reservado ───────────────▶│ ou erro          ▼
         (Challenges)                               │             VoidPayment
               │                                    ▼                  │
               └ saldo curto ─▶ Fail(InsufficientPoints)  ReleasePoints ◀┘ (se reservou)
                                                          │
                                                          ▼
                                                   Fail(erro original)
```

### 2.1 Requisitos

| Peça | Requisito |
|---|---|
| `challenges-reserve-points` (Lambda nova, Invoke síncrono) | Entrada `{ ownerId, orderId, points }`. Reaproveita `ReservePointsAsync` (débito condicionado a `Score >= points` + `REDEMPTION#<orderId>` `Reserved`, numa transação). Saídas: `Reserved`; `Reserved` também quando a linha já existe como `Reserved` ou `Used` (replay da mesma tentativa); `InsufficientPoints` com `{ availablePoints }`. **Uma recusa não grava nada**, porque a linha `Failed` só existia para alimentar o evento que sai |
| `challenges-release-points` (Lambda nova, Invoke síncrono) | Entrada `{ ownerId, orderId }`. `Reserved → Released` devolvendo o saldo na mesma transação (`CancelOrder` do domínio). Idempotente: sem linha, ou linha já liberada, é no-op com sucesso |
| `ReservePoints` (passo) | Só roda com `pointsToUse > 0`. `InsufficientPoints` → `Fail` com `cause` JSON `{ message, availablePoints }`. Retry só em erro transitório do Lambda |
| Compensação | Falha em `RegisterPayment` → `ReleasePoints`. Falha em `Checkout` → `VoidPayment` (módulo 1) → `ReleasePoints`. Os dois casos terminam em `Fail` com o **erro original**, para o SPA reagir a `InvalidCard`/`CartChanged` e não à compensação. `ReleasePoints` só roda quando o checkout reservou pontos. O replay "já fechado" é sucesso e **não** compensa |
| `ReleasePoints` (passo) | Retry com backoff (3 tentativas). Se esgotar, a execução falha com `CompensationFailed`, e o log de erro da state machine dispara o alarme no `duckstore-alerts` |
| Timeouts | Cada passo `Task` tem timeout próprio, e a soma fica abaixo do timeout da execução. Assim qualquer falha de passo é capturável e cai na compensação |
| Reserva sobre linha `Released` | Mesmo `orderId` com a reserva já liberada (tentativa compensada e repetida com o mesmo `checkoutId`) → `Fail(CheckoutRetryRequired)`. O SPA renova o `checkoutId` depois de **qualquer** recusa |

### 2.2 O que sai

| Serviço | Remoção |
|---|---|
| Challenges | `challenges-basket-checkout-consumer` (Lambda, regra e DLQ); `PointsReservedRule`, `PointsReservationFailedRule` e o `challenges-points-transactions-stream-publisher`, se ficar sem regras |
| Payment | Estado `AwaitingPoints`, `ConfirmPoints`/`RejectPoints`, os consumers `payment-points-reserved-consumer`/`payment-points-reservation-failed-consumer` e a `PaymentDeclinedInsufficientPointsRule`. O `payment-basket-checkout-consumer` passa a promover `Registered → Pending` **sempre**, sem o desvio para `AwaitingPoints` |
| BuildingBlocks.Messaging | `PointsReservedEvent`, `PointsReservationFailedEvent` |

**Fica neste módulo:** a liquidação continua por eventos até o módulo 3. Isso inclui `challenges-payment-authorized-consumer` (`Reserved → Used`) e `challenges-order-cancelled-consumer` (`Released`/`Refunded`). O `PointsToUse` continua no `BasketCheckoutEvent`.

### 2.3 Contratos

- **GraphQL:** novos `errorType`: `InsufficientPoints` (`errorInfo.availablePoints`) e `CheckoutRetryRequired`. O schema não muda.
- **SPA:** trata `InsufficientPoints` como hoje trata `PointsAboveCap`: atualiza o saldo e ajusta o campo de pontos. O `checkoutId` é renovado depois de qualquer recusa.
- **`local.ts`:** o check local de saldo (`:1045-1051`) dá lugar à mesma sequência reserva → checkout → compensação, com os mesmos `errorType`.
- **Ledger:** as linhas `Failed` antigas continuam legíveis no `myPointsHistory`. Só deixam de ser criadas.

## 3. ADR

Emenda à **ADR-0048 §5/§6**: a reserva vira síncrona na saga Express (ADR-0050), uma recusa não grava ledger e o `AwaitingPoints` deixa de existir. A emenda é escrita na primeira tarefa do módulo, com a skill `adr`.

## 4. Riscos

| Risco | Mitigação |
|---|---|
| A execução Express é abortada entre `ReservePoints` e `Checkout` sem passar pelo `Catch` (só um timeout da execução inteira escapa) | Os timeouts por passo tornam isso improvável. O que restar é coberto pelo reconciliador de reservas órfãs do módulo 3 |
| Deploy em ordem errada deixa checkouts com pontos sem reserva | O Challenges (Lambdas novas) sobe antes do AppSync (saga). Os consumers e as regras antigas só saem depois que a saga nova estiver no ar |
| Pagamentos `AwaitingPoints` antigos no dev ficam presos | Premissa do mapa: dados de dev em andamento são descartáveis |

## 5. Testes

| Nível | O que cobre |
|---|---|
| Unit (`Challenges.UnitTests`) | Reserva com saldo grava e debita; saldo curto devolve `InsufficientPoints` sem escrever; replay devolve `Reserved`; release devolve o saldo uma vez só; release sem linha é no-op |
| Unit (`Payment.UnitTests`) | A promoção leva sempre a `Pending`. Os testes de `AwaitingPoints` saem junto com o código |
| CDK | `cdk diff` com as 2 Lambdas novas, os passos e o `Catch` na saga, e a remoção dos consumers, das regras e do publisher |
| Dev AWS | Critérios abaixo, via SPA e via GraphQL |

## 6. Critérios de sucesso

1. Checkout com pontos e saldo suficiente: o pedido é criado e o ledger tem `REDEMPTION#<orderId>` `Reserved`, depois `Used` quando o pagamento é autorizado.
2. Checkout com pontos e saldo curto: `errorType = InsufficientPoints` na tela, sem pedido, sem pagamento, sem `BasketCheckoutEvent` e sem linha no ledger.
3. Checkout com pontos em que o `Checkout` falha (`CartChanged` forçado): o pagamento fica `Voided`, o saldo volta, o ledger mostra `Released` e o SPA recebe `CartChanged`.
3a. Checkout com pontos e cartão inválido: o saldo volta, o ledger mostra `Released` e o SPA recebe `InvalidCard`.
4. Duas execuções concorrentes com o mesmo `checkoutId` debitam **uma** vez.
5. Os consumers, as regras e os eventos da seção 2.2 não existem mais. Nenhum pagamento novo nasce `AwaitingPoints`.
6. O mesmo cenário dá o mesmo `errorType` no `local.ts` e na AWS.
7. Testes de Challenges e Payment verdes, `cdk synth` limpo, `pnpm build` verde.
