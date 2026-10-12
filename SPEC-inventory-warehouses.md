# Spec: inventory-warehouses

> Módulo 1 de 4 do mapa em [SPEC-inventory.md](./SPEC-inventory.md). Depende de: nenhum.
> Status: **APROVADO** (revisão 1, 2026-10-12). Plano: [`tasks/inventory/plan.md`](./tasks/inventory/plan.md).

## Objetivo

Criar o bounded context **Inventory** com o cadastro de **polos** (armazéns) e o grupo **StockKeeper**, para que o responsável pelo estoque cadastre, edite, ative e inative os polos no app de gestão. Ainda não há saldo nem movimento: isso é o módulo 2. Este módulo entrega o lugar onde o estoque vai morar e quem pode mexer nele.

### Critérios de aceite

1. Um usuário do grupo `StockKeeper` entra no Blazor e vê o item **Warehouses** no menu. Um `Admin` também vê. Um `Seller` não vê.
2. O StockKeeper cadastra o polo `ALPHA` ("Polo Alpha", endereço completo, prioridade 1) e o `BETA` (prioridade 2). Os dois aparecem na lista, **ativos**, ordenados por prioridade e depois por código.
3. Cadastrar outro polo com o código `ALPHA` (ou `alpha`, já que o código é normalizado para maiúsculas) falha com `WarehouseCodeTaken`, e nada é gravado.
4. Uma entrada inválida falha com `BadRequest` e `errorInfo.field` apontando o campo, sem gravar nada: código fora do formato, nome vazio, UF inexistente, CEP sem 8 dígitos ou prioridade fora de 1–999. O Blazor mostra o erro no campo, e a mesma validação roda no formulário antes do envio.
5. O StockKeeper edita nome, endereço e prioridade de um polo. O **código não é editável** (não está no input de edição).
6. O StockKeeper inativa o `BETA`, que continua na lista, marcado como inativo, e depois o reativa. Não existe delete.
7. Editar ou inativar um id que não existe falha com `NotFound`.
8. Um token do grupo `Seller`, ou um do pool de clientes (`Customer`), recebe `Unauthorized` em **todas** as operações deste módulo, inclusive as leituras.
9. Localmente (Aspire + SPA `local.ts` + Blazor com o principal de dev `Admin`), os critérios 2 a 7 funcionam contra o DynamoDB Local. O seeder cria a tabela e semeia `ALPHA` e `BETA`.

## Desenho

### Tabela `inventory-warehouses`

Dona: Inventory. PK `WarehouseId` (S), sem SK, sem índices e sem streams. As configurações (billing, removal policy, PITR) são as mesmas das demais tabelas (`infra/constructs/challenges-dynamodb.ts`). O stream entra no módulo 3, se o total da loja precisar reagir à inativação de um polo.

| Item | `WarehouseId` | Atributos |
|---|---|---|
| Polo | `<uuid>` (`util.autoId()`) | `Type = Warehouse`, `Code`, `Name`, `Street`, `Number`, `Complement?`, `District`, `City`, `State`, `ZipCode` (8 dígitos, sem hífen), `Priority` (N), `Active` (BOOL), `CreatedAt`, `UpdatedAt`, `UpdatedBy` (`ctx.identity.sub`) |
| Guarda do código | `CODE#<code>` | `Type = CodeGuard`, `TargetWarehouseId` |

- **Unicidade do código:** `createWarehouse` grava o polo e a guarda numa `TransactWriteItems`, as duas com `attribute_not_exists(WarehouseId)`. Se a guarda já existe, a transação inteira cai, e o erro é `WarehouseCodeTaken`. A guarda nunca é apagada: o código é imutável e o polo não é deletado.
- **Id interno ≠ código:** o saldo e o ledger (módulo 2) referenciam o `WarehouseId`. O código é o rótulo humano.
- **Polo inativo:** neste módulo é só um flag. Os efeitos (sem entradas, fora do total, fora da alocação) entram nos módulos 2 a 4.

### GraphQL (`graphql/schema.graphql`)

