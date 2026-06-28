/**
 * Resolves the Cognito ID token for the current request context.
 *
 * In DEV the local GraphQL handler requires no auth, so this returns undefined.
 *
 * In PROD, replace this with real token retrieval:
 *   - Server components: read from the httpOnly cookie set by Cognito Hosted UI
 *     (use `cookies()` from `next/headers` and extract the CognitoIdentityServiceProvider token).
 *   - Client components: read from memory / React context populated after the
 *     authorization code exchange (never from localStorage).
 *
 * The graphql-client never touches auth directly — it only receives the resolved token here.
 */
export async function getAuthToken(): Promise<string | undefined> {
  return undefined
}
