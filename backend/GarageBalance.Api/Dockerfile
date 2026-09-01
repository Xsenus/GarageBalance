FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /src

COPY backend/GarageBalance.Api/GarageBalance.Api.csproj backend/GarageBalance.Api/
COPY backend/GarageBalance.Api/packages.lock.json backend/GarageBalance.Api/
RUN dotnet restore backend/GarageBalance.Api/GarageBalance.Api.csproj --locked-mode

COPY backend/GarageBalance.Api/ backend/GarageBalance.Api/
RUN dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY infrastructure/deployment/postgresql-archive-keyring.asc /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
RUN echo "0144068502a1eddd2a0280ede10ef607d1ec592ce819940991203941564e8e76  /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc" | sha256sum --check --strict \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        curl=8.5.0-2ubuntu10.13 \
        libpq5=18.4-1.pgdg24.04+1 \
        mdbtools=1.0.0+dfsg-1.2ubuntu1 \
        postgresql-client-common=293.pgdg24.04+1 \
        postgresql-client-17=17.10-1.pgdg24.04+1 \
    && rm -rf /var/lib/apt/lists/* /var/cache/apt/* /var/log/apt/* /var/log/dpkg.log
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GarageBalance.Api.dll"]
