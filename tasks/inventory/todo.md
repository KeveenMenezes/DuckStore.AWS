# Tarefas: `inventory-warehouses`

> Plano: [`plan.md`](./plan.md) · Spec: [`SPEC-inventory-warehouses.md`](../../SPEC-inventory-warehouses.md)
> Usuários de teste: `~/.config/duckstore/e2e-inventory.env` (StockKeeper, Seller, Admin) e `~/.config/duckstore/e2e.env` (cliente).

## Fase 1: Fundação

- [x] ****T0: Branch e specs no controle de versão** (XS) — feito (`113aad1`); branch sem upstream (o push vai para `feat/inventory-warehouses`, nunca para `development`)
  - Aceite: `feat/inventory-warehouses` criado a partir de `origin/development` atualizado. Commit com `SPEC-inventory.md`, `SPEC-inventory-warehouses.md` e `tasks/inventory/`. As mudanças locais alheias (`next.config.mjs`, `sst-env.d.ts`, `docs/social/`) ficam fora do commit.
  - Verificar: `git log origin/development..HEAD` mostra só o commit de docs. `git status` sem os specs pendentes.
  - Arquivos: `SPEC-inventory*.md`, `tasks/inventory/*`

- [x] ****T1: ADR-0052, bounded context Inventory** (XS) — feito (`53ec3f7`, `d2b718b`); `adr-guardian`: nenhum bloqueante, 7 correções e 8 nits aplicados (critérios da ADR-0009 um por um, `TransactionConflict`, grant explícito, emenda à ADR-0032, ADR-0051 reservada no índice, `NotFound` PascalCase de propósito)
  - Aceite: ADR no padrão ADR-0000 (skill `adr`), status **Proposed**. Decide o dono do estoque por polo, o modelo do polo (id interno, código imutável e único por guarda, prioridade, inativação em vez de delete), o grupo `StockKeeper` e a matriz de leitura e escrita. Registra a direção dos módulos 2 a 4 (ledger, handoff, reserva nas sagas ADR-0050/0051). Indexada em `docs/adr/README.md`.
  - Verificar: `adr-guardian` sobre a ADR e o spec, com os achados aplicados.
  - Arquivos: `docs/adr/0052-inventory-bounded-context-warehouses.md`, `docs/adr/README.md`

- [x] ****T2: Tabela local: seeder + AppHost** (M) — feito (`bce54f6`); 4 itens no DynamoDB Local (2 polos + 2 guardas), e continua 4 depois de rodar o seeder de novo
  - Aceite: `Inventory.DevelopmentDataSeeder` (Worker, no molde do `Challenges.DevelopmentDataSeeder`) cria `inventory-warehouses` (PK `WarehouseId`) e semeia `ALPHA` (prioridade 1) e `BETA` (prioridade 2) com as guardas `CODE#`. É idempotente: o segundo `dotnet run` não duplica nada. `InventoryExtensions.AddInventoryServices(dynamoDb)` está registrado no `Program.cs`, o projeto entra no `DuckStore.slnx`, e o `AppHost.csproj` o referencia.
  - Verificar: `dotnet build DuckStore.slnx`. Com o Aspire de pé, `aws dynamodb scan --table-name inventory-warehouses --endpoint-url http://localhost:8000` mostra 4 itens (2 polos + 2 guardas), e a contagem continua 4 depois de reiniciar.
  - Arquivos: `src/Services/Inventory/Inventory.DevelopmentDataSeeder/{Program,Worker,DynamoTableInitializer,WarehousesSchema,InventoryInitialData}.cs` + `.csproj`, `src/AppHost/{InventoryExtensions.cs,Program.cs,AppHost.csproj}`, `DuckStore.slnx`

