# Build context is the repository root (see infra/constructs/catalog-lambdas.ts)
# so Directory.Packages.props, nuget.config, and the BuildingBlocks project
# references resolve the same way they do for a normal `dotnet build`.

FROM public.ecr.aws/lambda/dotnet:10 AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
