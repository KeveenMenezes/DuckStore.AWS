# Implementation Plan: Checkout e Pedido Orquestrados (Checkout Sagas v1)

> Specs aprovados (2026-10-12): [mapa](../SPEC-checkout.md) · [checkout-saga](../SPEC-checkout-saga.md) · [checkout-points-reservation](../SPEC-checkout-points-reservation.md) · [order-saga](../SPEC-order-saga.md)
> Lista de tarefas: [tasks/todo.md](./todo.md)
> Status: **APROVADO** (2026-10-12, `/build auto`). Decisões: branch `feat/checkout-sagas` a partir de `origin/development`; um PR por fase; deploy no dev **pré-aprovado por checkpoint** nas stacks listadas abaixo (com relatório do que subiu); os `serverless.template` regenerados entram num commit de preparação; as mudanças locais pré-existentes (`next.config.mjs`, `sst-env.d.ts`, `docs/social/`, `SPEC-inventory.md`) ficam fora dos commits.

## Overview

São 3 módulos em 3 fases, na ordem aprovada no mapa, com 34 tarefas, T0 a T33 (todas XS, S ou M) e um checkpoint por fase.

Toda tarefa deixa o build verde e o sistema funcionando. As mudanças de forma (evento, regra, consumer) passam por uma etapa **tolerante**: a peça nova aceita a forma antiga e a nova, e a forma antiga só sai numa tarefa de limpeza, depois que a peça nova está no ar.

## Fatos do código que orientam o plano

| Fato | Onde | Consequência |
|---|---|---|
| Os workflows de deploy são **por serviço** e rodam **em paralelo** no push para `development` | `.github/workflows/deploy-*-cdk.yml` | Não dá para garantir ordem de deploy entre stacks pelo CI. Por isso a regra de tolerância: cada commit precisa funcionar com qualquer ordem de deploy das stacks que ele toca |
| `Function.fromFunctionName` e a definição de uma state machine não validam se a Lambda existe | CDK/CloudFormation | Uma stack pode referenciar uma Lambda de outra stack que ainda não subiu. O efeito é só uma janela de erro em runtime, aceitável no dev |
| O `payment-basket-checkout-consumer` chama `CreatePaymentHandler.CreateNewPayment` direto, sem MediatR e sem o validador | `Payment.Function/.../BasketCheckout/Endpoint.cs` | O Luhn nunca rodou. A validação nasce no `RegisterPayment` (T5) |
| O `PaymentRequestedRule` já casa uma transição MODIFY (`AwaitingPoints → Pending`) | `PaymentRequestedRule.cs` | A transição `Registered → Pending` (T7) segue o mesmo molde |
| `ReservePointsAsync` já distingue `Reserved`/`Failed`/`AlreadyProcessed` pelo índice do item que falhou | `DynamoPlayerProgressRepository.cs:200-246` | O `challenges-reserve-points` (T17) reaproveita a transação. A diferença é que ele não grava a linha `Failed` |
| `PointsTransaction.CancelOrder` já faz `Reserved → Released` e `Used → Refunded` | `PointsTransaction.cs:66-69` | O `ReleasePoints` (T17) e o `UsePoints` (T27) reaproveitam o domínio sem regra nova |
| O Ordering cria o pedido pelo inbox por `evt.Id`, não por `OrderId` | `Ordering.Function/.../BasketCheckout/Endpoint.cs` | O starter (T25) troca isso por um Put condicional por `OrderId` |
| `Order.TotalPrice` é calculado pela soma das linhas | `Order.cs:118` | `ChargedTotal`/`PointsUsed` são campos novos, persistidos, e não substituem o cálculo (T24) |
| O `aws-cdk-lib` suporta `QueryLanguage.JSONATA` | `infra/node_modules/aws-cdk-lib/aws-stepfunctions` | O `DeriveOrderId` usa `$hash` (SHA-256) + `$substring` em JSONata para montar o GUID, sem Lambda. O `node_modules` local está na 2.260 e o `package.json` pede a 2.265: `npm ci` na T0 |
| O `local.ts` chama as Lambdas pelo emulador do Aspire (`invokeLambda`) | `local.ts:109` | Toda Lambda nova chamada pela saga precisa de registro no `src/AppHost/*Extensions.cs` para o dev local |
| O `CLAUDE.md` diz que o PaymentGateway é chamado por Lambda Invoke, mas hoje ele é um consumer de `PaymentRequestedEvent` | `CLAUDE.md` vs. `PaymentGateway.Function/.../PaymentRequested/Endpoint.cs` | O texto fica correto depois da fase 3. A T33 atualiza o `CLAUDE.md` |
| O branch atual (`feat/points-redemption`) foi mergeado no #176 e o remoto não existe mais | `git status`, PR #176 | O trabalho começa num branch novo a partir de `origin/development` (T0) |

