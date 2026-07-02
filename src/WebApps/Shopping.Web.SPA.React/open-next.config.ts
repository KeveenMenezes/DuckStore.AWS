import type { OpenNextConfig } from '@opennextjs/aws/types/open-next'

const config: OpenNextConfig = {
  default: {},
  // The image optimizer runs sharp. `open-next build` otherwise installs the
  // sharp binary for the *host* machine (e.g. darwin-arm64), which cannot load
  // on the Lambda, so Next silently falls back to serving the un-optimized
  // source. Force a real install for the Lambda's platform. Pin to 0.32.6 —
  // OpenNext's own default; sharp 0.33+ is known not to load in the bundled
  // image function. Platform must match spa-lambdas.ts (NODEJS_22_X + ARM_64).
  imageOptimization: {
    install: {
      packages: ['sharp@0.32.6'],
      arch: 'arm64',
      os: 'linux',
      libc: 'glibc',
      nodeVersion: '22',
    },
  },
}

export default config
