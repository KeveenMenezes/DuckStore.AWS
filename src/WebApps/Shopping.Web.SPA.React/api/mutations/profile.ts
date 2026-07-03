import { PROFILE_FIELDS } from '@/api/queries/profile'

export const UPDATE_PROFILE = `
  ${PROFILE_FIELDS}
  mutation UpdateProfile($input: UpdateProfileInput!) {
    updateProfile(input: $input) { ...ProfileFields }
  }
`
