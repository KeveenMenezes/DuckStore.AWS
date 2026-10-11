# Spec: review-eligibility

> Módulo 3 de 5 do mapa em [SPEC.md](./SPEC.md). Depende de: nada. Habilita: `review-points`.
> Status: **AGUARDANDO APROVAÇÃO.**

## Objetivo

Só quem comprou um produto pode avaliá-lo.

Quando um pedido é concluído, a Review cria, para cada produto do pedido, uma review do cliente com status `Eligible`. O detalhe do produto busca a review do cliente: se ela existe, o formulário aparece habilitado. O cliente pode publicar, editar, ver o último comentário e deletar a própria review.

**Usuário:** cliente autenticado na loja React.

### Critérios de aceite

1. Compro o produto P e o pagamento é autorizado. Em poucos segundos, o detalhe de P mostra "Avalie este produto" com o formulário habilitado.
2. Detalhe de um produto que **não** comprei: o formulário não aparece, e no lugar dele há a mensagem "Só quem comprou pode avaliar". Chamar `createReview` direto na API para esse produto é rejeitado.
3. Pedido recusado ou cancelado: nenhuma review `Eligible` é criada.
4. Publico a review: ela aparece na lista pública e a nota média do produto muda.
5. Volto ao detalhe: vejo minha review (nota e comentário) e posso editar. A edição atualiza a lista e a média por delta.
6. Deleto a review: ela sai da lista pública, a média é ajustada e o formulário volta a aparecer para eu publicar de novo enquanto a linha existir.
7. Cinco dias depois do delete, o TTL apaga a linha e eu não posso mais avaliar aquele produto, a menos que o compre de novo.
8. Comprar o mesmo produto de novo enquanto a review existe (`Eligible`, `Published` ou `Deleted`) **não** altera a review.
9. Uma review `Eligible` nunca aparece em `reviewsByProduct` nem conta na média ou no histograma.

## Desenho

### 1. Ordering publica `OrderCompletedEvent`

Uma regra nova `OrderCompletedRule` no `OrderStreamPublisherFunction` (padrão rule-based da ADR-0019):

- Match: `EventName == "MODIFY"`, `Old.Status != Completed` e `New.Status == Completed`. É uma transição, então eventos repetidos da stream não publicam de novo.
- Evento (em `BuildingBlocks.Messaging/Events`):

```csharp
public record OrderCompletedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }          // = Cognito sub (o checkout é só Cognito)
    public List<Guid> ProductIds { get; init; } = [];  // distintos
}
```

`OrderStreamImage` precisa expor `OrderItems` (ProductId) e `CustomerId` se ainda não expõe. Verificar no Plan.

A regra de `OrderCancelled` (para `cart-points-redemption`) vai no mesmo publisher, mas é escrita naquele módulo.

### 2. Status da review

| Status | Origem | GSI1 (`reviewsByProduct`) | Média e histograma |
|---|---|---|---|
| `Eligible` | consumer de `OrderCompleted` | **sem** atributos GSI1 (fora do índice esparso) | não |
| `Published` | `createReview` | com GSI1 | sim |
| `Deleted` | `deleteReview` | sem GSI1 + `ExpiresAt = now + 5d` | não (removida por delta) |

**GSI1 esparso:** só `Published` tem `GSI1PK`/`GSI1SK`. Assim, `reviewsByProduct` estruturalmente não enxerga `Eligible` nem `Deleted`, sem precisar de filtro (mesmo padrão do sparse GSI do Challenges).

Linhas legadas sem `Status` são tratadas como `Published` e já estão no GSI1. Isso dá compatibilidade com o que já existe sem migração.

Transições permitidas:

```
(nada) ──OrderCompleted──> Eligible ──createReview──> Published ──deleteReview──> Deleted
                                                         ^  │ createReview (editar)     │
                                                         │  └──────────┘              │
                                                         └──────── createReview ──────┘ (republicar remove ExpiresAt)
Deleted ──TTL 5d──> (linha removida)
```

### 3. Consumer `review-order-completed-consumer` (novo Lambda na Review)

