# Tasks: Pontos de Desafio como Desconto (Points-to-Discount v1)

> Plano: [tasks/plan.md](./plan.md). Toda tarefa cumpre a "Verificação padrão" do plano (build, testes do serviço, format, `cdk synth` e `pnpm lint/build` quando aplicável), além do que estiver listado nela.

---

## Fase 1 — `points-ledger` ([spec](../SPEC-points-ledger.md))

- [x] **T1: ADR-0048, ledger de pontos e resgate no carrinho** (XS) — feito; `adr-guardian` revisou, e os achados foram incorporados à ADR, ao spec e às tarefas
  - Aceite: a ADR cobre o ledger, a máquina de estados, o total do servidor/`PriceChanged` e a espera do Payment; emenda as ADRs 0045 e 0046; foi gravada com a skill `adr` e está indexada em `docs/adr/README.md`.
  - Verificar: `adr-guardian` sobre a ADR, sem conflito com ADR aceita.
  - Arquivos: `docs/adr/0048-*.md`, `docs/adr/README.md`. Dependências: nenhuma.

- [x] **T2: Tabela `points-transactions`** (M) — feito; o AppHost não precisou mudar (a stream source entra na T29); `RemovalPolicy.DESTROY`, igual às outras tabelas do Challenges
  - Aceite: a tabela tem PK `OwnerId`, SK `TransactionId`, LSI1 (`OwnerId` + `CreatedAt`), GSI1 esparso `OrderId` e stream `NEW_AND_OLD_IMAGES`, sem TTL; é criada pelo seeder local e pelo CDK; os nomes só existem em `PointsTransactionsSchema`.
  - Verificar: o Aspire sobe e a tabela aparece no DynamoDB Local (`aws dynamodb describe-table --endpoint-url http://localhost:8000`); `cdk synth ChallengesStack`.
  - Arquivos: `Modules/Progress/Data/PointsTransactionsSchema.cs`, `Challenges.DevelopmentDataSeeder/DynamoTableInitializer.cs`, `infra/constructs/challenges-dynamodb.ts`, `src/AppHost/ChallengesExtensions.cs` (se for preciso). Dependências: T1.

- [ ] **T3: Domínio `PointsTransaction`** (S)
  - Aceite: entidade + enums `PointsTransactionType`/`PointsTransactionStatus`; `ChallengeCredit(...)` rejeita pontos ≤ 0 e gera `CHALLENGE#<questionId>`, `Completed`.
  - Verificar: `dotnet test tests/Services/Challenges/Challenges.UnitTests`.
  - Arquivos: `Domain/Entities/PointsTransaction.cs`, `Domain/Enums/PointsTransaction{Type,Status}.cs`, teste. Dependências: T2.

- [ ] **T4: Crédito de desafio no ledger, na mesma transação** (M)
  - Aceite: uma resposta correta grava o attempt, o `ADD Score` e o Put no ledger (`attribute_not_exists`) atomicamente; uma resposta errada não grava no ledger; o reenvio continua no-op; o Lambda `challenges-submit-answer` tem grant no CDK.
  - Verificar: testes do repositório e do handler (3 itens se correta, 2 se errada, reenvio sem exceção); manual no Aspire: responder certo, conferir a linha na tabela, reenviar, continua 1 linha.
  - Arquivos: `Data/DynamoPlayerProgressRepository.cs`, `Features/SubmitAnswer/Handler.cs`, `infra/constructs/challenges-lambdas.ts`, testes. Dependências: T3.

- [ ] **T5: Query `myPointsHistory`** (M)
  - Aceite: resolver direto, Query no LSI1 com `ScanIndexForward:false`, paginado, só Cognito, owner vindo de `ctx.identity.sub`; tipos `PointsTransaction*` no schema; `local.ts` espelha.
  - Verificar: `cdk synth AppSyncStack`; no Aspire, a query pelo SPA local devolve as linhas da T4 em ordem decrescente; sem token = Unauthorized.
  - Arquivos: `graphql/schema.graphql`, `graphql/resolvers/challenges/queries/Query.myPointsHistory.js`, `infra/constructs/appsync-api.ts`, `app/api/graphql/local.ts`. Dependências: T4.