## Decisões de arquitetura (do plano)

1. **ADR primeiro em cada fase:** a ADR-0050 (fase 1), a emenda à ADR-0048 (fase 2) e a ADR-0051 (fase 3) são a primeira tarefa da fase, escritas com a skill `adr` e revisadas pelo `adr-guardian`.
2. **Tolerância, depois limpeza:** o Basket aceita o request antigo e o novo (T9), o `CheckoutedRule` casa MODIFY e INSERT (T9), e o consumer do Payment cria ou promove (T7). A forma antiga sai na T14, depois do deploy da saga. Na fase 3, os consumers da coreografia saem na T30, depois que o starter e a saga estão no ar.
3. **Baseline antes de trocar:** a latência do `checkoutBasket` atual é medida no dev (T0) antes de qualquer deploy, para o critério de latência do módulo 1 ter com o que comparar.
4. **Onde as state machines vivem:** a `checkout-saga` fica na `AppSyncStack`, ao lado da `product-create-saga`, porque é chamada só pelo AppSync. A `order-saga` fica na `OrderingStack`, porque o Ordering é dono do ciclo do pedido e o starter é dele.
5. **Logs das sagas sem dados de execução:** `LogLevel.ERROR` com `includeExecutionData: false` nas duas, para o cartão (Express) e o endereço nunca irem para o CloudWatch.
6. **Scaffolds e revisões:** consumers e publishers novos via `cdc-integration-scaffold`, testes faltantes via `handler-test-backfill`, e `adr-guardian` em cada checkpoint.
7. **`local.ts` acompanha cada mudança de contrato GraphQL,** na própria fase, nunca como dívida para o fim.

## Execução: o que roda em subagente

Seguindo a regra de só usar subagente quando a trilha é independente, substancial e compensa o custo de partida:

| Fase | Trilha em subagente (worktree) | Por quê | Inline |
|---|---|---|---|
| 1 | **Payment** (T5 a T7) | Serviço isolado, 3 tarefas, contrato fixado na T4 | T0 a T4, T8 a T15: o contrato, o Basket (toca o mesmo request que a saga consome) e a saga/SPA (caminho crítico) |
| 2 | — | São 6 tarefas em sequência, e quase todas tocam a saga | Tudo |
| 3 | **Ordering** (T24, T25) e **Payment + Gateway** (T26) | Serviços isolados, com o contrato de entrada e saída dos passos fixado na T26 | T22, T23, T27 a T33: o Challenges é pequeno, e a saga, o cutover e a documentação são caminho crítico |

No máximo 2 trilhas em paralelo, integradas uma de cada vez. O `adr-guardian` e o `code-reviewer` rodam em background nos checkpoints.

## Grafo de dependências

```
T0 branch + baseline + npm ci
Fase 1 checkout-saga
  T1 ADR-0050 ─> T2 Function URL
  T1 ─> T3 Pricing Lines ─┐
  T1 ─> T4 contratos ─────┼─> [Payment] T5 domínio ─> T6 Lambdas register/void ─> T7 consumer promove
                          ├─> T8 Version do carrinho ─> T9 Basket checkout atômico (tolerante)
                          └─> T10 saga: Derive/LoadCart/CatalogLines/Quote ─> T11 saga: Register/Checkout/Void
  T3,T6,T9,T11 ─> T12 resolver HTTP + schema ─> T13 local.ts + SPA
  T12,T13 deployados ─> T14 limpeza (cartão fora do evento, formas antigas, checkoutId obrigatório) ─> T15 docs
  ─> Checkpoint 1
Fase 2 checkout-points-reservation
  T16 emenda ADR-0048 ─> T17 Lambdas reserve/release ─> T18 saga: ReservePoints + compensação ─> T19 local.ts + SPA
  T18 deployado ─> T20 remover reserva assíncrona e AwaitingPoints ─> T21 docs ─> Checkpoint 2
Fase 3 order-saga
  T22 ADR-0051 ─> T23 contratos dos passos ─┬─> [Ordering] T24 ChargedTotal ─> T25 starter + Complete/Cancel
                                            ├─> [Payment] T26 gateway authorize + payment-authorize + regras de fato
                                            └─> T27 Challenges use-points
  T25,T26,T27 ─> T28 order-saga (Standard) + alarme ─> T29 reconciliadores
  T28 deployado ─> T30 cutover: remover a coreografia ─> T31 local.ts ─> T32 SPA "Meus pedidos" ─> T33 docs
  ─> Checkpoint 3
```

## Ordem de deploy no dev

