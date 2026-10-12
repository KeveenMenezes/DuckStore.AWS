import { util } from '@aws-appsync/utils'

// HTTP resolver → Step Functions StartSyncExecution of the checkout-saga Express state machine
// (ADR-0050), the same arrangement as createProductWithPrice (ADR-0032). The saga loads the saved
// cart, prices it in Pricing, names its lines from Catalog, registers the payment and closes the
// cart; this resolver only builds its input and maps its outcome. The state-machine ARN placeholder
// below is substituted at CDK synth time (see appsync-api.ts resolver helper).
// Cognito-only: the owner always comes from the token, never from the client.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  const input = ctx.args.input
  const sagaInput = {
    ownerId: `USER#${ctx.identity.sub}`,
    customerId: ctx.identity.sub,
    // Optional while the SPA rolls out (ADR-0050 §3); the saga mints one when it is absent.
    checkoutId: input.checkoutId ?? null,
    input,
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

// A typed refusal is a SUCCEEDED execution carrying `refusal`; only real faults end FAILED or
// TIMED_OUT, and those surface as a bare InternalError so no detail reaches the client (ADR-0050 §8).
// The response body also echoes the execution input (which holds the card): nothing here reads or
// returns it.
export function response(ctx) {
  if (ctx.error || ctx.result.statusCode !== 200) {
    util.error('The checkout could not be completed.', 'InternalError')
  }

  const execution = JSON.parse(ctx.result.body)
  if (execution.status !== 'SUCCEEDED') {
    util.error('The checkout could not be completed.', 'InternalError')
  }

  const output = JSON.parse(execution.output)
  if (output.refusal) {
    util.error(output.refusal.message, output.refusal.errorType, null, output.refusal.errorInfo ?? {})
  }

  return { isSuccess: output.isSuccess, orderId: output.orderId ?? null }
}
