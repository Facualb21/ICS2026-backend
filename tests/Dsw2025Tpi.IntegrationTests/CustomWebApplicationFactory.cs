using Dsw2025Tpi.Api;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.IntegrationTests;

/// <summary>
/// Levanta la API en memoria (TestServer) reemplazando SQL Server por dos bases SQLite en memoria,
/// una por cada DbContext. Se comparte entre todos los tests de la colección "IntegrationTests".
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsername = "admin.test";
    public const string AdminEmail = "admin.test@ecommerce.test";
    public const string AdminPassword = "Admin123!"; // cumple la política de Identity (8+, mayús., minús., dígito y símbolo)

    // SQLite en memoria compartida (Mode=Memory;Cache=Shared), como indica la guía de la cátedra,
    // con una conexión abierta para que la base viva durante toda la sesión de pruebas.
    // Son DOS bases separadas (una por DbContext) porque si los dos contextos comparten una,
    // EnsureCreated() del segundo no crea sus tablas.
    private readonly SqliteConnection _mainConnection = new("DataSource=Dsw2025TpiMain;Mode=Memory;Cache=Shared");
    private readonly SqliteConnection _authConnection = new("DataSource=Dsw2025TpiAuth;Mode=Memory;Cache=Shared");

    public CustomWebApplicationFactory()
    {
        _mainConnection.Open();
        _authConnection.Open();

        // El esquema se crea ANTES de que arranque la API, porque Program.cs siembra roles y clientes al iniciar
        // (dentro de un try/catch que oculta los errores si las tablas no existen).
        // Se usa EnsureCreated() y no migraciones porque las migraciones están generadas para SQL Server.
        using (var main = new Dsw2025TpiContext(
                   new DbContextOptionsBuilder<Dsw2025TpiContext>().UseSqlite(_mainConnection).Options))
        {
            main.Database.EnsureCreated();
        }

        using (var auth = new AuthenticateContext(
                   new DbContextOptionsBuilder<AuthenticateContext>().UseSqlite(_authConnection).Options))
        {
            auth.Database.EnsureCreated();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // EF Core 9 (el que usa el proyecto Data): además de DbContextOptions<T> hay que quitar
            // IDbContextOptionsConfiguration<T>; si no, quedan registrados SQL Server y SQLite a la vez.
            services.RemoveAll<DbContextOptions<Dsw2025TpiContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<Dsw2025TpiContext>>();
            services.RemoveAll<DbContextOptions<AuthenticateContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthenticateContext>>();

            services.AddDbContext<Dsw2025TpiContext>(options => options.UseSqlite(_mainConnection));
            services.AddDbContext<AuthenticateContext>(options => options.UseSqlite(_authConnection));
        });
    }

    // xUnit lo llama una vez, después de crear la factory. Acceder a Services arranca la API
    // (y con ella el seed de roles de Program.cs); después sembramos el admin de prueba,
    // porque admins.json no está en el repositorio.
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        if (await userManager.FindByNameAsync(AdminUsername) is null)
        {
            var admin = new IdentityUser { UserName = AdminUsername, Email = AdminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, AdminPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "No se pudo crear el admin de prueba: " + string.Join(", ", result.Errors.Select(e => e.Description)));
            }
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _mainConnection.Dispose();
            _authConnection.Dispose();
        }
    }

    // ---------- Helpers de datos para los tests (los usa la card 03-B) ----------

    /// <summary>Inserta entidades directamente en la base SQLite de negocio.</summary>
    public async Task SeedAsync(params object[] entities)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    /// <summary>Consulta la base SQLite de negocio para verificar el estado final de un test.</summary>
    public async Task<T> QueryDbAsync<T>(Func<Dsw2025TpiContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        return await query(db);
    }

    /// <summary>Borra órdenes y productos (no toca usuarios ni clientes) para evitar colisiones entre tests.</summary>
    public async Task ResetBusinessDataAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        await db.Set<OrderItem>().ExecuteDeleteAsync();
        await db.Set<Order>().ExecuteDeleteAsync();
        await db.Set<Product>().ExecuteDeleteAsync();
    }
}