- [x] ****T3: Infra: `InventoryStack`, grupo `StockKeeper`, workflow** (M) — feito (`8e91374`); `cdk synth DuckStoreInventoryStack` com uma tabela e nada mais; workflow sem .NET (ubuntu-latest)
  - Aceite: `inventory-dynamodb.ts` com as mesmas configurações das demais tabelas (billing, removal policy, PITR) e `inventory-stack.ts` (`DuckStoreInventoryStack`) registrado em `app.ts`. `CfnUserPoolGroup` `StockKeeper` no pool de gestão. `deploy-inventory-cdk.yml` só com o `cdk deploy` (sem publicar .NET), com gatilho em `infra/**` e `src/Services/Inventory/**`.
  - Verificar: `cd infra && npx cdk synth DuckStoreInventoryStack DuckStoreAppSyncStack` limpo. O template do Inventory tem uma tabela e nada mais, e o do AppSync tem o grupo novo.
  - Arquivos: `infra/constructs/inventory-dynamodb.ts`, `infra/stacks/inventory-stack.ts`, `infra/bin/app.ts`, `infra/constructs/appsync-auth.ts`, `.github/workflows/deploy-inventory-cdk.yml`

- [x] ****T4: Contrato GraphQL + validação compartilhada** (S) — feito (`5d08e9a`); `pnpm test` 27 casos verdes, `typecheck` limpo
  - Aceite: `Warehouse`, `WarehouseAddress`, os três inputs, `warehouses`/`warehouse` e as três mutations no `schema.graphql`, como no spec. `lib/warehouse-validation.ts` normaliza (trim, maiúsculas, CEP sem hífen) e valida, devolvendo `{ field, message }`. Os testes cobrem cada regra e as bordas listadas no spec.
  - Verificar: `cd src/WebApps/Shopping.Web.SPA.React && pnpm test && pnpm typecheck`.
  - Arquivos: `graphql/schema.graphql`, `src/WebApps/Shopping.Web.SPA.React/lib/warehouse-validation{.ts,.test.ts}`

### Checkpoint A
- [x] `dotnet build DuckStore.slnx`, `pnpm test`, `pnpm typecheck` e `cdk synth` (Inventory, AppSync) verdes.
- [x] Tabela local semeada e idempotente.

## Fase 2: Fatias

- [x] ****T5: Criar, listar e consultar polos** (M) — feito (`54904f4`, junto com a T6); `evaluate-code` 86/86 (`scripts/inventory/evaluate-resolvers.mjs`); BFF local 19/19 (`scripts/inventory/local-api-checks.mjs`); `cdk synth DuckStoreAppSyncStack`: grupo, data source com leitura + `PutItem`/`UpdateItem` e 5 resolvers. **Achado do runtime real:** APPSYNC_JS rejeita comparador no `sort` (nenhuma função pode ser passada como argumento) e ignorou um comparador nomeado sem erro; a ordenação usa uma chave em texto
  - Aceite: `Mutation.createWarehouse.js` (gate de grupo na primeira linha, validação, `TransactWriteItems` polo + guarda, `WarehouseCodeTaken`), `Query.warehouses.js` (`Scan` com filtro de `Type`, ordenado por prioridade e código) e `Query.warehouse.js` (`null` para guarda ou id inexistente). Data source por `Table.fromTableName('inventory-warehouses')` e os três resolvers no `appsync-api.ts`. O `local.ts` imita os três, com a validação de `lib/`.
  - Verificar: `aws appsync evaluate-code` com os contextos Seller, Customer e sem grupo (`Unauthorized`), um `BadRequest` por campo, o cancelamento pela guarda e a ordenação. `cdk synth DuckStoreAppSyncStack`. No local, `curl` no `/api/graphql` do BFF: criar `GAMMA`, repetir `gamma` (`WarehouseCodeTaken`), CEP inválido (`BadRequest`, `field = address.zipCode`) e listar (ALPHA, BETA, GAMMA).
  - Arquivos: `graphql/resolvers/inventory/mutations/Mutation.createWarehouse.js`, `graphql/resolvers/inventory/queries/Query.{warehouses,warehouse}.js`, `infra/constructs/appsync-api.ts`, `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`
  - Depende de: T2, T3, T4

- [x] ****T6: Editar e ativar/inativar** (S) — feito (`54904f4`); coberto pelos mesmos 86 + 19 casos
  - Aceite: `Mutation.updateWarehouse.js` e `Mutation.setWarehouseActive.js` (`UpdateItem` `ALL_NEW`, condição `attribute_exists AND Type = Warehouse`, `NotFound`, `UpdatedAt`/`UpdatedBy`). O código não é alterável. Wiring no `appsync-api.ts` e imitação no `local.ts`.
  - Verificar: `evaluate-code` (`Unauthorized`, `BadRequest`, `ConditionalCheckFailed` → `NotFound`). No local, com `curl`, editar a prioridade do `BETA`, inativar e reativar, e ver `NotFound` para id inexistente e para `CODE#ALPHA`.
  - Arquivos: `graphql/resolvers/inventory/mutations/Mutation.{updateWarehouse,setWarehouseActive}.js`, `infra/constructs/appsync-api.ts`, `local.ts`
  - Depende de: T5

