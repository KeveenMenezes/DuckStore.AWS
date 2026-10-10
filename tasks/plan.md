# Implementation Plan: Pontos de Desafio como Desconto (Points-to-Discount v1)

> Specs aprovados (2026-10-09): [SPEC.md](../SPEC.md) (mapa) · [points-ledger](../SPEC-points-ledger.md) · [challenge-points-admin](../SPEC-challenge-points-admin.md) · [review-eligibility](../SPEC-review-eligibility.md) · [review-points](../SPEC-review-points.md) · [cart-points-redemption](../SPEC-cart-points-redemption.md)
> Lista de tarefas: [tasks/todo.md](./todo.md)
> Status: **AGUARDANDO APROVAÇÃO do plano.**

## Overview

São 5 módulos entregues em 5 fases, uma por módulo, na ordem de dependência aprovada. São 36 tarefas, todas S ou M (nenhuma passa de ~5 arquivos), com um checkpoint ao fim de cada fase.

Cada tarefa deixa o build verde e o sistema funcionando. O modelo de cupom da ADR-0046 continua vivo até a última fase e só é removido depois que o resgate no carrinho funciona de ponta a ponta.

## Fatos do código que orientam o plano

Levantados na leitura do código, e não nos specs:

| Fato | Onde | Consequência |
|---|---|---|
| `PaymentDeclinedEvent` e `PaymentAuthorizedEvent` são publicados pelo **PaymentGateway**, não pelo Payment | `PaymentGateway.Function/.../Consumers/PaymentRequested/Endpoint.cs` | Uma recusa por pontos insuficientes decidida no Payment não chegaria ao Ordering. **Decisão:** o Payment ganha uma regra de stream que publica `PaymentDeclinedEvent` na transição `AwaitingPoints → Declined` (tarefa 30). O contrato do evento não muda |
| O Payment dispara o gateway pela regra `PaymentRequestedRule` (INSERT com `Status = Pending`) | `Payment.Function/.../Rules/PaymentRequestedRule.cs` | "Esperar a reserva" = nascer `AwaitingPoints` (o INSERT não casa) e a regra passar a casar também em MODIFY `AwaitingPoints → Pending` |
| `InstallmentCalculator.CalculateForCart(..., customerDiscountAmount)` já aplica um desconto de cliente **depois** da campanha, inclusive no `cashPrice` | `Pricing.Function/.../InstallmentCalculator.cs:57` | O desconto de pontos reaproveita esse parâmetro. A base do teto é o `Price` depois de `ApplyCartDiscounts` e antes de `ApplyCustomerDiscount`, o que exige expor o breakdown intermediário |
| O carrinho é salvo como JSON no atributo `Data` (tabela `shopping-carts`, PK `OwnerId`) | `DynamoShoppingCartRepository.cs` | O pipeline `checkoutBasket.loadCart` faz `GetItem` + `JSON.parse(Data)` no resolver |
| O `SaveAttemptAsync` já usa `TransactWriteItems` (attempt + PROFILE) e trata a reenvio como no-op pelo índice do item | `DynamoPlayerProgressRepository.cs:44` | O crédito do ledger entra como 3º item. O `ConditionFailedOn` passa a distinguir qual item falhou |
| As regras da Review hoje são INSERT = Created e MODIFY = Updated, e o `ReviewUpdated` também invalida o ISR da página do produto | `ReviewCreatedRule.cs`, `ReviewUpdatedRule.cs` | Reescritas por transição de status. O `ReviewUpdated` continua disparando em edição de comentário, para não quebrar o ISR |
| O `OrderStreamImage` tem `Status`, e as regras reidratam o `Order` pelo repositório | `OrderStreamImage.cs`, `OrderCreatedRule.cs` | `OrderCompletedRule` e `OrderCancelledRule` comparam `Old.Status` e `New.Status` sem mudar o image |
| `Order.ApplyPaymentResult(false)` já leva `Pending → Cancelled` | `Order.cs` | O `OrderCancelledEvent` cobre a recusa sem nenhuma mudança no domínio do Ordering |

## Decisões de arquitetura (do plano)

0. **Revisão `adr-guardian` da ADR-0048 (T1):** quatro mudanças de desenho foram incorporadas ao spec e às tarefas: a checagem do total foi para o Pricing (o JS do pipeline só mapeia, ADR-0009); `PointsDiscount` saiu do Basket e do evento (nenhuma moeda chega ao Challenges); alvo ausente gera exceção em vez de no-op; as emendas às ADRs 0025, 0026, 0045 e 0046 ficaram explícitas.
1. **ADRs:** a ADR-0048 cobre o ledger, a máquina de estados, o resgate no carrinho e o total do servidor, e emenda as ADRs 0045 e 0046. A ADR-0049 cobre o status da review, o GSI1 esparso, o TTL do `Deleted` e o `ReviewDeleted`, e emenda as ADRs 0011 e 0029. Cada uma é escrita na primeira tarefa da sua fase, antes do código, com a skill `adr`.
2. **Contrato antes de consumidor:** na fase 5, os eventos novos e os campos novos de `BasketCheckoutEvent` entram primeiro (aditivos). Os produtores e consumidores vêm depois, e a remoção do `DiscountId` vem por último.
3. **O pipeline do checkout vem cedo na fase 5:** é a mudança de maior risco, porque afeta **todo** checkout. Ele entra logo depois da cotação, para falhar rápido e ter o teste funcional como rede.
4. **Scaffolds:** os consumers e publishers novos são gerados com o agente `cdc-integration-scaffold`, e os resolvers com o `appsync-resolver-scaffold`, depois da classificação pela skill `resolver-selection`. Testes faltantes saem do `handler-test-backfill`.
5. **`local.ts` em toda tarefa que mexe no schema:** o backend GraphQL de dev precisa espelhar cada resolver. Não fica como dívida para o fim.
6. **Revisão por ADR:** o agente `adr-guardian` roda em cada checkpoint, sobre o diff da fase.

