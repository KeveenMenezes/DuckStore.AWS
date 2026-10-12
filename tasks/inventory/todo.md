# Tarefas: `inventory-warehouses`

> Plano: [`plan.md`](./plan.md) · Spec: [`SPEC-inventory-warehouses.md`](../../SPEC-inventory-warehouses.md)
> Usuários de teste: `~/.config/duckstore/e2e-inventory.env` (StockKeeper, Seller, Admin) e `~/.config/duckstore/e2e.env` (cliente).

## Fase 1: Fundação

- [ ] **T0: Branch e specs no controle de versão** (XS)
  - Aceite: `feat/inventory-warehouses` criado a partir de `origin/development` atualizado. Commit com `SPEC-inventory.md`, `SPEC-inventory-warehouses.md` e `tasks/inventory/`. As mudanças locais alheias (`next.config.mjs`, `sst-env.d.ts`, `docs/social/`) ficam fora do commit.
  - Verificar: `git log origin/development..HEAD` mostra só o commit de docs. `git status` sem os specs pendentes.
  - Arquivos: `SPEC-inventory*.md`, `tasks/inventory/*`

- [ ] **T1: ADR-0052, bounded context Inventory** (XS)
  - Aceite: ADR no padrão ADR-0000 (skill `adr`), status **Proposed**. Decide o dono do estoque por polo, o modelo do polo (id interno, código imutável e único por guarda, prioridade, inativação em vez de delete), o grupo `StockKeeper` e a matriz de leitura e escrita. Registra a direção dos módulos 2 a 4 (ledger, handoff, reserva nas sagas ADR-0050/0051). Indexada em `docs/adr/README.md`.
  - Verificar: `adr-guardian` sobre a ADR e o spec, com os achados aplicados.
  - Arquivos: `docs/adr/0052-inventory-bounded-context-warehouses.md`, `docs/adr/README.md`

- [ ] **T2: Tabela local: seeder + AppHost** (M)
  - Aceite: `Inventory.DevelopmentDataSeeder` (Worker, no molde do `Challenges.DevelopmentDataSeeder`) cria `inventory-warehouses` (PK `WarehouseId`) e semeia `ALPHA` (prioridade 1) e `BETA` (prioridade 2) com as guardas `CODE#`. É idempotente: o segundo `dotnet run` não duplica nada. `InventoryExtensions.AddInventoryServices(dynamoDb)` está registrado no `Program.cs`, o projeto entra no `DuckStore.slnx`, e o `AppHost.csproj` o referencia.
  - Verificar: `dotnet build DuckStore.slnx`. Com o Aspire de pé, `aws dynamodb scan --table-name inventory-warehouses --endpoint-url http://localhost:8000` mostra 4 itens (2 polos + 2 guardas), e a contagem continua 4 depois de reiniciar.
  - Arquivos: `src/Services/Inventory/Inventory.DevelopmentDataSeeder/{Program,Worker,DynamoTableInitializer,WarehousesSchema,InventoryInitialData}.cs` + `.csproj`, `src/AppHost/{InventoryExtensions.cs,Program.cs,AppHost.csproj}`, `DuckStore.slnx`

- [ ] **T3: Infra: `InventoryStack`, grupo `StockKeeper`, workflow** (M)
  - Aceite: `inventory-dynamodb.ts` com as mesmas configurações das demais tabelas (billing, removal policy, PITR) e `inventory-stack.ts` (`DuckStoreInventoryStack`) registrado em `app.ts`. `CfnUserPoolGroup` `StockKeeper` no pool de gestão. `deploy-inventory-cdk.yml` só com o `cdk deploy` (sem publicar .NET), com gatilho em `infra/**` e `src/Services/Inventory/**`.
  - Verificar: `cd infra && npx cdk synth DuckStoreInventoryStack DuckStoreAppSyncStack` limpo. O template do Inventory tem uma tabela e nada mais, e o do AppSync tem o grupo novo.
  - Arquivos: `infra/constructs/inventory-dynamodb.ts`, `infra/stacks/inventory-stack.ts`, `infra/bin/app.ts`, `infra/constructs/appsync-auth.ts`, `.github/workflows/deploy-inventory-cdk.yml`

