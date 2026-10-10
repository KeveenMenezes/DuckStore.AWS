# Spec: points-ledger

> Módulo 1 de 5 do mapa em [SPEC.md](./SPEC.md). Depende de: nada. Habilita: `review-points`, `cart-points-redemption`.
> Status: **AGUARDANDO APROVAÇÃO.**

## Objetivo

Dar ao cliente um saldo de pontos confiável e um histórico legível, a partir de um ledger único.

Hoje o saldo existe (`challenge-progress` PROFILE `Score`), mas não há registro do motivo de cada movimento. Este módulo cria a tabela `points-transactions`, passa a registrar nela todo crédito de desafio, expõe o histórico e mostra o saldo na SPA. Ele não implementa avaliação nem resgate, só o ledger que os dois vão usar.

**Usuário:** cliente autenticado na loja React (Cognito, com login próprio, Google ou Amazon).

### Critérios de aceite

1. Entro com **Google** e vejo meu saldo no header. Entro com **Amazon** e vejo meu saldo no header. São contas distintas no Cognito, cada uma com o próprio saldo.
2. Visitante (sem login) não vê saldo nem a página de histórico. A página redireciona para o login.
3. Resolvo um desafio corretamente: o saldo sobe exatamente pelos pontos ganhos (`Question.Points - hints × 25`), e uma linha nova aparece no histórico com data, motivo "Desafio" e valor `+N`.
4. Errar um desafio não cria linha no histórico nem muda o saldo.
5. Reenviar a resposta do mesmo desafio (ADR-0045 §4) não duplica a linha nem o crédito.
6. O histórico lista as linhas da mais recente para a mais antiga, paginado (`pageSize`, `nextToken`).
7. O saldo do header e o `score` de `myChallengeProgress` são sempre o mesmo número.

## Desenho

### Tabela `points-transactions` (dona: Challenges)

| Chave / índice | Valor |
|---|---|
| PK | `OwnerId` (`USER#<cognito-sub>`) |
| SK | `TransactionId`, **determinístico** por origem (garante idempotência via `attribute_not_exists`) |
| LSI1 | PK `OwnerId`, SK `CreatedAt` (ISO-8601). Ordena o histórico por data |
| GSI1 | PK `OrderId`. **Esparso**: só linhas de resgate têm o atributo. Usado por `cart-points-redemption` |
| Streams | `NEW_AND_OLD_IMAGES` (reservado para auditoria/analytics futuros; nenhum publisher neste módulo) |
| TTL | **nenhum** (o ledger é permanente) |

O LSI só pode ser criado junto com a tabela, por isso ele entra agora, ainda que só a leitura do histórico o use.

Formato do `TransactionId` por tipo:

| `Type` | `TransactionId` | Escrito por |
|---|---|---|
| `ChallengeCredit` | `CHALLENGE#<questionId>` | este módulo |
| `ReviewCredit` | `REVIEW#<productId>` | `review-points` |
| `Redemption` | `REDEMPTION#<orderId>` | `cart-points-redemption` |

Atributos: `Type`, `Points` (positivo em crédito, negativo em resgate), `Status`, `CreatedAt`, `UpdatedAt`, `SourceId`, `OrderId` (só resgate).

Status: `Completed` para créditos (terminal). Os status do resgate (`Reserved`, `Used`, `Released`, `Failed`, `Refunded`) são declarados no enum agora e implementados em `cart-points-redemption`.

### Escrita do crédito de desafio

`DynamoPlayerProgressRepository.SaveAttemptAsync` já grava o `ATTEMPT#` e faz `ADD Score` em uma `TransactWriteItems`. Para respostas corretas, um terceiro item entra **na mesma transação**:

```
Put points-transactions { OwnerId, TransactionId = CHALLENGE#<questionId>, Type = ChallengeCredit,
                          Points = +earned, Status = Completed, CreatedAt }
ConditionExpression: attribute_not_exists(TransactionId)
```

O saldo, o attempt e a linha do ledger ficam atômicos. Transação entre tabelas do mesmo serviço é permitida, e não há invoke nem evento.

**Saldo continua em `challenge-progress` PROFILE `Score`.** O ledger explica o saldo e não o substitui: somar o ledger a cada leitura seria O(n), e o `ConditionExpression Score >= :pts` do resgate precisa de um único item.

### Leitura

- `myPointsHistory(pageSize: Int, nextToken: String): PointsHistoryPage!`, resolver **direto DynamoDB** (`Query` no LSI1, `ScanIndexForward: false`), só Cognito. Classificação via skill `resolver-selection`: leitura de um único agregado por chave, sem regra de domínio, então é direto.
- O saldo vem do `myChallengeProgress.score` que já existe. Não é criada uma query nova de saldo.

```graphql
enum PointsTransactionType { ChallengeCredit ReviewCredit Redemption }
enum PointsTransactionStatus { Completed Reserved Used Released Failed Refunded }

type PointsTransaction @aws_cognito_user_pools {
  id: ID!
  type: PointsTransactionType!
  status: PointsTransactionStatus!
  points: Int!
  createdAt: AWSDateTime!
  orderId: ID
}

type PointsHistoryPage @aws_cognito_user_pools {
  items: [PointsTransaction!]!
  nextToken: String
}
```

A SPA traduz `type` para o motivo exibido ("Desafio", "Avaliação", "Resgate") e mostra o valor com sinal.

### SPA

- `features/points/`: novo feature module, no mesmo formato de `features/challenges` (`components/`, `hooks/`, `services/`, `types/`).
- Saldo no header, para usuário autenticado: `usePointsBalance()` lê `myChallengeProgress.score`. Depois de `submitChallengeAnswer`, a SPA usa o `newScore` da resposta, sem refetch.
- Nova rota `app/my-points/page.tsx` com a tabela de histórico e um link a partir de `my-profile`.
- `app/api/graphql/local.ts` (backend GraphQL de dev) ganha `myPointsHistory`, espelhando o resolver.

