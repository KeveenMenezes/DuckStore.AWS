export interface Review {
  id: string
  productId: string
  userName: string
  rating: number
  comment: string
  createdAt: string
}

export type ReviewStatus = "Eligible" | "Published" | "Deleted"

// The signed-in customer's own row for a product (myReview). Its existence is the purchase gate:
// no row = never bought, so the form is not offered (ADR-0049).
export interface MyReview extends Review {
  status: ReviewStatus
}

export interface ReviewPage {
  items: Review[]
  nextToken: string | null
}
