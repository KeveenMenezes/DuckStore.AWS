export const PROFILE_FIELDS = `
  fragment ProfileFields on UserProfile {
    userId email name phone addressLine city state zipCode country
  }
`

// No argument — the AppSync resolver derives the user from the Cognito token (ADR-0017).
export const GET_MY_PROFILE = `
  ${PROFILE_FIELDS}
  query GetMyProfile {
    myProfile { ...ProfileFields }
  }
`