- É o primeiro consumer da Review. Fica em `Modules/Reviews/EventsIntegration/Consumers/OrderCompleted/` (`Endpoint`, `Handler`, `Mapper`), no formato da ADR-0019 (usar o agente `cdc-integration-scaffold`).
- Para cada `ProductId`, um `PutItem` **individual** com `ConditionExpression: attribute_not_exists(Id)`, tratando `ConditionalCheckFailed` como no-op.
- Não usa `TransactWriteItems` nem inbox `processed-events`: a condição por item já é idempotente, e em uma transação um único produto já avaliado derrubaria a criação dos demais.
- O item nasce com `Id = <productId>#<customerId>`, `ProductId`, `UserId`, `Status = Eligible` e `CreatedAt`, sem `Rating` e `Comment`.
- O `UserName` (exibição) é gravado no `createReview` a partir do token, como hoje.
- CDK: regra no bus `duckstore-event-bus` para `detail-type = OrderCompletedEvent`, com DLQ e alarme no `duckstore-alerts`.

### 4. Mutations e query

| Operação | Tipo | Regra |
|---|---|---|
| `myReview(productId: ID!): Review` | direto, `GetItem` | `Id = productId#sub`; retorna `null` se não existir. Retorna `Eligible`/`Published`/`Deleted` com `status` |
| `createReview(input)` | pipeline direto (o que já existe) | `checkExisting` passa a **exigir** que a linha exista (`util.unauthorized()` caso contrário). `upsert` vira `UpdateItem` com `SET Status = Published, Rating, Comment, UserName, GSI1PK, GSI1SK REMOVE ExpiresAt` e condição `attribute_exists(Id)` |
| `deleteReview(productId: ID!): Review!` | direto, `UpdateItem` | `SET Status = Deleted, ExpiresAt = now+5d REMOVE GSI1PK, GSI1SK` e condição `Status = Published OR attribute_not_exists(Status)` |

O schema ganha `enum ReviewStatus { Eligible Published Deleted }` e o campo `status: ReviewStatus!` em `Review`. Resolvers legados que leem linhas sem `Status` devolvem `Published`.

### 5. Publisher de reviews e CatalogView (média)

O publisher que já existe (`ReviewCreatedRule`, `ReviewUpdatedRule`) passa a decidir pela **transição de status**, não pela existência da linha:

| Transição (stream) | Evento | Efeito no CatalogView |
|---|---|---|
| INSERT `Eligible` | nenhum | — |
| `Eligible` → `Published` | `ReviewCreated` | +1 review, +rating |
| `Deleted` → `Published` | `ReviewCreated` | +1 review, +rating |
| `Published` → `Published` (rating mudou) | `ReviewUpdated` | delta do rating (ADR-0029) |
| `Published` → `Deleted` | **`ReviewDeleted`** (novo) | −1 review, −rating antigo |
| REMOVE (TTL) | nenhum | — (já ajustado no delete) |

Linha legada sem `Status` = `Published`.

O `ReviewDeletedEvent { ReviewId, ProductId, Rating }` é tratado no CatalogView por uma estratégia nova `ReviewDeletedStrategy` no `Consumers/ReviewSync/` (ADR-0040: estratégia nova no dispatcher do produtor, nunca um `switch`). O CDK adiciona `ReviewDeletedEvent` à regra que já existe do `ReviewSync`.

O `ReviewCreatedEvent` ganha `UserId` (o Cognito sub). Ele é usado por `review-points`, mas o campo entra aqui porque o publisher é alterado neste módulo.

### 6. SPA

- `features/reviews/services`: `getMyReview(productId)` e `deleteReview(productId)`.
- `reviews-section.tsx` com quatro estados:
  - Sem login: "Entre para avaliar".
  - `null`: "Só quem comprou pode avaliar".
  - `Eligible` ou `Deleted`: formulário vazio.
  - `Published`: card "Sua avaliação" com nota, comentário e data, e botões **Editar** e **Excluir** (com confirmação).
- `review-form.tsx` também funciona em modo edição, com os valores atuais.
- `local.ts`: `myReview`, `deleteReview` e o `createReview` com a nova exigência. O dev local também precisa simular o `OrderCompleted`: o caminho local de checkout deve criar a linha `Eligible`, e o mecanismo exato é definido no Plan.

### Fora do escopo

- Pontos por avaliação (`review-points`).
- Backfill para pedidos concluídos antes do deploy (só pedidos novos).
- Moderação e denúncia de reviews.

## Tech stack

.NET 10 Lambda (AOT), DynamoDB Streams, EventBridge, AppSync JS, Next.js, CDK v2.

## Comandos

