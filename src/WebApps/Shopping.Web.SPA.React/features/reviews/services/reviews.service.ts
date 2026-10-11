import { gql, gqlPublic } from "@/api"
import { GET_MY_REVIEW, GET_REVIEWS_BY_PRODUCT } from "@/api/queries/reviews"
import { CREATE_REVIEW, DELETE_REVIEW } from "@/api/mutations/review"
import type { GqlReview, GqlReviewPage, GqlCreateReviewResult, CreateReviewInput } from "@/graphql/types"
import type { MyReview, Review, ReviewPage } from "@/features/reviews/types/review.types"

/** Anti-corruption boundary: translates the wire-shaped `GqlReview` into the domain `Review`. */
function toReview(gql: GqlReview): Review {
  return {
    id: gql.id,
    productId: gql.productId,
    userName: gql.userName,
    rating: gql.rating,
    comment: gql.comment,
    createdAt: gql.createdAt,
  }
}

function toReviewPage(gql: GqlReviewPage): ReviewPage {
  return {
    items: gql.items.map(toReview),
    nextToken: gql.nextToken,
  }
}

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
  return toReviewPage(data.reviewsByProduct)
}

// Creating a review is an authenticated mutation (client-side) — keep the
// cookie-aware `gql` so the signed-in user's token is attached. userName is no longer part of
// the input (the server derives identity from the Cognito token) — the response returns the
// server-derived display name so callers don't have to assume it matches the local `user.name`.
export async function createReview(
  input: CreateReviewInput,
): Promise<{ id: string; userName: string }> {
  const data = await gql<{ createReview: GqlCreateReviewResult }>(CREATE_REVIEW, { input })
  return data.createReview
}

// The caller's own review row (authenticated, client-side). null = the customer never bought the
// product, so they can't review it (ADR-0049).
export async function getMyReview(productId: string): Promise<MyReview | null> {
  const data = await gql<{ myReview: GqlReview | null }>(GET_MY_REVIEW, { productId })
  return data.myReview ? { ...toReview(data.myReview), status: data.myReview.status } : null
}

// Withdraws the caller's published review; the row stays Deleted (re-publishable) until its TTL.
export async function deleteReview(productId: string): Promise<void> {
  await gql<{ deleteReview: { id: string } }>(DELETE_REVIEW, { productId })
}

export function formatReviewDate(isoString: string): string {
  return new Date(isoString).toLocaleDateString("pt-BR", {
    year: "numeric",
    month: "short",
    day: "numeric",
  })
}
