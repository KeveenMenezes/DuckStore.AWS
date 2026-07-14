import { util } from '@aws-appsync/utils'

// HTTP resolver → Step Functions StartSyncExecution (ADR-0032). Runs the
// product-create-saga Express workflow synchronously: PutItem on Catalog's
// products table, UpdateItem on Pricing's prices table, and a compensating
// product delete if the price write fails. The state-machine ARN placeholder below
// is substituted with the real ARN at CDK synth time (see appsync-api.ts resolver helper).
// Image invariants (ADR-0034): valid ULID keys, at most 12, exactly one main. Enforced at
// the write edge — the imageIds are client-echoed values from createProductImageUpload.
function validateImages(images) {
  if (images.length > 12) util.error('A product can have at most 12 images.', 'BadRequest')
  const mainCount = images.filter(i => i.isMain).length
  if (images.length > 0 && mainCount !== 1) {
    util.error('Exactly one image must be marked as main.', 'BadRequest')
  }
  images.forEach(i => {
    if (!util.matches('^[0-9A-HJKMNP-TV-Z]{26}$', i.imageId)) {
      util.error(`Invalid imageId "${i.imageId}".`, 'BadRequest')
    }
  })
}

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  const { name, description, images, stock, categoryIds, price, cost } = ctx.args.input
  validateImages(images)
  const id = util.autoId()
  // Saga input contract: N-typed values travel as strings — the state machine maps
  // them with numberFromString because DynamoDB's wire format requires N as a string.
  // Images travel fully in DynamoDB wire format: the saga's PutProduct maps the list
  // with listFromJsonPath, which expects attribute-value JSON, not plain objects.
  const sagaInput = {
    id,
    name,
    description,
    images: images.map(i => ({
      M: { ImageId: { S: i.imageId }, IsMain: { BOOL: i.isMain }, Order: { N: `${i.order}` } },
    })),
    categoryIds,
    stock: `${stock}`,
    price: `${price}`,
    cost: `${cost}`,
  }
  return {
    method: 'POST',
    resourcePath: '/',
    params: {
      headers: {
        'Content-Type': 'application/x-amz-json-1.0',
        'X-Amz-Target': 'AWSStepFunctions.StartSyncExecution',
      },
      body: JSON.stringify({
        stateMachineArn: '__STATE_MACHINE_ARN__',
        input: JSON.stringify(sagaInput),
      }),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (ctx.result.statusCode !== 200) {
    util.error(`StartSyncExecution failed: ${ctx.result.body}`, 'StepFunctionsError')
  }
  const execution = JSON.parse(ctx.result.body)
  // A FAILED execution still comes back as HTTP 200 — the saga outcome is in `status`.
  if (execution.status !== 'SUCCEEDED') {
    util.error(
      execution.cause ?? execution.error ?? 'Product creation saga failed',
      execution.error ?? 'SagaFailed',
    )
  }
  // `output` is the execution result as a JSON string — { id }.
  return JSON.parse(execution.output)
}