- [ ] **T6: Saldo no header da SPA** (M)
  - Aceite: `features/points` (service, hook `usePointsBalance`, `points-balance-badge`); o badge aparece só autenticado; após `submitChallengeAnswer`, usa o `newScore` sem refetch; o valor é igual ao `myChallengeProgress.score`.
  - Verificar: `pnpm lint && pnpm build`; manual: logado vê o saldo, visitante não, e responder certo atualiza o badge.
  - Arquivos: `features/points/{services,hooks,components,types}/*`, componente de header. Dependências: T5.

- [ ] **T7: Página `/my-points` (histórico)** (S)
  - Aceite: tabela com data, motivo ("Desafio", "Avaliação", "Resgate") e valor com sinal; paginação; visitante é redirecionado ao login; link a partir de `my-profile`.
  - Verificar: `pnpm build`; manual com 2 desafios respondidos.
  - Arquivos: `app/my-points/page.tsx`, `features/points/components/points-history-table.tsx`, link em `app/my-profile`. Dependências: T6.

### Checkpoint 1 — points-ledger
- [ ] `dotnet test tests/Services/Challenges/Challenges.UnitTests` verde; build e `pnpm build` limpos.
- [ ] Deploy no AWS dev: login com **Google** e com **Amazon**, saldo no header, responder um desafio e o saldo e o histórico batem (critérios 1 a 7 do spec).
- [ ] `adr-guardian` no diff da fase.
- [ ] Revisão humana antes de seguir.

---

## Fase 2 — `challenge-points-admin` ([spec](../SPEC-challenge-points-admin.md)), paralelizável

- [ ] **T8: Mutation `updateChallengePoints`** (S)
  - Aceite: resolver direto `UpdateItem` só no item `PUBLIC`, com condição `attribute_exists`; só o grupo `Admin` (Seller, cliente e API key recebem Unauthorized); `points` inteiro > 0; teste de `Question.Grade` provando que o crédito usa o `Points` carregado.
  - Verificar: `cdk synth AppSyncStack`; teste unitário; no AWS dev, chamar como Admin e como Seller.
  - Arquivos: `graphql/schema.graphql`, `graphql/resolvers/challenges/mutations/Mutation.updateChallengePoints.js`, `infra/constructs/appsync-api.ts`, teste em `Challenges.UnitTests`. Dependências: nenhuma.

- [ ] **T9: Página "Desafios" no Blazor** (M)
  - Aceite: lista título, linguagem, dificuldade e pontos; edição inline com validação > 0; mostra o erro do servidor; item no menu.
  - Verificar: `dotnet build`; manual no AWS dev: ajustar, ver o valor novo na loja, responder e ganhar o valor novo; tentativa antiga mantém o crédito original.
  - Arquivos: `Pages/Challenges/ChallengeList.razor`, `Services/ChallengeAdminService.cs`, `Services/Models.cs`, `Layout/NavMenu.razor` (ou equivalente), `Program.cs` (registro do serviço). Dependências: T8.

### Checkpoint 2 — challenge-points-admin
- [ ] Critérios 1 a 6 do spec no AWS dev.
- [ ] Revisão humana.

---

## Fase 3 — `review-eligibility` ([spec](../SPEC-review-eligibility.md)), paralelizável com a fase 1

- [x] **T10: ADR-0049, status da review, GSI1 esparso, TTL e `ReviewDeleted`** (XS)
  - Aceite: emenda as ADRs 0011 e 0029; registra as transições e a regra "o TTL não gera evento".
  - Arquivos: `docs/adr/0049-*.md`, `docs/adr/README.md`. Dependências: nenhuma.

- [x] **T11: Ordering publica `OrderCompletedEvent`** (M)
  - Aceite: `OrderCompletedRule` casa só em MODIFY `Old.Status != Completed` e `New.Status == Completed`; o evento leva `OrderId`, `CustomerId` e `ProductIds` distintos; o evento está registrado no `MessagingSerializerContext`.
  - Verificar: testes da regra (Pending→Completed casa; Completed→Completed não; Pending→Cancelled não; produtos duplicados viram distintos).
  - Arquivos: `BuildingBlocks.Messaging/Events/OrderCompletedEvent.cs`, `.../Serialization/MessagingSerializerContext.cs`, `Ordering.Function/.../Rules/OrderCompletedRule.cs`, registro da regra (DI do publisher), teste. Dependências: T10.

