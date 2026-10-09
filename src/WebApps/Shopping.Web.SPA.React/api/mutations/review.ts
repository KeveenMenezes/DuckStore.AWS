export const CREATE_REVIEW = `
  mutation CreateReview($input: CreateReviewInput!) {
    createReview(input: $input) { id userName }
  }
`

export const DELETE_REVIEW = `
  mutation DeleteReview($productId: ID!) {
    deleteReview(productId: $productId) { id status }
  }
`
