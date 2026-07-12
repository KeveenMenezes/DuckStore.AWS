import { util } from '@aws-appsync/utils'

// Cognito-only. Direct DynamoDB UpdateItem resolver (ADR-0009) — lazily provisions the profile
// from the token claims on first access (ADR-0017). `if_not_exists` on Email/Name replicates the
// old Lambda's get-or-create exactly: it seeds them once and never overwrites on later calls (the
// old handler never re-applied claims once a profile existed) — Phone/AddressLine/etc. are only
// ever set by updateProfile, so this expression must not touch them.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'UpdateItem',
    key: { UserId: util.dynamodb.toDynamoDB(ctx.identity.sub) },
    update: {
      expression: 'SET Email = if_not_exists(Email, :email), #name = if_not_exists(#name, :name)',
      expressionNames: { '#name': 'Name' },
      expressionValues: util.dynamodb.toMapValues({
        ':email': ctx.identity.claims.email,
        ':name': ctx.identity.claims.name,
      }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  const p = ctx.result
  return {
    userId: p.UserId,
    email: p.Email,
    name: p.Name,
    phone: p.Phone ?? null,
    addressLine: p.AddressLine ?? null,
    city: p.City ?? null,
    state: p.State ?? null,
    zipCode: p.ZipCode ?? null,
    country: p.Country ?? null,
  }
}
