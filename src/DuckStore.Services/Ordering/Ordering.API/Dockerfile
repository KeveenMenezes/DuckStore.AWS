FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DuckStore.Services/Ordering/Ordering.API/Ordering.API.csproj", "DuckStore.Services/Ordering/Ordering.API/"]
COPY ["DuckStore.Services/Ordering/Ordering.Application/Ordering.Application.csproj", "DuckStore.Services/Ordering/Ordering.Application/"]
COPY ["DuckStore.BuildingBlocks/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj", "DuckStore.BuildingBlocks/BuildingBlocks.Messaging/"]
COPY ["DuckStore.BuildingBlocks/BuildingBlocks/BuildingBlocks.csproj", "DuckStore.BuildingBlocks/BuildingBlocks/"]
COPY ["DuckStore.Services/Ordering/Ordering.Domain/Ordering.Domain.csproj", "DuckStore.Services/Ordering/Ordering.Domain/"]
COPY ["DuckStore.Services/Ordering/Ordering.Infrastructure/Ordering.Infrastructure.csproj", "DuckStore.Services/Ordering/Ordering.Infrastructure/"]
RUN dotnet restore "./DuckStore.Services/Ordering/Ordering.API/./Ordering.API.csproj"
COPY . .
WORKDIR "/src/DuckStore.Services/Ordering/Ordering.API"
RUN dotnet build "./Ordering.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ordering.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ordering.API.dll"]
