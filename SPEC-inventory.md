# Capability Map: Controle de Estoque por Polo (Inventory v1)

> Status: **Mapa APROVADO** (revisão 2, 2026-10-12: a reserva passa para as sagas do checkout, ADR-0050). Cada spec de módulo passa pela própria aprovação:
>
> | Ordem | Módulo | Spec |
> |---|---|---|
> | 1 | `inventory-warehouses` | [`SPEC-inventory-warehouses.md`](./SPEC-inventory-warehouses.md) (aprovado; plano em [`tasks/inventory/`](./tasks/inventory/plan.md)) |
> | 2 | `inventory-movements` | `SPEC-inventory-movements.md` (a escrever) |
> | 3 | `catalog-stock-handoff` | `SPEC-catalog-stock-handoff.md` (a escrever) |
> | 4 | `checkout-stock-reservation` | `SPEC-checkout-stock-reservation.md` (a escrever, depois do `order-saga`) |

## 1. Objetivo

Saber **quantas unidades de cada produto existem e em qual polo**, registrar toda entrada e saída e tirar do Seller a tarefa de informar o estoque. Ela passa para um novo grupo, o **StockKeeper** (o responsável pelo estoque), no app de gestão (Blazor, `management-duckstore.dev.keveenmenezes.com`).

Exemplo-alvo: o produto A tem 20 unidades no polo Alpha e 50 no Beta. A loja mostra 70 disponíveis. Uma venda de 30 sai do polo de maior prioridade e, se ele não tiver o bastante, o restante sai do próximo.

Usuários: o **StockKeeper** e o **Admin** operam o estoque no Blazor. O **Seller** só vê o total. O **cliente** da loja React vê o disponível e não consegue comprar além dele.

### Regras de negócio v1

| Regra | Valor |
|---|---|
| Localização | Polo (armazém) com código, nome, endereço completo (rua, número, bairro, cidade, UF, CEP), **prioridade** (inteiro, menor atende primeiro) e flag ativo/inativo. Sem posição interna (corredor/prateleira) |
| Granularidade do saldo | produto × polo, com `Disponível` e `Reservado` separados |
| Movimentos | **Entrada** (recebimento), **Ajuste** (+/−, motivo obrigatório: contagem, perda, avaria), **Transferência** (polo A → polo B, atômica), **Venda** (reserva → baixa → liberação) |
| Saldo negativo | proibido. Toda saída é condicional (`Disponível >= qtd`) |
| Histórico | uma linha por movimento: data, autor (Cognito `sub`/e-mail ou "sistema" na venda), tipo, polo(s), quantidade, motivo, `OrderId` na venda |
| Total na loja | soma do `Disponível` dos polos **ativos** |
| Alocação da venda | por **prioridade** do polo, dividindo entre polos quando necessário. Empate desfeito pelo código do polo. A prioridade é o ponto de extensão para depois trocar por "polo mais perto do cliente" |
| Venda sem saldo | o **checkout recusa na hora** (`OutOfStock`, com os itens em falta) e nenhum pedido é criado |
| Ciclo da venda | a Express `checkout-saga` **reserva** (Disponível → Reservado). A Standard `order-saga` faz a **baixa** quando o pagamento é autorizado (Reservado → sai) ou a **liberação** quando o pedido é cancelado (Reservado → Disponível) |
| Estoque atual (`Product.stock`) | **zerado**. Não há migração, e o StockKeeper lança as entradas reais |

### Decisões já tomadas

- Os quatro tipos de movimento entram na v1.
- A localização é o polo com endereço, sem coordenadas e sem posição interna.
- Permissões: `StockKeeper` e `Admin` escrevem. O `Seller` só lê o total.
- Migração: zerar e recontar.
- Venda: recusa síncrona no checkout, alocação por prioridade e o ciclo reserva → baixa → libera.
- **(revisão 2)** O checkout é a saga Express `checkout-saga` (ADR-0050), não mais o pipeline AppSync. A reserva de estoque é um **estado dessa saga**, com compensação, como a ADR-0050 exige ("New pre-order validations (stock, freight quote) MUST be added as states of `checkout-saga`"). A baixa e a liberação são **passos da `order-saga`** ([`SPEC-order-saga.md`](./SPEC-order-saga.md)), não consumers de eventos. Assim não nasce coreografia nova que a iniciativa do checkout teria de remover.

