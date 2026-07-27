import { execSync } from 'child_process';
import { createHash } from 'crypto';
import { readdirSync, readFileSync, statSync } from 'fs';
import * as path from 'path';
import { AssetHashType, DockerImage } from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';

/** Every .NET Lambda here is arm64 — cheaper per GB-second, and what the ZIP is published for. */
export const DOTNET_ARCH = lambda.Architecture.ARM_64;

/**
 * net10.0 has no managed Lambda runtime, so the ZIP carries its own runtime (self-contained)
 * and runs on the custom runtime, which executes a file literally named `bootstrap` (ADR-0042).
 */
export const DOTNET_RUNTIME = lambda.Runtime.PROVIDED_AL2023;

/**
 * Memory for every .NET Lambda, in MB.
 *
 * Measured via AWS Lambda Power Tuning + CloudWatch Logs Insights on 2026-07-27 against
 * basket-checkout-basket, basket-merge-basket, and pricing-get-installment-plan (3 of the 6
 * synchronous, user-facing functions): cold start (Init Duration + first-invocation Duration)
 * at 256 MB ranged 670-730ms; at 512 MB it dropped to 380-420ms. Gains beyond 512 MB flatten
 * out (1024-3008 MB shaved only another ~100-150ms) for cost that no longer tracks the
 * improvement, so 512 is the balanced point, not the ceiling.
 *
 * Applied to every function, not just the three measured — including the 19 asynchronous ones,
 * where per ADR-0042 the latency is invisible to any user. That's a deliberate simplicity
 * tradeoff (one constant, same as before) over a measured-only per-function override; the
 * GB-second cost of doubling their memory is small enough in absolute terms not to matter.
 */
export const DOTNET_MEMORY_MB = 512;

const REPO_ROOT = path.join(__dirname, '..', '..');

/**
 * Referenced by tag, never built here.
 *
 * `DockerImage.fromBuild()` runs `docker build` at *construct* time, which happens while
 * `bin/app.ts` instantiates every stack — outside the gate CDK uses to skip bundling for stacks
 * that are not being deployed (`Stack.bundlingRequired`, from `aws:cdk:bundling-stacks`). That
 * made every workflow, including the ones deploying stacks with no .NET Lambda at all, build eight
 * arm64 images; on an x64 runner it failed outright with `exec format error`.
 * `fromRegistry` touches Docker only from inside the gated bundling path.
 */
const SDK_IMAGE = 'mcr.microsoft.com/dotnet/sdk:10.0';

/** The SDK image has no native linker; the ILCompiler needs clang and zlib to link. */
const AOT_TOOLCHAIN =
  'apt-get update -qq && apt-get install -y -qq --no-install-recommends clang zlib1g-dev >/dev/null';

/** Staged into the bundling container; mirrors what the old Dockerfiles chose to COPY. */
const ASSET_INCLUDES = ['Directory.Packages.props', 'nuget.config', 'src/BuildingBlocks'];

/**
 * Native AOT links a real native binary, so unlike ReadyToRun it cannot cross-compile — not
 * across architectures and not across operating systems. The build must therefore happen on
 * linux-arm64, which is why bundling defaults to the container even when a host SDK exists
 * (ADR-0042 §8).
 */
function publishCommand(project: string, outputDir: string): string {
  const publish = [
    'dotnet publish',
    project,
    '-c Release',
    '-r linux-arm64',
    '-p:PublishAot=true',
    // Referenced projects would otherwise drop their .pdb next to the binary.
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    `-o ${outputDir}`,
  ].join(' ');

  // AOT strips native symbols into bootstrap.dbg — ~25 MB that Lambda never reads and that
  // would nearly triple the uploaded package.
  return `${publish} && rm -f ${outputDir}/*.dbg ${outputDir}/*.pdb`;
}

/** True only where a native linux-arm64 link is possible without emulation. */
const hostCanBuildAot = () => process.platform === 'linux' && process.arch === 'arm64';

/**
 * Hashes only the inputs that can change the artifact: the service's own sources plus
 * BuildingBlocks and the central package pins. Hashing the repo root instead would rebuild and
 * redeploy all eight services on every unrelated commit.
 */
function sourceHash(serviceDir: string): string {
  const hash = createHash('sha256');

  const walk = (target: string) => {
    if (statSync(target).isFile()) {
      hash.update(target.replace(REPO_ROOT, '')).update(readFileSync(target));
      return;
    }
    for (const entry of readdirSync(target).sort()) {
      if (entry === 'bin' || entry === 'obj') continue;
      walk(path.join(target, entry));
    }
  };

  [serviceDir, ...ASSET_INCLUDES].forEach((relative) => walk(path.join(REPO_ROOT, relative)));
  return hash.digest('hex');
}

/**
 * Publishes a `*.Function` project into a ZIP asset shared by every Lambda in that service —
 * the artifact-level equivalent of the single Docker image the service used to build, with
 * ANNOTATIONS_HANDLER replacing the per-function `cmd` override.
 *
 * Bundling runs the host SDK only on a linux-arm64 machine — the one place a native
 * linux-arm64 link is possible without emulation — and otherwise uses the arm64 SDK container.
 * On Apple Silicon that container is native too, so a laptop build costs no emulation; an x64
 * CI runner would, which is why the deploy workflows moved to `ubuntu-24.04-arm` (ADR-0042 §8).
 *
 * @param serviceDir repo-relative directory holding the service, e.g. `src/Services/Catalog`
 * @param projectPath repo-relative path to the .csproj
 */
export function dotnetLambdaCode(serviceDir: string, projectPath: string): lambda.Code {
  return lambda.Code.fromAsset(REPO_ROOT, {
    assetHashType: AssetHashType.CUSTOM,
    assetHash: sourceHash(serviceDir),
    exclude: [
      '**',
      `!${serviceDir}/**`,
      ...ASSET_INCLUDES.map((include) => `!${include}${include.includes('.') ? '' : '/**'}`),
    ],
    bundling: {
      image: DockerImage.fromRegistry(SDK_IMAGE),
      platform: 'linux/arm64',
      // Root so the toolchain install works: CDK otherwise runs bundling as the calling uid,
      // which cannot apt-get. Outputs stay readable for the staging copy that follows.
      user: 'root',
      command: [
        'bash',
        '-c',
        `${AOT_TOOLCHAIN} && ${publishCommand(path.posix.join('/asset-input', projectPath), '/asset-output')}`,
      ],
      local: {
        tryBundle(outputDir: string): boolean {
          if (!hostCanBuildAot()) {
            return false;
          }
          try {
            execSync('dotnet --version', { stdio: 'ignore' });
          } catch {
            return false;
          }
          execSync(publishCommand(path.join(REPO_ROOT, projectPath), outputDir), {
            stdio: 'inherit',
          });
          return true;
        },
      },
    },
  });
}
