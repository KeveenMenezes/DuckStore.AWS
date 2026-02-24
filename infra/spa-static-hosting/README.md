# DuckStore SPA — S3 + CloudFront Static Hosting

Infraestrutura para publicar o **Shopping.Web.SPA** (Angular) como site estático no AWS S3 com CloudFront CDN.

## Arquitetura

```
                    ┌─────────────────────────────────┐
                    │          CloudFront CDN         │
                    │  (HTTPS, HTTP/2+3, Gzip/Brotli) │
                    └──┬──────────────────────────┬───┘
                       │                          │
         Default: /*   │    /catalog-service/*    │
         (static)      │    /ordering-service/*   │
                       │    /basket-service/*     │
                       ▼                          ▼
              ┌─────────────┐           ┌──────────────────┐
              │  S3 Bucket  │           │ YARP API Gateway │
              │  (Angular)  │           │ (Backend APIs)   │
              └─────────────┘           └──────────────────┘
```

### Por que CloudFront + S3?

| Recurso | Benefício |
|---|---|
| **OAC (Origin Access Control)** | Bucket S3 privado — acesso somente via CloudFront |
| **Múltiplas origens** | API Gateway como 2ª origin — URLs relativas do Angular funcionam sem mudanças |
| **Função CloudFront para SPA routing** | `SpaRoutingFunction` reescreve rotas de cliente para `/index.html` — SPA routing funciona sem depender de Custom Error Responses |
| **Cache inteligente** | Assets hasheados com cache de 1 ano; `index.html` com cache de 60s |
| **Security Headers** | HSTS, X-Content-Type-Options, X-Frame-Options, etc. |
| **HTTP/2 + HTTP/3** | Performance otimizada |

## Pré-requisitos

- [AWS CLI v2](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html) configurado
- Node.js >= 20
- Permissões IAM para: CloudFormation, S3, CloudFront

## Deploy Manual

### 1. Primeiro deploy (infra + app)

```bash
cd infra/spa-static-hosting

./deploy.sh \
  --env production \
  --api-domain api.duckstore.com \
  --region us-east-1
```

### 2. Atualizar somente o app (sem mudanças de infra)

```bash
./deploy.sh --app-only
```

### 3. Atualizar somente a infra

```bash
./deploy.sh --infra-only --api-domain api.duckstore.com
```

### 4. Deploy em staging

```bash
./deploy.sh --env staging --api-domain api-staging.duckstore.com
```

## Deploy via GitHub Actions

O workflow `.github/workflows/deploy-spa.yml` executa automaticamente em push na `main` quando há mudanças no SPA ou na infra.

### Secrets necessários

| Secret | Descrição |
|---|---|
| `AWS_DEPLOY_ROLE_ARN` | ARN do IAM Role para OIDC federation com GitHub Actions |
| `API_GATEWAY_DOMAIN` | Domínio do YARP API Gateway (e.g., `api.duckstore.com`) |

### Trigger manual

Vá em **Actions → Deploy Shopping SPA → Run workflow** e escolha o environment.

## Pós-deploy

### 1. Atualizar Cognito

Adicione a URL do CloudFront como **Allowed Callback URL** no Cognito User Pool:

```
https://dXXXXXXXXXX.cloudfront.net
```

### 2. Atualizar `environment.production.ts`

Substitua `redirectUrl` com a URL real do CloudFront:

```typescript
redirectUrl: 'https://dXXXXXXXXXX.cloudfront.net',
```

### 3. (Opcional) Custom domain

Para usar um domínio customizado (e.g., `shop.duckstore.com`):

1. Crie um certificado ACM na região `us-east-1`
2. Adicione `Aliases` e `ViewerCertificate` no CloudFormation template
3. Crie um CNAME/A-Record no Route53 ou DNS provider

## Estrutura de arquivos

```
infra/spa-static-hosting/
├── template.yaml   # CloudFormation (S3, CloudFront, OAC, Policies)
├── deploy.sh       # Script de deploy (build + sync + invalidation)
└── README.md       # Este arquivo
```

## Troubleshooting

| Problema | Solução |
|---|---|
| 403 ao acessar rota `/shop` | Verificar Custom Error Responses no CloudFront (403→/index.html→200) |
| API retorna CORS error | Verificar se o path pattern no CloudFront está correto (`catalog-service/*`) |
| Login Cognito não redireciona | Adicionar URL do CloudFront no Cognito Allowed Callback URLs |
| Build falha no CI | Verificar `Node.js >= 20` e `npm ci` executou com sucesso |
