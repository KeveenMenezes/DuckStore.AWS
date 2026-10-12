# Tasks: Checkout e Pedido Orquestrados (Checkout Sagas v1)

> Plano: [tasks/plan.md](./plan.md). Toda tarefa cumpre a "Verificação padrão" do plano (build, testes do serviço, format, `cdk synth` e `pnpm lint/typecheck/build` quando aplicável), além do que estiver listado nela.

---

## Preparação

- [x] **T0: Branch, dependências e baseline de latência** (S) — feito: branch criado, `npm ci` (aws-cdk-lib 2.265.0) e templates regenerados no commit de preparação. **Baseline (pipeline antigo, dev, 2026-10-12, `Checkout.E2ETests` `Category=Latency`, 10 chamadas aquecidas): p50 = 590 ms, p95 = 830 ms** (mín. 402, máx. 830)
  - Aceite: branch `feat/checkout-sagas` criado a partir de `origin/development`, sem incluir as mudanças locais pré-existentes nos commits; `npm ci` em `infra/` (aws-cdk-lib 2.265); p50 e p95 de 10 `checkoutBasket` aquecidos no dev registrados nesta tarefa.
  - Verificar: `git log origin/development..HEAD` vazio no início; `npm ls aws-cdk-lib` = 2.265.0; números anotados aqui.
  - Arquivos: nenhum de código. Dependências: aprovação de branch e dev.

## Fase 1 — `checkout-saga` ([spec](../SPEC-checkout-saga.md))

- [x] **T1: ADR-0050, checkout como saga Express** (XS) — feito, status **Proposed** (vira Accepted no PR da fase 1, ADR-0000). Duas passadas do `adr-guardian`: a 1ª reprovou (4 bloqueantes: condição do Delete com o carrinho apagado, void em erro ambíguo, replay do registro, corrida da reserva de pontos com `Registered`; 10 a corrigir: emendas não declaradas a 0008/0016/0043, cartão em log e no `PaymentRequestedEvent`, etc.); a 2ª confirmou tudo resolvido e apontou 2 novos (orçamento de timeout, renovação do `checkoutId`), aplicados. Specs dos módulos 1 e 2 e tarefas T5–T14 alinhadas
  - Aceite: a ADR cobre a saga, o `checkoutId`, o `OrderId` derivado, o fechamento atômico com versão, as linhas do Pricing e do Catalog, o `RegisterPayment` e o cartão fora do evento; emenda as ADRs 0048 §4, 0025 e 0038; escrita com a skill `adr` e indexada em `docs/adr/README.md`.
  - Verificar: `adr-guardian` sem conflito com ADR aceita.
  - Arquivos: `docs/adr/0050-*.md`, `docs/adr/README.md`. Dependências: T0.

- [x] **T2: Remover a Function URL pública do checkout** (XS) — feito; `cdk synth DuckStoreBasketStack` sem `AWS::Lambda::Url` e sem o output; nada lia o export (nem o `sst.config.ts`). O `cdk diff` contra o dev fica para o checkpoint 1 (precisa de credenciais)
  - Aceite: sem `addFunctionUrl` no `basket-checkout-basket`, sem a propriedade `checkoutBasketUrl` e sem o `CfnOutput`/export `CheckoutBasketUrl`; nada importa o export.
  - Verificar: `npx cdk diff BasketStack` mostra só a remoção do `AWS::Lambda::Url`, da permissão e do output; `grep -rn CheckoutBasketUrl infra/` vazio.
  - Arquivos: `infra/constructs/basket-lambdas.ts`, `infra/stacks/basket-stack.ts`. Dependências: T1.

- [x] **T3: Pricing devolve `Lines` na cotação** (S) — feito; `CartDiscountAllocation.LineValuesFrom` (mesma política de `TotalReductionFrom`, testado que as duas concordam), `CartPricingBreakdown.LineUnitPrices`, linhas tiradas antes da recotação com pontos; 7 testes novos, 125 verdes
  - Aceite: `GetBasketInstallmentPlanResponse.Lines: [{ ProductId, Quantity, UnitPrice }]` com o preço unitário pós-campanha (alocação ADR-0043); `UnitPrice` = parte da linha no preço pós-campanha ÷ quantidade, arredondado ao centavo (Σ pode diferir pelo arredondamento, no máximo meio centavo por unidade — ADR-0050 §5); a alocação por linha mora no `CartDiscountAllocation` e concorda com `TotalReductionFrom`; a query `basketInstallmentPlan` não muda.
  - Verificar: `dotnet test tests/Services/Pricing/Pricing.UnitTests` com casos de 1 item, vários itens com campanha e arredondamento.
  - Arquivos: `Modules/Prices/Features/GetBasketInstallmentPlan/{Endpoint,Handler,Query}.cs`, domain service de alocação, testes. Dependências: T1.

