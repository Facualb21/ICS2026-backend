# Dockerización de la API (.NET 8)

Guía para construir, ejecutar y verificar localmente la API `Dsw2025Tpi.Api` dentro de un
contenedor Docker. Corresponde al **Punto 2** del TP (Dockerfile multi-stage, inyección de
variables de entorno y verificación local).

## Estructura del Dockerfile

El `Dockerfile` usa **multi-stage** para separar la compilación de la ejecución:

| Etapa | Imagen | Rol |
|-------|--------|-----|
| `build` | `mcr.microsoft.com/dotnet/sdk:8.0` | Restaura, compila y publica en `/app/publish` |
| `runtime` | `mcr.microsoft.com/dotnet/aspnet:8.0` | Imagen liviana que solo ejecuta la API publicada |

Buenas prácticas aplicadas:

- Se copian **primero los `.csproj`** y se corre `dotnet restore` para aprovechar el cache de capas.
- Se publica con `-c Release` y `/p:UseAppHost=false` (binario administrado, óptimo para contenedores).
- La imagen final **no incluye el SDK** (más liviana).
- Corre con el **usuario no-root** `app` y con `/app/Logs` escribible.
- Expone el puerto **8080**.

## Variables de entorno

Los valores por defecto del contenedor se pueden **sobrescribir** con `-e` (o desde el `environment`
de Docker Compose / Azure). La configuración de .NET mapea las claves con doble guion bajo (`__`).

| Variable | Default en Dockerfile | Descripción |
|----------|-----------------------|-------------|
| `ASPNETCORE_URLS` | `http://+:8080` | URL/puerto donde escucha Kestrel |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Entorno (`Development` habilita Swagger) |
| `ConnectionStrings__Dsw2025TpiEntities` | *(de `appsettings.json`)* | Cadena de conexión a SQL Server |
| `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience` | *(de `appsettings.json`)* | Configuración del JWT |
| `LogPath` | *(de `appsettings.json`)* | Ruta del log de archivo (`Logs/api-log.txt`) |

> La cadena de `appsettings.json` apunta a `(localdb)\MSSQLLocalDB`, que **no existe en Linux**.
> Para usar la base de datos dentro del contenedor hay que inyectar una cadena válida por
> `ConnectionStrings__Dsw2025TpiEntities`.

## Construcción de la imagen

Desde la raíz del repositorio:

```bash
docker build -t ics2026-api:local .
```

## Ejecución local (sin base de datos)

La API arranca aunque la base de datos no esté disponible (el seed está protegido con `try/catch`),
por lo que se puede verificar el arranque, el health check y Swagger:

```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ASPNETCORE_URLS=http://+:8080 \
  ics2026-api:local
```

## Verificación local

Con el contenedor en ejecución:

```bash
# Health check -> debe devolver "Healthy"
curl http://localhost:8080/healthcheck

# Swagger UI (disponible porque el entorno es Development)
# Abrir en el navegador:
#   http://localhost:8080/swagger
```

Resultado esperado:

- `GET /healthcheck` responde `Healthy`.
- `GET /swagger` muestra la interfaz de Swagger.
- Los endpoints que dependen de la base de datos (Products / Orders) fallarán mientras no se
  configure una cadena de conexión válida; esto es esperado en esta etapa.

## Ejecución con base de datos (opcional)

Para probar los endpoints reales se necesita un SQL Server accesible desde el contenedor y
aplicar las migraciones. Ejemplo con la cadena inyectada:

```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ConnectionStrings__Dsw2025TpiEntities="Server=<host>,1433;Database=Dsw2025Tpi;User Id=sa;Password=<password>;TrustServerCertificate=True" \
  ics2026-api:local
```

Y aplicar las migraciones contra esa base (por única vez):

```bash
dotnet ef database update --project Dsw2025Tpi.Data --startup-project Dsw2025Tpi.Api
```

## Comandos útiles

```bash
# Listar imágenes construidas
docker images | grep ics2026-api

# Detener un contenedor en ejecución
docker stop <container_id>

# Inspeccionar logs del contenedor
docker logs <container_id>
```