```graphql
# Inventory (ADR-0052). Management pool only — StockKeeper or Admin (enforced in every resolver).
type Warehouse @aws_cognito_user_pools {
  id: ID!
  # Human label, unique, uppercase, immutable after creation (e.g. ALPHA, SP-01).
  code: String!
  name: String!
  address: WarehouseAddress!
  # Lower serves first when a sale is allocated (module 4); ties broken by code.
  priority: Int!
  active: Boolean!
  createdAt: AWSDateTime!
  updatedAt: AWSDateTime!
}

type WarehouseAddress @aws_cognito_user_pools {
  street: String!
  number: String!
  complement: String
  district: String!
  city: String!
  state: String!   # UF, e.g. SP
  zipCode: String! # CEP, 8 digits
}

input WarehouseAddressInput { street: String!, number: String!, complement: String, district: String!, city: String!, state: String!, zipCode: String! }
input CreateWarehouseInput { code: String!, name: String!, address: WarehouseAddressInput!, priority: Int! }
input UpdateWarehouseInput { id: ID!, name: String!, address: WarehouseAddressInput!, priority: Int! }

# Query
warehouses: [Warehouse!]! @aws_cognito_user_pools
warehouse(id: ID!): Warehouse @aws_cognito_user_pools

# Mutation
createWarehouse(input: CreateWarehouseInput!): Warehouse! @aws_cognito_user_pools
updateWarehouse(input: UpdateWarehouseInput!): Warehouse! @aws_cognito_user_pools
setWarehouseActive(id: ID!, active: Boolean!): Warehouse! @aws_cognito_user_pools
```

### Resolvers (todos diretos, ADR-0009)

Nenhum dos quatro critérios de escalação se aplica. São um item (mais a guarda do próprio código) numa tabela, sem chamada a outro serviço, e toda validação cabe no resolver.

| Campo | Operação | Detalhe |
|---|---|---|
| `createWarehouse` | `TransactWriteItems` (polo + guarda) | Normaliza: `trim`, código e UF em maiúsculas, CEP sem hífen. Nasce `Active = true`. A resposta é montada a partir do item gravado, porque a transação não devolve itens. Cancelamento pela guarda → `WarehouseCodeTaken` |
| `updateWarehouse` | `UpdateItem` `ALL_NEW` | `SET` nome, endereço, prioridade, `UpdatedAt`, `UpdatedBy`. Condição `attribute_exists(WarehouseId) AND #Type = :warehouse` → `NotFound` |
| `setWarehouseActive` | `UpdateItem` `ALL_NEW` | `SET Active`. Mesma condição e mesmo `NotFound` |
| `warehouses` | `Scan` com filtro `#Type = :warehouse` | A ordenação (prioridade, depois código) é feita no `response`. Sem paginação: a premissa é que haverá dezenas de polos, não milhares |
| `warehouse` | `GetItem` | `null` se não existe ou se `Type ≠ Warehouse` (um id `CODE#...` nunca vira polo) |

Todo `request` começa pela checagem de grupo, como em `Mutation.updateChallengePoints.js`: `StockKeeper` ou `Admin`, senão `util.unauthorized()`.

**Validação** (fonte única de verdade: o resolver, espelhado no `local.ts` e no Blazor com comentário apontando os três lugares, como o `MAX_POINTS`):

| Campo | Regra |
|---|---|
| `code` | `^[A-Z0-9][A-Z0-9-]{1,19}$` depois de maiúsculas |
| `name` | 1–80 caracteres |
| `street` / `number` / `complement` / `district` / `city` | 1–120 / 1–10 / 0–60 / 1–60 / 1–60 |
| `state` | uma das 27 UFs |
| `zipCode` | 8 dígitos depois de remover `-` |
| `priority` | inteiro 1–999 |

Erros: `util.error(msg, 'BadRequest', null, { field })`, `'WarehouseCodeTaken'`, `'NotFound'`. Os campos `field` usam o nome do GraphQL (`address.zipCode`, por exemplo).

### Cognito

`infra/constructs/appsync-auth.ts`: um `CfnUserPoolGroup` `StockKeeper` no pool de gestão (`duckstore-management-users`), com a descrição "Stock keepers who manage warehouses and stock". Os usuários são criados como os demais (AdminCreateUser + grupo). O grupo vale a partir do próximo login, porque o grupo vai no token.

