using System.Net;
using System.Net.Http.Json;
using Dsw2025Tpi.Application.Dtos;
using FluentAssertions;

namespace Dsw2025Tpi.IntegrationTests.E2E;

/// <summary>
/// Prueba End-to-End (CARD-TP4-05): recorre el flujo completo de compra tal como lo haría un usuario real,
/// encadenando llamadas HTTP a la API. NO accede directamente a la base de datos: cada paso usa
/// la respuesta del paso anterior y el resultado final se verifica consultando la propia API.
/// Flujo: Login admin → Crear producto → Listar y verificar → Registrar cliente → Crear orden → Verificar stock.
/// </summary>
[Collection("IntegrationTests")]
public class PurchaseFlowE2ETests
{
    private readonly CustomWebApplicationFactory _factory;

    public PurchaseFlowE2ETests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Solo los campos que el flujo necesita leer de cada respuesta
    private record LoginResponse(string Token);
    private record RegisterResponse(string Token, Guid? CustomerId);
    private record OrderCreatedResponse(Guid OrderId, decimal TotalAmount);

    [Fact]
    public async Task PurchaseFlow_AdminCreatesProduct_CustomerBuys_StockIsUpdated()
    {
        // ARRANGE: dos "usuarios" independientes y datos únicos para no chocar con otros tests
        var adminClient = _factory.CreateClient();
        var customerClient = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sku = $"E2E-{suffix}";
        const int initialStock = 10;
        const int quantityToBuy = 3;
        const decimal unitPrice = 250m;

        // PASO 1 — El administrador inicia sesión
        var loginResponse = await adminClient.PostAsJsonAsync("/api/auth/login", new
        {
            username = CustomWebApplicationFactory.AdminUsername,
            password = CustomWebApplicationFactory.AdminPassword
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        login!.Token.Should().NotBeNullOrWhiteSpace();
        adminClient.WithBearerToken(login.Token);

        // PASO 2 — El administrador crea un producto con stock 10
        // Orden del record: (Sku, InternalCode, Name, Description, CurrentUnitPrice, StockQuantity)
        var productRequest = new ProductModel.Request(
            sku, $"INT-{sku}", $"Auriculares {sku}", "Producto creado en la prueba E2E", unitPrice, initialStock);

        var createProductResponse = await adminClient.PostAsJsonAsync("/api/products", productRequest);
        createProductResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProduct = await createProductResponse.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>();
        var productId = createdProduct!.Id;

        // PASO 3 — Un visitante (sin login) busca el producto en el catálogo público y lo encuentra
        var catalogResponse = await customerClient.GetAsync($"/api/products?search={sku}&pageNumber=1&pageSize=10");
        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        catalog!.ProductItems.Should().ContainSingle(p => p.Id == productId && p.StockQuantity == initialStock);

        // PASO 4 — El visitante se registra como cliente
        var registerResponse = await customerClient.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"e2e_{suffix}",
            password = "Cliente123!",
            email = $"e2e_{suffix}@test.com"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterResponse>();
        registered!.Token.Should().NotBeNullOrWhiteSpace();
        registered.CustomerId.Should().NotBeNull();
        customerClient.WithBearerToken(registered.Token);

        // PASO 5 — El cliente compra 3 unidades
        var orderRequest = new OrderRequest(
            registered.CustomerId!.Value,
            "Av. Siempre Viva 742",
            "Av. Siempre Viva 742",
            "Compra realizada en la prueba E2E",
            new List<OrderItemRequest> { new(productId, quantityToBuy) });

        var orderResponse = await customerClient.PostAsJsonAsync("/api/orders", orderRequest);
        orderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        orderResponse.Headers.Location.Should().NotBeNull();
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderCreatedResponse>();
        order!.OrderId.Should().NotBe(Guid.Empty);
        order.TotalAmount.Should().Be(unitPrice * quantityToBuy); // 750

        // PASO 6 — Se consulta el producto por la API y el stock bajó de 10 a 7
        var productResponse = await customerClient.GetAsync($"/api/products/{productId}");
        productResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var productAfterPurchase = await productResponse.Content.ReadFromJsonAsync<ProductModel.ResponseProduct>();
        productAfterPurchase!.StockQuantity.Should().Be(initialStock - quantityToBuy); // 7
    }
}