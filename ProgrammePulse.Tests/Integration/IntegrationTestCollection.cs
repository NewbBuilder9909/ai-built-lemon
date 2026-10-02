namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Every real-database integration test class shares this collection so
/// xUnit never runs two of them in parallel — two WebApplicationFactory
/// hosts booting concurrently against the same LocalDB test database would
/// race on Umbraco's MainDom lock and the shared schema. Unit tests over
/// fakes are unaffected; xUnit still parallelizes across other classes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<ProgrammePulseWebApplicationFactory>
{
    public const string Name = "ProgrammePulse integration tests";
}
