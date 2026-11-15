using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dsw2025Tpi.IntegrationTests.Controllers;

/// <summary>
/// Pruebas de integración de los endpoints de productos (CARD-TP4-03-B):
/// CA1 = creación de productos (Admin) y CA3 = listado/catálogo.
/// Todos los tests comparten la misma base SQLite, por eso cada uno usa datos únicos (SKU / nombre con Guid).
/// </summary>
[Collection("IntegrationTests")]
public class ProductsControllerTests
{
    private const string ProductsUrl = "/api/products";
    private readonly CustomWebApplicationFactory _factory;

    public ProductsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---------- Helpers ----------

    private static string UniqueTag() => Guid.NewGuid().ToString("N")[..8];

    private static ProductModel.Request NewProductRequest(string sku) =>
        // Orden del record: (Sku, InternalCode, Name, Description, CurrentUnitPrice, StockQuantity)
        new(sku, $"INT-{sku}", $"Teclado Mecanico {sku}", "Producto de prueba de integracion", 1500.50m, 10);

    // InternalCode es obligatorio en la base: siempre se siembra con valor
    private static Product NewProductEntity(string sku, string name, int stock = 10, bool isActive = true) =>
        new(sku, name, 1000m, stock, "Producto sembrado", $"INT-{sku}") { IsActive = isActive };

    private record ErrorResponse(string? Message);

    #region CA1 — Creación de productos (Admin)

    [Fact]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden()
    {
        // ARRANGE
        var sku = $"SKU-{UniqueTag()}";
        var request = NewProductRequest(sku);
        var anonymousClient = _factory.CreateClient();

        var customerClient = _factory.CreateClient();
        var (customerToken, _) = await AuthHelper.RegisterCustomerAsync(customerClient);
        customerClient.WithBearerToken(customerToken);

        // ACT
        var anonymousResponse = await anonymousClient.PostAsJsonAsync(ProductsUrl, request);
        var customerResponse = await customerClient.PostAsJsonAsync(ProductsUrl, request);

        // ASSERT
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized); // sin token
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);     // con token sin rol Admin

        var persisted = await _factory.QueryDbAsync(db => db.Set<Product>().AnyAsync(p => p.Sku == sku));
        persisted.Should().BeFalse();
    }

    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        // ARRANGE
        var sku = $"SKU-{UniqueTag()}";
        var request = NewProductRequest(sku);
        var client = _factory.CreateClient();
        client.WithBearerToken(await AuthHelper.GetAdminTokenAsync(client));

        // ACT
        var response = await client.PostAsJsonAsync(ProductsUrl, request);

        // ASSERT: respuesta HTTP
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>();
        body!.Sku.Should().Be(sku);

        // ASSERT: estado real en SQLite
        var saved = await _factory.QueryDbAsync(db => db.Set<Product>().SingleAsync(p => p.Sku == sku));
        saved.Id.Should().Be(body.Id);
        saved.Name.Should().Be(request.Name);
        saved.InternalCode.Should().Be(request.InternalCode);
        saved.CurrentUnitPrice.Should().Be(request.CurrentUnitPrice);
        saved.StockQuantity.Should().Be(request.StockQuantity);
        saved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
    {
        // ARRANGE: el SKU ya existe en SQLite
        var sku = $"SKU-{UniqueTag()}";
        await _factory.SeedAsync(NewProductEntity(sku, "Producto existente"));

        var client = _factory.CreateClient();
        client.WithBearerToken(await AuthHelper.GetAdminTokenAsync(client));

        // ACT
        var response = await client.PostAsJsonAsync(ProductsUrl, NewProductRequest(sku));

        // ASSERT: 409 o 400 (la API devuelve 400 por DuplicatedEntityException), nunca 500
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Message.Should().Contain("SKU");

        var count = await _factory.QueryDbAsync(db => db.Set<Product>().CountAsync(p => p.Sku == sku));
        count.Should().Be(1); // no se insertó un duplicado
    }

    #endregion

    #region CA3 — Listado de productos (catálogo público y endpoint de Admin)

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        // ARRANGE: al menos un producto activo, para que la API devuelva 200 y no 204
        var sku = $"SKU-{UniqueTag()}";
        await _factory.SeedAsync(NewProductEntity(sku, $"Mouse Publico {sku}"));
        var client = _factory.CreateClient(); // sin credenciales

        // ACT
        var response = await client.GetAsync(ProductsUrl);

        // ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.TryGetProperty("productItems", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        items.GetArrayLength().Should().BeGreaterThan(0);

        root.TryGetProperty("total", out var total).Should().BeTrue();
        total.GetInt32().Should().BeGreaterThan(0);

        // Cada ítem expone los campos del contrato
        var first = items[0];
        first.TryGetProperty("id", out _).Should().BeTrue();
        first.TryGetProperty("sku", out _).Should().BeTrue();
        first.TryGetProperty("name", out _).Should().BeTrue();
        first.TryGetProperty("currentUnitPrice", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        // ARRANGE: 3 productos con un tag único para no mezclarse con datos de otros tests.
        // Nota: en SQLite el filtro distingue mayúsculas/minúsculas, por eso se busca con las mismas mayúsculas.
        var tag = UniqueTag();
        await _factory.SeedAsync(
            NewProductEntity($"SKU-M-{tag}", $"Mouse Gamer {tag}"),
            NewProductEntity($"SKU-T-{tag}", $"Teclado Mecanico {tag}"),
            NewProductEntity($"SKU-P-{tag}", $"Monitor {tag}"));

        var client = _factory.CreateClient();
        var url = $"{ProductsUrl}?search={Uri.EscapeDataString($"Mouse Gamer {tag}")}&pageNumber=1&pageSize=10";

        // ACT
        var response = await client.GetAsync(url);

        // ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();

        body!.Total.Should().Be(1);
        body.ProductItems.Should().ContainSingle();
        body.ProductItems.Single().Name.Should().Be($"Mouse Gamer {tag}");
    }

    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        // ARRANGE: un producto activo y uno inactivo con el mismo tag
        var tag = UniqueTag();
        var activeSku = $"SKU-A-{tag}";
        var inactiveSku = $"SKU-I-{tag}";
        await _factory.SeedAsync(
            NewProductEntity(activeSku, $"Activo {tag}"),
            NewProductEntity(inactiveSku, $"Inactivo {tag}", isActive: false));

        var client = _factory.CreateClient();
        client.WithBearerToken(await AuthHelper.GetAdminTokenAsync(client));

        // ACT
        var response = await client.GetAsync($"{ProductsUrl}/admin?search={tag}&pageNumber=1&pageSize=10");

        // ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();

        body!.ProductItems.Should().HaveCount(2);
        body.ProductItems.Should().Contain(p => p.Sku == inactiveSku && !p.IsActive);
        body.ProductItems.Should().Contain(p => p.Sku == activeSku && p.IsActive);
    }

    #endregion
}
 