- [x] **T4: Contratos da saga (tipos e nomes)** (S) — feito; `CheckoutBasketRequest` com `CheckoutId`/`CartVersion` opcionais (fora do DTO, para não vazar no evento) e `CheckoutBasketResponse.ErrorType`; `RegisterPayment*`/`VoidPayment*` no Payment, registrados no serializer AOT, com `ToString` redigido e teste; ADR-0050 §10 com a tabela de contratos. As linhas e o `OrderId` da saga viajam no próprio `BasketCheckoutDto` (`Items`, `OrderId`). Replay do registro compara valor + `CardDetails` (cobre forma e parcelas, que mudam o valor)
  - Aceite: definidos, sem comportamento: o `CheckoutBasketRequest` novo (`CheckoutId`, `OrderId`, `CartVersion`, `Lines`, sem cartão) **aceitando também o formato antigo**; os records de request e response de `payment-register`/`payment-void`; os nomes de função; o formato do `cause` de cada `Fail` (`errorType` → campos de `errorInfo`); a fórmula do `OrderId` com 3 vetores de teste fixos.
  - Verificar: build verde; contratos registrados na seção 3 da ADR-0050.
  - Arquivos: `Basket.Function/.../CheckoutBasket/Endpoint.cs`, `Payment.Function/.../Features/{RegisterPayment,VoidPayment}/Contracts.cs`, ADR-0050. Dependências: T1.

- [x] **T5: Payment — domínio `Registered`/`Voided` e validação do cartão** (M) — *trilha Payment (subagente)* — feito (`c786b7a`); validação em `CardDetails.Validated(..., TimeProvider)` (o `Of` segue estrutural para dados legados `MM/YY`), Luhn em `Domain/Services/Luhn.cs`, `Void()`/`Promote()` devolvem `bool`, `ConfirmPoints`/`RejectPoints` lançam `PaymentNotYetPromotedException` com `Registered`
  - Aceite: `PaymentStatus.Registered` e `Voided` (acrescentados no fim do enum); `Payment.Register(...)` com validação (Luhn, `MM/AAAA` não vencida, CVV de 3 ou 4 dígitos; dinheiro dispensa cartão) que lança `InvalidCardException(field)`; `Void()` só a partir de `Registered`, no-op nos demais; `IsSameRegistration(amount, method, installments)` para o replay; o command de registro redige o cartão no `ToString`; `CardDetails.Of` aceita CVV de 4 dígitos; `Promote(awaitingPoints)` faz `Registered → Pending/AwaitingPoints`; `PaymentId` derivado do `OrderId` (um pagamento por pedido).
  - Verificar: `dotnet test tests/Services/Payment/Payment.UnitTests` com casos de cada regra de validação e de cada transição.
  - Arquivos: `Domain/Entities/Payment.cs`, `Domain/Enums/PaymentStatus.cs`, `Domain/Exceptions/InvalidCardException.cs`, testes. Dependências: T4.

- [x] **T6: Payment — Lambdas `payment-register` e `payment-void`** (M) — *trilha Payment* — feito (`a606ec3`); handlers fora do MediatR (nada no caminho do cartão passa pelo `LoggingBehavior`), Put condicional por `PaymentId = OrderId`, void condicionado a `Status = Registered`, `GetByIdAsync` sempre consistente; CDK sem Function URL e AppHost
  - Aceite: endpoints Invoke (`[LambdaFunction]`); o register grava condicionado ao `PaymentId`; a repetição só devolve o pagamento se ainda `Registered` com os mesmos termos, senão `CheckoutRetryRequired`; `InvalidCard` e `CheckoutRetryRequired` voltam como recusa tipada (`{ ErrorType, Message, Field }`), não como exceção da Lambda; o void é idempotente; CDK (função, grants, AOT) e registro no AppHost.
  - Verificar: testes unitários dos handlers; `npx cdk synth PaymentStack`; no Aspire, invocar as duas pelo emulador.
  - Arquivos: `Features/{RegisterPayment,VoidPayment}/*`, `Data/DynamoPaymentRepository.cs`, `infra/constructs/payment-lambdas.ts`, `src/AppHost/PaymentExtensions.cs`, testes. Dependências: T5.

- [x] **T7: Payment — consumer de checkout promove em vez de criar (tolerante)** (M) — *trilha Payment* — feito (`b9b1ccd`); promoção condicionada a `Status = Registered` dentro da transação do inbox; o consumer ganhou leitura na tabela `payments`. 140 testes do Payment verdes
  - Aceite: com pagamento `Registered` do pedido, o consumer promove (`AwaitingPoints` se `PointsToUse > 0`, senão `Pending`); sem pagamento registrado, cria como hoje (forma antiga, até a T14); o `PaymentRequestedRule` casa `Registered → Pending` e **mantém** INSERT `Pending` (caminho antigo); o gateway nunca é acionado para `Registered`; o `PointsReservationHandler` lança erro quando o pagamento ainda está `Registered` (ADR-0050 §6).
  - Verificar: testes do consumer (os dois caminhos), da regra (INSERT `Registered` não casa; MODIFY `Registered → Pending` casa; INSERT `Pending` casa) e do handler de pontos (`Registered` lança).
  - Arquivos: `EventsIntegration/Consumers/{BasketCheckout,PointsReservation}/*`, `EventsIntegration/Publishers/Rules/PaymentRequestedRule.cs`, testes. Dependências: T6.