## Grafo de dependências

```
Fase 1 points-ledger
  T1 ADR-0048 ─> T2 tabela ─> T3 domínio ─> T4 crédito desafio ─> T5 myPointsHistory ─> T6 saldo SPA ─> T7 página histórico
                                   │
Fase 2 challenge-points-admin      │  (independente; pode rodar em paralelo à fase 1)
  T8 mutation ─> T9 Blazor         │
                                   │
Fase 3 review-eligibility          │  (independente da fase 1)
  T10 ADR-0049 ─> T11 OrderCompleted ─┐
                 T12 status Review ───┼─> T13 consumer ─> T14 CDK consumer
                                      ├─> T15 regras por transição ─> T16 CatalogView ReviewDeleted
                                      └─> T17 GraphQL ─> T18 local.ts ─> T19 SPA reviews
                                   │
Fase 4 review-points  (precisa de T3/T4 e T15)
  T20 consumer ReviewCreated ─> T21 CDK + aviso SPA
                                   │
Fase 5 cart-points-redemption  (precisa de T2/T3)
  T22 Pricing teto ─> T23 cotação GraphQL ─> T24 contratos ─> T25 pipeline checkout ─> T26 local.ts checkout
  T24 ─> T27 OrderCancelled
  T24 ─> T28 reserva ─> T29 publisher points-transactions
  T29 ─> T30 Payment AwaitingPoints ─> T31 Payment consumers + CDK
  T28,T27 ─> T32 liquidação (Used/Released/Refunded)
  T23,T25 ─> T33 SPA carrinho
  tudo ─> T34 remover cupom (backend) ─> T35 remover DiscountId ─> T36 remover recursos CDK (perguntar antes)
```

## Paralelização

- **Seguro em paralelo:** a fase 2 com qualquer outra. A fase 3 (T10 a T19) com a fase 1.
- **Sequencial:** a fase 4 depois das fases 1 e 3. A fase 5 depois da fase 1. Dentro da fase 5, T24 (contratos) bloqueia quase tudo.
- **Coordenar:** T15 muda o `ReviewCreatedEvent`, que é consumido pelo CatalogView (que já existe) e pelo T20 (novo).

## Riscos e mitigação

| Risco | Impacto | Mitigação |
|---|---|---|
| O pipeline do `checkoutBasket` quebra o checkout sem pontos (ele roda em todo checkout) | Alto | T25 logo após a cotação; o `Ordering.FunctionalTests` roda no checkpoint; o `PriceChanged` é testado com um total adulterado e com um total correto |
| Divergência de arredondamento entre a cotação exibida e a checagem do checkout gera `PriceChanged` falso | Alto | A cotação e a checagem rodam no mesmo Lambda do Pricing, que compara em centavos. Teste unitário da diferença de 1 centavo e teste manual com parcelas com juros |
| Evento chega antes da linha alvo (o Payment ainda não criou o pagamento; o GSI1 do ledger ainda não propagou) | Alto | Alvo ausente = exceção, com retry do EventBridge e DLQ (ADR-0048 §5). No-op só para condição falhada sobre uma linha que existe. Testado em T31 e T32 |
| Corrida entre a reserva e o pagamento | Alto | Payment em `AwaitingPoints` (T30/T31). Teste unitário de que o INSERT `AwaitingPoints` não dispara o `PaymentRequested` |
| Evento duplicado ou fora de ordem debita ou estorna duas vezes | Alto | Toda transição é condicional pelo status de origem; `TransactionId` determinístico; testes por transição (T28, T32) |
| A reescrita das regras da Review quebra a média ou o ISR do produto | Médio | Tabela de transições do spec como casos de teste (T15); linha legada sem `Status` = `Published` |
| O EventBridge não existe localmente, então o fluxo de ponta a ponta só roda no AWS dev | Médio | O `local.ts` simula síncrono; os checkpoints 3 a 5 exigem validação no AWS dev |
| A remoção da tabela `customer-discounts` no CDK com `RETAIN` ou dados | Médio | T36 é "perguntar antes", separada do resto da remoção |
| O `ConditionFailedOn` do `SaveAttemptAsync` passa a ter 3 itens e interpreta errado qual falhou | Médio | T4 cobre com teste: reenvio = no-op; crédito duplicado sem reenvio = impossível por construção |

## Verificação padrão (Definition of Done de toda tarefa)

```bash
dotnet build DuckStore.slnx
dotnet test <projeto de teste do serviço tocado>
dotnet format DuckStore.slnx --verify-no-changes
cd infra && npx cdk synth <Stack tocada>                         # se tocou infra/
cd src/WebApps/Shopping.Web.SPA.React && pnpm lint && pnpm build  # se tocou a SPA
```

## Perguntas abertas

Nenhuma bloqueante. A T36 (remover recursos com estado do CDK) pede confirmação quando chegar a vez dela.