- [x] **T12: Status da review no modelo da Review** (M)
  - Aceite: enum `ReviewStatus`; `ReviewSchema` com `Status`, `ExpiresAt` e o helper `EffectiveStatus` (ausente = `Published`); `ReviewStreamImage` carrega `Status`; TTL `ExpiresAt` na tabela (seeder + CDK).
  - Verificar: testes do `EffectiveStatus`; `cdk synth ReviewStack`.
  - Arquivos: `Domain/Enums/ReviewStatus.cs`, `Data/ReviewSchema.cs`, `Publishers/ReviewStreamImage.cs`, `Review.DevelopmentDataSeeder/DynamoTableInitializer.cs`, `infra/constructs/review-dynamodb.ts`. Dependências: T10.

- [ ] **T13: Consumer `review-order-completed-consumer`** (M)
  - Aceite: um `PutItem` por produto com `Status = Eligible`, sem atributos GSI1, condição `attribute_not_exists(Id)`; `ConditionalCheckFailed` é no-op; registrado no AppHost.
  - Verificar: testes (N produtos = N puts; condição presente; falha condicional não propaga).
  - Arquivos: `Consumers/OrderCompleted/{Endpoint,Handler,Mapper}.cs`, `src/AppHost/ReviewExtensions.cs`, teste. Dependências: T11, T12. Usar `cdc-integration-scaffold`.

- [ ] **T14: CDK do consumer `OrderCompleted`** (S)
  - Aceite: regra EventBridge `detail-type = OrderCompletedEvent` no `duckstore-event-bus`, DLQ, alarme no `duckstore-alerts`, grant de escrita em `reviews`, `functionName` fixo.
  - Verificar: `cdk synth ReviewStack && cdk diff ReviewStack`.
  - Arquivos: `infra/constructs/review-lambdas.ts`. Dependências: T13.

- [ ] **T15: Regras da Review por transição + `ReviewDeletedEvent` + `UserId`** (M)
  - Aceite: todas as linhas da tabela §5 do spec (INSERT `Eligible` = nada; →`Published` = Created; `Published→Published` = Updated; →`Deleted` = Deleted; REMOVE = nada; legado = `Published`); o `ReviewCreatedEvent` ganha `UserId`.
  - Verificar: um teste por linha da tabela.
  - Arquivos: `Rules/ReviewCreatedRule.cs`, `Rules/ReviewUpdatedRule.cs`, `Rules/ReviewDeletedRule.cs`, `BuildingBlocks.Messaging/Events/{ReviewCreatedEvent,ReviewDeletedEvent}.cs` (+ serializer context), testes. Dependências: T12.

- [ ] **T16: CatalogView `ReviewDeletedStrategy`** (M)
  - Aceite: estratégia nova no `ReviewSync` (sem `switch`, ADR-0040); decrementa a contagem e remove o rating do histograma, com piso em zero; `ReviewDeletedEvent` adicionado à regra EventBridge do `ReviewSync`.
  - Verificar: testes da estratégia; `cdk synth CatalogViewStack`.
  - Arquivos: `ReviewSync/Strategies/ReviewDeleteStrategy.cs`, registro no dispatcher/DI, `infra/constructs/catalogview-lambdas.ts`, teste. Dependências: T15.

- [ ] **T17: GraphQL `myReview`, `deleteReview` e `createReview` restrito** (M)
  - Aceite: `myReview` com GetItem, retorna `null` sem linha; `createReview` exige linha existente e passa a ser `UpdateItem` (SET `Published`, GSI1, REMOVE `ExpiresAt`); `deleteReview` faz SET `Deleted` + `ExpiresAt = now + 5d`, REMOVE GSI1, com condição `Published` ou legado; `status` no tipo `Review`.
  - Verificar: `cdk synth AppSyncStack`; no AWS dev: `createReview` sem compra = Unauthorized.
  - Arquivos: `graphql/schema.graphql`, `Query.myReview.js`, `Mutation.deleteReview.js`, `Mutation.createReview.{checkExisting,upsert}.js`, `infra/constructs/appsync-api.ts`. Dependências: T12. Usar `appsync-resolver-scaffold`.

- [ ] **T18: `local.ts` para reviews com status** (S)
  - Aceite: `myReview`, `deleteReview` e `createReview` com a exigência de linha; o checkout local cria as linhas `Eligible` dos produtos do pedido.
  - Verificar: no Aspire, comprar e avaliar funciona; avaliar sem comprar falha.
  - Arquivos: `app/api/graphql/local.ts`. Dependências: T17.

