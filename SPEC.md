# Capability Map: Pontos de Desafio como Desconto (Points-to-Discount v1)

> Status: **Mapa APROVADO** (revisão 3, 2026-10-09). Specs de módulo **aguardando aprovação**:
>
> | Ordem | Módulo | Spec |
> |---|---|---|
> | 1 | `points-ledger` | [SPEC-points-ledger.md](./SPEC-points-ledger.md) |
> | 2 | `challenge-points-admin` | [SPEC-challenge-points-admin.md](./SPEC-challenge-points-admin.md) |
> | 3 | `review-eligibility` | [SPEC-review-eligibility.md](./SPEC-review-eligibility.md) |
> | 4 | `review-points` | [SPEC-review-points.md](./SPEC-review-points.md) |
> | 5 | `cart-points-redemption` | [SPEC-cart-points-redemption.md](./SPEC-cart-points-redemption.md) |

## 1. Objetivo

Permitir que o cliente transforme pontos ganhos em desafios e avaliações em desconto no carrinho.

Usuário-alvo: cliente autenticado (Cognito, com Google e Amazon) da loja React. Admin: usuário do pool de gestão, no Blazor.

### Regras de negócio v1

| Regra | Valor |
|---|---|
| Valor do ponto | 100 pontos = R$ 1,00 (1 ponto = R$ 0,01) |
| Teto de resgate | até 20% do valor do pedido |
| Pontos por avaliação | 15, só para quem comprou o produto, uma vez por produto |
| Histórico | por linha: data, motivo (desafio, avaliação ou resgate) e valor |
| Validade | sem expiração |

### Decisões já tomadas

- **Resgate no carrinho:** o cliente informa o **total de pontos** que quer usar. Sem cupom intermediário. O Pricing converte e aplica o teto.
- **Estorno:** se o pagamento do pedido for cancelado ou recusado, os pontos voltam para o cliente.
- **Tabela de transações:** uma nova tabela DynamoDB registra cada movimento de pontos com o id do pedido. O status da transação muda conforme o fluxo (seção 4).
- **Status da review** (substitui a "review nula"): a review passa a ter um status. Ela nasce com o status adequado quando o pedido é concluído, e o cliente também pode **deletar** a própria review (seção 5).
- **Compra verificada:** só quem tem uma review criada pelo pedido concluído pode avaliar. O detalhe do produto busca a review do cliente, habilita a avaliação, permite editar e mostra o último comentário.
- **Admin:** página no Blazor, restrita ao grupo Admin. Vale só para respostas futuras, sem recalcular tentativas passadas.
- **Base do teto:** total do carrinho depois da campanha (ADR-0043), sem frete e sem juros.

## 2. Avaliação de arquitetura (estado atual → lacuna)

| Capacidade | Hoje | Lacuna |
|---|---|---|
| Login Google/Amazon | OK em `infra/constructs/appsync-auth.ts` | Validar ponta a ponta e mostrar o saldo |
| Saldo | `myChallengeProgress.score` | Nenhuma UI global de saldo |
| Pontos por desafio | OK (`SubmitAnswer`, `Question.Points`) | Falta tela para ajustar `points` |
| Resgate | Modelo de **cupom** (ADR-0046): `redeemChallengePoints` → `CustomerDiscount` → `basketInstallmentPlan(discountId)` | Conversão atual 100 pts = **R$10**, expira em **90 dias**, **sem teto**. O modelo muda para pontos direto no carrinho com transações |
| UI do carrinho | `myRewards`/`rewardConversion` marcadas "NOT WIRED UP" | Construir |
| Pontos por avaliação | Não existe. `createReview` é resolver direto, sem checagem de compra | Status da review, elegibilidade, delete e crédito |
| Evento de pedido concluído | O Ordering só publica `OrderCreated` | Nova regra de stream (MODIFY → `Completed`) |
| Histórico | Existem `ATTEMPT#` e `REDEMPTION#`, sem `REVIEW#` | Tabela de transações como ledger |

### ADRs afetadas (emendas necessárias)

- **ADR-0046:** o modelo de cupom (`customer-discounts`, `myRewards`, `discountId`) é substituído por pontos informados no carrinho e transações com status. Os §1 (Challenges dono do saldo, Pricing dono da conversão) e §3 (CDC, sem invoke síncrono) continuam valendo.
- **ADR-0045:** Challenges deixa de ser CDC-out only e ganha consumers (crédito por avaliação, reserva e liberação de pontos).
- **ADR-0011/0029:** a review precisa de status, e só `Published` entra na média e nas listas públicas. Deletar uma review publicada ajusta a média por delta.
- **Nova ADR (0048):** tabela de transações de pontos, máquina de estados e estorno.

