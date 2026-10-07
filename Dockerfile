# ==========================================
# ETAPA 1: BUILD (Compilación y Publicación)
# ==========================================
# Usamos la imagen oficial del SDK de .NET 8.0 para compilar la aplicación.
# Esta imagen es pesada porque incluye todas las herramientas de desarrollo.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. Copiamos los archivos de los proyectos (.csproj) para aprovechar el caché de Docker
# Se copian respetando la estructura de carpetas que me mostraste
COPY ["Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj", "Dsw2025Tpi.Api/"]
COPY ["Dsw2025Tpi.Application/Dsw2025Tpi.Application.csproj", "Dsw2025Tpi.Application/"]
COPY ["Dsw2025Tpi.Data/Dsw2025Tpi.Data.csproj", "Dsw2025Tpi.Data/"]
COPY ["Dsw2025Tpi.Domain/Dsw2025Tpi.Domain.csproj", "Dsw2025Tpi.Domain/"]

# 2. Restauramos las dependencias (Nuget)
RUN dotnet restore "Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj"

# 3. Copiamos el resto del código fuente del repositorio
COPY . .

# 4. Compilamos y publicamos el proyecto principal (Dsw2025Tpi.Api) en modo Release
WORKDIR "/src/Dsw2025Tpi.Api"
RUN dotnet publish "Dsw2025Tpi.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false


# ==========================================
# ETAPA 2: RUNTIME (Ejecución Liviana)
# ==========================================
# Usamos la imagen oficial de ASP.NET Core Runtime 8.0.
# Es una imagen muy liviana porque solo tiene lo necesario para correr la app (sin SDK).
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Exponemos el puerto estándar 8080 en el que escucha .NET 8 por defecto
EXPOSE 8080

# Configuramos variables de entorno requeridas por la cátedra
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Copiamos ÚNICAMENTE los archivos publicados desde la etapa 'build' anterior
COPY --from=build /app/publish .

# Definimos el comando que ejecutará Docker al iniciar el contenedor
ENTRYPOINT ["dotnet", "Dsw2025Tpi.Api.dll"]