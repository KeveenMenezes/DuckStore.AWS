import type { OpenNextConfig } from '@opennextjs/aws/types/open-next'

const config: OpenNextConfig = {
  default: {},
  // The image optimizer runs sharp. `open-next build` otherwise installs a
  // sharp binary for the *host* machine (e.g. darwin-arm64), which cannot load
  // on the Lambda. Force a real install for the Lambda's platform so the
  // function actually runs (NODEJS_22_X + ARM_64 in infra/constructs/spa-lambdas.ts).
  imageOptimization: {
    install: {
      packages: ['sharp@0.33.5'],
      arch: 'arm64',
      os: 'linux',
      libc: 'glibc',
      nodeVersion: '22',
    },
  },
}

export default config