### Blazor (`src/WebApps/Managment.Web.Blazor`)

- `Auth/CognitoGroups.cs`: constante `StockKeeper` e `CanManageStock(user)` = `Admin` ou `StockKeeper`.
- `Layout/MainLayout.razor`: item **Warehouses**, visível só com `CanManageStock`. Os itens existentes não mudam.
- `Pages/Warehouses/`: `WarehouseList.razor` (código, nome, cidade/UF, prioridade, status, botão Ativar/Inativar, link Editar), `WarehouseCreate.razor`, `WarehouseEdit.razor` e `WarehouseForm.razor` (compartilhado, código só leitura na edição). Mesma estrutura de `Pages/Products/`. As páginas exigem `CanManageStock`. Sem o grupo, mostram "sem acesso".
- `Services/WarehouseAdminService.cs` + modelos em `Services/Models.cs`, registrado em `Program.cs`. A UI segue em inglês, como o resto do app.

### Local

- `src/Services/Inventory/Inventory.DevelopmentDataSeeder` (Worker, como o do Challenges): cria `inventory-warehouses` e semeia `ALPHA` (prioridade 1) e `BETA` (prioridade 2), com as guardas. É idempotente: não duplica nada a cada `dotnet run`. Os nomes e as chaves ficam numa classe de schema no próprio seeder até o módulo 2 criar o `Inventory.Function`, quando passam para lá.
- `src/AppHost/InventoryExtensions.cs` + `builder.AddInventoryServices(dynamoDb)` em `Program.cs`.
- `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`: os cinco campos contra o DynamoDB Local, com a mesma validação e os mesmos erros e sem checagem de grupo (como o resto do `local.ts`). A validação fica em `lib/warehouse-validation.ts`, testada.

### Infra

- `infra/constructs/inventory-dynamodb.ts` + `infra/stacks/inventory-stack.ts` (`DuckStoreInventoryStack`), registrado em `infra/bin/app.ts`. Ainda sem `inventory-lambdas.ts`: não há Lambda neste módulo.
- `infra/constructs/appsync-api.ts`: `Table.fromTableName('inventory-warehouses')` (nome fixo, nunca `Fn.importValue`), um data source e os cinco resolvers.
- `.github/workflows/deploy-inventory-cdk.yml`: só `cdk deploy DuckStoreInventoryStack` (sem publicar artefato .NET, porque ainda não há função), com gatilho em `infra/**` e `src/Services/Inventory/**`. O passo de publicação entra no módulo 2.
- Ordem de deploy no dev: `DuckStoreInventoryStack` antes de `DuckStoreAppSyncStack` (os resolvers referenciam a tabela pelo nome).

### ADR-0052

Nova, escrita neste módulo (status **Proposed**, vira **Accepted** no PR). Decide: o Inventory é o dono do estoque por polo, o modelo do polo (id interno, código imutável e único por guarda, prioridade, inativação em vez de delete), o grupo `StockKeeper` e quem lê e escreve. Registra como **direção** (os detalhes ficam para emendas dos módulos 2 a 4) o saldo produto × polo com ledger, o handoff do Catalog e a reserva nas sagas (ADR-0050/0051). Revisada pelo `adr-guardian` antes de aceitar.

### Fora do escopo

- Saldo, movimentos e histórico (módulo 2).
- Coordenadas, posição interna e mapa.
- Delete de polo e edição do código.
- Paginação de `warehouses`.
- Esconder do StockKeeper os itens de menu existentes (Products, Discounts, Challenges). Os resolvers já rejeitam o que ele não pode fazer.

## Comandos

