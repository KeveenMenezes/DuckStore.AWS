## Descrição

<!-- O que muda e por quê. Se houver uma issue relacionada, use "Closes #123". -->

## Tipo de mudança

- [ ] Correção de bug
- [ ] Nova funcionalidade
- [ ] Refatoração (sem mudança de comportamento)
- [ ] Infraestrutura / CDK
- [ ] Documentação / ADR

## Bounded context afetado

<!-- Ex.: Catalog, Basket, Ordering, Pricing, Review, Challenges, CatalogView, Payment, User -->

## Checklist

- [ ] `dotnet build DuckStore.slnx` passa
- [ ] `dotnet test` passa
- [ ] Testes adicionados ou atualizados para o comportamento alterado
- [ ] Se a mudança é uma decisão de arquitetura, um ADR foi adicionado em `docs/adr/`
- [ ] Se o schema GraphQL mudou, `graphql/` foi atualizado junto
- [ ] Nenhum segredo, credencial ou identificador de conta AWS foi commitado

## Notas para revisão

<!-- Pontos que merecem atenção, trade-offs considerados, ou o que ficou de fora. -->