- [x] ****T7: Blazor: grupos, menu, serviço e lista** (M) — feito (`609d620`); `dotnet build` limpo. Sem verificação de UI local (o agente não dirige browser por MCP, por decisão do usuário): a UI é validada pelo E2E na T11
  - Aceite: `CognitoGroups.StockKeeper` + `CanManageStock`. Item Warehouses no `MainLayout` só com `CanManageStock`. `WarehouseAdminService` (`warehouses`, `warehouse`, as três mutations e os erros tipados), registrado no `Program.cs`. `WarehouseList.razor` com código, nome, cidade/UF, prioridade, status e "sem acesso" sem o grupo.
  - Verificar: `dotnet build src/WebApps/Managment.Web.Blazor`. No local, o Blazor (principal de dev Admin) lista ALPHA, BETA e GAMMA pelo BFF local, conferido pela requisição GraphQL no log do BFF.
  - Arquivos: `Auth/CognitoGroups.cs`, `Layout/MainLayout.razor`, `Services/{WarehouseAdminService.cs,Models.cs}`, `Program.cs`, `Pages/Warehouses/WarehouseList.razor`
  - Depende de: T5

- [x] ****T8: Blazor: formulário, criar, editar e ativar/inativar** (M) — feito (`609d620`); o `GraphQLClient` passou a expor `ErrorType`/`ErrorField` (AppSync no próprio erro, yoga em `extensions`)
  - Aceite: `WarehouseForm.razor` compartilhado (DataAnnotations espelhando a tabela de validação, com comentário cruzado, e o código só leitura na edição), `WarehouseCreate.razor`, `WarehouseEdit.razor` e o botão Ativar/Inativar na lista. O erro do servidor aparece no campo apontado por `errorInfo.field`, e `WarehouseCodeTaken` aparece no código.
  - Verificar: `dotnet build`. Os seletores usados pela T10 são estáveis (`id` nos inputs, como no `ProductForm`).
  - Arquivos: `Pages/Warehouses/{WarehouseForm,WarehouseCreate,WarehouseEdit,WarehouseList}.razor`, `Services/Models.cs`
  - Depende de: T6, T7

### Checkpoint B
- [x] `evaluate-code` verde nos cinco resolvers, em todos os cenários das T5 e T6 (86/86).
- [x] Critérios 2 a 7 e 9 do spec verificados contra o BFF local, pela API (19/19).
- [x] `dotnet build DuckStore.slnx` (0 warnings), `dotnet test` (unitários verdes; os 2 funcionais do Ordering só falharam com o Aspire local ocupando as portas e passaram depois de pará-lo), `pnpm test`, `pnpm typecheck` e `pnpm lint` (0 erros; os 6 warnings não são de arquivos deste módulo).

## Fase 3: E2E, deploy e docs

- [x] ****T9: `ManagementSession` e configurações de gestão** (M) — feito (`316c7f3`); sem as variáveis, a suíte pula os 12 testes sem baixar browser
  - Aceite: `ManagementSettings` lê `E2E_MANAGEMENT_URL` e os pares `E2E_{STOCKKEEPER,SELLER,ADMIN}_{EMAIL,PASSWORD}`. Sem eles, `[E2EFact]` pula. `ManagementSession` faz o login no Blazor pelo managed login do pool de gestão (um contexto de browser por papel), com o trace iniciado só depois do login (sem senha no trace, como o `StorefrontSession`), e expõe o access token (do `sessionStorage` do OIDC) para chamar o AppSync. O `README` do E2E ganha o contexto Inventory e as variáveis.
  - Verificar: `dotnet build tests/E2E`. `dotnet test` sem as variáveis pula tudo, sem baixar o browser.
  - Arquivos: `tests/E2E/E2E.Infrastructure/{ManagementSettings.cs,ManagementSession.cs,E2EFactAttribute.cs}`, `tests/E2E/README.md`
  - Depende de: T0

