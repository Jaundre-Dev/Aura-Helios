namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// One shared API + database for every integration test class. Collection fixtures run the
/// classes serially, which matters here: <see cref="HeliosApiFactory"/> drops and recreates
/// the <c>helios_test</c> schema, so two classes initialising it in parallel would corrupt
/// each other's run.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HeliosApiCollection : ICollectionFixture<HeliosApiFactory>
{
    public const string Name = "Helios API";
}