## 2. Avaliação de arquitetura (estado atual → lacuna)

| Capacidade | Hoje | Lacuna |
|---|---|---|
| Dono do saldo | `Product.Stock` (int) no Catalog, digitado pelo Seller em `ProductForm.razor`. Vem em `CreateProductInput`/`UpdateProductInput`/`CreateProductWithPriceInput` | Novo bounded context **Inventory** passa a ser o dono. O Catalog perde `stock` |
| Polos | não existem | Cadastro de polos |
| Movimentos | não existem. Nada baixa estoque: checkout, pedido e pagamento ignoram `stock` | Ledger de movimentos + saldo por polo |
| Loja | `Product.stock` vem do CatalogView (`SearchDocument.Stock`, alimentado por `ProductSynced`). A SPA o usa no botão de adicionar ao carrinho | O CatalogView passa a receber o total do Inventory por CDC |
| Checkout | `checkout-saga` (Express, ADR-0050): `DeriveOrderId → LoadCheckoutRecord → LoadCart → CatalogLines → Quote → RegisterPayment → Checkout (→ VoidPayment)`. O `OrderId` já existe desde o primeiro estado. A fase 2 da iniciativa do checkout acrescenta `ReservePoints`/`ReleasePoints`, e a fase 3 a `order-saga` Standard com reconciliadores | Estado `ReserveStock` + compensação `ReleaseStock` na Express. Passos `CommitStock`/`ReleaseStock` na Standard. Reserva órfã no reconciliador |
| Grupos Cognito | `Admin`, `Seller` no pool de gestão (`infra/constructs/appsync-auth.ts`) | Novo grupo `StockKeeper` |
| Blazor | Products, Discounts, Challenges | Páginas Warehouses, Estoque por produto, Movimentos. O campo Stock sai do `ProductForm` |

### ADRs afetadas

- **Nova ADR (0052):** bounded context Inventory. Dono do saldo por polo, ledger de movimentos, alocação por prioridade e ciclo reserva/baixa/liberação. A 0050 é a saga do checkout e a 0051 está reservada para a orquestração do pedido (`SPEC-checkout.md` §5). Ela é escrita no módulo 1, que cria o contexto, e emendada nos seguintes.
- **ADR-0050 (emenda, módulo 4):** estados `ReserveStock`/`ReleaseStock` na `checkout-saga`, o `errorType` `OutOfStock` no contrato do §8 e o orçamento de timeout recalculado.
- **ADR-0051 (emenda, módulo 4):** passos `CommitStock`/`ReleaseStock` na `order-saga` e a reserva órfã no reconciliador.
- **ADR-0040 (módulo 3):** o CatalogView ganha um terceiro produtor, com dispatcher e estratégia próprios (`Consumers/InventorySync/`, `IInventorySyncStrategy`).
- **ADR-0009:** cadastros e consultas são resolvers diretos. Entrada, ajuste e transferência são classificadas no spec do módulo 2. A reserva não é resolver: é uma Lambda invocada só pela saga (ADR-0050 §2).

## 3. Mapa de módulos