```bash
dotnet build DuckStore.slnx
dotnet test tests/Services/Review/Review.UnitTests/Review.UnitTests.csproj
dotnet test tests/Services/Ordering/Ordering.UnitTests/Ordering.UnitTests.csproj
dotnet test tests/Services/CatalogView/CatalogView.UnitTests/CatalogView.UnitTests.csproj
dotnet test --filter "FullyQualifiedName~Ordering.FunctionalTests"     # requer Docker
dotnet run --project src/AppHost/AppHost.csproj
cd infra && npx cdk synth ReviewStack && npx cdk synth OrderingStack && npx cdk synth CatalogViewStack && npx cdk synth AppSyncStack
cd src/WebApps/Shopping.Web.SPA.React && pnpm lint && pnpm build
```

## Estrutura

```
src/BuildingBlocks/BuildingBlocks.Messaging/Events/OrderCompletedEvent.cs        novo
src/BuildingBlocks/BuildingBlocks.Messaging/Events/ReviewDeletedEvent.cs         novo
src/BuildingBlocks/BuildingBlocks.Messaging/Events/ReviewCreatedEvent.cs         + UserId
src/Services/Ordering/.../EventsIntegration/Publishers/Rules/OrderCompletedRule.cs
src/Services/Review/Review.Function/Modules/Reviews/
  Data/ReviewSchema.cs                        Status, ExpiresAt, nomes GSI1
  Domain/Enums/ReviewStatus.cs                novo
  EventsIntegration/Consumers/OrderCompleted/{Endpoint,Handler,Mapper}.cs
  EventsIntegration/Publishers/Rules/{ReviewCreatedRule,ReviewUpdatedRule,ReviewDeletedRule}.cs
src/Services/CatalogView/.../Consumers/ReviewSync/Strategies/ReviewDeletedStrategy.cs
src/AppHost/ReviewExtensions.cs               novo Lambda consumer
src/Services/Review/Review.DevelopmentDataSeeder/DynamoTableInitializer.cs        TTL em ExpiresAt
infra/constructs/review-dynamodb.ts           timeToLiveAttribute: 'ExpiresAt'
infra/constructs/review-lambdas.ts            consumer + regra EventBridge + DLQ + alarme
infra/constructs/catalogview-lambdas.ts       + ReviewDeletedEvent na regra ReviewSync
graphql/schema.graphql                        ReviewStatus, myReview, deleteReview
graphql/resolvers/reviews/queries/Query.myReview.js
graphql/resolvers/reviews/mutations/Mutation.deleteReview.js
graphql/resolvers/reviews/mutations/Mutation.createReview.{checkExisting,upsert}.js
infra/constructs/appsync-api.ts
src/WebApps/Shopping.Web.SPA.React/features/reviews/**
src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts
tests/Services/{Review,Ordering,CatalogView}/...
```

## Estilo de código

```csharp
// Dispara na transição, não na existência: uma linha Eligible (criada pelo OrderCompleted) não é
// uma review pública e não pode mexer na média (SPEC-review-eligibility §5). Linha legada sem
// Status conta como Published.
public sealed class ReviewCreatedRule : IStreamRule<ReviewStreamImage>
{
    public bool Match(StreamContext<ReviewStreamImage> context) =>
        context.New is { } next
        && next.EffectiveStatus == ReviewStatus.Published
        && context.Old?.EffectiveStatus is null or ReviewStatus.Eligible or ReviewStatus.Deleted;
    // ...
}
```

## Testes

- **Unit:**
  - `OrderCompletedRule`: casa só na transição para `Completed` e ignora MODIFY repetido e `Cancelled`. `ProductIds` vêm distintos.
  - Consumer `OrderCompleted`: um `PutItem` por produto, condição `attribute_not_exists`, e `ConditionalCheckFailed` não propaga.
  - Rules da Review: todas as linhas da tabela §5, incluindo legado sem `Status` e REMOVE de TTL (sem evento).
  - `ReviewDeletedStrategy`: decrementa a contagem e remove o rating do histograma, com piso em zero.
- **Functional (`Ordering.FunctionalTests`, opcional):** checkout com pagamento autorizado gera o `OrderCompleted`.
- **Manual:** critérios 1 a 9. O critério 7 (TTL) é verificado no AWS dev conferindo `ExpiresAt` e o filtro por status, sem esperar 5 dias.

## Limites

- **Sempre:** o `UserId` vem de `ctx.identity.sub` ou do evento, nunca do input; manter o GSI1 esparso como a única forma de esconder `Eligible`/`Deleted`; atualizar o `local.ts`.
- **Perguntar antes:** mudar a chave da tabela `reviews`; reprocessar ou migrar reviews legadas.
- **Nunca:** emitir evento de média para `Eligible` ou para REMOVE de TTL; deixar `createReview` criar uma linha que não existe.

## Perguntas abertas

Nenhuma.
