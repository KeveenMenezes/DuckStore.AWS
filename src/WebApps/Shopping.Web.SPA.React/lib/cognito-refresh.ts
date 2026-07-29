// Cognito token endpoint calls. Callers live in lib/auth/ only — session state and the decision
// of *when* to refresh belong to lib/auth/session.ts (ADR-0041); this module just talks to Cognito.

export type RefreshedTokens = {
  accessToken: string
  idToken: string
  expiresIn: number
  /**
   * Present only when refresh token rotation is enabled on the user pool client
   * (`refreshTokenRotationGracePeriod`, infra/constructs/appsync-auth.ts). With rotation on, the
   * token used for this exchange is invalidated once the grace period lapses, so this value MUST
   * be persisted — dropping it silently kills the session a few seconds later.
   */
  refreshToken?: string
}

/**
 * The three outcomes a refresh can have, kept distinct because the caller's reaction to each is
 * irreversible in one case: only `invalid_grant` justifies ending the session.
 */
export type RefreshOutcome =
  /** New tokens in hand. */
  | { status: 'refreshed'; tokens: RefreshedTokens }
  /** Cognito actively rejected the refresh token: expired, revoked, or the user was disabled. */
  | { status: 'invalid_grant' }
  /** Cognito was throttled, unreachable, or misconfigured — it said nothing about the token. */
  | { status: 'transient' }

/**
 * Exchanges a refresh token for a new access/id token pair via Cognito's /oauth2/token endpoint.
 *
 * Any failure that is not `invalid_grant` is reported as transient. A 429, a 5xx, or a dropped
 * connection carries no information about whether the refresh token is still valid, so collapsing
 * them into "the session is over" would end a 30-day session on a momentary blip — the caller
 * deletes the session record on that signal and cannot undo it.
 */
export async function refreshAccessToken(refreshToken: string): Promise<RefreshOutcome> {
  let res: Response

  try {
    res = await fetch(`${process.env.COGNITO_HOSTED_UI_URL}/oauth2/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'refresh_token',
        client_id: process.env.COGNITO_CLIENT_ID!,
        refresh_token: refreshToken,
      }),
    })
  } catch {
    return { status: 'transient' }
  }

  if (!res.ok) {
    // OAuth 2.0 (RFC 6749 §5.2) puts the machine-readable reason in the body, not the status:
    // invalid_grant arrives as a 400, the same status as a malformed request.
    const body = (await res.json().catch(() => null)) as { error?: string } | null
    return body?.error === 'invalid_grant' ? { status: 'invalid_grant' } : { status: 'transient' }
  }

  const { access_token, id_token, expires_in, refresh_token } = (await res.json()) as {
    access_token: string
    id_token: string
    expires_in: number
    refresh_token?: string
  }

  return {
    status: 'refreshed',
    tokens: {
      accessToken: access_token,
      idToken: id_token,
      expiresIn: expires_in,
      refreshToken: refresh_token,
    },
  }
}

/**
 * Best-effort revocation at the IdP. The authoritative logout is deleting the session record —
 * this only ensures the refresh token can't be replayed against Cognito directly.
 */
export async function revokeRefreshToken(refreshToken: string): Promise<void> {
  try {
    await fetch(`${process.env.COGNITO_HOSTED_UI_URL}/oauth2/revoke`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        token: refreshToken,
        client_id: process.env.COGNITO_CLIENT_ID!,
      }),
    })
  } catch {
    // Cognito unreachable — the session record is deleted regardless, so access is already gone.
  }
}
