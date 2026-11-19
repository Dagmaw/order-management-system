# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .

RUN dotnet restore \
    src/OrderManagement.DatabaseSetup/OrderManagement.DatabaseSetup.csproj

RUN dotnet publish \
    src/OrderManagement.DatabaseSetup/OrderManagement.DatabaseSetup.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ADD --chmod=444 \
    https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem \
    /app/certs/global-bundle.pem

ENV PGSSLROOTCERT=/app/certs/global-bundle.pem

USER $APP_UID

ENTRYPOINT ["dotnet", "OrderManagement.DatabaseSetup.dll"]