import { gql } from "@/api"
import { GET_MY_PROFILE } from "@/api/queries/profile"
import { UPDATE_PROFILE } from "@/api/mutations/profile"
import type { GqlUserProfile, UpdateProfileInput } from "@/graphql/types"

// The AppSync resolvers derive the user from the Cognito token (ADR-0017) — no id is sent.
export async function getMyProfile(): Promise<GqlUserProfile> {
  const data = await gql<{ myProfile: GqlUserProfile }>(GET_MY_PROFILE)
  return data.myProfile
}

export async function updateProfile(input: UpdateProfileInput): Promise<GqlUserProfile> {
  const data = await gql<{ updateProfile: GqlUserProfile }>(UPDATE_PROFILE, { input })
  return data.updateProfile
}
