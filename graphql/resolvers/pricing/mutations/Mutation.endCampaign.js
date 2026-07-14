import { util } from '@aws-appsync/utils'

// Admin only. Invokes Pricing's EndCampaign Lambda — retracts the campaign's product-discounts
// rows via a TransactWriteItems (ADR-0026).
export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: { CampaignId: ctx.args.campaignId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return ctx.result.Ended
}
