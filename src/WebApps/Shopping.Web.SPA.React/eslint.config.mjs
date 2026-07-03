import next from 'eslint-config-next'

// eslint-config-next 16 ships a native flat config (array), so no FlatCompat is needed.
const eslintConfig = [
  {
    ignores: [
      '.next/**',
      '.open-next/**',
      'node_modules/**',
      'components/ui/**', // shadcn/ui primitives — not hand-maintained (see CLAUDE.md)
    ],
  },
  ...next,
]

export default eslintConfig
