FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 6969

# Npgsql may load libgssapi_krb5 for GSSAPI/Kerberos; install it so PostgreSQL works in container.
# Kept in the base stage (before the app is copied) so it stays cached across code changes.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && \
    rm -rf /var/lib/apt/lists/*

# Create data directory. The image runs as root by default; if a USER is ever set,
# switch this to `chown $APP_UID:$APP_UID /app/data` so the app can still write here.
RUN mkdir -p /app/data && \
    chmod 755 /app/data

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BazaarCompanionWeb/BazaarCompanionWeb.csproj", "BazaarCompanionWeb/"]
RUN dotnet restore "BazaarCompanionWeb/BazaarCompanionWeb.csproj"
COPY . .
RUN dotnet publish "BazaarCompanionWeb/BazaarCompanionWeb.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "BazaarCompanionWeb.dll"]
