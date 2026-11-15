using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Dsw2025Tpi.IntegrationTests.Controllers;

/// <summary>
/// Tests de humo del entorno de integración: comprueban que la API levanta sobre SQLite,
/// que el admin de prueba existe y que los tokens JWT generados son aceptados.
/// </summary>
[Collection("IntegrationTests")]
public class AuthenticateControllerTests
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthenticateControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private record LoginResponse(string Token);

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndToken()
    {
        // ARRANGE
        var client = _factory.CreateClient();

        // ACT
        var response = await AuthHelper.PostLoginAsync(client,
            CustomWebApplicationFactory.AdminUsername,
            CustomWebApplicationFactory.AdminPassword);

        // ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401()
    {
        // ARRANGE
        var client = _factory.CreateClient();

        // ACT
        var response = await AuthHelper.PostLoginAsync(client,
            CustomWebApplicationFactory.AdminUsername,
            "PasswordIncorrecta1!");

        // ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminToken_IsAcceptedByAdminOnlyEndpoint()
    {
        // ARRANGE
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetAdminTokenAsync(client);
        client.WithBearerToken(token);

        // ACT
        var response = await client.GetAsync("/api/products/admin");

        // ASSERT: 200 si hay productos o 204 si la base está vacía; nunca 401/403
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RegisterCustomer_ReturnsTokenAndCustomerId_AndTokenIsNotAdmin()
    {
        // ARRANGE
        var client = _factory.CreateClient();

        // ACT
        var (token, customerId) = await AuthHelper.RegisterCustomerAsync(client);
        client.WithBearerToken(token);
        var response = await client.GetAsync("/api/products/admin");

        // ASSERT
        token.Should().NotBeNullOrWhiteSpace();
        customerId.Should().NotBe(Guid.Empty);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden); // un cliente no puede entrar al endpoint de Admin
    }
}
