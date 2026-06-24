FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/GarageBalance.Api/GarageBalance.Api.csproj backend/GarageBalance.Api/
RUN dotnet restore backend/GarageBalance.Api/GarageBalance.Api.csproj

COPY backend/GarageBalance.Api/ backend/GarageBalance.Api/
RUN dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GarageBalance.Api.dll"]