- [ ] **T19: SPA, avaliação no detalhe do produto** (M)
  - Aceite: os 4 estados (sem login / `null` / `Eligible` ou `Deleted` / `Published` com Editar e Excluir com confirmação); formulário em modo edição; a lista e a média são atualizadas após publicar, editar ou excluir.
  - Verificar: `pnpm lint && pnpm build`; roteiro manual dos critérios 1, 2, 4, 5 e 6.
  - Arquivos: `features/reviews/services/*`, `components/reviews-section.tsx`, `components/review-form.tsx`, `components/my-review-card.tsx` (novo). Dependências: T18.

### Checkpoint 3 — review-eligibility
- [ ] Testes de Review, Ordering e CatalogView verdes; `dotnet test --filter "FullyQualifiedName~Ordering.FunctionalTests"`.
- [ ] AWS dev: critérios 1 a 9 do spec (o 7 conferindo o `ExpiresAt`).
- [ ] `adr-guardian` no diff da fase. Revisão humana.

---

## Fase 4 — `review-points` ([spec](../SPEC-review-points.md))

- [ ] **T20: Consumer `challenges-review-created-consumer`** (M)
  - Aceite: `PointsPolicy.ReviewCredit = 15`; `PointsTransaction.ReviewCredit` gera `REVIEW#<productId>`; uma transação com Put no ledger (`attribute_not_exists`) + `ADD Score 15`; falha condicional = no-op; um PROFILE inexistente é criado e lido sem erro; registrado no AppHost.
  - Verificar: testes do domínio, do handler e do mapper (`UserId` vazio rejeitado); teste do `PlayerProgress.Load` com PROFILE só com `Score`.
  - Arquivos: `Domain/PointsPolicy.cs`, `Domain/Entities/PointsTransaction.cs`, `Consumers/ReviewCreated/{Endpoint,Handler,Mapper}.cs`, `Data/DynamoPlayerProgressRepository.cs`, `src/AppHost/ChallengesExtensions.cs`. Dependências: T4, T15. Usar `cdc-integration-scaffold`.

- [ ] **T21: CDK do consumer + aviso na SPA + `local.ts`** (S)
  - Aceite: regra `ReviewCreatedEvent` (a segunda, ao lado da do CatalogView), DLQ, alarme e grants; a SPA mostra "+15 pontos em instantes" na primeira publicação e invalida o saldo; o `local.ts` credita na primeira publicação.
  - Verificar: `cdk synth ChallengesStack`; `pnpm build`.
  - Arquivos: `infra/constructs/challenges-lambdas.ts`, `features/reviews/components/reviews-section.tsx`, `app/api/graphql/local.ts`. Dependências: T20.

### Checkpoint 4 — review-points
- [ ] AWS dev: critérios 1 a 7 (publicar, editar, deletar e republicar, evento duplicado via replay da DLQ ou reenvio manual).
- [ ] O histórico mostra "Avaliação +15". Revisão humana.

---

## Fase 5 — `cart-points-redemption` ([spec](../SPEC-cart-points-redemption.md))

- [ ] **T22: Pricing, conversão 100:1, teto de 20% e desconto de pontos** (M)
  - Aceite: `RewardOptions` com `MaxDiscountPercent` (sem `ExpiryDays` para o resgate novo); `GetBasketInstallmentPlanQuery.PointsToUse`; `MaxRedeemablePoints` sobre o `Price` pós-campanha; `PointsAboveCapException` (BadRequest); o desconto é aplicado pelo parâmetro `customerDiscountAmount` que já existe; o resultado tem `MaxRedeemablePoints` e `PointsDiscount`; com `ExpectedTotal`/`PaymentMethod`/`Installments`, escolhe o total da forma de pagamento (`CashPrice` ou `InstallmentPlan[N].TotalValue`), compara em centavos e lança `PriceChangedException` (`ServerTotal`), ou devolve `ChargedTotal`; parcela N inexistente = BadRequest; `reward-config.ts` com 100, 1 e 20, e as env vars no `pricing-lambdas.ts`.
  - Verificar: testes (R$200 → 4000; R$0,99 → 19; acima do teto rejeita; o desconto entra depois da campanha; à vista e parcelado recalculados; total igual → `ChargedTotal`; diferente em 1 centavo → `PriceChanged`; parcela inexistente → BadRequest).
  - Arquivos: `Shared/Configuration/RewardOptions.cs`, `GetBasketInstallmentPlan/{Query,Handler,Endpoint}.cs`, `InstallmentCalculator.cs`, `Shared/Exceptions/{PointsAboveCap,PriceChanged}Exception.cs`, `infra/constructs/{reward-config,pricing-lambdas}.ts`. Pode chegar a 6 arquivos; se crescer, separar o `PriceChanged` em uma T22b. Dependências: T3.

