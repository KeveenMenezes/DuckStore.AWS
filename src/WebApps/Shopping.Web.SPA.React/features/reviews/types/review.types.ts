export interface Review {
  id: string
  productId: string
  userName: string
  rating: number
  comment: string
  createdAt: string
}

export interface ReviewPage {
  items: Review[]
  nextToken: string | null
}
