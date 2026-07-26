import next from 'eslint-config-next'

// eslint-config-next 16 ships a native flat config (array), so no FlatCompat is needed.
const eslintConfig = [
  {
    ignores: [
      '.next/**',
      '.open-next/**',
      '.sst/**',
      'node_modules/**',
      'components/ui/**', // shadcn/ui primitives — not hand-maintained (see CLAUDE.md)
    ],
  },
  ...next,
  {
    // ADR-0041: lib/auth/ owns authentication state. Users were being logged out after an hour
    // because four modules independently read the Cognito token cookies and only one of them knew
    // refresh existed — a defect that reappears the moment a fifth reader is added. Tokens now
    // live server-side and these cookies no longer exist, so reading them is always a bug.
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector:
            'CallExpression[callee.property.name="get"] > Literal[value=/^(access_token|id_token|refresh_token)$/]',
          message:
            'Cognito tokens are not stored in cookies (ADR-0041). Resolve identity through getSession() from @/lib/auth/session.',
        },
      ],
    },
  },
  {
    // The one module allowed to hold auth state — it talks to Cognito and the session table.
    files: ['lib/auth/**'],
    rules: { 'no-restricted-syntax': 'off' },
  },
]

export default eslintConfig