- [ ] **T4: Contrato GraphQL + validação compartilhada** (S)
  - Aceite: `Warehouse`, `WarehouseAddress`, os três inputs, `warehouses`/`warehouse` e as três mutations no `schema.graphql`, como no spec. `lib/warehouse-validation.ts` normaliza (trim, maiúsculas, CEP sem hífen) e valida, devolvendo `{ field, message }`. Os testes cobrem cada regra e as bordas listadas no spec.
  - Verificar: `cd src/WebApps/Shopping.Web.SPA.React && pnpm test && pnpm typecheck`.
  - Arquivos: `graphql/schema.graphql`, `src/WebApps/Shopping.Web.SPA.React/lib/warehouse-validation{.ts,.test.ts}`

### Checkpoint A
- [ ] `dotnet build DuckStore.slnx`, `pnpm test`, `pnpm typecheck` e `cdk synth` (Inventory, AppSync) verdes.
- [ ] Tabela local semeada e idempotente.

## Fase 2: Fatias

- [ ] **T5: Criar, listar e consultar polos** (M)
  - Aceite: `Mutation.createWarehouse.js` (gate de grupo na primeira linha, validação, `TransactWriteItems` polo + guarda, `WarehouseCodeTaken`), `Query.warehouses.js` (`Scan` com filtro de `Type`, ordenado por prioridade e código) e `Query.warehouse.js` (`null` para guarda ou id inexistente). Data source por `Table.fromTableName('inventory-warehouses')` e os três resolvers no `appsync-api.ts`. O `local.ts` imita os três, com a validação de `lib/`.
  - Verificar: `aws appsync evaluate-code` com os contextos Seller, Customer e sem grupo (`Unauthorized`), um `BadRequest` por campo, o cancelamento pela guarda e a ordenação. `cdk synth DuckStoreAppSyncStack`. No local, `curl` no `/api/graphql` do BFF: criar `GAMMA`, repetir `gamma` (`WarehouseCodeTaken`), CEP inválido (`BadRequest`, `field = address.zipCode`) e listar (ALPHA, BETA, GAMMA).
  - Arquivos: `graphql/resolvers/inventory/mutations/Mutation.createWarehouse.js`, `graphql/resolvers/inventory/queries/Query.{warehouses,warehouse}.js`, `infra/constructs/appsync-api.ts`, `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`
  - Depende de: T2, T3, T4

- [ ] **T6: Editar e ativar/inativar** (S)
  - Aceite: `Mutation.updateWarehouse.js` e `Mutation.setWarehouseActive.js` (`UpdateItem` `ALL_NEW`, condição `attribute_exists AND Type = Warehouse`, `NotFound`, `UpdatedAt`/`UpdatedBy`). O código não é alterável. Wiring no `appsync-api.ts` e imitação no `local.ts`.
  - Verificar: `evaluate-code` (`Unauthorized`, `BadRequest`, `ConditionalCheckFailed` → `NotFound`). No local, com `curl`, editar a prioridade do `BETA`, inativar e reativar, e ver `NotFound` para id inexistente e para `CODE#ALPHA`.
  - Arquivos: `graphql/resolvers/inventory/mutations/Mutation.{updateWarehouse,setWarehouseActive}.js`, `infra/constructs/appsync-api.ts`, `local.ts`
  - Depende de: T5

- [ ] **T7: Blazor: grupos, menu, serviço e lista** (M)
  - Aceite: `CognitoGroups.StockKeeper` + `CanManageStock`. Item Warehouses no `MainLayout` só com `CanManageStock`. `WarehouseAdminService` (`warehouses`, `warehouse`, as três mutations e os erros tipados), registrado no `Program.cs`. `WarehouseList.razor` com código, nome, cidade/UF, prioridade, status e "sem acesso" sem o grupo.
  - Verificar: `dotnet build src/WebApps/Managment.Web.Blazor`. No local, o Blazor (principal de dev Admin) lista ALPHA, BETA e GAMMA pelo BFF local, conferido pela requisição GraphQL no log do BFF.
  - Arquivos: `Auth/CognitoGroups.cs`, `Layout/MainLayout.razor`, `Services/{WarehouseAdminService.cs,Models.cs}`, `Program.cs`, `Pages/Warehouses/WarehouseList.razor`
  - Depende de: T5

