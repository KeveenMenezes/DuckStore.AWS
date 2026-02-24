#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# DuckStore SPA — Deploy to S3 + CloudFront
#
# Usage:
#   ./deploy.sh                          # Deploy with defaults
#   ./deploy.sh --env staging            # Deploy to staging
#   ./deploy.sh --stack-name my-stack    # Custom stack name
#   ./deploy.sh --skip-build             # Skip Angular build
#   ./deploy.sh --infra-only             # Only deploy infra (no S3 sync)
#   ./deploy.sh --app-only               # Only build & sync (no infra)
# ─────────────────────────────────────────────────────────────
set -euo pipefail

# ── Defaults ──
ENVIRONMENT="production"
STACK_NAME=""
API_GATEWAY_DOMAIN="placeholder.example.com"
API_GATEWAY_PROTOCOL="https-only"
CUSTOM_DOMAIN="shop.thecodeduck.com"
SKIP_BUILD=false
INFRA_ONLY=false
APP_ONLY=false
AWS_REGION="${AWS_REGION:-us-east-1}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="${SCRIPT_DIR}"
SPA_DIR="${SCRIPT_DIR}/../../src/WebApps/Shopping.Web.SPA"
DIST_DIR="${SPA_DIR}/dist/shopping/browser"

# ── Colors ──
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log()   { echo -e "${CYAN}[deploy]${NC} $*"; }
warn()  { echo -e "${YELLOW}[warn]${NC} $*"; }
error() { echo -e "${RED}[error]${NC} $*" >&2; }
ok()    { echo -e "${GREEN}[ok]${NC} $*"; }

# ── Parse args ──
while [[ $# -gt 0 ]]; do
  case $1 in
    --env)             ENVIRONMENT="$2"; shift 2 ;;
    --stack-name)      STACK_NAME="$2"; shift 2 ;;
    --api-domain)      API_GATEWAY_DOMAIN="$2"; shift 2 ;;
    --api-protocol)    API_GATEWAY_PROTOCOL="$2"; shift 2 ;;
    --domain)          CUSTOM_DOMAIN="$2"; shift 2 ;;
    --region)          AWS_REGION="$2"; shift 2 ;;
    --skip-build)      SKIP_BUILD=true; shift ;;
    --infra-only)      INFRA_ONLY=true; shift ;;
    --app-only)        APP_ONLY=true; shift ;;
    -h|--help)
      echo "Usage: $0 [options]"
      echo ""
      echo "Options:"
      echo "  --env <env>            Environment: production|staging (default: production)"
      echo "  --stack-name <name>    CloudFormation stack name (default: duckstore-spa-<env>)"
      echo "  --api-domain <domain>  YARP API Gateway domain (required for infra deploy)"
      echo "  --api-protocol <proto> API origin protocol: https-only|http-only (default: https-only)"
      echo "  --domain <domain>      Custom domain for the SPA (default: shop.thecodeduck.com)"
      echo "  --region <region>      AWS region (default: us-east-1)"
      echo "  --skip-build           Skip the Angular production build"
      echo "  --infra-only           Only deploy CloudFormation (no S3 sync)"
      echo "  --app-only             Only build & sync to S3 (no infra changes)"
      echo "  -h, --help             Show this help"
      exit 0
      ;;
    *) error "Unknown option: $1"; exit 1 ;;
  esac
done

STACK_NAME="${STACK_NAME:-duckstore-spa-${ENVIRONMENT}}"

# ── Validate ──
command -v aws >/dev/null 2>&1 || { error "AWS CLI is required. Install: brew install awscli"; exit 1; }

if [[ "$APP_ONLY" == false && "$API_GATEWAY_DOMAIN" == "placeholder.example.com" ]]; then
  warn "Using placeholder API Gateway domain — API routes won't work until YARP Gateway is deployed."
  warn "Use --api-domain <domain> to set the real domain."
fi