- [x] **T8: `Version` no carrinho** (S) — feito; `BuildItem` (usado pelo `MergeAsync`/`StoreCart`), o resolver `storeBasket` (`util.autoId()`) e o `local.ts` gravam um token novo por escrita; teste do repositório com `IAmazonDynamoDB` mockado
  - Aceite: o `storeBasket` grava `Version = util.autoId()` a cada escrita; o `MergeAsync` grava uma `Version` nova; a query `basket` não muda; o `local.ts` grava `Version` no `storeBasket` local.
  - Verificar: `npx cdk synth AppSyncStack`; teste do `MergeAsync`; no Aspire, o item na tabela ganha `Version` a cada alteração do carrinho.
  - Arquivos: `graphql/resolvers/basket/mutations/Mutation.storeBasket.js`, `Basket.Function/.../DynamoShoppingCartRepository.cs`, `app/api/graphql/local.ts`, teste. Dependências: T4.

- [x] **T9: Basket — checkout atômico e idempotente (tolerante)** (M) — feito; `CloseCartAsync` (Put `CHECKOUT#<owner>#<checkoutId>` + Delete condicionado; falha do Put tem precedência = replay; só o Delete = `CartChanged`; outros motivos relançados como erro ambíguo), `CartChanged` devolvido como dado, regra em INSERT e MODIFY; 10 testes novos, 35 verdes
  - Aceite: com `CheckoutId` no request, um `TransactWriteItems` (Put `CHECKOUT#<owner>#<checkoutId>` condicional + Delete do carrinho condicionado à `Version`); Delete condicionado a `Version = :v` ou, sem versão lida, a `attribute_exists(OwnerId) AND attribute_not_exists(Version)` (nunca o OR nu, ADR-0050 §4); a falha do Put tem precedência e é replay (devolve o `OrderId` gravado); a falha só do Delete é `CartChanged` (recusa tipada); usa as linhas e o `OrderId` do request sem reler o carrinho. Sem `CheckoutId`, o caminho antigo (até a T14). O `CheckoutedRule` casa INSERT do `CHECKOUT#` e MODIFY `Type = Checkout`. `ExpiresAt` de 24 h no registro.
  - Verificar: `dotnet test tests/Services/Basket/Basket.UnitTests`: feliz, replay, versão mudou, carrinho antigo sem `Version`, carrinho já apagado por outra tentativa (`CartChanged`), os dois formatos da regra.
  - Arquivos: `Features/CheckoutBasket/{Handler,Command,Endpoint}.cs`, `Data/{IShoppingCartRepository,DynamoShoppingCartRepository}.cs`, `Publishers/Rules/CheckoutedRule.cs`, `ShoppingCartStreamImage` (se precisar), testes. Dependências: T4, T8.

- [x] **T10: `checkout-saga` — Derive, LoadCart, CatalogLines, Quote** (M) — feito junto com a T11 (um construct só); ASL JSONata explícito em `infra/constructs/checkout-saga.ts`, instanciado na `AppSyncStack`. Verificado por um simulador de ASL no scratchpad (lib `jsonata` oficial + polyfills de `$hash`/`$uuid`/`$parse`, tasks mockadas): 16 cenários, incluindo os 3 vetores do `OrderId`, carrinho de 1 item (arrays), mesmo produto 2x, replay antes do carrinho, `ProductUnavailable`, `UnprocessedKeys` ≠ produto apagado. **Pendente para o checkpoint:** `aws stepfunctions test-state` no `DeriveOrderId` com os vetores (confirma o `$hash` real)
  - Aceite: construct `infra/constructs/checkout-saga.ts` (Express, JSONata, `LogLevel.ERROR`, `includeExecutionData: false`, X-Ray); `DeriveOrderId` bate com os 3 vetores da T4; `LoadCart` faz `BatchGetItem` consistente do carrinho e do `CHECKOUT#`, verifica o replay **antes** do `BadRequest` (carrinho vazio ou inexistente) e devolve itens e versão; `CatalogLines` com `BatchGetItem` em `products` e `ProductUnavailable`; `Quote` com retry transitório e `Rejection` → `Fail`. Ainda sem ligação ao AppSync.
  - Verificar: `npx cdk synth AppSyncStack`; teste da fórmula de derivação (vetores) num teste de infra ou no `local.ts` (T13); revisão visual do ASL sintetizado.
  - Arquivos: `infra/constructs/checkout-saga.ts`, `infra/stacks/appsync-stack.ts`. Dependências: T3, T4.

- [x] **T11: `checkout-saga` — RegisterPayment, Checkout, VoidPayment** (M) — feito; recusas terminam `SUCCEEDED` com `refusal` (decisão de desenho registrada na ADR-0050 §8: assim `ExecutionsFailed`/`ExecutionsTimedOut` medem só falha real e são alarmados direto, sem metric filter); `Checkout` com retry também em `TransactionCanceledException`; erro ambíguo → `InternalError` sem void; execução de 25 s (orçamento 21 s / 23 s no código); IAM explícito (GetItem, BatchGetItem, InvokeFunction nas 4 funções)
  - Aceite: `RegisterPayment` → `Checkout` → `Output`; `InvalidCard`/`CheckoutRetryRequired`/`CartChanged` como `Fail` com `cause` JSON; `Checkout` com retry em erro transitório e timeout; **só** a recusa `CartChanged` → `VoidPayment` (retry 3x) → `Fail(CartChanged)`, e `CompensationFailed` se a compensação esgotar; erro ambíguo esgotado → `Fail(InternalError)` **sem** void; metric filter + alarme no `duckstore-alerts` para `CompensationFailed`/`InternalError`; orçamento de timeout da ADR-0050 §1 (execução de 20 s; Σ timeout × (1 + tentativas) + backoff abaixo disso no caminho mais lento); confirmar que o log de erro com `includeExecutionData: false` traz o nome do `error`, senão alarmar por `ExecutionsFailed`; grants de invoke para Pricing, Payment e Basket.
  - Verificar: `npx cdk synth AppSyncStack`; `cdk diff AppSyncStack` mostra a state machine e o IAM.
  - Arquivos: `infra/constructs/checkout-saga.ts`. Dependências: T6, T9, T10.