| Module id | Responsabilidade | Serviços | Depende de |
|---|---|---|---|
| `inventory-warehouses` | Bounded context Inventory (ADR-0052, `InventoryStack` CDK, seeder local). Grupo Cognito `StockKeeper`. Tabela de polos + `createWarehouse`/`updateWarehouse`/`setWarehouseActive`/`warehouses`/`warehouse` (resolvers diretos, sem delete). Página **Warehouses** no Blazor, visível para StockKeeper e Admin | Inventory (infra + seeder), AppSync, Cognito, Blazor, SPA `local.ts` | — |
| `inventory-movements` | Projeto `Inventory.Function` (ADR-0019, AOT). Saldo produto × polo (`Disponível`/`Reservado`) e ledger. Mutations de **Entrada**, **Ajuste** e **Transferência**, cada uma gravando o saldo e a linha do ledger numa transação. Consultas `stockByProduct` (por polo) e `stockMovements` (histórico). Blazor: tela de estoque do produto + formulário de movimento + histórico | Inventory, AppSync, Blazor, `local.ts` | `inventory-warehouses` |
| `catalog-stock-handoff` | O Inventory publica o total disponível por CDC (stream do saldo → `StockLevelChangedEvent`). O CatalogView consome (`InventorySync`) e é a fonte do `Product.stock` da loja. O Catalog remove `stock` do domínio, dos inputs GraphQL, do `ProductSynced` e da saga `createProductWithPrice`. Blazor: o campo Stock sai do formulário, e a lista de produtos mostra o total em leitura. O stock atual fica zerado | Inventory, Catalog, CatalogView, AppSync, Blazor, SPA (só tipos) | `inventory-movements` |
| `checkout-stock-reservation` | Lambdas `inventory-reserve-stock`, `inventory-release-stock` e `inventory-commit-stock`, só invocadas pelas sagas e idempotentes pelo `OrderId`. Na Express: `ReserveStock` (aloca por prioridade, divide entre polos e reserva de forma atômica, ou recusa `OutOfStock`) e `ReleaseStock` como compensação de toda recusa tipada posterior. Na Standard: `CommitStock` depois de `CompleteOrder` e `ReleaseStock` depois de `CancelOrder`. Reserva órfã no reconciliador. `local.ts` e SPA tratam `OutOfStock` | Inventory, AppSync (sagas), SPA | `inventory-movements`, `catalog-stock-handoff`, e da iniciativa do checkout: `checkout-points-reservation`, `order-saga` |

Ordem de build: `inventory-warehouses` → `inventory-movements` → `catalog-stock-handoff` → `checkout-stock-reservation`.

```
inventory-warehouses ──> inventory-movements ──> catalog-stock-handoff ──┐
                                                                          ├──> checkout-stock-reservation
SPEC-checkout.md:  checkout-points-reservation ──> order-saga ────────────┘
```

- Os módulos 1 a 3 não tocam no checkout, então podem andar **em paralelo** com as fases 2 e 3 da iniciativa do checkout.
- O módulo 4 espera a `order-saga`. Antes dela, a baixa e a liberação teriam de ser consumers de `PaymentAuthorizedEvent`/`OrderCancelledEvent`, justamente a coreografia que a fase 3 do checkout remove. Ele também espera o `checkout-points-reservation`, que estabelece a compensação encadeada e o rebalanceamento do orçamento de timeout que o `ReserveStock` vai reaproveitar.
- O `catalog-stock-handoff` vem antes da reserva para a vitrine e o checkout lerem o mesmo dono. Sem isso, a vitrine diria "70 disponíveis" com o número antigo, e o checkout recusaria por falta de saldo.

### Cobertura dos critérios de aceite

| Critério do negócio | Módulo |
|---|---|
| Cadastro os polos Alpha e Beta com endereço e prioridade | `inventory-warehouses` |
| Um usuário do grupo StockKeeper entra no Blazor e vê as telas de estoque. Um Seller não consegue lançar movimentos | `inventory-warehouses` (grupo + gate) e `inventory-movements` (gate nas mutations) |
| Lanço a entrada de 20 un. do produto A no Alpha e de 50 no Beta, e vejo o saldo por polo | `inventory-movements` |
| Ajusto −2 por avaria com motivo, e transfiro 10 do Beta para o Alpha | `inventory-movements` |
| Vejo o histórico de todos os movimentos do produto | `inventory-movements` |
| A loja mostra 70 disponíveis para o produto A | `catalog-stock-handoff` |
| O Seller não informa mais o estoque ao criar ou editar um produto | `catalog-stock-handoff` |
| Compro 30: sai do polo de maior prioridade e completa com o próximo se preciso | `checkout-stock-reservation` |
| Tento comprar 80: o checkout recusa e nenhum pedido é criado | `checkout-stock-reservation` |
| Pagamento autorizado: o reservado vira baixa. Pedido cancelado: o reservado volta ao disponível | `checkout-stock-reservation` |

## 4. Esboço do modelo (para orientar os specs de módulo)

Proposta, aberta a ajuste nos specs de módulo:

