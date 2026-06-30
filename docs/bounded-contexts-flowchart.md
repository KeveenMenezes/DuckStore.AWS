# Fluxograma de Processo de Negócio — DuckStore Bounded Contexts

Cada seção descreve as ações de um Bounded Context, incluindo o papel da ação no fluxo de negócio e como ela se integra aos demais contextos via eventos (EventBridge / DynamoDB Streams CDC).

---

## 1. Catalog (Catálogo de Produtos)

Responsável por manter o catálogo de patos de borracha disponíveis para venda. É a fonte de verdade de produtos e categorias, e é o contexto que mantém a média de avaliações de cada produto atualizada.

### CreateProduct
**Gatilho:** Administrador envia dados de um novo produto via GraphQL (`createProduct`).  
**Fluxo:**
1. O resolver do AppSync/GraphQL executa um `PutItem` direto no DynamoDB (tabela `products`).
2. A escrita dispara o DynamoDB Stream da tabela `products`.
3. `CatalogStreamEventPublisher` captura o registro do stream e publica um `CatalogUpdatedEvent` no EventBridge.
4. `CatalogUpdatedConsumer` recebe o evento e chama o webhook da SPA Next.js para invalidar o cache ISR da listagem de produtos.

**Resultado de negócio:** Novo produto fica disponível para os clientes imediatamente após a revalidação do cache.

---

### UpdateProduct
**Gatilho:** Administrador edita um produto existente via GraphQL (`updateProduct`).  
**Fluxo:**
1. Validação: verifica que o produto existe (condition `attribute_exists`); lança `ProductNotFoundException` caso contrário.
2. O resolver executa um `UpdateItem` no DynamoDB com os novos atributos.
3. O DynamoDB Stream é acionado → `CatalogStreamEventPublisher` → `CatalogUpdatedEvent` no EventBridge → `CatalogUpdatedConsumer` invalida o cache ISR.

**Resultado de negócio:** Alterações de preço, estoque ou descrição refletem na vitrine dentro do próximo ciclo de revalidação.

---

### DeleteProduct
**Gatilho:** Administrador remove um produto via GraphQL (`deleteProduct`).  
**Fluxo:**
1. O resolver executa um `DeleteItem` no DynamoDB.
2. O DynamoDB Stream é acionado → mesma cadeia de publicação/invalidação de cache descrita acima.

**Resultado de negócio:** Produto removido do catálogo e da vitrine.

---

### CatalogStreamEventPublisher *(CDC — ADR-0005)*
**Gatilho:** Qualquer escrita (INSERT/MODIFY/REMOVE) na tabela `products` via DynamoDB Streams.  
**Fluxo:**
1. Lambda lê os registros do stream.
2. Para cada registro, publica um `CatalogUpdatedEvent` (com `ChangeType = INSERT|MODIFY|REMOVE`) no EventBridge.

**Papel no negócio:** Desacopla a escrita no catálogo da invalidação de cache — nenhuma Lambda de negócio precisa saber que existe uma SPA com ISR.

---

### ReviewCreatedConsumer *(ADR-0011)*
**Gatilho:** Evento `ReviewCreatedEvent` publicado pelo contexto de Review no EventBridge.  
**Fluxo:**
1. Recebe o `ProductId` e o `Rating` do evento.
2. Executa um `TransactWriteItems` atômico: incrementa `RatingCount` e `RatingSum` no produto + registra o evento como processado (idempotência).
3. Relê `RatingSum / RatingCount` e persiste o campo calculado `AverageRating`.

**Resultado de negócio:** A média de avaliações do produto é mantida atualizada em tempo real. A transação garante que um evento entregue mais de uma vez não duplique a contagem.

---

### CatalogUpdatedConsumer
**Gatilho:** Evento `CatalogUpdatedEvent` no EventBridge (publicado por `CatalogStreamEventPublisher`).  
**Fluxo:**
1. Lambda chama o endpoint webhook da SPA Next.js (`POST /api/revalidate`).
2. A SPA executa `revalidateTag('products')`, invalidando o cache ISR da página de catálogo.