- [x] **T12: Resolver HTTP no lugar do pipeline** (M) — feito; mesmo construct id → mesmo logical ID (`AppSyncApiCheckoutBasketResolver5144D868`), atualização no lugar `PIPELINE → UNIT` no `SfnDS`; os 3 passos JS, as 3 AppSync Functions e o `CheckoutDS` saíram; recusa = execução `SUCCEEDED` com `refusal`, `FAILED`/`TIMED_OUT`/HTTP ≠ 200 = `InternalError` sem detalhe; ADR-0009 §3 atualizada
  - Aceite: `checkoutId: ID` opcional no `CheckoutInput`; `Mutation.checkoutBasket.js` chama `StartSyncExecution` com o dono do token e mapeia `SUCCEEDED`/`FAILED` → `{ isSuccess, orderId }` ou erro com `errorType`/`errorInfo`, e qualquer `error` fora da tabela da ADR-0050 §8 vira `InternalError` sem `cause`; a linha do `checkoutBasket` na tabela da ADR-0009 §3 passa a "HTTP → saga Express, critério 2"; sem `checkoutId`, a saga usa `$uuid()`; os 3 arquivos de passo e o `pipelineResolver` do checkout são apagados; os data sources que ficarem sem uso saem.
  - Verificar: `npx cdk synth AppSyncStack`; `cdk diff` com a troca do resolver; `grep -rn checkoutBasket.loadCart` vazio.
  - Arquivos: `graphql/schema.graphql`, `graphql/resolvers/basket/mutations/Mutation.checkoutBasket*.js`, `infra/constructs/appsync-api.ts`, `src/WebApps/Shopping.Web.SPA.React/graphql/types.ts`, `docs/adr/0009-*.md`. Dependências: T11.

- [x] **T13: `local.ts` e SPA no contrato novo** (M) — código feito; `local.ts` imita a saga passo a passo (derivação em `lib/checkout-order-id.ts`, 3 vetores da ADR verificados com `node --experimental-strip-types`), SPA com `checkoutId` por tentativa (renova em sucesso ou recusa tipada, mantém em `InternalError`/rede) e tratamento de `CartChanged`/`ProductUnavailable`/`InvalidCard`/`CheckoutRetryRequired`/`InternalError`; `typecheck` ok, `lint` sem erro novo. **Validação local feita (Aspire + SPA local + Lambdas reais no emulador, sessão de teste no DynamoDB Local):** 8 cenários verdes — caminho feliz com o `OrderId` derivado, replay com o mesmo `checkoutId`, carrinho já fechado (`BadRequest`), `PriceChanged` com `serverTotal`, `InvalidCard` no `CardNumber`, `checkoutId` inválido, endereço em branco, dinheiro sem cartão; e o estado gravado conferido (registro `CHECKOUT#` com nome do Catalog e preço do Pricing no lugar dos valores forjados, sem cartão; pagamento `Registered` com `Id = OrderId`). Para subir as Lambdas localmente foi preciso contornar um problema **pré-existente** do ambiente (ver nota no fim deste arquivo). **Pendente para o checkpoint 1:** o `pnpm build` (o SSG de `/` precisa de backend GraphQL; o CI só roda lint + typecheck)
  - Aceite: o `local.ts` imita a saga passo a passo (mesma derivação, mesmos `errorType`, register/void pelo emulador); o SPA gera o `checkoutId` ao abrir o checkout e o mantém nos retries, renovando só depois de sucesso ou de recusa tipada (`InternalError`/rede mantêm o id); `CartChanged`/`ProductUnavailable` recotam e pedem confirmação, e `InvalidCard` marca o campo do cartão.
  - Verificar: `pnpm lint && pnpm typecheck && pnpm build`; no Aspire: checkout feliz, duplo clique (um pedido), carrinho alterado em outra aba (`CartChanged`) e cartão inválido (`InvalidCard`).
  - Arquivos: `app/api/graphql/local.ts`, `features/checkout/{hooks/use-checkout.ts,services/checkout.service.ts,components/*}`. Dependências: T12.

