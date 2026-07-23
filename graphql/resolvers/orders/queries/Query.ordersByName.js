import { util } from "@aws-appsync/utils";

// Ordering.Function stores PaymentMethod as the C# enum's string name (Payment.cs .ToString()),
// not its numeric value — the GraphQL schema declares paymentMethod as Int!, so it must be
// converted here. Keep in sync with Ordering.Function.Modules.Orders.Domain.Enums.PaymentMethod.
const PAYMENT_METHOD_TO_INT = { Debit: 1, Credit: 2, Cash: 3 };

export function request(ctx) {
  const groups = ctx.identity?.groups ?? [];
  if (!groups.includes("Admin")) util.unauthorized();

  const { name, pageSize = 10, nextToken } = ctx.args;
  return {
    operation: "Scan",
    filter: {
      expression: "contains(OrderName, :name) AND #Type = :type",
      expressionNames: { "#Type": "Type" },
      expressionValues: {
        ":name": util.dynamodb.toDynamoDB(name),
        ":type": util.dynamodb.toDynamoDB("Order"),
      },
    },
    limit: pageSize,
    nextToken,
  };
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type);
  return {
    items: ctx.result.items.map((item) => ({
      id: item.Id,
      customerId: item.CustomerId,
      orderName: item.OrderName,
      status: item.Status,
      createdAt: item.CreatedAt ?? null,
      shippingAddress: {
        firstName: item.ShippingAddress?.FirstName ?? "",
        lastName: item.ShippingAddress?.LastName ?? "",
        emailAddress: item.ShippingAddress?.EmailAddress ?? "",
        addressLine: item.ShippingAddress?.AddressLine ?? "",
        country: item.ShippingAddress?.Country ?? "",
        state: item.ShippingAddress?.State ?? "",
        zipCode: item.ShippingAddress?.ZipCode ?? "",
      },
      payment: {
        paymentMethod: PAYMENT_METHOD_TO_INT[item.Payment?.PaymentMethod] ?? 0,
        installments: item.Payment?.Installments ?? 1,
      },
      orderItems: (item.OrderItems ?? []).map((orderItem) => ({
        productId: orderItem.ProductId,
        quantity: orderItem.Quantity,
        price: orderItem.Price,
      })),
    })),
    nextToken: ctx.result.nextToken ?? null,
  };
}
