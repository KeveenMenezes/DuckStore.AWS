#!/usr/bin/env bash
#
# One-off cleanup for the Catalog `products` table on AWS.
#
# Rating aggregation (ADR-0027/ADR-0030) and price ownership (ADR-0026) moved out of
# Catalog, but items written before those migrations — plus items touched by the old
# catalog-review-created-consumer (ADR-0011 §4) — still carry the legacy attributes.
# Catalog's repository does a full-replace PutItem, so only untouched items keep them.
#
# Scans `products` for items holding any legacy attribute and issues an UpdateItem
# REMOVE for all of them (removing a missing attribute is a no-op).
#
# Requirements: aws cli v2 + jq, credentials with dynamodb:Scan/UpdateItem on `products`.
#
# Usage:
#   ./scripts/cleanup-products-legacy-attributes.sh            # dry run (lists item ids)
#   ./scripts/cleanup-products-legacy-attributes.sh --apply    # actually removes attributes
set -euo pipefail

TABLE_NAME="${TABLE_NAME:-products}"
LEGACY_ATTRS=(Price OriginalPrice CashPrice AverageRating RatingCount RatingSum)

APPLY=false
[[ "${1:-}" == "--apply" ]] && APPLY=true

# Build "attribute_exists(#a0) OR attribute_exists(#a1) ..." plus the name map,
# using placeholders so reserved words like Price never hit the expression parser.
filter=""
names="{"
for i in "${!LEGACY_ATTRS[@]}"; do
  [[ -n "$filter" ]] && filter+=" OR "
  filter+="attribute_exists(#a$i)"
  [[ "$names" != "{" ]] && names+=","
  names+="\"#a$i\":\"${LEGACY_ATTRS[$i]}\""
done
names+="}"

remove_expr="REMOVE $(printf '#a%s,' "${!LEGACY_ATTRS[@]}")"
remove_expr="${remove_expr%,}"

echo "Table: $TABLE_NAME"
echo "Legacy attributes: ${LEGACY_ATTRS[*]}"
$APPLY || echo "DRY RUN — pass --apply to remove the attributes."

total=0
start_key=""
while :; do
  # ${extra[@]+...} keeps `set -u` happy on macOS bash 3.2 when the array is empty.
  extra=()
  [[ -n "$start_key" ]] && extra=(--exclusive-start-key "$start_key")

  page=$(aws dynamodb scan \
    --table-name "$TABLE_NAME" \
    --filter-expression "$filter" \
    --expression-attribute-names "$names" \
    --projection-expression "Id" \
    ${extra[@]+"${extra[@]}"})

  while IFS= read -r id; do
    total=$((total + 1))
    if $APPLY; then
      aws dynamodb update-item \
        --table-name "$TABLE_NAME" \
        --key "{\"Id\":{\"S\":\"$id\"}}" \
        --update-expression "$remove_expr" \
        --expression-attribute-names "$names"
      echo "cleaned  $id"
    else
      echo "would clean  $id"
    fi
  done < <(jq -r '.Items[].Id.S' <<<"$page")

  start_key=$(jq -c '.LastEvaluatedKey // empty' <<<"$page")
  [[ -z "$start_key" ]] && break
done

echo "Done. Items with legacy attributes: $total"