- [x] ****T10: Suíte `Inventory.E2ETests`** (M) — feito (`316c7f3`); 12 testes: UI (StockKeeper, Admin, Seller) e API (StockKeeper, Seller, cliente)
  - Aceite:
    - **UI:** menu Warehouses visível para StockKeeper e Admin e ausente para o Seller. Criar `E2E-<sufixo>`, editar a prioridade, inativar, reativar e o erro de campo (CEP).
    - **API:** `WarehouseCodeTaken`, `BadRequest` com `field`, `NotFound`, `Unauthorized` do Seller nas cinco operações e do cliente (`StorefrontSession`) pela `/api/graphql`.
    - **Limpeza:** cada teste inativa o que criou.
  - Verificar: compila e pula sem as variáveis. A execução real fica na T11.
  - Arquivos: `tests/E2E/Inventory/Inventory.E2ETests/{.csproj,AssemblyInfo.cs,WarehousesUiTests.cs,WarehousesApiTests.cs,ManagementApp.cs}`, `DuckStore.slnx`
  - Depende de: T8, T9

- [x] ****T11: Deploy no dev, grupo do StockKeeper e E2E verde** (M), *com as aprovações do plano* — feito (2026-10-12); `cdk diff` conferidos antes de cada deploy: Inventory = só a tabela; AppSync = grupo, data source (leitura + `PutItem`/`UpdateItem`), 5 resolvers e o schema só com acréscimos (comparado com o template implantado) + a renovação anual do `Expires` da API key que todo deploy faz; Management = só o bundle do site. Deploy local na ordem Inventory → AppSync → Management (credenciais SSO exportadas para o CDK, que não lê o perfil `login`; `appsettings.json` gerado como na workflow, idêntico ao implantado, e restaurado depois). `e2e-inventory-stockkeeper@duckstore.test` no grupo `StockKeeper`. **`Inventory.E2ETests` 12/12 na primeira execução; regressão `Checkout.E2ETests` 12/12**
  - Aceite: `cdk diff` mostrado e conferido, e então o deploy, nesta ordem: Inventory → AppSync → Management. `e2e-inventory-stockkeeper@duckstore.test` no grupo `StockKeeper` e o comentário "PENDING" atualizado no `e2e-inventory.env`. `Inventory.E2ETests` verde: critérios 1 a 8. A suíte do checkout continua verde, como regressão do AppSync.
  - Verificar: `source ~/.config/duckstore/e2e.env && source ~/.config/duckstore/e2e-inventory.env && dotnet test tests/E2E/Inventory/Inventory.E2ETests` e `dotnet test tests/E2E/Checkout/Checkout.E2ETests`.
  - Depende de: T10

- [x] ****T12: Documentação** (S) — feito (`d0610c4`); `CLAUDE.md`, `src/Services/Inventory/README.md`, linha no `README.md`, ADR-0052 com "As Built", página **Inventory** no `.drawio` + card na **Main** (11 contextos); validador 15 páginas sem achados; só `main.svg` e `inventory.svg` versionados (os outros SVGs re-exportados eram ruído do exportador e foram descartados)
  - Aceite: `CLAUDE.md` (Inventory na lista de serviços, `InventoryStack` no deploy, `StockKeeper` nos grupos), `src/Services/Inventory/README.md` e a ADR-0052 com o "como construído". Diagramas pela skill `architecture-diagrams`: página do Inventory se o validador ou a skill pedirem. Se não for criada, uma linha dizendo por quê.
  - Verificar: `python3 scripts/validate-diagrams.py` sem achados.
  - Arquivos: `CLAUDE.md`, `src/Services/Inventory/README.md`, `docs/adr/0052-*.md`, diagramas (se for o caso)
  - Depende de: T11

### Checkpoint C (fim do módulo)
- [ ] `adr-guardian` e `agent-skills:code-reviewer` sobre o diff do módulo, com os achados aplicados.
- [ ] Critérios 1 a 9 do spec cumpridos, cada um com a evidência registrada aqui.
- [ ] ADR-0052 → **Accepted** no PR.
- [ ] PR `feat/inventory-warehouses` → `development` (título e resumo mostrados antes).
- [ ] `SPEC-inventory.md` atualizado (módulo 1 concluído), e então o spec do módulo 2.
