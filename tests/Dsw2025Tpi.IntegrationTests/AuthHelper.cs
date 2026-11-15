using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Dsw2025Tpi.IntegrationTests;

/// <summary>
/// Helpers para obtener tokens JWT reales llamando a los endpoints de autenticación de la API.
/// </summary>
public static class AuthHelper
{
    private record LoginResponse(string Token);
    private record RegisterResponse(string Token, Guid? CustomerId);

    /// <summary>POST /api/auth/login. Devuelve la respuesta cruda (para tests que verifican el status code).</summary>
    public static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { username, password });

    /// <summary>Hace login con el admin sembrado por la factory y devuelve su JWT (rol Admin).</summary>
    public static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var response = await PostLoginAsync(client,
            CustomWebApplicationFactory.AdminUsername,
            CustomWebApplicationFactory.AdminPassword);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    /// <summary>
    /// Registra un cliente NUEVO (usuario único) con POST /api/auth/register. La API crea el usuario con rol "User"
    /// y su entidad Customer, y devuelve el JWT y el CustomerId.
    /// </summary>
    public static async Task<(string Token, Guid CustomerId)> RegisterCustomerAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"cliente_{suffix}",
            password = "Cliente123!",
            email = $"cliente_{suffix}@test.com"
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        return (body!.Token, body.CustomerId!.Value);
    }

    /// <summary>Agrega el header Authorization: Bearer {token} al cliente HTTP.</summary>
    public static HttpClient WithBearerToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
