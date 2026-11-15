namespace Dsw2025Tpi.IntegrationTests;

/// <summary>
/// Todos los tests marcados con [Collection("IntegrationTests")] comparten UNA sola factory
/// (una sola API y una sola base SQLite) y se ejecutan en secuencia, sin colisiones de concurrencia.
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationTestsCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}