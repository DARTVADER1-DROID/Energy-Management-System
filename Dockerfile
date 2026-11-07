# Build the Tailwind stylesheet with the same Node version used by CI.
FROM node:22-alpine AS frontend-build
WORKDIR /src

COPY package.json ./
RUN npm install --no-audit --no-fund

COPY . .
RUN npm run build

# Restore and publish the ASP.NET Core application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

COPY . .
COPY --from=frontend-build /src/src/EnergyManagement.Api/wwwroot/css/site.css ./src/EnergyManagement.Api/wwwroot/css/site.css

RUN dotnet restore src/EnergyManagement.Api/EnergyManagement.Api.csproj
RUN dotnet publish src/EnergyManagement.Api/EnergyManagement.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Install the GSSAPI runtime library used by Npgsql's optional GSS/Kerberos negotiation.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

COPY --from=dotnet-build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "EnergyManagement.Api.dll"]
