using System.Net;
using System.Net.Http.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dsw2025Tpi.IntegrationTests.Controllers;

/// <summary>
/// Pruebas de integración del endpoint de órdenes (CARD-TP4-03-B, CA2 — Creación de órdenes por el cliente).
/// Cada test siembra su propio producto y registra su propio cliente, para no depender de otros tests.
/// </summary>
[Collection("IntegrationTests")]
public class OrdersControllerTests
{
    private const string OrdersUrl = "/api/orders";
    private readonly CustomWebApplicationFactory _factory;

    public OrdersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---------- Helpers ----------

    private async Task<Product> SeedProductAsync(int stock, decimal price = 100m)
    {
        var sku = $"SKU-O-{Guid.NewGuid().ToString("N")[..8]}";
        // InternalCode es obligatorio en la base: siempre se siembra con valor
        var product = new Product(sku, $"Producto Orden {sku}", price, stock, "Producto para ordenes", $"INT-{sku}");
        await _factory.SeedAsync(product);
        return product;
    }

    private static OrderRequest NewOrderRequest(Guid customerId, Guid productId, int quantity) =>
        new(customerId, "Calle Falsa 123", "Calle Falsa 123", "Orden de prueba de integracion",
            new List<OrderItemRequest> { new(productId, quantity) });

    // Solo los campos que se verifican (Status viene como texto por JsonStringEnumConverter)
    private record OrderCreatedResponse(Guid OrderId, decimal TotalAmount);
    private record ErrorResponse(string? Message);

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // ARRANGE
        var product = await SeedProductAsync(stock: 10);
        var client = _factory.CreateClient(); // sin token
        var request = NewOrderRequest(Guid.NewGuid(), product.Id, 1);

        // ACT
        var response = await client.PostAsJsonAsync(OrdersUrl, request);

        // ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var stock = await _factory.QueryDbAsync(db =>
            db.Set<Product>().Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        stock.Should().Be(10); // el stock no se tocó
    }

    [Fact]
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        // ARRANGE: producto con stock 10 y un cliente registrado
        var product = await SeedProductAsync(stock: 10, price: 100m);
        var client = _factory.CreateClient();
        var (token, customerId) = await AuthHelper.RegisterCustomerAsync(client);
        client.WithBearerToken(token);

        // ACT: compra 3 unidades
        var response = await client.PostAsJsonAsync(OrdersUrl, NewOrderRequest(customerId, product.Id, 3));

        // ASSERT: respuesta HTTP
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<OrderCreatedResponse>();
        body!.TotalAmount.Should().Be(300m);

        // ASSERT: la orden quedó registrada en la tabla Orders con su ítem
        var order = await _factory.QueryDbAsync(db =>
            db.Set<Order>().Include(o => o.OrderItems).SingleAsync(o => o.Id == body.OrderId));
        order.CustomerId.Should().Be(customerId);
        order.TotalAmount.Should().Be(300m);
        order.OrderItems.Should().ContainSingle(i => i.ProductId == product.Id && i.Quantity == 3);

        // ASSERT: el stock real en SQLite bajó de 10 a 7
        var stock = await _factory.QueryDbAsync(db =>
            db.Set<Product>().Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        stock.Should().Be(7);
    }

    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        // ARRANGE: producto con stock 2 y un cliente registrado
        var product = await SeedProductAsync(stock: 2);
        var client = _factory.CreateClient();
        var (token, customerId) = await AuthHelper.RegisterCustomerAsync(client);
        client.WithBearerToken(token);

        // ACT: pide 5 unidades
        var response = await client.PostAsJsonAsync(OrdersUrl, NewOrderRequest(customerId, product.Id, 5));

        // ASSERT: 400 con mensaje JSON que explica la falta de stock
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Message.Should().Contain("stock");

        // ASSERT: no se creó la orden ni se modificó el stock
        var orderExists = await _factory.QueryDbAsync(db =>
            db.Set<Order>().AnyAsync(o => o.CustomerId == customerId));
        orderExists.Should().BeFalse();

        var stock = await _factory.QueryDbAsync(db =>
            db.Set<Product>().Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
        stock.Should().Be(2);
    }
}