- [x] **T14: Limpeza das formas antigas e cartão fora do evento** (M) — feito; o consumer do Payment só promove (`PromotePaymentCommand`, lança erro sem pagamento registrado) e o caminho de criação antigo, o validador e o `CreatePaymentTests` saíram; o evento e o DTO do Basket sem campos de cartão; o Basket sem o caminho antigo (`MarkCheckoutAsync`/`DeleteBasket` removidos, `CheckoutId` obrigatório); `CheckoutedRule` só em INSERT; `PaymentRequestedRule` sem INSERT `Pending` e **com `Voided → Pending`** (lacuna da correção do code-review: a promoção de um pagamento anulado não chegava ao gateway); `checkoutId: ID!` no schema, no resolver, na saga (sem `$uuid()`), no `local.ts` e no SPA; ADR-0050 §3/§6/§7 atualizadas
  - Aceite: `BasketCheckoutDto`/`BasketCheckoutEvent` sem `CardName`/`CardNumber`/`Expiration`/`Cvv` (consumers ajustados: Payment, Ordering, Challenges); o Basket sem o caminho sem `CheckoutId`; o `CheckoutedRule` só em INSERT; o consumer do Payment só promove, e um pedido sem pagamento registrado lança erro; o `PaymentRequestedRule` deixa de casar INSERT `Pending`; `checkoutId` obrigatório no schema e no `local.ts`.
  - Verificar: build e testes de todos os serviços que referenciam o evento; `grep -rn "CardNumber" src/BuildingBlocks src/Services/Basket` vazio.
  - Arquivos: `BuildingBlocks.Messaging/Events/BasketCheckoutEvent.cs`, `Basket.Function/.../Dtos/BasketCheckoutDto.cs`, os mappers dos consumers, `CheckoutedRule.cs`, consumer do Payment, `graphql/schema.graphql`, `local.ts`, testes. Dependências: checkpoint parcial no dev (T13 no ar).

- [x] **T15: Documentação da fase 1** (S) — feito; `CLAUDE.md` (Basket, Payment — corrigida também a descrição do gateway, que é por eventos e não por Lambda invoke —, Pricing, a seção do AppSync, comandos e a categoria E2E em Testing); READMEs de Basket, Payment e Pricing; diagramas Basket (saga, transação `CHECKOUT#`, INSERT), Payment (faixa nova com saga, `payment-register` e `payment-void`, ciclo renumerado ①–⑥) e Pricing (passo `Quote` da saga); validador sem achados, antes e depois
  - Aceite: `CLAUDE.md` (Basket e o `checkoutBasket` como saga, Payment com register/void, sem cartão no evento); READMEs de Basket, Payment e Pricing; diagrama atualizado com a skill `architecture-diagrams`, e o validador sem drift.
  - Verificar: o validador da skill passa.
  - Arquivos: `CLAUDE.md`, `src/Services/{Basket,Payment,Pricing}/README.md`, `docs/duckstore-process-flow.drawio` + SVG. Dependências: T14.

### Checkpoint 1 — checkout-saga