- [ ] **T8: Blazor: formulário, criar, editar e ativar/inativar** (M)
  - Aceite: `WarehouseForm.razor` compartilhado (DataAnnotations espelhando a tabela de validação, com comentário cruzado, e o código só leitura na edição), `WarehouseCreate.razor`, `WarehouseEdit.razor` e o botão Ativar/Inativar na lista. O erro do servidor aparece no campo apontado por `errorInfo.field`, e `WarehouseCodeTaken` aparece no código.
  - Verificar: `dotnet build`. Os seletores usados pela T10 são estáveis (`id` nos inputs, como no `ProductForm`).
  - Arquivos: `Pages/Warehouses/{WarehouseForm,WarehouseCreate,WarehouseEdit,WarehouseList}.razor`, `Services/Models.cs`
  - Depende de: T6, T7

### Checkpoint B
- [ ] `evaluate-code` verde nos cinco resolvers, em todos os cenários das T5 e T6.
- [ ] Critérios 2 a 7 e 9 do spec verificados contra o BFF local, pela API.
- [ ] `dotnet build DuckStore.slnx`, `pnpm test`, `pnpm typecheck` e `pnpm lint` (sem erro novo) verdes.

## Fase 3: E2E, deploy e docs

- [ ] **T9: `ManagementSession` e configurações de gestão** (M)
  - Aceite: `ManagementSettings` lê `E2E_MANAGEMENT_URL` e os pares `E2E_{STOCKKEEPER,SELLER,ADMIN}_{EMAIL,PASSWORD}`. Sem eles, `[E2EFact]` pula. `ManagementSession` faz o login no Blazor pelo managed login do pool de gestão (um contexto de browser por papel), com o trace iniciado só depois do login (sem senha no trace, como o `StorefrontSession`), e expõe o access token (do `sessionStorage` do OIDC) para chamar o AppSync. O `README` do E2E ganha o contexto Inventory e as variáveis.
  - Verificar: `dotnet build tests/E2E`. `dotnet test` sem as variáveis pula tudo, sem baixar o browser.
  - Arquivos: `tests/E2E/E2E.Infrastructure/{ManagementSettings.cs,ManagementSession.cs,E2EFactAttribute.cs}`, `tests/E2E/README.md`
  - Depende de: T0

- [ ] **T10: Suíte `Inventory.E2ETests`** (M)
  - Aceite:
    - **UI:** menu Warehouses visível para StockKeeper e Admin e ausente para o Seller. Criar `E2E-<sufixo>`, editar a prioridade, inativar, reativar e o erro de campo (CEP).
    - **API:** `WarehouseCodeTaken`, `BadRequest` com `field`, `NotFound`, `Unauthorized` do Seller nas cinco operações e do cliente (`StorefrontSession`) pela `/api/graphql`.
    - **Limpeza:** cada teste inativa o que criou.
  - Verificar: compila e pula sem as variáveis. A execução real fica na T11.
  - Arquivos: `tests/E2E/Inventory/Inventory.E2ETests/{.csproj,AssemblyInfo.cs,WarehousesUiTests.cs,WarehousesApiTests.cs,ManagementApp.cs}`, `DuckStore.slnx`
  - Depende de: T8, T9

- [ ] **T11: Deploy no dev, grupo do StockKeeper e E2E verde** (M), *com as aprovações do plano*
  - Aceite: `cdk diff` mostrado e conferido, e então o deploy, nesta ordem: Inventory → AppSync → Management. `e2e-inventory-stockkeeper@duckstore.test` no grupo `StockKeeper` e o comentário "PENDING" atualizado no `e2e-inventory.env`. `Inventory.E2ETests` verde: critérios 1 a 8. A suíte do checkout continua verde, como regressão do AppSync.
  - Verificar: `source ~/.config/duckstore/e2e.env && source ~/.config/duckstore/e2e-inventory.env && dotnet test tests/E2E/Inventory/Inventory.E2ETests` e `dotnet test tests/E2E/Checkout/Checkout.E2ETests`.
  - Depende de: T10

- [ ] **T12: Documentação** (S)
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
