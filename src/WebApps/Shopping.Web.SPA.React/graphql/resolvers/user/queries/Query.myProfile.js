import { util } from "@aws-appsync/utils";

// Cognito-only. Passes the identity claims to the user-get-profile Lambda, which returns the
// profile — lazily provisioning it (seeded from these claims) on first access. See ADR-0017.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized();

  return {
    operation: "Invoke",
    payload: {
      UserId: ctx.identity.sub,
      Email: ctx.identity.claims.email,
      Name: ctx.identity.claims.name,
    },
  };
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type);
  const p = ctx.result;
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
  };
}