- [ ] **T23: Cotação GraphQL com `pointsToUse`** (S)
  - Aceite: `basketInstallmentPlan(items, pointsToUse)` com `maxRedeemablePoints` e `pointsDiscount` na resposta; `pointsToUse > 0` sem token = Unauthorized; `rewardConversion` sem `expiryDays`; o `local.ts` espelha (o `discountId` continua aceito até a T35).
  - Verificar: `cdk synth AppSyncStack`; consulta manual com e sem pontos.
  - Arquivos: `graphql/schema.graphql`, `Query.basketInstallmentPlan.js`, `infra/constructs/appsync-api.ts`, `api/queries/pricing.ts`, `app/api/graphql/local.ts`. Dependências: T22.

- [ ] **T24: Contratos de evento (aditivo)** (S)
  - Aceite: `BasketCheckoutEvent` + `PointsToUse` (**sem** campo em moeda — ADR-0048 §4); novos `PointsReservedEvent`, `PointsReservationFailedEvent` e `OrderCancelledEvent` no serializer context; o Basket repassa os campos novos do DTO ao evento. O `DiscountId` ainda **não** é removido.
  - Verificar: teste do Basket (campos chegam inalterados ao evento); `dotnet build` da solução.
  - Arquivos: `BuildingBlocks.Messaging/Events/{BasketCheckoutEvent,PointsReservedEvent,PointsReservationFailedEvent,OrderCancelledEvent}.cs`, `MessagingSerializerContext.cs`, `Basket.Function/.../Dtos/BasketCheckoutDto.cs` + `CheckoutBasket/Handler.cs`. Dependências: T22.

- [ ] **T25: Pipeline `checkoutBasket` (`loadCart` → `quote` → `checkout`)** (M) ⚠ maior risco
  - Aceite: roda em **todo** checkout; usa os itens do carrinho salvo (`JSON.parse(Data)`); `quote` envia `expectedTotal`, `paymentMethod` e `installments` ao Pricing e repassa `PriceChanged`/`PointsAboveCap` com `errorType` e `serverTotal`; o Basket recebe `TotalPrice = chargedTotal` e `PointsToUse`; o JS só mapeia (nenhuma regra de preço no resolver — ADR-0009); `pipelineResolver()` ganha a variante com um data source por função.
  - Verificar: `cdk synth AppSyncStack`; no AWS dev: checkout normal OK, `totalPrice` adulterado → `PriceChanged`, preço alterado entre cotação e checkout → `PriceChanged`, pontos acima do teto → erro; `Ordering.FunctionalTests` verde.
  - Arquivos: `Mutation.checkoutBasket.js` + `.loadCart.js` + `.quote.js` + `.checkout.js`, `graphql/schema.graphql` (input `pointsToUse`), `infra/constructs/appsync-api.ts`. Dependências: T23, T24.

- [ ] **T26: `local.ts` checkout com total do servidor e pontos** (S)
  - Aceite: o checkout local recalcula, compara (`PriceChanged`), simula a reserva e o débito síncronos e grava `REDEMPTION#<orderId>` como `Used`.
  - Verificar: no Aspire, checkout com e sem pontos e com total adulterado.
  - Arquivos: `app/api/graphql/local.ts`. Dependências: T25.

- [ ] **T27: Ordering publica `OrderCancelledEvent`** (S)
  - Aceite: `OrderCancelledRule` casa em MODIFY `Old.Status != Cancelled` e `New.Status == Cancelled` (de `Pending` ou `Completed`).
  - Verificar: testes da regra.
  - Arquivos: `Rules/OrderCancelledRule.cs`, registro da regra, teste. Dependências: T24.

