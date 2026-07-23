export const GET_ORDERS_BY_CUSTOMER = `
  query GetOrdersByCustomer($customerId: ID!) {
    ordersByCustomer(customerId: $customerId) {
      items {
        id
        status
        createdAt
        shippingAddress {
          firstName lastName emailAddress addressLine country state zipCode
        }
        payment {
          paymentMethod installments
        }
        orderItems {
          productId productName imageId quantity price
        }
      }
      nextToken
    }
  }
`
