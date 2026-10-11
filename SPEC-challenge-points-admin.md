# Spec: challenge-points-admin

> Módulo 2 de 5 do mapa em [SPEC.md](./SPEC.md). Depende de: nada (pode ir em paralelo com os outros).
> Status: **AGUARDANDO APROVAÇÃO.**

## Objetivo

Permitir que o admin veja a pontuação de cada desafio e a ajuste sem deploy. O valor novo vale **só para respostas futuras**: tentativas e créditos passados não são recalculados.

**Usuário:** usuário do pool de gestão no grupo `Admin`, no `Managment.Web.Blazor`.

### Critérios de aceite

1. Como Admin, abro "Desafios" no Blazor e vejo a lista com título, linguagem, dificuldade e pontos atuais.
2. Altero os pontos de um desafio e salvo: a lista mostra o valor novo e a loja (`challenges`/`challenge`) passa a exibir o mesmo valor.
3. Um cliente que responde corretamente **depois** da mudança ganha o valor novo (menos a penalidade de dica).
4. Um cliente que já tinha respondido **antes** mantém o crédito e a linha do histórico originais.
5. Valor ≤ 0 ou não inteiro é rejeitado com mensagem de erro, no Blazor e no servidor.
6. Usuário do grupo `Seller`, cliente da loja ou chamada com API key recebe `Unauthorized`.

## Desenho

### Escrita

Mutation nova `updateChallengePoints(id: ID!, points: Int!): Challenge!`, com resolver **direto DynamoDB**:

- `UpdateItem` no item `PUBLIC` do desafio (`challenges` table): `SET Points = :points, UpdatedAt = :now`.
- `ConditionExpression: attribute_exists(QuestionId)`. Desafio inexistente vira erro de "não encontrado", não um item fantasma.
- `points > 0` é validado no resolver antes do `UpdateItem`.
- Autorização igual a `createProduct`/`createProductWithPrice`: `ctx.identity.groups` precisa conter `Admin`. Nesta mutation **só Admin**; `Seller` não entra.

Classificação (skill `resolver-selection`): é um único item e um único atributo, e o invariante (`> 0`) cabe no resolver. Não há regra de domínio entre agregados, então é direto, sem Lambda.

Por que só o item `PUBLIC`: `DynamoQuestionRepository` lê `Points` do `PUBLIC` (linha 106) e `Question.Grade` usa esse valor no momento da resposta. A próxima resposta já usa o valor novo, e isso é exatamente o comportamento "dali em diante".

O item `ANSWER` não é tocado, e o isolamento da ADR-0045 §2 continua valendo.

### Leitura

Usa a query `challenges` que já existe (paginada, com filtros). Ela já retorna `points`. Não é preciso query nova.

### Blazor

- `Pages/Challenges/ChallengeList.razor`: tabela com edição inline do campo pontos (mesmo padrão visual de `Pages/Campaigns`).
- `Services/ChallengeAdminService.cs`: chamadas GraphQL via o `GraphQLClient` que já existe (mesmo formato de `CampaignAdminService`).
- `Services/Models.cs`: DTO `ChallengeSummary`.
- Item "Desafios" no menu do `Layout`.

### Fora do escopo

- Criar ou excluir desafios, ou editar enunciado, opções e resposta.
- Recalcular saldos de quem já respondeu.
- Auditoria de quem mudou a pontuação. O `UpdatedAt` basta na v1.

## Tech stack

Blazor WebAssembly (.NET 10), AppSync JS resolvers, CDK v2.

## Comandos

```bash
dotnet build DuckStore.slnx
dotnet run --project src/AppHost/AppHost.csproj
cd infra && npx cdk synth AppSyncStack && npx cdk diff AppSyncStack
dotnet format DuckStore.slnx
```

## Estrutura

```
graphql/schema.graphql                                     mutation updateChallengePoints
graphql/resolvers/challenges/mutations/Mutation.updateChallengePoints.js
infra/constructs/appsync-api.ts                            resolver no data source da tabela challenges
src/WebApps/Managment.Web.Blazor/Pages/Challenges/ChallengeList.razor
src/WebApps/Managment.Web.Blazor/Services/ChallengeAdminService.cs
src/WebApps/Managment.Web.Blazor/Services/Models.cs
src/WebApps/Managment.Web.Blazor/Layout/...                item de menu
```

## Estilo de código

```js
import { util } from '@aws-appsync/utils'

// Admin-only, direto (ADR-0009): um item, um atributo, invariante local. Só o item PUBLIC muda —
// Question.Grade lê Points dele na hora da resposta, então o valor novo vale "dali em diante" sem
// tocar em attempts nem no ledger (points-ledger). O item ANSWER fica intocado (ADR-0045 §2).
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  const { id, points } = ctx.args
  if (!Number.isInteger(points) || points <= 0) util.error('points must be a positive integer', 'BadRequest')

  return {
    operation: 'UpdateItem',
    key: util.dynamodb.toMapValues({ QuestionId: id, SK: 'PUBLIC' }),
    update: { expression: 'SET Points = :p, UpdatedAt = :now', expressionValues: util.dynamodb.toMapValues({ ':p': points, ':now': util.time.nowISO8601() }) },
    condition: { expression: 'attribute_exists(QuestionId)' },
  }
}
```

Os nomes reais de PK/SK e dos atributos vêm de `ChallengesSchema.cs`. Confirmar no Plan antes de escrever.

## Testes

- O resolver JS não tem teste unitário no repo, então a validação é manual. Rodar `cdk synth AppSyncStack` garante que o resolver compila e está ligado.
- **Unit (Challenges.UnitTests)**: um teste de `Question.Grade` provando que o crédito usa o `Points` carregado (garantia do "dali em diante").
- **Manual**: critérios 1 a 6 no ambiente AWS dev, que é onde existem os grupos do Cognito de gestão. Localmente, o Blazor aponta para o AppSync dev.

## Limites

- **Sempre:** checar `Admin` no próprio resolver (não só na UI); validar `points > 0` no servidor.
- **Perguntar antes:** liberar a mutation para `Seller`; recalcular créditos passados.
- **Nunca:** ler ou escrever o item `ANSWER`; expor a resposta correta na lista do admin.

## Perguntas abertas

Nenhuma.
