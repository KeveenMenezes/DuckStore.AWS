import type { OpenNextConfig } from '@opennextjs/aws/types/open-next'

const config: OpenNextConfig = {
  // Enables Lambda response streaming (SST wires the Function URL to
  // invokeMode=RESPONSE_STREAM off this). Without it, OpenNext buffers the
  // whole SSR response server-side, so a route's loading.tsx Suspense
  // fallback (e.g. app/challenges/loading.tsx) can never flush early — the
  // browser sees nothing until the entire page, including all data fetches,
  // is done.
  default: {
    override: {
      wrapper: "aws-lambda-streaming",
    },
  },
  // The image optimizer runs sharp. OpenNext's DEFAULT install (sharp@0.32.6)
  // is broken for cross-builds from a Mac: 0.32.x uses prebuild-install, which
  // reads `npm_config_platform`, but OpenNext only passes `--os` — so it always
  // fetches the *host* (darwin) binary, which can't load on the Lambda and
  // makes Next silently serve the un-optimized source. sharp 0.33+ ships
  // `@img/sharp-*` optional packages that npm filters by `--os/--arch/--libc`,
  // so this pin actually produces a linux-arm64 binary. Platform must match
  // spa-lambdas.ts (NODEJS_22_X + ARM_64).
  //
  // `arch: 'arm64'` above is what OpenNext's installer *documents*, but its
  // installDeps.js (@opennextjs/aws@4.0.3) turns it into an `--arch=arm64`
  // npm flag that doesn't exist (npm only understands `--cpu` for this) — npm
  // prints "Unknown cli config \"--arch\"" and silently installs for the
  // *host*'s own CPU instead. On an Apple Silicon dev machine that accidentally
  // "works" (host already is arm64), which is why this went unnoticed locally;
  // on the GitHub Actions runner (ubuntu-latest, x64) it silently installed
  // sharp-linux-x64 into an arm64 Lambda, which fails to load, and — same
  // failure mode as the paragraph above — Next just serves the un-optimized
  // source with no error surfaced anywhere. additionalArgs passes the npm flag
  // that actually works, alongside the (harmless but ignored) arch option.
  imageOptimization: {
    install: {
      packages: ['sharp@0.33.5'],
      arch: 'arm64',
      os: 'linux',
      libc: 'glibc',
      nodeVersion: '22',
      additionalArgs: '--cpu=arm64',
    },
  },
}

export default config