## 3. Mapa de módulos

| Module id | Responsabilidade | Serviços | Depende de |
|---|---|---|---|
| `points-ledger` | Tabela `points-transactions` (nova), máquina de estados, `myPointsHistory`, saldo no header e página de histórico. Valida o login Google/Amazon ponta a ponta. Passa a registrar também os créditos de desafio | Challenges, SPA | — |
| `challenge-points-admin` | Admin lista desafios e ajusta `points` (só dali em diante) | Challenges, Blazor, AppSync | — |
| `review-eligibility` | Ordering publica `OrderCompleted` → Review cria a review com status `Eligible` por (cliente, produto). Delete de review. `myReview(productId)`. O detalhe do produto habilita, edita e mostra a avaliação. Só `Published` entra na média | Ordering, Review, SPA | — |
| `review-points` | O primeiro preenchimento (`Eligible` → `Published`) credita 15 pontos, idempotente por (cliente, produto). Editar, deletar e republicar não creditam de novo | Review, Challenges | `points-ledger`, `review-eligibility` |
| `cart-points-redemption` | O cliente informa o total de pontos no carrinho. Pricing converte (100:1) e aplica o teto de 20%. O checkout carrega os pontos e o id do pedido. Reserva, uso e estorno via `points-transactions` | Pricing, Basket (passthrough), Ordering, Payment (eventos), Challenges, SPA | `points-ledger` |

Ordem de build (linear): `points-ledger` → `challenge-points-admin` → `review-eligibility` → `review-points` → `cart-points-redemption`.

```
points-ledger ─┬─> review-points  (também depende de review-eligibility)
               └─> cart-points-redemption
challenge-points-admin   (independente, pode ir em paralelo)
review-eligibility       (independente, precede review-points)
```

### Cobertura dos critérios de aceite

| Critério do negócio | Módulo |
|---|---|
| Entro com Google e Amazon e vejo o saldo | `points-ledger` |
| Resolvo um desafio e o saldo sobe | `points-ledger` |
| Avalio um produto que comprei e ganho pontos | `review-eligibility` + `review-points` |
| Tento avaliar um que não comprei e não ganho | `review-eligibility` (sem review `Eligible`, sem avaliação e sem crédito) |
| Uso pontos no carrinho e o desconto respeita o teto de 20% | `cart-points-redemption` |
| O histórico mostra tudo corretamente | `points-ledger` (todos os tipos de linha) |
| Vejo e consigo ajustar a pontuação dos desafios | `challenge-points-admin` |

## 4. Tabela `points-transactions` e fluxo de status (proposta)

Dona: Challenges (dona do saldo, ADR-0046 §1). Chave: PK `OwnerId`, SK `TransactionId`. GSI1 por `OrderId`, para os eventos de pagamento acharem a transação. Streams ligados.

| Atributo | Uso |
|---|---|
| `Type` | `ChallengeCredit` \| `ReviewCredit` \| `Redemption` |
| `Points` | positivo no crédito, negativo no resgate |
| `Status` | ver abaixo |
| `OrderId` | só no resgate |
| `SourceId` | `QuestionId` ou `ProductId`, para idempotência (um crédito de avaliação por produto) |
| `CreatedAt` / `UpdatedAt` | histórico (data) |
| `StatusHistory` | opcional, trilha de auditoria |

**Máquina de estados do resgate** (proposta, aberta a melhorias):

```
                    ┌─> Used            (PaymentAuthorizedEvent)
Reserved ───────────┤
(AwaitingPayment)   ├─> Released        (PaymentDeclinedEvent: pontos voltam ao saldo)
                    └─> Failed          (saldo insuficiente na reserva: o pedido é cancelado)

Used ──> Refunded   (pedido cancelado depois de pago: pontos voltam ao saldo)
```

- `Reserved`: o saldo é debitado de forma condicional (`Score >= :pts`), e a transação é gravada na mesma `TransactWriteItems`.
- Toda transição é um `UpdateItem` condicional pelo status de origem, o que a torna idempotente e à prova de evento duplicado.
- Créditos de desafio e avaliação nascem `Completed` (terminal).
- O histórico mostra: data, motivo (desafio, avaliação ou resgate) e valor, a partir desta tabela.

