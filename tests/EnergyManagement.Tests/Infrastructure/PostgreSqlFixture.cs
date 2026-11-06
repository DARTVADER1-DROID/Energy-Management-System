using Xunit;

namespace EnergyManagement.Tests.Infrastructure;

public sealed class PostgreSqlFixture
{
    public string ConnectionString =>
        Environment.GetEnvironmentVariable("EMS_TEST_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=energy_management_tests;Username=test_user;Password=test_password";
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration tests";
}