```bash
dotnet build DuckStore.slnx
dotnet run --project src/AppHost/AppHost.csproj
cd src/WebApps/Shopping.Web.SPA.React && pnpm test && pnpm typecheck && pnpm lint
cd infra && npx cdk synth DuckStoreInventoryStack DuckStoreAppSyncStack
cd infra && npx cdk diff DuckStoreInventoryStack DuckStoreAppSyncStack
# Resolver contra o runtime real do APPSYNC_JS (sem deploy), um contexto por cenário:
aws appsync evaluate-code --runtime name=APPSYNC_JS,runtimeVersion=1.0.0 \
  --code file://graphql/resolvers/inventory/mutations/Mutation.createWarehouse.js \
  --function request --context file://<scratchpad>/ctx-seller.json
# E2E contra o dev (Playwright); a primeira execução baixa o Chromium
source ~/.config/duckstore/e2e.env && source ~/.config/duckstore/e2e-inventory.env
dotnet test tests/E2E/Inventory/Inventory.E2ETests
```

## Estrutura

```
graphql/schema.graphql                                               tipos, inputs, 2 queries, 3 mutations
graphql/resolvers/inventory/queries/Query.{warehouses,warehouse}.js
graphql/resolvers/inventory/mutations/Mutation.{createWarehouse,updateWarehouse,setWarehouseActive}.js
infra/constructs/inventory-dynamodb.ts                               novo
infra/stacks/inventory-stack.ts                                      novo
infra/bin/app.ts                                                     + DuckStoreInventoryStack
infra/constructs/appsync-api.ts                                      + data source e resolvers
infra/constructs/appsync-auth.ts                                     + grupo StockKeeper
.github/workflows/deploy-inventory-cdk.yml                           novo
src/Services/Inventory/Inventory.DevelopmentDataSeeder/**            novo
src/Services/Inventory/README.md                                     novo
src/AppHost/InventoryExtensions.cs, Program.cs, AppHost.csproj
src/WebApps/Managment.Web.Blazor/{Auth/CognitoGroups.cs, Layout/MainLayout.razor, Program.cs}
src/WebApps/Managment.Web.Blazor/Pages/Warehouses/*.razor           novo
src/WebApps/Managment.Web.Blazor/Services/{WarehouseAdminService.cs, Models.cs}
src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts
src/WebApps/Shopping.Web.SPA.React/lib/warehouse-validation{.ts,.test.ts}
tests/E2E/E2E.Infrastructure/{ManagementSession.cs, E2ESettings.cs}   sessão do Blazor + variáveis de gestão
tests/E2E/Inventory/Inventory.E2ETests/**                            nova suíte Playwright
tests/E2E/README.md                                                  + contexto Inventory e variáveis
docs/adr/0052-*.md, docs/adr/README.md, CLAUDE.md
DuckStore.slnx                                                       + seeder
```

## Estilo de código

