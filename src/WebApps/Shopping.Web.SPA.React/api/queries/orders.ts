export const GET_ORDERS_BY_CUSTOMER = `
  query GetOrdersByCustomer($customerId: ID!) {
    ordersByCustomer(customerId: $customerId) {
      items {
        id
        customerId
        orderName
        status
        createdAt
        shippingAddress {
          firstName lastName emailAddress addressLine country state zipCode
        }
        payment {
          cardName cardNumber expiration cvv paymentMethod
        }
        orderItems {
          productId quantity price
        }
      }
      nextToken
    }
  }
`