# ── Step 1: Deploy CloudFormation ──
deploy_infra() {
  log "Deploying CloudFormation stack: ${STACK_NAME} (${ENVIRONMENT})"

  # Look up Route 53 Hosted Zone ID automatically
  HOSTED_ZONE_ID=$(aws route53 list-hosted-zones-by-name \
    --dns-name thecodeduck.com \
    --query "HostedZones[0].Id" \
    --output text | sed 's|/hostedzone/||')

  if [[ -z "$HOSTED_ZONE_ID" || "$HOSTED_ZONE_ID" == "None" ]]; then
    warn "Could not find Route 53 Hosted Zone for thecodeduck.com — deploying without custom domain."
    CUSTOM_DOMAIN=""
    HOSTED_ZONE_ID=""
  else
    log "Route 53 Hosted Zone: ${HOSTED_ZONE_ID}"
    log "Custom domain:        ${CUSTOM_DOMAIN}"
  fi

  aws cloudformation deploy \
    --template-file "${INFRA_DIR}/template.yaml" \
    --stack-name "${STACK_NAME}" \
    --region "${AWS_REGION}" \
    --parameter-overrides \
      Environment="${ENVIRONMENT}" \
      ApiGatewayOriginDomain="${API_GATEWAY_DOMAIN}" \
      ApiGatewayOriginProtocol="${API_GATEWAY_PROTOCOL}" \
      CustomDomain="${CUSTOM_DOMAIN}" \
      Route53HostedZoneId="${HOSTED_ZONE_ID}" \
    --no-fail-on-empty-changeset \
    --tags \
      Project=DuckStore \
      Component=Shopping-SPA \
      Environment="${ENVIRONMENT}"

  ok "CloudFormation stack deployed successfully."
}

# ── Step 2: Get stack outputs ──
get_stack_outputs() {
  log "Fetching stack outputs..."

  S3_BUCKET=$(aws cloudformation describe-stacks \
    --stack-name "${STACK_NAME}" \
    --region "${AWS_REGION}" \
    --query "Stacks[0].Outputs[?OutputKey=='S3BucketName'].OutputValue" \
    --output text)

  CF_DISTRIBUTION_ID=$(aws cloudformation describe-stacks \
    --stack-name "${STACK_NAME}" \
    --region "${AWS_REGION}" \
    --query "Stacks[0].Outputs[?OutputKey=='CloudFrontDistributionId'].OutputValue" \
    --output text)

  CF_DOMAIN=$(aws cloudformation describe-stacks \
    --stack-name "${STACK_NAME}" \
    --region "${AWS_REGION}" \
    --query "Stacks[0].Outputs[?OutputKey=='CloudFrontDomainName'].OutputValue" \
    --output text)

  SPA_URL=$(aws cloudformation describe-stacks \
    --stack-name "${STACK_NAME}" \
    --region "${AWS_REGION}" \
    --query "Stacks[0].Outputs[?OutputKey=='SpaUrl'].OutputValue" \
    --output text)

  log "S3 Bucket:       ${S3_BUCKET}"
  log "CF Distribution: ${CF_DISTRIBUTION_ID}"
  log "CF Domain:       https://${CF_DOMAIN}"
  log "SPA URL:         ${SPA_URL}"
}

# ── Step 2.5: Inject Cognito redirect URL ──
inject_redirect_url() {
  if [[ "$SKIP_BUILD" == true ]]; then
    return
  fi

  log "Injecting Cognito redirectUrl → ${SPA_URL}"
  local env_file="${SPA_DIR}/src/environments/environment.production.ts"

  if [[ -f "$env_file" ]]; then
    local tmp_file
    tmp_file="$(mktemp)"
    sed "s|redirectUrl: '.*'|redirectUrl: '${SPA_URL}'|" "$env_file" > "$tmp_file"
    mv "$tmp_file" "$env_file"
    ok "environment.production.ts updated."
  else
    warn "environment.production.ts not found — skipping."
  fi
}