```js
import { util } from '@aws-appsync/utils'

// StockKeeper or Admin (ADR-0052). Direct TransactWriteItems resolver (ADR-0009): the warehouse
// and the CODE#<code> guard are written together, both attribute_not_exists, so a taken code
// cancels the whole write — the guard is what makes the code unique, not a read before the write.
// Validation mirrored by local.ts (lib/warehouse-validation.ts) and the Blazor WarehouseForm.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('StockKeeper') && !groups.includes('Admin')) util.unauthorized()

  const warehouse = normalize(ctx.args.input)   // throws BadRequest with { field }
  const id = util.autoId()
  const now = util.time.nowISO8601()
  ctx.stash.warehouse = { ...warehouse, id, active: true, createdAt: now, updatedAt: now }

  return {
    operation: 'TransactWriteItems',
    transactItems: [
      putIfAbsent({ WarehouseId: id }, toItem(ctx.stash.warehouse, ctx.identity.sub)),
      putIfAbsent({ WarehouseId: `CODE#${warehouse.code}` }, { Type: 'CodeGuard', TargetWarehouseId: id }),
    ],
  }
}
```

No Blazor, seguir `Pages/Products/` (form compartilhado, serviço por contexto, `GraphQLClient`).

## Testes

- **Unit (SPA `lib`):** `warehouse-validation.test.ts`, com um caso por regra da tabela de validação, incluindo os de borda: código com 2 e com 20 caracteres, `alpha` normalizado, CEP com hífen aceito, UF minúscula aceita, prioridade 0 e 1000 rejeitadas.
- **Resolver (APPSYNC_JS real, sem deploy):** `aws appsync evaluate-code` nos cinco resolvers. `request` com Seller, Customer e sem grupo (todos `Unauthorized`). Um `BadRequest` por campo (os mesmos casos do unit test, que pegam divergência entre as duas cópias). `response` de `createWarehouse` com a transação cancelada pela guarda (`WarehouseCodeTaken`). `response` de `update`/`setActive` com `ConditionalCheckFailed` (`NotFound`). `warehouses` ignorando a guarda e ordenando por prioridade e código.
- **Infra:** `cdk synth` dos dois stacks limpo. O `cdk diff` no dev mostra a tabela, o grupo, o data source e os resolvers, e nada além disso.
- **E2E (Playwright, AWS dev):** nova suíte `tests/E2E/Inventory/Inventory.E2ETests`, no padrão do `Checkout.E2ETests` (xUnit + Playwright .NET, `[E2EFact]`, pulada sem as variáveis de ambiente). O `E2E.Infrastructure` ganha um `ManagementSession`, que faz o login no Blazor pelo managed login do pool de gestão, como o `StorefrontSession` faz na loja. Cobre os critérios 1 a 8:
  - **UI:** menu Warehouses para StockKeeper e Admin, e ausente para o Seller. Criar, editar, inativar e reativar. O erro por campo no formulário.
  - **API:** com o access token da sessão do Blazor, chama o AppSync direto para os erros (`WarehouseCodeTaken`, `BadRequest` por campo, `NotFound`) e para o `Unauthorized` do Seller. O `Unauthorized` do cliente sai do `StorefrontSession` existente (`e2e-checkout@duckstore.test`), pela `/api/graphql` da loja.
  - Os polos criados pelos testes usam código com prefixo `E2E-` e um sufixo aleatório, porque não há delete: a suíte os inativa no fim, e eles ficam como histórico no dev.
- **Manual local:** critérios 2 a 7 e 9 no Aspire.
- Não há unit test .NET: este módulo não tem lógica .NET além do seeder. A validação de UI é feita pela suíte Playwright, não por ferramenta de navegador do agente.

## Limites

- **Sempre:** a checagem de grupo é a primeira linha de todo resolver de Inventory. Validação idêntica nos três lugares, com comentário cruzado. Tabela referenciada pelo nome fixo.
- **Perguntar antes:** permitir editar o código, deletar polo, acrescentar campos ao polo (coordenadas, responsável, telefone) ou abrir leitura ao `Seller`.
- **Nunca:** `Fn.importValue` para a tabela. Um `Scan` sem o filtro de `Type` (ele devolveria as guardas como polos). Gravar o polo sem a guarda.

## Usuários de teste (AWS dev)

Criados em 2026-10-12 no pool `duckstore-management-users` (`us-east-1_YEh8NXpcQ`), no mesmo formato do `e2e-checkout@duckstore.test`: e-mail verificado, senha definitiva e status `CONFIRMED`. As credenciais ficam só em `~/.config/duckstore/e2e-inventory.env` (permissão 600, fora do repo), carregado com `source` antes da suíte, junto com o `e2e.env` do checkout (que traz o cliente).

| Nome | E-mail | Grupo | Variáveis |
|---|---|---|---|
| E2E Inventory StockKeeper | `e2e-inventory-stockkeeper@duckstore.test` | `StockKeeper` (**pendente**: o grupo nasce no deploy deste módulo, e a tarefa de deploy o atribui) | `E2E_STOCKKEEPER_EMAIL`/`_PASSWORD` |
| E2E Inventory Seller | `e2e-inventory-seller@duckstore.test` | `Seller` | `E2E_SELLER_EMAIL`/`_PASSWORD` |
| E2E Inventory Admin | `e2e-inventory-admin@duckstore.test` | `Admin` | `E2E_ADMIN_EMAIL`/`_PASSWORD` |
| E2E Checkout (já existia, pool de clientes) | `e2e-checkout@duckstore.test` | — (cliente) | `E2E_USER_EMAIL`/`_PASSWORD` em `e2e.env` |

O grupo `StockKeeper` **não** é criado à mão: o CDK o cria como `CfnUserPoolGroup`, e um grupo pré-existente com o mesmo nome faria o deploy falhar.

## Perguntas abertas

Nenhuma.
