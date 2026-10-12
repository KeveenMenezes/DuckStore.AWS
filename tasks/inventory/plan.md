# Plano de implementação: `inventory-warehouses` (módulo 1 do Inventory)

> Spec: [`SPEC-inventory-warehouses.md`](../../SPEC-inventory-warehouses.md) (aprovado, revisão 1) · Mapa: [`SPEC-inventory.md`](../../SPEC-inventory.md) · Tarefas: [`todo.md`](./todo.md)
> Status: **AGUARDANDO APROVAÇÃO.**
>
> `tasks/plan.md` e `tasks/todo.md` continuam sendo da iniciativa do checkout (fases 2 e 3 em aberto). O Inventory fica em `tasks/inventory/`, e nada do checkout é tocado.

## Visão geral

Criar o bounded context Inventory com o cadastro de polos: a tabela `inventory-warehouses`, cinco resolvers diretos com o gate `StockKeeper`/`Admin`, o grupo Cognito `StockKeeper`, as páginas Warehouses no Blazor, a emulação no `local.ts`, o seeder local e a suíte Playwright `Inventory.E2ETests`, que valida tudo no AWS dev com os usuários de teste já criados.

## Decisões de arquitetura

- **Sem Lambda e sem `Inventory.Function`.** Os cinco campos são resolvers diretos (ADR-0009, nenhum critério de escalação). O projeto .NET só nasce no módulo 2. Até lá, o schema da tabela fica numa classe do seeder.
- **Unicidade por item-guarda.** `CODE#<code>` na mesma `TransactWriteItems` que grava o polo. Não há leitura antes da escrita e, por isso, não há corrida.
- **Validação em três cópias com comentário cruzado:** o resolver é a autoridade, o `local.ts` usa `lib/warehouse-validation.ts` (com testes) e o Blazor tem as DataAnnotations. Os mesmos casos rodam contra o resolver real com `aws appsync evaluate-code`, o que pega divergência entre as cópias. É o arranjo do `MAX_POINTS`.
- **Fatias verticais:** "criar e listar" ponta a ponta (resolver + `local.ts` + Blazor) antes de "editar e ativar/inativar". A fundação (tabela local, infra, contrato GraphQL) vem antes.
- **E2E sobre o deploy real.** A UI é validada pela suíte Playwright no dev, não por ferramenta de navegador do agente. Localmente valido pela API (GraphQL do BFF local com `curl`) e pela compilação do Blazor.
- **Branch próprio** `feat/inventory-warehouses`, a partir de `origin/development` (que já tem a saga do checkout, #179). Ele não depende do `feat/checkout-sagas`.

### Trilhas e subagentes

Tudo roda **inline**. O módulo é pequeno (cerca de 30 arquivos, a maioria curtos), e as trilhas que poderiam ir em paralelo (Blazor e E2E) dependem uma da outra pelos seletores da UI. Um subagente gastaria mais para re-derivar o contexto do que economizaria. Em segundo plano, só revisores somente leitura: o `adr-guardian` na T1 e, no checkpoint final, o `adr-guardian` + `agent-skills:code-reviewer`.

## Lista de tarefas

O detalhe (aceite, verificação, arquivos) está em [`todo.md`](./todo.md).

### Fase 1: Fundação
- T0: Branch e specs no controle de versão (XS)
- T1: ADR-0052 (Proposed) (XS)
- T2: Tabela local: seeder + AppHost (M)
- T3: Infra: `InventoryStack`, grupo `StockKeeper`, workflow (M)
- T4: Contrato GraphQL + validação compartilhada (S)

**Checkpoint A:** build, `pnpm test`, `cdk synth` dos três stacks, tabela local semeada.

### Fase 2: Fatias
- T5: Criar, listar e consultar polos (resolvers + data source + `local.ts`) (M)
- T6: Editar e ativar/inativar (resolvers + `local.ts`) (S)
- T7: Blazor: grupos, menu, serviço e lista (M)
- T8: Blazor: formulário, criar, editar e ativar/inativar (M)

**Checkpoint B:** `evaluate-code` em todos os cenários. Critérios 2 a 7 e 9 contra o BFF local. Blazor compilando.

### Fase 3: E2E, deploy e docs
- T9: `ManagementSession` + configurações de gestão no `E2E.Infrastructure` (M)
- T10: Suíte `Inventory.E2ETests` (M)
- T11: Deploy no dev + grupo do StockKeeper + E2E verde (M)
- T12: Documentação (S)

**Checkpoint C:** revisores, critérios 1 a 9, ADR → Accepted, PR.

## Aprovações (pedidas juntas, antes da T11)

| Ação | Alvo | O que muda | Reversível? |
|---|---|---|---|
| `cdk deploy DuckStoreInventoryStack` | AWS dev (603767527989), local, como na fase 1 do checkout | **Cria** a tabela `inventory-warehouses` (vazia) | Sim: apagar o stack. A tabela segue a removal policy das demais |
| `cdk deploy DuckStoreAppSyncStack` | AWS dev | **Cria** o grupo `StockKeeper` no pool de gestão, um data source e cinco resolvers. **Altera** o schema (só acréscimos). Nada é removido | Sim: redeploy do commit anterior |
| `cdk deploy DuckStoreManagementStack -c deployManagement=true` | AWS dev | Publica o Blazor com as páginas novas, repetindo localmente os passos do `deploy-management-cdk.yml` (`appsettings.json` gerado a partir dos outputs do AppSync) | Sim: redeploy |
| `admin-add-user-to-group` | pool de gestão do dev | `e2e-inventory-stockkeeper@duckstore.test` → `StockKeeper` | Sim: `admin-remove-user-from-group` |
| Rodar `Inventory.E2ETests` | AWS dev | Cria polos `E2E-<sufixo>` e os inativa no fim. Sem delete, eles ficam como histórico | Não (não há delete de polo, por desenho) |
| Push de `feat/inventory-warehouses` + PR para `development` | GitHub | Título e resumo mostrados antes | — |

O `cdk diff` de cada stack é mostrado antes do deploy. Se ele trouxer algo além do listado acima, eu paro e pergunto.

## Riscos e mitigações

| Risco | Impacto | Mitigação |
|---|---|---|
| Formato do erro de `TransactWriteItems` cancelada no APPSYNC_JS (onde ficam os `cancellationReasons`) | Médio: `WarehouseCodeTaken` viraria um erro genérico | `evaluate-code` do `response` com o contexto de cancelamento documentado, e o E2E confirma no serviço real (T10) |
| O access token do Blazor para os testes de API: o OIDC do WASM o guarda no `sessionStorage` (`oidc.user:<authority>:<clientId>`) | Médio: sem token, os testes de erro e de `Unauthorized` não chamam o AppSync direto | Ler pelo `page.EvaluateAsync`. Se o formato não servir, cobrir os mesmos casos pela UI (erro no formulário) e pelo `evaluate-code`, e registrar a troca na `todo.md` |
| O deploy do AppSync referencia uma tabela que ainda não existe | Baixo | Ordem fixa: Inventory → AppSync → Management. No merge, as duas workflows disparam juntas, mas a tabela já existe no dev |
| Deploy local do Management: a workflow monta o `appsettings.json` | Baixo | Repetir os mesmos passos da workflow e conferir o arquivo gerado antes do `cdk deploy` (não há segredo nele) |
| O `local.ts` cresce (já tem ~1.400 linhas) | Baixo | Os resolvers de polo ficam num bloco próprio e importam a validação de `lib/` |
| Os polos `E2E-*` se acumulam no dev, sem delete | Baixo | Sufixo aleatório e inativação no fim, e a lista do Blazor mostra o status. Se incomodar, o filtro de inativos entra no módulo 2 |

## Perguntas abertas

Nenhuma.