| Tabela | Chave | Uso |
|---|---|---|
| `inventory-warehouses` | PK `WarehouseId` | polos + item-guarda `CODE#<code>` (unicidade do código). Desenho no spec do módulo 1 |
| `inventory-stock` | PK `ProductId`, SK `WAREHOUSE#<id>` | `Available`, `Reserved`. Streams ligados (fonte do `StockLevelChangedEvent`) |
| `inventory-movements` | PK `ProductId`, SK `MovementId` determinístico | ledger. LSI por `CreatedAt` para o histórico. Na venda, `SALE#<orderId>#<warehouseId>` torna reserva, baixa e liberação idempotentes |

- **O id da venda é o `OrderId` da saga.** O `DeriveOrderId` (ADR-0050 §3) o produz antes de qualquer escrita, e duas execuções da mesma tentativa derivam o mesmo id. É o mesmo arranjo do `REDEMPTION#<orderId>` dos pontos.
- **Prioridade → proximidade:** a alocação vive num domain service (`StockAllocationPolicy`). Trocar "prioridade" por "mais perto do CEP" depois é trocar a política, sem mudar o modelo de saldo nem a saga.
- **Reserva órfã:** uma execução abortada depois do `ReserveStock`, ou uma falha ambígua (que pela ADR-0050 §1 nunca compensa), deixa uma reserva sem pedido. Ela é liberada pelo reconciliador da `order-saga` (`SPEC-order-saga.md` §2.1, a cada 15 min: reserva com mais de 15 min e sem `order-saga` com `name = orderId` → liberada), como as reservas de pontos.
- **Orçamento de timeout (risco):** a fase 2 do checkout já aponta que `ReservePoints` + `ReleasePoints` estouram os 25 s da Express (T18). Somar `ReserveStock` + `ReleaseStock` piora o quadro. Uma opção a avaliar no spec do módulo 4 é rodar as duas reservas num **estado `Parallel`**, que custa o caminho do mais lento e não a soma, e compensar a que deu certo quando a outra recusa.

## 5. Premissas que estou assumindo (corrija agora ou sigo com elas)

1. **Bounded context próprio, Inventory**, em vez de estender o Catalog. O estoque tem ciclo de vida, permissões e eventos próprios. O projeto `.NET Inventory.Function` só nasce no módulo 2, quando aparece a primeira Lambda. O módulo 1 é todo resolver direto.
2. Polo **inativo** não recebe entradas nem transferências e não atende vendas novas, e o saldo dele sai do total da loja. Ele **pode ser inativado a qualquer momento**: reservas que já existem nele são baixadas ou liberadas normalmente.
3. O polo não é deletado: só inativado, para preservar o histórico. O **código é imutável** depois de criado.
4. A **quantidade é inteira e positiva** em todo movimento. O sinal vem do tipo (o ajuste tem direção explícita).
5. A transferência é **instantânea** (sai de A e entra em B na mesma transação), sem estado "em trânsito".
6. O cliente na loja vê **só o total**, nunca o polo. O polo que atendeu aparece só para o StockKeeper, no histórico.
7. O **Admin herda** tudo o que o StockKeeper faz. Ninguém do grupo `Seller` lança movimentos, e o resolver aplica isso.
8. O StockKeeper é cadastrado como os demais usuários de gestão (AdminCreateUser + grupo), sem self-signup.
9. **Zerar e recontar** vale para dev: depois do deploy do módulo 3, todos os produtos aparecem esgotados até as entradas serem lançadas. Depois do módulo 4, ninguém compra até lá. Não há dados reais de produção a preservar.
10. A baixa acontece no passo da `order-saga` que segue a autorização do pagamento, e a liberação no que segue o cancelamento. Eventos de fato (`OrderCompleted`/`OrderCancelled`) continuam publicados, mas o Inventory não depende deles.
11. Pedido cancelado **depois de pago** não devolve estoque automaticamente na v1. Esse fluxo não existe no Ordering (`SPEC-checkout.md` §6), então a devolução física é uma Entrada manual.
12. O carrinho (`storeBasket`) não reserva nada: o saldo só é tocado no checkout.

## 6. Perguntas abertas

1. **Alerta de estoque baixo** (um limite por produto que notifica o StockKeeper): fica fora da v1? Assumo que **fica fora**.

## 7. Próximo passo

Aprovar o plano do módulo 1 ([`tasks/inventory/plan.md`](./tasks/inventory/plan.md)) e implementá-lo. Depois dele, o spec do módulo 2.