Cada tarefa é desenhada para funcionar com qualquer ordem de deploy das stacks que toca. Mesmo assim, quando eu fizer o deploy manual no dev para validar um checkpoint (com aprovação), sigo esta ordem: o serviço de origem primeiro (Pricing, Payment, Basket, Challenges, Ordering, PaymentGateway), depois a `AppSyncStack` (que contém a `checkout-saga`), e por último a SPA (SST).

## Riscos e mitigação

| Risco | Impacto | Mitigação |
|---|---|---|
| A saga substitui o caminho de **todo** checkout, com ou sem pontos | Alto | Forma tolerante até a T14. O `local.ts` espelha a saga na mesma fase (T13). O checkpoint exige checkout sem pontos, com pontos e com dinheiro no dev |
| Erro de mapeamento entre `Fail.error`/`cause` da saga e o `errorType`/`errorInfo` que o SPA lê | Alto | O resolver HTTP segue o molde do `createProductWithPrice`. Há um caso de teste manual por `errorType` no checkpoint 1 (6 tipos) |
| `DeriveOrderId` em JSONata gera um GUID diferente entre o caminho novo e a expectativa do Basket | Médio | O Basket recebe o `OrderId` pronto e nunca deriva. A derivação existe num só lugar (a saga) e no `local.ts`, que usa a mesma fórmula, testada com vetores fixos (T10, T13) |
| Pagamentos ou reservas presos durante as trocas de forma | Médio | Premissa do mapa: dados em andamento no dev são descartáveis. Os reconciliadores (T29) limpam o resto |
| A troca de MODIFY por INSERT no `CheckoutedRule` perde ou duplica evento | Médio | Na forma tolerante (T9), cada escrita de checkout produz exatamente um dos dois formatos, nunca os dois. Há teste unitário dos dois casos |
| Remoção de recursos AWS (Lambdas, regras, filas, tabelas de inbox) | Médio | As remoções de código e de CDK estão nas tarefas de limpeza (T14, T20, T30). Apagar tabela com dados (`ordering-processed-events`, `payment-processed-events`) é ação separada, pedida com evidência na hora |
| Validação no dev exige um usuário Cognito logado | Médio | Passo só seu (abaixo). O resto é validado via SPA no Chrome com a sua sessão, ou você executa os roteiros |

## Verificação padrão (Definition of Done de toda tarefa)

```bash
dotnet build DuckStore.slnx
dotnet test <projeto de teste do serviço tocado>
dotnet format DuckStore.slnx --verify-no-changes
cd infra && npm run build && npx cdk synth <Stack tocada>           # se tocou infra/
cd src/WebApps/Shopping.Web.SPA.React && pnpm lint && pnpm typecheck && pnpm build   # se tocou a SPA
```

O `pr-checks.yml` roda o mesmo conjunto (`dotnet build`/`test`, `pnpm lint`/`typecheck`, `npm run build` do infra).

## Aprovações pedidas agora

| Ação | Alvo | Impacto | Reversível |
|---|---|---|---|
| Criar o branch `feat/checkout-sagas` a partir de `origin/development` | local | Os arquivos já modificados (`serverless.template`, `next.config.mjs`, `sst-env.d.ts`, `docs/social/`, a deleção de `tasks/` antigo) vêm junto e **não entram nos meus commits** | Sim |
| Commits locais por tarefa | `feat/checkout-sagas` | Hook de `dotnet format` ativo | Sim |
| Push do branch | `origin/feat/checkout-sagas` | Só o branch, sem PR | Sim |
| Deploy no dev em cada checkpoint (e o baseline da T0, só leitura e checkouts de teste) | AWS dev: `PricingStack`, `PaymentStack`, `BasketStack`, `ChallengesStack`, `OrderingStack`, `AppSyncStack`, `MonitoringStack` e a SPA via SST | Atualiza Lambdas, a state machine, resolvers e regras. Remove os recursos listados nas tarefas de limpeza (Lambdas, regras, filas). **Não apaga tabelas** | Sim, por redeploy do commit anterior, exceto os dados gravados nos testes |
| Um PR por fase para `development` | GitHub | O título e o resumo são mostrados antes de abrir | Sim |

## Passos só seus (de uma vez)

1. Estar logado no SPA do dev no Chrome, com um usuário que tenha pontos, para as validações dos checkpoints. Ou, se preferir, executar você mesmo os roteiros de validação que eu deixo em cada checkpoint.
2. Credenciais AWS do dev ativas no terminal (`aws sso login` ou equivalente) para os `cdk diff`/`cdk deploy`.

## Perguntas abertas

Nenhuma. As duas perguntas do plano foram respondidas: um PR por fase, e deploy no dev pré-aprovado por checkpoint.
