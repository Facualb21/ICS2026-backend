# syntax=docker/dockerfile:1

# ---------- Etapa 1: build / publish ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar solo los .csproj para aprovechar el cache de capas en el restore
COPY ["Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj", "Dsw2025Tpi.Api/"]
COPY ["Dsw2025Tpi.Application/Dsw2025Tpi.Application.csproj", "Dsw2025Tpi.Application/"]
COPY ["Dsw2025Tpi.Data/Dsw2025Tpi.Data.csproj", "Dsw2025Tpi.Data/"]
COPY ["Dsw2025Tpi.Domain/Dsw2025Tpi.Domain.csproj", "Dsw2025Tpi.Domain/"]

RUN dotnet restore "Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj"

# Copiar el resto del código y publicar en Release
COPY . .
RUN dotnet publish "Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------- Etapa 2: runtime liviano ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Valores por defecto; se pueden sobrescribir con -e / environment del host
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development

# Carpeta de logs escribible por el usuario no-root
RUN mkdir -p /app/Logs && chown -R app:app /app

EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "Dsw2025Tpi.Api.dll"]