# ── Step 3: Build Angular app ──
build_app() {
  if [[ "$SKIP_BUILD" == true ]]; then
    warn "Skipping Angular build (--skip-build)"
    return
  fi

  log "Building Angular app for production..."
  cd "${SPA_DIR}"

  if [[ ! -d "node_modules" ]]; then
    log "Installing dependencies..."
    npm ci
  fi

  npx ng build --configuration production
  ok "Angular build complete. Output: ${DIST_DIR}"
  cd "${SCRIPT_DIR}"
}

# ── Step 4: Sync to S3 ──
sync_to_s3() {
  if [[ ! -d "$DIST_DIR" ]]; then
    error "Build output not found at: ${DIST_DIR}"
    error "Run without --skip-build or build manually first."
    exit 1
  fi

  log "Syncing build output to s3://${S3_BUCKET}..."

  # Upload hashed assets with long cache
  aws s3 sync "${DIST_DIR}" "s3://${S3_BUCKET}" \
    --region "${AWS_REGION}" \
    --delete \
    --exclude "index.html" \
    --exclude "*.json" \
    --cache-control "public,max-age=31536000,immutable"

  # Upload index.html and JSON with short cache (for updates)
  aws s3 cp "${DIST_DIR}/index.html" "s3://${S3_BUCKET}/index.html" \
    --region "${AWS_REGION}" \
    --cache-control "public,max-age=60,s-maxage=0,must-revalidate" \
    --content-type "text/html"

  # Upload any JSON config files with short cache
  if compgen -G "${DIST_DIR}/*.json" > /dev/null; then
    for json_file in "${DIST_DIR}"/*.json; do
      aws s3 cp "${json_file}" "s3://${S3_BUCKET}/$(basename "${json_file}")" \
        --region "${AWS_REGION}" \
        --cache-control "public,max-age=60,must-revalidate" \
        --content-type "application/json"
    done
  fi

  ok "S3 sync complete."
}

# ── Step 5: Invalidate CloudFront cache ──
invalidate_cache() {
  log "Invalidating CloudFront cache..."

  INVALIDATION_ID=$(aws cloudfront create-invalidation \
    --distribution-id "${CF_DISTRIBUTION_ID}" \
    --paths "/index.html" "/*.json" \
    --query "Invalidation.Id" \
    --output text)

  ok "CloudFront invalidation created: ${INVALIDATION_ID}"
  log "It may take a few minutes for the invalidation to propagate."
}

# ── Main ──
main() {
  echo ""
  log "═══════════════════════════════════════════════"
  log "  DuckStore SPA — S3 + CloudFront Deployment"
  log "═══════════════════════════════════════════════"
  log "Environment:  ${ENVIRONMENT}"
  log "Stack:        ${STACK_NAME}"
  log "Region:       ${AWS_REGION}"
  echo ""

  if [[ "$APP_ONLY" == false ]]; then
    deploy_infra
  fi

  get_stack_outputs

  if [[ "$INFRA_ONLY" == false ]]; then
    inject_redirect_url
    build_app
    # Restore environment file to avoid dirty git state
    if git -C "${SPA_DIR}" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
      if ! git -C "${SPA_DIR}" checkout -- src/environments/environment.production.ts >/dev/null 2>&1; then
        warn "Failed to restore src/environments/environment.production.ts in ${SPA_DIR}; please check your working tree."
      fi
    else
      warn "Skipping environment file restore: ${SPA_DIR} is not a git repository."
    fi
    sync_to_s3
    invalidate_cache
  fi

  echo ""
  ok "═══════════════════════════════════════════════"
  ok "  Deployment complete!"
  ok "  URL: ${SPA_URL}"
  ok "═══════════════════════════════════════════════"
  echo ""
  warn "Remember to add ${SPA_URL} as an allowed callback URL in Cognito."
}

main
