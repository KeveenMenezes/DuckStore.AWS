import { util } from "@aws-appsync/utils";

// Lambda resolver (ADR-0034): invokes product-images-presign to issue batch presigned
// POSTs for direct browser->S3 uploads. Lambda is a justified ADR-0009 escalation —
// SigV4 signing is impossible in APPSYNC_JS. The Lambda is stateless; the imageIds it
// returns only become product data when a later create/update mutation embeds them.
export function request(ctx) {
  const groups = ctx.identity?.groups ?? [];
  if (!groups.includes("Admin") && !groups.includes("Seller"))
    util.unauthorized();

  return {
    operation: "Invoke",
    payload: { contentTypes: ctx.args.input.contentTypes },
  };
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type);
  return ctx.result.map((upload) => ({
    imageId: upload.imageId,
    url: upload.url,
    fields: upload.fields,
  }));
}