> **Deploy e validação no dev (2026-10-12):** deploy local com o CDK (o environment `development` do GitHub só aceita o branch `development`), na ordem Pricing → Payment → Basket → AppSync → SPA (SST), com `cdk diff` antes de cada stack. Validação pela suíte `tests/E2E/Checkout` (12 testes verdes: cartão e dinheiro no navegador, cartão inválido, replay, envios concorrentes da mesma tentativa, `PriceChanged`, `BadRequest`, linhas com nome do Catalog e preço do Pricing lidas do pedido criado pelo Ordering) e por inspeção: Function URL inexistente na AWS, `test-state` do `DeriveOrderId` com os 3 vetores da ADR (o `$hash` real confere), pagamentos dos checkouts novos `Authorized` e pedidos `Completed`, nenhum pagamento preso em `Registered`/`Voided`/`Pending`, 24 registros `CHECKOUT#` sem cartão. Latência com a saga em 4 medições (10 chamadas aquecidas cada): **p50 de 506 a 717 ms, p95 de 740 a 838 ms**; baseline do pipeline antigo, uma medição: 590 / 830 — no pior caso +127 ms no p50, dentro do critério (≤ +200 ms). Depois da T14 a suíte E2E inteira passou de novo (12/12); a única intermitência vista (o checkout em dinheiro parado em "Calculating total...") veio de uma página do teste anterior que continuava sincronizando o carrinho, corrigida fechando as páginas ao fim de cada teste. O E2E achou um defeito que o simulador não pegava (campo de `Arguments` indefinido falha o estado no serviço real) — corrigido e redeployado. Não cobertos por E2E: `CartChanged` com anulação (precisa de concorrência entre cotar e fechar; coberto por testes de unidade) e `ProductUnavailable` (precisa apagar um produto do Catalog).
- [x] Testes de Basket, Payment e Pricing verdes; `cdk synth` limpo; `pnpm build` verde (o build do SST contra o dev).
- [x] `adr-guardian` e `code-reviewer` sobre o diff da fase (achados aplicados: orçamento de timeout, corrida void × close, recusas em vez de `InternalError` para entrada do cliente, defesas para deploy fora de ordem).
- [x] Deploy no dev (Pricing, Payment, Basket, AppSync, SPA) e validação dos critérios 1 a 11 do spec, incluindo um caso manual por `errorType` (`PriceChanged`, `PointsAboveCap`, `CartChanged`, `ProductUnavailable`, `InvalidCard`, `BadRequest`) e a latência contra o baseline da T0.
- [x] PR da fase 1 (título e resumo mostrados antes): [#179](https://github.com/KeveenMenezes/DuckStore.AWS/pull/179).

## Fase 2 — `checkout-points-reservation` ([spec](../SPEC-checkout-points-reservation.md))

- [ ] **T16: Emenda à ADR-0048 §5/§6** (XS)
  - Aceite: reserva síncrona na saga Express, recusa sem linha no ledger e fim do `AwaitingPoints`; referencia a ADR-0050.
  - Verificar: `adr-guardian`.
  - Arquivos: `docs/adr/0048-*.md`, `docs/adr/README.md`. Dependências: checkpoint 1.

- [ ] **T17: Challenges — Lambdas `challenges-reserve-points` e `challenges-release-points`** (M)
  - Aceite: o reserve reaproveita a transação de `ReservePointsAsync` **sem** gravar a linha `Failed` (saldo curto → recusa `InsufficientPoints` com `availablePoints`); `AlreadyProcessed` com linha `Reserved`/`Used` → `Reserved`; linha `Released` → `CheckoutRetryRequired`; o release usa `CancelOrder` + `SaveSettlementAsync` e é no-op sem linha; CDK e AppHost.
  - Verificar: `dotnet test tests/Services/Challenges/Challenges.UnitTests` (os 5 critérios do spec §5); `npx cdk synth ChallengesStack`.
  - Arquivos: `Modules/Progress/Features/{ReservePoints,ReleasePoints}/*`, `Data/DynamoPlayerProgressRepository.cs`, `infra/constructs/challenges-lambdas.ts`, `src/AppHost/ChallengesExtensions.cs`, testes. Dependências: T16.

- [ ] **T18: Saga — `ReservePoints` e compensação encadeada** (M) — **atenção ao orçamento de timeout:** com reserva e liberação (+5 s cada), o caminho mais lento passa dos 25 s; rebalancear (ex.: tentativas de 1 s nas leituras, ou menos retries no `ReleasePoints`) e recalcular no comentário do construct
  - Aceite: `Choice` em `pointsToUse > 0` → `ReservePoints` antes do `RegisterPayment`; falha em `RegisterPayment` → `ReleasePoints`; falha em `Checkout` → `VoidPayment` → `ReleasePoints`; os dois terminam com o erro original; `InsufficientPoints` e `CheckoutRetryRequired` como `Fail`; o consumer do Payment promove sempre para `Pending` (sem `AwaitingPoints`).
  - Verificar: `npx cdk synth AppSyncStack PaymentStack`; testes do consumer.
  - Arquivos: `infra/constructs/checkout-saga.ts`, `Payment.Function/.../Consumers/BasketCheckout/*`, testes. Dependências: T17.

- [ ] **T19: `local.ts` e SPA — recusa de pontos síncrona** (S)
  - Aceite: o `local.ts` troca o check de saldo (`:1045-1051`) por reserva → register → checkout com compensação, igual à saga; o SPA trata `InsufficientPoints` como `PointsAboveCap` (atualiza o saldo e ajusta o campo) e renova o `checkoutId`.
  - Verificar: `pnpm build`; no Aspire: saldo curto (recusa e nada gravado), cartão inválido com pontos (saldo volta).
  - Arquivos: `app/api/graphql/local.ts`, `features/checkout/hooks/use-checkout.ts`. Dependências: T18.

- [ ] **T20: Remover a reserva assíncrona e o `AwaitingPoints`** (M) — *depois do deploy de T17 a T19 no dev*
  - Aceite: saem `challenges-basket-checkout-consumer` (Lambda, regra, DLQ), `PointsReservedRule`, `PointsReservationFailedRule` e o `challenges-points-transactions-stream-publisher` (se ficar sem regras); no Payment, saem `AwaitingPoints`, `ConfirmPoints`/`RejectPoints`, os 2 consumers de pontos e a `PaymentDeclinedInsufficientPointsRule`; saem também `PointsReservedEvent`/`PointsReservationFailedEvent` e os registros no AppHost.
  - Verificar: build e testes de Challenges e Payment; `cdk diff ChallengesStack PaymentStack` só com remoções; `grep -rn "PointsReserved\|AwaitingPoints" src/` vazio.
  - Arquivos: `src/Services/{Challenges,Payment}/...`, `BuildingBlocks.Messaging/Events/*`, `infra/constructs/{challenges,payment}-lambdas.ts`, `src/AppHost/*Extensions.cs`, testes. Dependências: T19 no ar.

- [ ] **T21: Documentação da fase 2** (S)
  - Aceite: `CLAUDE.md` (Challenges e Payment sem a reserva assíncrona), READMEs, diagrama sem drift.
  - Verificar: validador da skill `architecture-diagrams`.
  - Arquivos: `CLAUDE.md`, `src/Services/{Challenges,Payment}/README.md`, diagrama. Dependências: T20.

### Checkpoint 2 — checkout-points-reservation
- [ ] Testes de Challenges e Payment verdes; `cdk synth` limpo; `pnpm build` verde.
- [ ] `adr-guardian` e `code-reviewer`.
- [ ] Deploy no dev (Challenges, Payment, AppSync, SPA) e critérios 1 a 7 do spec.
- [ ] PR da fase 2.

## Fase 3 — `order-saga` ([spec](../SPEC-order-saga.md))

- [ ] **T22: ADR-0051, orquestração do pedido** (XS)
  - Aceite: Standard para a transação do pedido e coreografia para avisos; emenda as ADRs 0004/0005, 0025, 0038 e 0048 §5; registra `ChargedTotal`/`PointsUsed`.
  - Verificar: `adr-guardian`.
  - Arquivos: `docs/adr/0051-*.md`, `docs/adr/README.md`. Dependências: checkpoint 2.

- [ ] **T23: Contratos dos passos da `order-saga`** (S)
  - Aceite: request e response de `ordering-complete-order`, `ordering-cancel-order`, `payment-authorize`, `paymentgateway-authorize` e `challenges-use-points`; o input da execução (`orderId`, `customerId`, `pointsUsed`); os nomes de função; nenhum campo de cartão ou endereço.
  - Verificar: build verde; contratos na ADR-0051.
  - Arquivos: `Contracts.cs` em cada serviço, ADR-0051. Dependências: T22.

- [ ] **T24: Ordering — `ChargedTotal` e `PointsUsed` no pedido** (M) — *trilha Ordering (subagente)*
  - Aceite: campos no `Order` (domínio, persistência e `Load`); `chargedTotal: Float` e `pointsUsed: Int` no tipo GraphQL `Order` (nulos em pedidos antigos) e nos resolvers de pedido; `local.ts` espelha.
  - Verificar: `dotnet test tests/Services/Ordering/Ordering.UnitTests`; `npx cdk synth AppSyncStack`.
  - Arquivos: `Domain/Entities/Order.cs`, `Data/DynamoOrderRepository.cs`, `graphql/schema.graphql`, `graphql/resolvers/orders/*`, `local.ts`, testes. Dependências: T23.

- [ ] **T25: Ordering — starter e passos Complete/Cancel** (M) — *trilha Ordering*
  - Aceite: `ordering-order-saga-starter` (regra em `BasketCheckoutEvent`) cria o pedido condicionado ao `OrderId`, com `ChargedTotal`/`PointsUsed`, e chama `StartExecution(name = orderId)`; pedido existente e `ExecutionAlreadyExists` são sucesso; `ordering-complete-order`/`ordering-cancel-order` aplicam `ApplyPaymentResult` (idempotente); CDK, DLQ e AppHost.
  - Verificar: testes do starter (feliz, redelivery, execução existente) e dos passos; `npx cdk synth OrderingStack`.
  - Arquivos: `Modules/Orders/{EventsIntegration/Consumers/OrderSagaStarter,Features/CompleteOrder,Features/CancelOrder}/*`, `infra/constructs/ordering-lambdas.ts`, `src/AppHost/OrderingExtensions.cs`, testes. Dependências: T24.

- [ ] **T26: Payment e PaymentGateway — autorização síncrona e eventos de fato** (M) — *trilha Payment (subagente)*
  - Aceite: `paymentgateway-authorize` devolve a decisão do `SimulatedGatewayDecision`; `payment-authorize` carrega o pagamento `Registered`/`Pending` do pedido, invoca o gateway, aplica e grava, e o replay devolve o resultado gravado; regras novas `PaymentAuthorizedRule`/`PaymentDeclinedRule` no stream de `payments` publicam os eventos de fato; CDK (grant de invoke Payment → Gateway) e AppHost.
  - Verificar: `dotnet test` de Payment e PaymentGateway; `npx cdk synth PaymentStack`.
  - Arquivos: `PaymentGateway.Function/Modules/Gateway/Features/Authorize/*`, `Payment.Function/.../Features/AuthorizePayment/*`, `Publishers/Rules/Payment{Authorized,Declined}Rule.cs`, `infra/constructs/payment-lambdas.ts`, `src/AppHost/PaymentExtensions.cs`, testes. Dependências: T23.

- [ ] **T27: Challenges — `challenges-use-points`** (S)
  - Aceite: `Reserved → Used` via `MarkUsed`/`SaveSettlementAsync`, idempotente e no-op sem linha; CDK e AppHost.
  - Verificar: testes; `npx cdk synth ChallengesStack`.
  - Arquivos: `Modules/Progress/Features/UsePoints/*`, `infra/constructs/challenges-lambdas.ts`, `src/AppHost/ChallengesExtensions.cs`, testes. Dependências: T23.

- [ ] **T28: `order-saga` (Standard) e alarme** (M)
  - Aceite: construct `infra/constructs/order-saga.ts` na `OrderingStack` (Standard, `includeExecutionData: false`, X-Ray): `AuthorizePayment` → `Choice` → `CompleteOrder` + (`UsePoints` se houver pontos) ou `CancelOrder` + (`ReleasePoints` se houver pontos); retry com backoff em todo passo; regra EventBridge em *Execution Status Change* `FAILED` → `duckstore-alerts`; grant `states:StartExecution` ao starter.
  - Verificar: `npx cdk synth OrderingStack`; `cdk diff` com a state machine, a regra e o IAM.
  - Arquivos: `infra/constructs/order-saga.ts`, `infra/stacks/ordering-stack.ts`. Dependências: T25, T26, T27.

- [ ] **T29: Reconciliadores de órfãos** (M) — **incluir (code-review do checkpoint 1, #9):** pagamentos `Registered` órfãos e `Voided` guardam o cartão completo (número e CVV) sem prazo; o reconciliador anula **e apaga os dados do cartão** desses registros (ou TTL nos dois status). O CVV já era persistido antes desta iniciativa; avaliar parar de guardá-lo depois do registro, já que o `SimulatedGatewayDecision` não o usa
  - Aceite: `challenges-redemption-reconciler` (`Reserved` há mais de 15 min sem execução `order-saga` → `Released`) e `payment-registration-reconciler` (`Registered` há mais de 15 min sem execução → `Voided`), agendados a cada 15 min pelo EventBridge Scheduler; consulta por índice esparso de pendentes, não por scan; `DescribeExecution` com o ARN derivado de `name = orderId`.
  - Verificar: testes (libera ou anula só os antigos e sem execução; ignora os que têm execução); `npx cdk synth ChallengesStack PaymentStack`.
  - Arquivos: `Modules/Progress/Features/ReconcileRedemptions/*`, `Payment.Function/.../Features/ReconcileRegistrations/*`, índices nos `*-dynamodb.ts`, `infra/constructs/{challenges,payment}-lambdas.ts`, testes. Dependências: T28.

- [ ] **T30: Cutover — remover a coreografia do pedido** (M) — *depois do deploy de T24 a T29 no dev*
  - Aceite: saem os itens da seção 2.2 do spec: os consumers `ordering-basket-checkout`, `ordering-payment-authorized`, `ordering-payment-declined`, `payment-basket-checkout`, `payment-result-authorized`, `payment-result-declined`, `paymentgateway-payment-requested`, `challenges-payment-authorized` e `challenges-order-cancelled`, mais o `PaymentRequestedRule` e o `PaymentRequestedEvent`, com Lambdas, regras, DLQs e registros no AppHost. As tabelas de inbox ficam (a remoção é pedida à parte, com evidência).
  - Verificar: build e testes de todos os serviços; `cdk diff` só com remoções nos recursos listados; `grep -rn "PaymentRequested" src/` vazio.
  - Arquivos: `src/Services/{Ordering,Payment,PaymentGateway,Challenges}/...`, `BuildingBlocks.Messaging/Events/PaymentRequestedEvent.cs`, `infra/constructs/*-lambdas.ts`, `src/AppHost/*Extensions.cs`, testes. Dependências: T28 e T29 no ar.

- [ ] **T31: `local.ts` — starter e saga em processo** (M)
  - Aceite: primeiro confirmar como o pedido nasce localmente hoje. Depois do checkout, o `local.ts` invoca pelo emulador o starter (criação do pedido) e os passos da saga na mesma ordem; o pedido local termina `Completed` ou `Cancelled`.
  - Verificar: no Aspire, checkout com cartão aprovado (pedido `Completed`, pontos `Used`) e recusado (`Cancelled`, pontos de volta).
  - Arquivos: `app/api/graphql/local.ts`, `src/AppHost/*Extensions.cs` (se faltar registro). Dependências: T30.

- [ ] **T32: SPA — "Meus pedidos" com o valor cobrado** (S)
  - Aceite: `/my-orders` mostra `chargedTotal` quando existe e a soma das linhas nos pedidos antigos; mostra os pontos usados quando houver.
  - Verificar: `pnpm lint && pnpm typecheck && pnpm build`; manual com um pedido com pontos.
  - Arquivos: `features/orders/*` (ou equivalente em `app/my-orders`), `api/queries/*`, `graphql/types.ts`. Dependências: T24.

- [ ] **T33: Documentação da fase 3** (M)
  - Aceite: `CLAUDE.md` (Ordering, Payment, PaymentGateway e Challenges orquestrados; gateway síncrono; seção do modelo serverless com orquestração + coreografia); READMEs dos 4 serviços; diagramas sem drift.
  - Verificar: validador da skill `architecture-diagrams`.
  - Arquivos: `CLAUDE.md`, `src/Services/{Ordering,Payment,PaymentGateway,Challenges}/README.md`, diagramas. Dependências: T30, T31, T32.

### Checkpoint 3 — order-saga
- [ ] Testes de todos os serviços verdes; `Ordering.FunctionalTests` verde (requer Docker); `cdk synth` de todas as stacks; `pnpm build` verde.
- [ ] `adr-guardian` e `code-reviewer`.
- [ ] Deploy no dev (Ordering, Payment, Challenges, AppSync, SPA) e critérios 1 a 11 do spec, com a inspeção de uma execução autorizada e de uma recusada no console.
- [ ] PR da fase 3.

---

## Nota: dev local quebrado antes desta iniciativa (fora do escopo)

Nenhuma Lambda sobe no Aspire hoje (todas ficam `Finished`). Duas causas, as duas no wrapper que o `Aspire.Hosting.AWS` 13.6.0 gera em `$TMPDIR` para cada função:

1. O wrapper referencia `Amazon.Lambda.RuntimeSupport` 2.0.0, e as funções usam 2.2.0 (bump do Dependabot, #107): NU1605 (downgrade), tratado como erro, e o wrapper não compila.
2. No macOS, `$TMPDIR` (`/var/folders/...`) é link simbólico para `/private/var/folders/...`; os caminhos relativos das `BuildingBlocks` resolvidos a partir do wrapper apontam para fora do repositório.

Contorno usado na validação (local, sem mudar o repositório): compilar cada wrapper pelo caminho real com `dotnet build /private/var/folders/.../Wrapper<X>.csproj -p:NoWarn=NU1605` e reiniciar o recurso com `aspire resource <nome> start`. Correção definitiva a decidir com o usuário (ex.: rodar o AppHost com `TMPDIR` apontando para um caminho sem link simbólico e alinhar a versão do `RuntimeSupport` com a do wrapper).

