namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// One shared API and disposable database for every integration test class in a run. Each run
/// gets its own uniquely named database, so concurrent runs no longer collide; within a run the
/// classes share it and isolate themselves with fresh accounts and companies per test.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HeliosApiCollection : ICollectionFixture<HeliosApiFactory>
{
    public const string Name = "Helios API";
}
