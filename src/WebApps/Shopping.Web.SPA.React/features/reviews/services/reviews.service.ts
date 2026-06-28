import { gql } from "@/api"
import { GET_REVIEWS_BY_PRODUCT } from "@/api/queries/reviews"
import { CREATE_REVIEW } from "@/api/mutations/review"
import type { GqlReviewPage, GqlCreateReviewResult, CreateReviewInput } from "@/graphql/types"
import type { Review, ReviewPage } from "@/features/reviews/types/review.types"

export async function getReviewsByProduct(
  productId: string,
  pageSize = 10,
  nextToken?: string,
  init?: RequestInit,
): Promise<ReviewPage> {
  const data = await gql<{ reviewsByProduct: GqlReviewPage }>(
    GET_REVIEWS_BY_PRODUCT,
    { productId, pageSize, nextToken },
    init,
  )
  return data.reviewsByProduct
}

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
