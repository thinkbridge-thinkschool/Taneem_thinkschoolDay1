namespace Quotes.Tests.Integration;

// Declares a named xUnit collection that shares ONE SqlServerFixture across all test classes.
// Test classes tagged [Collection("Integration")] run sequentially — no parallel container startups.
[CollectionDefinition("Integration")]
public sealed class IntegrationTestCollection : ICollectionFixture<SqlServerFixture> { }
