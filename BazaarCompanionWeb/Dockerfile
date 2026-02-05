FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 6969

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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

# Npgsql may load libgssapi_krb5 for GSSAPI/Kerberos; install it so PostgreSQL works in container
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && \
    rm -rf /var/lib/apt/lists/*

# Create data directory with proper permissions
# The .NET base image runs as a non-root user (typically UID 1000)
RUN mkdir -p /app/data && \
    chmod 755 /app/data

ENTRYPOINT ["dotnet", "BazaarCompanionWeb.dll"]
