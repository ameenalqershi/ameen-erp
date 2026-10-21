FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/Ameen.Erp.Api/Ameen.Erp.Api.csproj --locked-mode
RUN dotnet publish src/Ameen.Erp.Api/Ameen.Erp.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8443
ENV ASPNETCORE_URLS=https://+:8443
ENTRYPOINT ["dotnet", "Ameen.Erp.Api.dll"]
