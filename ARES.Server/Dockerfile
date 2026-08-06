FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ARES.Shared/ARES.Shared.csproj ARES.Shared/
COPY ARES.Server/ARES.Server.csproj ARES.Server/
RUN dotnet restore ARES.Server/ARES.Server.csproj
COPY ARES.Shared/ ARES.Shared/
COPY ARES.Server/ ARES.Server/
RUN dotnet publish ARES.Server/ARES.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
# Render limita las instancias inotify. Usar sondeo evita que .NET cree
# observadores de archivos durante el inicio de la configuracion.
ENV DOTNET_USE_POLLING_FILE_WATCHER=1
EXPOSE 8080
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "ARES.Server.dll"]
