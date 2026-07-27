# Build toolchain for the Lambda ZIPs — not a deployment artifact.
#
# The .NET SDK image has no native linker, and the ILCompiler needs one: Native AOT emits object
# files that clang links against zlib. Installing them here instead of inside the bundling command
# means the layer is cached once rather than re-run for each of the eight services, and it does not
# need root at bundle time (CDK runs bundling as the calling user).
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# dotnet writes its CLI home and NuGet cache under $HOME; bundling runs as an arbitrary uid that
# has no home directory, so point both at a world-writable path.
ENV DOTNET_CLI_HOME=/tmp \
    XDG_DATA_HOME=/tmp \
    NUGET_PACKAGES=/tmp/nuget \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