## 5. Status da review (proposta)

| Status | Quando | Visível publicamente | Entra na média |
|---|---|---|---|
| `Eligible` | Pedido concluído e o cliente ainda não avaliou | Não | Não |
| `Published` | O cliente preencheu nota e comentário | Sim | Sim |
| `Deleted` | O cliente deletou a própria review (soft delete) | Não | Não (a média é ajustada por delta) |

- Chave continua `ProductId#UserId`, então segue 1 review por cliente e produto.
- Um cliente com review `Deleted` pode publicar de novo, mas **não ganha os 15 pontos outra vez** (a idempotência está em `points-transactions`, não no status).
- **TTL em `Deleted`:** a linha com `Status = Deleted` ganha um atributo TTL de 5 dias. Regras: (a) o publisher de reviews ignora o `REMOVE` gerado pelo TTL, porque a média já foi ajustada no delete; (b) as consultas filtram por `Status` e não dependem do TTL disparar (ele pode atrasar até 48h); (c) quando a linha some, o cliente perde a elegibilidade para aquele produto, e isso é intencional; (d) a trava de pontos fica em `points-transactions` (`TransactionId = REVIEW#<productId>`, `attribute_not_exists`), então o TTL nunca reabre o crédito; (e) `points-transactions` não tem TTL.
- `createReview` deixa de ser um upsert livre: exige que a review exista e esteja `Eligible`, `Published` ou `Deleted`. Sem review, o resolver rejeita.

## 6. Premissas que estou assumindo (corrija agora ou sigo com elas)

1. "Pedido concluído" = transição `Pending → Completed` no Ordering, após `PaymentAuthorizedEvent`.
2. Os 15 pontos saem na primeira publicação (`Eligible` → `Published`), não na criação da review `Eligible`.
3. Pedido cancelado ou recusado não cria review `Eligible`.
4. O cliente informa qualquer quantidade positiva de pontos, limitada pelo saldo e pelo teto de 20%. O desconto arredonda para baixo em centavos.
5. Ordem de aplicação (ADR-0046 §5): campanha primeiro, depois o desconto de pontos, sobre o total do carrinho, com piso em zero.
6. Os cupons `CustomerDiscount` já emitidos em dev são descartáveis, sem migração.
7. "Estornar o valor do produto" significa devolver os **pontos** ao saldo do cliente, não dinheiro.
8. Deletar uma review `Published` não retira os 15 pontos já creditados.
9. A SPA limita o campo de pontos a `min(saldo, maxRedeemablePoints)`, onde `maxRedeemablePoints` vem do `basketInstallmentPlan` (Pricing), e não é calculado no front. O servidor é a autoridade: o Pricing rejeita um valor acima do teto de 20% e o Challenges rejeita um valor acima do saldo, ambos com erro explícito e sem cortar o valor em silêncio.
10. Valem só para pedidos novos: não há backfill de reviews `Eligible`.
11. `Refunded` entra na v1 com gatilho: Ordering publica `OrderCancelled` (regra de stream para qualquer transição para `Cancelled`), e um consumer único em Challenges trata os dois casos: `Reserved` → `Released` (pagamento recusado) e `Used` → `Refunded` (cancelado depois de pago). Hoje o Ordering não tem a transição `Completed` → `Cancelled`, então a v1 entrega o consumer e a regra de stream, e o cancelamento pós-pagamento em si fica para quando esse fluxo existir. Se você queria também a ação de cancelar o pedido na v1, me diga.

## 7. Decisões fechadas nesta rodada

1. **Saldo insuficiente na reserva:** a SPA limita o campo ao que o cliente tem e ao que é autorizado (premissa 9). Se o cliente burlar o front, o servidor rejeita no Pricing (teto) e, se ainda faltar saldo na reserva, a transação vai a `Failed` e o pedido é cancelado.
2. **`Refunded`:** entra na v1 (premissa 11).
3. **Backfill:** não haverá. Vale só para pedidos novos.

4. **Cancelamento pós-pagamento:** a v1 **não** inclui a ação de cancelar um pedido pago. O consumer e a regra de stream do `OrderCancelled` ficam prontos, e o `Refunded` é validado por teste unitário.

## 8. Próximo passo

Aprovar os specs de módulo (tabela no topo). Depois disso vem a Phase 2 (Plan), módulo a módulo, na ordem de build.
