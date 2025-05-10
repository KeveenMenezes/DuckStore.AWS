# Contributing to DuckStore

Obrigado por considerar contribuir para o DuckStore! Este guia ajudará você a começar e garantir que suas contribuições sejam consistentes e úteis para o projeto.

## Como Contribuir

Existem várias formas de contribuir com o DuckStore:

1. Reportar problemas (issues).
2. Sugerir novas funcionalidades.
3. Enviar pull requests com correções de bugs ou melhorias.
4. Melhorar a documentação.

## Requisitos para Contribuições

Antes de começar, certifique-se de:

- Ter uma conta no GitHub.
- Ter o [Git](https://git-scm.com/) instalado em sua máquina.
- Ler e seguir o [Código de Conduta](./CODE_OF_CONDUCT.md) do projeto.

## Reportando Problemas

Se você encontrar um problema, crie uma nova issue fornecendo as seguintes informações:

1. **Descrição clara do problema**.
2. **Passos para reproduzir o erro**.
3. **Comportamento esperado**.
4. **Logs ou mensagens de erro, se aplicável**.

## Enviando Pull Requests

1. **Faça um fork do repositório** para sua conta.
2. Crie um branch para sua contribuição:
   ```bash
   git checkout -b minha-contribuicao
   ```
3. Faça suas alterações no código.
4. Certifique-se de que os testes existentes não falhem:
   ```bash
   dotnet test
   ```
5. Envie suas alterações para seu fork:
   ```bash
   git push origin minha-contribuicao
   ```
6. Abra um pull request no repositório principal.

### Dicas para Pull Requests

- Descreva suas alterações no pull request.
- Referencie issues relacionadas usando `#<número-da-issue>`.
- Certifique-se de que seu código segue os padrões de estilo do projeto.

## Padrões de Código

- Utilize as boas práticas de C# e siga as convenções de nomenclatura.
- Formate o código de acordo com o estilo do projeto (use ferramentas como `dotnet format`).
- Adicione comentários claros e úteis, quando necessário.

## Testes

- Inclua testes para todas as alterações que você fizer.
- Certifique-se de que novos testes cobrem todos os casos possíveis.

## Comunicação

Se tiver dúvidas, entre em contato por meio de:

- [Issues](https://github.com/KeveenMenezes/DuckStore/issues)
- [Discussions](https://github.com/KeveenMenezes/DuckStore/discussions)

Agradecemos por suas contribuições e por ajudar a melhorar o DuckStore!
