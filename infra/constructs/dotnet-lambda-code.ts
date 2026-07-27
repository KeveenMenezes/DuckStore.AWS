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
 * 512 was sized for the container era, when the package carried a full CoreCLR. Native AOT roughly
 * halved the runtime footprint — published benchmarks put a comparable .NET 10 AOT Lambda at
 * 42–48 MB peak against 88–93 MB for the JIT build — so 256 leaves a wide margin and halves the
 * GB-second bill (ADR-0042 §9).
 *
 * Memory is also the CPU knob on Lambda: halving it slows init and execution. That trade is free
 * for the 19 asynchronous functions and real for the 6 synchronous ones, and it has not been
 * measured on this workload. `Max Memory Used` and `Billed Duration` in CloudWatch are the numbers
 * that settle it; raising this constant is the rollback.
 */
export const DOTNET_MEMORY_MB = 256;

const REPO_ROOT = path.join(__dirname, '..', '..');

/** SDK plus the clang/zlib toolchain the ILCompiler links against; see the Dockerfile. */
const AOT_BUILD_IMAGE = path.join(__dirname, '..', 'docker', 'dotnet-aot');

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
      image: DockerImage.fromBuild(AOT_BUILD_IMAGE, { platform: 'linux/arm64' }),
      platform: 'linux/arm64',
      command: [
        'bash',
        '-c',
        publishCommand(path.posix.join('/asset-input', projectPath), '/asset-output'),
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
