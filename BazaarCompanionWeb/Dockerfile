FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 6969

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BazaarCompanionWeb/BazaarCompanionWeb.csproj", "BazaarCompanionWeb/"]
RUN dotnet restore "BazaarCompanionWeb/BazaarCompanionWeb.csproj"
COPY . .
WORKDIR "/src/BazaarCompanionWeb"
RUN dotnet build "BazaarCompanionWeb.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BazaarCompanionWeb.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create data directory with proper permissions
# The .NET base image runs as a non-root user (typically UID 1000)
RUN mkdir -p /app/data && \
    chmod 755 /app/data

ENTRYPOINT ["dotnet", "BazaarCompanionWeb.dll"]
