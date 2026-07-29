/**
 * Validates a caller-supplied post-login redirect target, returning it as a same-origin relative
 * path or null when it points anywhere else. Shared by /api/auth/login (which stores the target)
 * and /api/auth/callback (which acts on it) so the two can never drift apart.
 *
 * A regex over the raw string is not enough. The WHATWG URL parser normalizes backslashes to
 * forward slashes for special schemes, so `/\evil.com` — which passes a "starts with exactly one
 * slash" test — resolves to `https://evil.com/` the moment it reaches `new URL(target, siteUrl)`.
 * Parsing the target the same way the redirect will, and rejecting anything that escapes the
 * origin, is the only check that matches what actually happens.
 */
export function sanitizeReturnTo(target: string | null | undefined): string | null {
  if (!target) return null

  // Any origin works as a yardstick as long as it is unreachable: a target that stays on it is
  // relative by definition, and one that leaves it is absolute by definition. `.invalid` is
  // reserved by RFC 2606 precisely so it can never resolve.
  const sentinel = 'https://redirect-guard.invalid'

  try {
    const url = new URL(target, sentinel)
    if (url.origin !== sentinel) return null

    // Rebuilt from the parsed parts rather than returned verbatim, so the value handed to the
    // redirect is the normalized one that was actually validated.
    return `${url.pathname}${url.search}${url.hash}`
  } catch {
    return null
  }
}
