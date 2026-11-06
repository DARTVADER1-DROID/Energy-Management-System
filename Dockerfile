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

# Keep the final image small: only the .NET 10 runtime and published application.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

COPY --from=dotnet-build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "EnergyManagement.Api.dll"]
