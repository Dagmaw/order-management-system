FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore src/OrderManagement.API/OrderManagement.API.csproj

RUN dotnet publish src/OrderManagement.API/OrderManagement.API.csproj \
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

RUN chmod 755 /app/certs

ENV PGSSLROOTCERT=/app/certs/global-bundle.pem
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

RUN test -r /app/certs/global-bundle.pem

ENTRYPOINT ["dotnet", "OrderManagement.API.dll"]