### Fora do escopo

- Crédito por avaliação (`review-points`) e resgate (`cart-points-redemption`).
- Backfill: saldos que já existem em dev não ganham linhas retroativas no ledger. O histórico começa no deploy.
- Expiração de pontos.

## Tech stack

.NET 10 Lambda (Native AOT, ADR-0042), DynamoDB, AppSync JS resolvers, Next.js/React (pnpm), CDK v2 TypeScript.

## Comandos

```bash
dotnet build DuckStore.slnx
dotnet test tests/Services/Challenges/Challenges.UnitTests/Challenges.UnitTests.csproj
dotnet format DuckStore.slnx
dotnet run --project src/AppHost/AppHost.csproj           # dev local (Aspire)
cd infra && npx cdk synth ChallengesStack && npx cdk diff ChallengesStack
cd infra && npx cdk synth AppSyncStack
cd src/WebApps/Shopping.Web.SPA.React && pnpm lint && pnpm build
```

## Estrutura

```
src/Services/Challenges/Challenges.Function/Modules/Progress/
  Data/PointsTransactionsSchema.cs            novo: nome da tabela, LSI1/GSI1, prefixos de TransactionId
  Data/DynamoPlayerProgressRepository.cs      alterado: 3º item na TransactWriteItems
  Domain/Entities/PointsTransaction.cs        novo
  Domain/Enums/PointsTransactionType.cs       novo
  Domain/Enums/PointsTransactionStatus.cs     novo
src/Services/Challenges/Challenges.DevelopmentDataSeeder/DynamoTableInitializer.cs   cria a tabela
src/AppHost/ChallengesExtensions.cs           wiring local, se houver ajuste
infra/constructs/challenges-dynamodb.ts       tabela + LSI1 + GSI1
infra/constructs/challenges-lambdas.ts        grant de escrita para challenges-submit-answer
graphql/schema.graphql                        tipos + myPointsHistory
graphql/resolvers/challenges/queries/Query.myPointsHistory.js
infra/constructs/appsync-api.ts               data source + resolver
src/WebApps/Shopping.Web.SPA.React/features/points/**
src/WebApps/Shopping.Web.SPA.React/app/my-points/page.tsx
src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts
tests/Services/Challenges/Challenges.UnitTests/...   espelha a estrutura de src
docs/adr/0048-points-transactions-ledger-and-in-cart-redemption.md   (ver Limites)
```

## Estilo de código

Seguir o padrão "thin handlers, rich domain" que já existe. O domínio decide o crédito e o repositório só traduz para itens DynamoDB:

```csharp
// PointsTransaction é imutável depois de criada como crédito; só o resgate muda de status.
public static PointsTransaction ChallengeCredit(OwnerId ownerId, QuestionId questionId, int points, DateTime at)
{
    if (points <= 0) throw new BadRequestException(nameof(points), points, "must be greater than zero");

    return new PointsTransaction
    {
        Id = $"{PointsTransactionsSchema.ChallengePrefix}{questionId.Value}",
        OwnerId = ownerId,
        Type = PointsTransactionType.ChallengeCredit,
        Status = PointsTransactionStatus.Completed,
        Points = points,
        CreatedAt = at
    };
}
```

- Nomes de tabela, índices e prefixos ficam só em `PointsTransactionsSchema` e são reutilizados pelo seeder, pelo repositório e pelo AppHost (mesmo padrão de `ProgressSchema`).
- Comentários explicam o *porquê* e citam a ADR, como no restante do código.
- Resolvers JS: `ctx.identity.sub` como única fonte do owner. Nunca aceitar owner vindo do cliente.

## Testes

- **Unit (xUnit + Moq + Moq.AutoMock)**, em `tests/Services/Challenges/Challenges.UnitTests`:
  - `PointsTransaction.ChallengeCredit` rejeita pontos ≤ 0 e gera o `TransactionId` determinístico.
  - `SubmitAnswerHandler`: resposta correta inclui o crédito, resposta errada não inclui.
  - Repositório: a `TransactWriteItems` montada tem 3 itens para resposta correta e 2 para errada, e o Put do ledger tem `attribute_not_exists`.
- **Manual (Aspire local)**: os critérios 1 a 7. Os critérios 1 e 2 (Google e Amazon) só podem ser validados no ambiente AWS dev, porque a federação não existe no DynamoDB Local nem no `local.ts`.
- **SPA**: o projeto não tem framework de teste. A verificação é `pnpm lint`, `pnpm build` e o roteiro manual.

## Limites

- **Sempre:** atualizar `local.ts` junto com cada resolver novo; manter crédito, attempt e saldo na mesma transação; rodar os testes do Challenges antes de commitar.
- **Perguntar antes:** mudar a chave ou os índices de `challenge-progress`; qualquer mudança no fluxo de federação do Cognito (`appsync-auth.ts`).
- **Nunca:** colocar valor em moeda no ledger ou em qualquer evento do Challenges (ADR-0046 §1); calcular o saldo somando o ledger em leitura; pôr TTL no ledger.

## ADR

Escrever a **ADR-0048** (skill `adr`) neste módulo, cobrindo o ledger, a máquina de estados e o resgate no carrinho. Ela emenda a ADR-0046 (cupom → pontos no carrinho) e a ADR-0045 (Challenges ganha consumers). Os módulos seguintes só fazem referência a ela.

## Perguntas abertas

Nenhuma.
