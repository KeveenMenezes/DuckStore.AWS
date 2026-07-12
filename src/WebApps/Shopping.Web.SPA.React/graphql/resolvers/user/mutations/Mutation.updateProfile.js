import { util } from "@aws-appsync/utils";

// Cognito-only. UpdateItem keyed by ctx.identity.sub. Email is Cognito-authoritative (kept in
// sync from the token, never from the client); name is required; the rest are optional and only
// written when provided. Upsert-safe: creates the item if the profile wasn't provisioned yet.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized();

  const input = ctx.args.input;
  const sets = ["Email = :email", "#name = :name"];
  const names = { "#name": "Name" };
  const values = { ":email": ctx.identity.claims.email, ":name": input.name };

  if (input.phone != null) {
    sets.push("Phone = :phone");
    values[":phone"] = input.phone;
  }
  if (input.addressLine != null) {
    sets.push("AddressLine = :addressLine");
    values[":addressLine"] = input.addressLine;
  }
  if (input.city != null) {
    sets.push("City = :city");
    values[":city"] = input.city;
  }
  if (input.state != null) {
    sets.push("#state = :state");
    names["#state"] = "State";
    values[":state"] = input.state;
  }
  if (input.zipCode != null) {
    sets.push("ZipCode = :zipCode");
    values[":zipCode"] = input.zipCode;
  }
  if (input.country != null) {
    sets.push("Country = :country");
    values[":country"] = input.country;
  }

  return {
    operation: "UpdateItem",
    key: util.dynamodb.toMapValues({ UserId: ctx.identity.sub }),
    update: {
      expression: "SET " + sets.join(", "),
      expressionNames: names,
      expressionValues: util.dynamodb.toMapValues(values),
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
