import type { OpenNextConfig } from '@opennextjs/aws/types/open-next'

const config: OpenNextConfig = {
  default: {},
  // The image optimizer runs sharp. OpenNext's DEFAULT install (sharp@0.32.6)
  // is broken for cross-builds from a Mac: 0.32.x uses prebuild-install, which
  // reads `npm_config_platform`, but OpenNext only passes `--os` — so it always
  // fetches the *host* (darwin) binary, which can't load on the Lambda and
  // makes Next silently serve the un-optimized source. sharp 0.33+ ships
  // `@img/sharp-*` optional packages that npm filters by `--os/--arch/--libc`,
  // so this pin actually produces a linux-arm64 binary. Platform must match
  // spa-lambdas.ts (NODEJS_22_X + ARM_64).
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