- [ ] **T28: Challenges reserva os pontos no `BasketCheckout`** (M)
  - Aceite: `PointsTransaction.Reserve/Fail` (`Failed` guarda os pontos pedidos); com `PointsToUse > 0`, uma transação faz `ADD Score -pts` (condição `Score >= pts`) + Put `REDEMPTION#<orderId>` `Reserved` com `OrderId` (GSI1); se faltar saldo, Put `Failed`; checkout duplicado = no-op; `PointsToUse = 0` = ignora; consumer `challenges-basket-checkout-consumer` no AppHost.
  - Verificar: testes (saldo suficiente, insuficiente, duplicado, zero pontos).
  - Arquivos: `Domain/Entities/PointsTransaction.cs`, `Consumers/BasketCheckout/{Endpoint,Handler,Mapper}.cs`, `Data/` (repositório do ledger), `src/AppHost/ChallengesExtensions.cs`. Dependências: T24, T3.

- [ ] **T29: Publisher `challenges-points-transactions-stream-publisher`** (M)
  - Aceite: `PointsReservedRule` (INSERT `Reserved`) e `PointsReservationFailedRule` (INSERT `Failed`); sem moeda nos eventos; stream source no AppHost e no CDK, com DLQ `onFailure` alarmada (ADR-0015 §1); CDK do consumer da T28 (regra `BasketCheckoutEvent`, DLQ, alarme, grants).
  - Verificar: testes das regras; `cdk synth ChallengesStack`.
  - Arquivos: `Publishers/PointsTransactions/{Endpoint,PointsTransactionStreamImage}.cs`, `Rules/{PointsReservedRule,PointsReservationFailedRule}.cs`, `src/AppHost/ChallengesExtensions.cs`, `infra/constructs/challenges-lambdas.ts`. Dependências: T28. Usar `cdc-integration-scaffold`.

- [ ] **T30: Payment, status `AwaitingPoints`** (M)
  - Aceite: `PaymentStatus.AwaitingPoints`; checkout com pontos cria `AwaitingPoints`; `Payment.ConfirmPoints()` (→ `Pending`) e `Payment.RejectPoints()` (→ `Declined`, motivo `InsufficientPoints`); a `PaymentRequestedRule` também casa em MODIFY `AwaitingPoints→Pending`; a nova `PaymentDeclinedInsufficientPointsRule` publica `PaymentDeclinedEvent` em MODIFY `AwaitingPoints→Declined`; sem pontos, o comportamento não muda.
  - Verificar: testes (o INSERT `AwaitingPoints` não dispara `PaymentRequested`; as transições; a regra de recusa local; a regressão sem pontos).
  - Arquivos: `Domain/Enums/PaymentStatus.cs`, `Domain/Entities/Payment.cs`, `Consumers/BasketCheckout/{Command,Handler,Mapper}.cs`, `Rules/{PaymentRequestedRule,PaymentDeclinedInsufficientPointsRule}.cs`. Dependências: T24.

- [ ] **T31: Payment consome `PointsReserved` e `PointsReservationFailed`** (M)
  - Aceite: slice `Consumers/PointsReservation/` (Lambda `payment-points-reservation-consumer`) com dois `[LambdaFunction]`, no formato do `PaymentResult/`, chamando `ConfirmPoints`/`RejectPoints`; **pagamento não encontrado = exceção** (retry/DLQ), nunca no-op; idempotência pelo `payment-processed-events`; teste de que o `PaymentResult` do próprio Payment faz no-op ao receber o `PaymentDeclinedEvent` publicado pela T30; Lambdas no AppHost; CDK com duas regras, DLQ, alarme e grants.
  - Verificar: testes dos handlers; `cdk synth PaymentStack`.
  - Arquivos: `Consumers/PointsReservation/{Endpoint,Handler,Mapper}.cs`, `src/AppHost/PaymentExtensions.cs`, `infra/constructs/payment-lambdas.ts`. Dependências: T29, T30.

- [ ] **T32: Challenges liquida o resgate (`Used`/`Released`/`Refunded`)** (M)
  - Aceite: `challenges-payment-authorized-consumer` (`Reserved→Used`) e `challenges-order-cancelled-consumer` (`Reserved→Released` / `Used→Refunded` com `ADD Score +|pts|`; `Failed` = no-op); busca por GSI1 `OrderId`, e **linha não encontrada = exceção** (retry/DLQ, porque o GSI1 é eventualmente consistente); toda transição é condicional, e uma condição falhada sobre uma linha que existe é no-op; CDK com regras, DLQ e grants.
  - Verificar: um teste por transição + evento duplicado + `Failed` no-op + linha ausente lança exceção.
  - Arquivos: `Domain/Entities/PointsTransaction.cs`, `Consumers/{PaymentAuthorized,OrderCancelled}/*`, `src/AppHost/ChallengesExtensions.cs`, `infra/constructs/challenges-lambdas.ts`. Dependências: T27, T28.