**Resultado de negócio:** Garante que a vitrine exiba o catálogo atualizado sem precisar de um TTL fixo — a revalidação acontece sob demanda ao detectar uma mudança real nos dados.

---

## 2. Basket (Carrinho de Compras)

Gerencia o carrinho de compras de cada usuário. Integra-se ao módulo de cupons (Discount, incorporado ao contexto após ADR-0012) para aplicar descontos em tempo real, e inicia o fluxo de pedido ao realizar o checkout.

### StoreBasket
**Gatilho:** Cliente adiciona/atualiza itens no carrinho (GraphQL `storeBasket` → Lambda).  
**Fluxo:**
1. Recebe o DTO do carrinho com todos os itens.
2. Para cada item, consulta a tabela `coupons` buscando um cupom pelo nome do produto (`DynamoCouponRepository`).
3. Aplica os descontos encontrados ao agregado `ShoppingCart` (regra de negócio in-process).
4. Persiste o carrinho no DynamoDB (tabela `shopping-carts`) via `CacheBasketRepository` → `BasketRepository` (cache-aside Redis).

**Resultado de negócio:** O carrinho é salvo com os preços já com desconto aplicado, garantindo que o cliente veja o preço final antes de confirmar.

---

### CheckoutBasket
**Gatilho:** Cliente confirma a compra na tela de checkout (GraphQL `checkoutBasket` → Lambda).  
**Fluxo:**
1. Lê o carrinho atual do usuário no DynamoDB.
2. Serializa os dados do checkout (endereço, pagamento, itens) como JSON no campo `CheckoutData` e define `Type = "Checkout"` via `MarkCheckoutAsync` — escrita atômica no DynamoDB.
3. Remove o carrinho (`DeleteBasket`).
4. O DynamoDB Stream detecta o MODIFY com `Type = Checkout` e aciona `ShoppingCartsEventPublisher`.

**Resultado de negócio:** O checkout não depende de uma chamada síncrona ao Ordering — a escrita no DynamoDB é o compromisso; a criação do pedido acontece de forma assíncrona e resiliente.

---

### ShoppingCartsEventPublisher *(CDC — ADR-0005)*
**Gatilho:** DynamoDB Stream da tabela `shopping-carts` ao detectar registro com `Type = "Checkout"`.  
**Fluxo:**
1. Lê `CheckoutData` (JSON) da imagem nova do stream.
2. Desserializa e publica `BasketCheckoutEvent` no EventBridge.
3. Exclui o item do carrinho do DynamoDB (cleanup pós-publicação).

**Papel no negócio:** Garante a entrega confiável do evento de checkout para o contexto de Ordering, mesmo que o serviço de Ordering esteja temporariamente indisponível no momento do checkout.

---

## 3. Ordering (Pedidos)

Recebe o evento de checkout, cria e armazena o pedido do cliente, e publica o evento de pedido criado para ser consumido pelo contexto de Notification.

### BasketCheckoutConsumer *(Consumer EventBridge)*
**Gatilho:** Evento `BasketCheckoutEvent` publicado pelo contexto de Basket no EventBridge.  
**Fluxo:**
1. Mapeia o evento para um `CreateOrderCommand` com endereço de entrega, pagamento e itens.
2. Constrói o agregado `Order` com seus value objects (`OrderId`, `CustomerId`, `Address`, `Payment`).
3. Executa um `TransactWriteItems`: persiste o pedido + registra o evento como processado (idempotência via `ordering-processed-events`).

**Resultado de negócio:** O pedido é criado exatamente uma vez, mesmo que o event bus entregue o evento de checkout mais de uma vez. A transação garante consistência total.

---

