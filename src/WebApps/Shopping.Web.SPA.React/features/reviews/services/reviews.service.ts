import { gql, gqlPublic } from "@/api"
import { GET_REVIEWS_BY_PRODUCT } from "@/api/queries/reviews"
import { CREATE_REVIEW } from "@/api/mutations/review"
import type { GqlReviewPage, GqlCreateReviewResult, CreateReviewInput } from "@/graphql/types"
import type { ReviewPage } from "@/features/reviews/types/review.types"

// Reading reviews is public — use the cookie-free client so the product detail
// Server Component that fetches them stays statically renderable.
export async function getReviewsByProduct(
  productId: string,
  pageSize = 10,
  nextToken?: string,
  init?: RequestInit,
): Promise<ReviewPage> {
  const data = await gqlPublic<{ reviewsByProduct: GqlReviewPage }>(
    GET_REVIEWS_BY_PRODUCT,
    { productId, pageSize, nextToken },
    init,
  )
  return data.reviewsByProduct
}

// Creating a review is an authenticated mutation (client-side) — keep the
// cookie-aware `gql` so the signed-in user's token is attached.
export async function createReview(input: CreateReviewInput): Promise<string> {
  const data = await gql<{ createReview: GqlCreateReviewResult }>(CREATE_REVIEW, { input })
  return data.createReview.id
}

export function formatReviewDate(isoString: string): string {
  return new Date(isoString).toLocaleDateString("pt-BR", {
    year: "numeric",
    month: "short",
    day: "numeric",
  })
}
