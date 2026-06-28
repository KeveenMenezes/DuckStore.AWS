export const GET_REVIEWS_BY_PRODUCT = `
  query GetReviewsByProduct($productId: ID!, $pageSize: Int, $nextToken: String) {
    reviewsByProduct(productId: $productId, pageSize: $pageSize, nextToken: $nextToken) {
      items { id productId userName rating comment createdAt }
      nextToken
    }
  }
`
