# ==========================================
# ETAPA 1: Runtime
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# ==========================================
# ETAPA 2: Build
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar archivos .csproj
COPY ["src/Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj", "src/Dsw2025Tpi.Api/"]
COPY ["src/Dsw2025Tpi.Application/Dsw2025Tpi.Application.csproj", "src/Dsw2025Tpi.Application/"]
COPY ["src/Dsw2025Tpi.Data/Dsw2025Tpi.Data.csproj", "src/Dsw2025Tpi.Data/"]

COPY ["src/Dsw2025Tpi.Domain/Dsw2025Tpi.Domain.csproj", "src/Dsw2025Tpi.Domain/"]

RUN dotnet restore "src/Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj"

# Copiar todo el código fuente
COPY . .
WORKDIR "/src/src/Dsw2025Tpi.Api"
RUN dotnet build "Dsw2025Tpi.Api.csproj" -c Release -o /app/build

# ==========================================
# ETAPA 3: Publish
# ==========================================
FROM build AS publish
RUN dotnet publish "Dsw2025Tpi.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# ETAPA 4: Final
# ==========================================
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "Dsw2025Tpi.Api.dll"]