- [ ] **T33: SPA, pontos no carrinho e `PriceChanged`** (M)
  - Aceite: `points-redeem-input` com máximo `min(saldo, maxRedeemablePoints)` e a conversão; cotação com debounce; o checkout envia `pointsToUse` e o `totalPrice` exibido; `PriceChanged` gera alerta inline + nova cotação + novo total destacado, sem reenviar; os pontos são ajustados se o teto cair; status de resgate no histórico (Aguardando pagamento, Usado, Estornado, Reembolsado, Falhou).
  - Verificar: `pnpm lint && pnpm build`; roteiro manual dos critérios 1 a 3, 6 e 14.
  - Arquivos: `features/cart/components/points-redeem-input.tsx`, `features/cart/hooks/*`, `features/checkout/services/checkout.service.ts`, `features/checkout/components/*` (resumo e alerta), `features/points/components/points-history-table.tsx`. Dependências: T23, T25.

### Checkpoint 5a — resgate funcionando (cupom ainda presente)
- [ ] `dotnet test` (solução inteira) verde; `Ordering.FunctionalTests` verde.
- [ ] AWS dev: critérios 1 a 8, 10, 11, 13 e 14 do spec; o 9 pelo teste unitário.
- [ ] `adr-guardian` no diff da fase. Revisão humana antes da remoção.

- [ ] **T34: Remover o cupom do backend (Challenges + Pricing)** (M)
  - Aceite: saem a feature `RedeemPoints`, `PlayerProgress.Redeem`/`MinimumRedeemablePoints`, `Redemption`/`REDEMPTION#` em `challenge-progress`, `PointsRedeemedRule`/`Event`, o módulo `Pricing/CustomerDiscounts` e o `DiscountId` no `GetBasketInstallmentPlan`; o registro de DI e o AppHost são limpos; os testes correspondentes são removidos ou ajustados.
  - Verificar: `dotnet build` + `dotnet test` (Challenges e Pricing).
  - Arquivos: diretórios removidos + `ServiceRegistration.cs` dos dois serviços + `src/AppHost/{Challenges,Pricing}Extensions.cs`. Dependências: Checkpoint 5a.

- [ ] **T35: Remover o `DiscountId` e o schema de cupom** (M)
  - Aceite: o `DiscountId` sai de `BasketCheckoutEvent`, `PaymentRequestedEvent`, `PaymentAuthorizedEvent`, do `Payment` e do PaymentGateway; saem `redeemChallengePoints`, `myRewards`, `CustomerDiscount` e `discountId` do schema, dos resolvers, do `local.ts` e da SPA (`GET_MY_REWARDS`); Lambdas e regras de CDK sem estado são removidas.
  - Verificar: `dotnet test` (solução); `cdk synth` nas stacks tocadas; `pnpm build`; `grep -ri "discountId\|myRewards\|redeemChallengePoints"` sem resultados fora de docs/ADRs.
  - Arquivos: eventos, `Payment.cs` e o consumer, o PaymentGateway, `graphql/**`, `infra/constructs/{appsync-api,pricing-lambdas,challenges-lambdas}.ts`, SPA. Pode passar de 5 arquivos, por ser uma remoção mecânica; se crescer, dividir por serviço. Dependências: T34.

- [ ] **T36: Remover recursos com estado do CDK** (S) ⚠ **perguntar antes**
  - Aceite: a tabela `customer-discounts` é removida do `pricing-dynamodb.ts` depois de confirmar a `RemovalPolicy` e o descarte dos dados de dev com o humano.
  - Verificar: `cdk diff PricingStack` mostra só a remoção esperada.
  - Arquivos: `infra/constructs/pricing-dynamodb.ts`, `infra/stacks/pricing-stack.ts`. Dependências: T35 + aprovação explícita.

### Checkpoint 5 — Complete
- [ ] Todos os critérios de aceite do negócio (tabela de cobertura do `SPEC.md`) validados no AWS dev.
- [ ] `dotnet test` e `pnpm build` verdes; `cdk diff` revisado em todas as stacks tocadas.
- [ ] READMEs dos contextos e diagramas atualizados (skills `readme-docs` e `architecture-diagrams`).
- [ ] Pronto para PR contra `development`.
