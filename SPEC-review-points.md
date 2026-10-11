# Spec: review-points

> Módulo 4 de 5 do mapa em [SPEC.md](./SPEC.md). Depende de: `points-ledger`, `review-eligibility`.
> Status: **AGUARDANDO APROVAÇÃO.**

## Objetivo

Creditar **15 pontos** ao cliente na primeira vez que ele publica a avaliação de um produto que comprou, uma única vez por produto.

### Critérios de aceite

1. Publico a review de um produto comprado (`Eligible` → `Published`): o saldo sobe 15, e o histórico ganha uma linha com data, "Avaliação" e `+15`.
2. Edito a mesma review: o saldo não muda.
3. Deleto e publico de novo: o saldo não muda.
4. Deleto, o TTL apaga a linha, compro de novo e publico de novo: o saldo não muda (a trava está no ledger).
5. Produto não comprado: não há review, logo não há crédito (garantido pelo `review-eligibility`).
6. O mesmo `ReviewCreatedEvent` entregue duas vezes pelo EventBridge credita uma vez só.
7. Reviews `Published` antigas, de antes do deploy, não geram crédito retroativo.

## Desenho

### Fluxo

```
Review stream ──(Eligible|Deleted → Published)──> ReviewCreatedEvent { ReviewId, ProductId, Rating, UserId }
EventBridge ──> challenges-review-created-consumer
                 TransactWriteItems:
                   Put  points-transactions { OwnerId = USER#<UserId>, TransactionId = REVIEW#<productId>,
                                             Type = ReviewCredit, Points = +15, Status = Completed }
                        cond attribute_not_exists(TransactionId)
                   Update challenge-progress PROFILE  ADD Score :15
                 ConditionalCheckFailed  → no-op (já creditado)
```

- **Idempotência:** o `TransactionId` determinístico `REVIEW#<productId>` no ledger cobre os critérios 3, 4 e 6, sem inbox `processed-events`. Se o Put falha, o Update do saldo cai junto.
- **15 pontos:** constante de domínio do Challenges, `PointsPolicy.ReviewCredit = 15`, com comentário apontando a regra de negócio. Não é configuração: mudar o valor é decisão de produto e vai por PR. Se o negócio quiser ajustar sem deploy, o caminho é estender o `challenge-points-admin`, e isso é **fora** deste módulo.
- **PROFILE inexistente** (cliente que nunca jogou um desafio): o `ADD Score` cria o item. Os demais atributos (`Completed` etc.) ficam ausentes e são lidos como 0, como no primeiro `SubmitAnswer`. Confirmar no Plan que o `Load` tolera isso.
- **ADR-0045:** este é o primeiro consumer do Challenges. A emenda já está prevista na ADR-0048 (escrita em `points-ledger`).

### Lambda

- `Modules/Progress/EventsIntegration/Consumers/ReviewCreated/{Endpoint,Handler,Mapper}.cs` (usar o agente `cdc-integration-scaffold`).
- Nome: `challenges-review-created-consumer` (convenção `<service>-<resource>-consumer`).
- CDK: regra EventBridge para `detail-type = ReviewCreatedEvent` (o mesmo evento já consumido pelo CatalogView, agora com uma segunda regra), DLQ, alarme no `duckstore-alerts`, e grants de escrita em `points-transactions` e `challenge-progress`.
- AppHost: registrar o Lambda em `ChallengesExtensions.cs`.

### SPA

- Depois de publicar a primeira review, a SPA mostra "+15 pontos em instantes" e invalida o saldo. O crédito é assíncrono, então ela não promete o número exato na hora.
- O histórico já exibe `ReviewCredit` como "Avaliação" (feito em `points-ledger`).

### Fora do escopo

- Retirar pontos ao deletar a review (decidido: não retira).
- Crédito retroativo.
- Pontuação diferente por tamanho do comentário ou por foto.

## Comandos

```bash
dotnet build DuckStore.slnx
dotnet test tests/Services/Challenges/Challenges.UnitTests/Challenges.UnitTests.csproj
dotnet run --project src/AppHost/AppHost.csproj
cd infra && npx cdk synth ChallengesStack && npx cdk diff ChallengesStack
```

## Estrutura

```
src/Services/Challenges/Challenges.Function/Modules/Progress/
  Domain/PointsPolicy.cs                                          novo (ReviewCredit = 15)
  Domain/Entities/PointsTransaction.cs                            + ReviewCredit(...)
  EventsIntegration/Consumers/ReviewCreated/{Endpoint,Handler,Mapper}.cs
  Data/DynamoPlayerProgressRepository.cs                          + itens de crédito de review
src/AppHost/ChallengesExtensions.cs
infra/constructs/challenges-lambdas.ts
tests/Services/Challenges/Challenges.UnitTests/...
src/WebApps/Shopping.Web.SPA.React/features/reviews/**            aviso de pontos
```

## Estilo de código

```csharp
public partial class Functions
{
    // Credita a primeira publicação de uma review (SPEC-review-points). A trava "uma vez por
    // produto" é o TransactionId determinístico REVIEW#<productId> no ledger — não o status da
    // review, que pode ir e voltar (Deleted -> Published) ou sumir por TTL.
    [LambdaFunction]
    public async Task ReviewCreatedConsumer(EventBridgeEvent<ReviewCreatedEvent> evt, [FromServices] IPointsLedger ledger)
    {
        var credit = PointsTransaction.ReviewCredit(
            ReviewCreatedMapper.ToOwnerId(evt.Detail.UserId), evt.Detail.ProductId, DateTime.UtcNow);

        await ledger.CreditAsync(credit);   // ConditionalCheckFailed => already credited, no-op
    }
}
```

## Testes

- **Unit:**
  - `PointsTransaction.ReviewCredit` gera `REVIEW#<productId>` e `+15`.
  - O handler monta Put com `attribute_not_exists` e `ADD Score 15` na mesma transação.
  - `ConditionalCheckFailed` (transação cancelada pelo Put) não propaga.
  - O mapper rejeita `UserId` vazio.
- **Manual:** critérios 1 a 7 no Aspire local. O evento é best-effort localmente (EventBridge só existe na AWS), então o fluxo de ponta a ponta é validado no AWS dev. Localmente, `local.ts` simula o crédito no `createReview`.

## Limites

- **Sempre:** creditar e somar o saldo na mesma transação; idempotência pelo `TransactionId` do ledger.
- **Perguntar antes:** tornar os 15 pontos configuráveis em tempo de execução.
- **Nunca:** a Review escrever no ledger ou no saldo (o Challenges é o dono do saldo, ADR-0046 §1); usar o status da review como trava de crédito.

## Perguntas abertas

Nenhuma.