### GetOrdersByCustomer
**Gatilho:** Cliente acessa "Meus Pedidos" na SPA (GraphQL `ordersByCustomer` → Lambda).  
**Fluxo:**
1. Consulta o DynamoDB via GSI1 (`GSI1PK = customerId`), que lista os pedidos mais recentes do cliente.
2. O GSI usa `ProjectionType.ALL`, portanto não é necessário um `BatchGetItem` extra — os atributos completos já estão disponíveis.

**Resultado de negócio:** O cliente visualiza todo o histórico de pedidos de forma eficiente, sem varredura completa da tabela.

---

### DeleteOrder
**Gatilho:** Administrador ou cliente solicita cancelamento de pedido (GraphQL `deleteOrder` → Lambda).  
**Fluxo:**
1. Executa um `TransactWriteItems` que remove o item do pedido e todos os `ORDERITEM` associados em uma única operação atômica.

**Resultado de negócio:** Pedido removido completamente, sem deixar itens órfãos na tabela.

---

### OrderCreatedPublisher *(CDC — ADR-0005)*
**Gatilho:** DynamoDB Stream da tabela `ordering` ao detectar um INSERT de `Type = "Order"`.  
**Fluxo:**
1. Verifica se o feature flag `FeatureManagement:OrderFullfilment` está habilitado.
2. Lê o pedido completo via `GetByIdAsync`.
3. Mapeia para `OrderCreatedEvent` e publica no EventBridge.

**Papel no negócio:** Notifica downstream (ex.: Notification) sobre a criação do pedido. O feature flag permite ativar o fulfillment de pedidos de forma gradual sem redeploy.

---

## 4. Review (Avaliações)

Permite que clientes registrem avaliações (nota + comentário) de produtos comprados. Os dados de rating são propagados de volta ao Catalog via CDC para manter a média atualizada.

### CreateReview
**Gatilho:** Cliente submete uma avaliação na página de produto (GraphQL `createReview`).  
**Fluxo:**
1. O resolver executa um `PutItem` direto na tabela `reviews` (direct DynamoDB resolver — ADR-0009).
2. O DynamoDB Stream da tabela `reviews` é acionado.
3. `ReviewCreatedPublisherFunction` captura o INSERT e publica `ReviewCreatedEvent` no EventBridge.
4. O contexto de Catalog consome o evento e atualiza `AverageRating` / `RatingCount` do produto.

**Resultado de negócio:** Avaliação registrada e média do produto atualizada de forma assíncrona. A escrita direta sem Lambda torna a operação mais rápida e barata.

---

### ReviewCreatedPublisher *(CDC — ADR-0005/0011)*
**Gatilho:** DynamoDB Stream da tabela `reviews` ao detectar um INSERT.  
**Fluxo:**
1. Lê `ProductId` e `Rating` da imagem nova do stream.
2. Publica `ReviewCreatedEvent` no EventBridge.

**Papel no negócio:** Desacopla Review de Catalog — nenhum dos dois precisa chamar o outro diretamente. O bus garante entrega e o consumidor garante idempotência.

---

## 5. Notification (Notificações)

Serviço em Go que persiste notificações de eventos de negócio (atualmente: pedido criado / checkout realizado) em DynamoDB para rastreabilidade e futura entrega ao cliente.

### ProcessNotification
**Gatilho:** Mensagem na fila SQS `duckstore-notifications` (enviada a partir do `OrderCreatedEvent` ou `BasketCheckoutEvent` via EventBridge → SQS).  
**Fluxo:**
1. Serviço roda em modo poller SQS (Aspire/standalone) ou como Lambda (AWS).
2. Desserializa a mensagem no modelo `NotificationEvent` com dados de endereço, pagamento e status.
3. Persiste na tabela DynamoDB `duckstore-notifications` com idempotência (usando `notification-processed-events`).
4. Remove a mensagem da fila após processamento bem-sucedido.

**Resultado de negócio:** Histórico de notificações auditável, resistente a reentregas da fila. Base para futuros canais de notificação ao cliente (e-mail, push, SMS).
