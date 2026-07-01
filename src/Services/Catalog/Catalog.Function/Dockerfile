# Build context is the repository root (see infra/constructs/catalog-lambdas.ts)
# so Directory.Packages.props, nuget.config, and the BuildingBlocks project
# references resolve the same way they do for a normal `dotnet build`.

FROM public.ecr.aws/lambda/dotnet:10 AS base

# Build on the runner's native arch (BUILDPLATFORM) and cross-compile to
# linux-arm64. This avoids running an arm64 dotnet SDK under emulation on an
# x86 host (GitHub runners are x86_64) — which fails with "exec format error".
# Only the final `base` stage is arm64, and it merely COPYs artifacts (no RUN),
# so no arm64 binary executes at build time.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

COPY Directory.Packages.props nuget.config ./
COPY src/Services/Catalog/Catalog.Function/ src/Services/Catalog/Catalog.Function/
COPY src/BuildingBlocks/ src/BuildingBlocks/
RUN dotnet publish src/Services/Catalog/Catalog.Function/Catalog.Function.csproj \
    -c Release -r linux-arm64 --self-contained false \
    -o /app/publish

FROM base
WORKDIR /var/task
COPY --from=build /app/publish .
