using System;
using System.IO;
using System.Linq;
using MySqlConnector;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

[Collection("P0T4MySql")]
[Trait("P0Gate", "Required")]
public sealed class P0MySqlIsolationGuardTests
{
    private const string Safe = "Server=127.0.0.1;Port=33306;Database=admincore_p0_t4_acceptance;User ID=admincore_acceptance;Password=must-not-leak;SslMode=Required";

    [Theory]
    [InlineData("Port=33306", "Port=3306")]
    [InlineData("Port=33306", "Port=33307")]
    [InlineData("127.0.0.1", "localhost")]
    [InlineData("127.0.0.1", "192.0.2.1")]
    [InlineData("admincore_p0_t4_acceptance", "admincore_host_log")]
    [InlineData("admincore_p0_t4_acceptance", "mysql")]
    [InlineData("User ID=admincore_acceptance", "User ID=root")]
    [InlineData("SslMode=Required", "SslMode=None")]
    public void UnsafeTargetsAreRejectedWithoutOpeningAConnection(string oldValue, string newValue)
    {
        var error = Assert.Throws<InvalidOperationException>(() => P0MySqlAcceptanceEnvironment.ValidateTarget(Safe.Replace(oldValue, newValue)));
        Assert.DoesNotContain("must-not-leak", error.ToString());
    }

    [Fact]
    public void SchemaMustMatchItsConnectionRole()
    {
        Assert.Throws<InvalidOperationException>(() => P0MySqlAcceptanceEnvironment.ValidateTarget(Safe, "admincore_host"));
        Assert.Equal((uint)33306, P0MySqlAcceptanceEnvironment.ValidateTarget(Safe).Port);
    }

    [Fact]
    public void CleanupCannotBypassTheDailyDatabaseBlock()
    {
        Assert.Throws<InvalidOperationException>(() => AcceptanceFixtureCleanup.ForMySql(Safe.Replace("Port=33306", "Port=3306")));
    }

    [Fact]
    public void MissingInstanceProofIsRejectedBeforeAuthentication()
    {
        var originalUuid = Environment.GetEnvironmentVariable("ADMINCORE_ACCEPTANCE_SERVER_UUID");
        var originalDirectory = Environment.GetEnvironmentVariable("ADMINCORE_ACCEPTANCE_DATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("ADMINCORE_ACCEPTANCE_SERVER_UUID", null);
            Environment.SetEnvironmentVariable("ADMINCORE_ACCEPTANCE_DATA_DIRECTORY", null);
            var error = Assert.Throws<InvalidOperationException>(() => P0MySqlAcceptanceEnvironment.VerifyConnection(Safe));
            Assert.Contains("proof is missing", error.Message);
            Assert.DoesNotContain("must-not-leak", error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMINCORE_ACCEPTANCE_SERVER_UUID", originalUuid);
            Environment.SetEnvironmentVariable("ADMINCORE_ACCEPTANCE_DATA_DIRECTORY", originalDirectory);
        }
    }

    [Theory]
    [InlineData(3306, false, false)]
    [InlineData(33306, true, false)]
    [InlineData(33306, false, true)]
    public void ForwardedDailyServerOrWrongInstanceIsRejected(int port, bool wrongUuid, bool wrongDirectory)
    {
        var uuid = Guid.NewGuid().ToString();
        var directory = Path.Combine(Path.GetTempPath(), "admincore-acceptance-identity", "data");
        Assert.Throws<InvalidOperationException>(() => P0MySqlAcceptanceEnvironment.ValidateIdentity(port,
            wrongUuid ? Guid.NewGuid().ToString() : uuid, wrongDirectory ? directory + "-other" : directory, uuid, directory));
        P0MySqlAcceptanceEnvironment.ValidateIdentity(33306, uuid, directory + Path.DirectorySeparatorChar, uuid, directory);
    }

    [P0T4MySqlFact]
    [Trait("P0Dependency", "MySql")]
    public void ManagedInstanceHasOnlyApprovedSchemasAndNoGlobalDataPrivileges()
    {
        foreach (var entry in P0MySqlAcceptanceEnvironment.Schemas)
            P0MySqlAcceptanceEnvironment.RequireConnection(entry.Key);
        using var connection = new MySqlConnection(P0MySqlAcceptanceEnvironment.RequireConnection("ADMINCORE_T4_MYSQL"));
        connection.Open();
        using var command = new MySqlCommand("show grants", connection);
        using (var reader = command.ExecuteReader())
        {
            var grants = new System.Collections.Generic.List<string>();
            while (reader.Read()) grants.Add(reader.GetString(0));
            Assert.DoesNotContain(grants, grant => grant.Contains(" ON *.* ", StringComparison.Ordinal) && !grant.StartsWith("GRANT USAGE ", StringComparison.Ordinal));
            Assert.Equal(4, grants.Count(grant => grant.StartsWith("GRANT ALL PRIVILEGES ON ", StringComparison.Ordinal)));
        }
        using var forbidden = new MySqlCommand("select count(*) from mysql.user", connection);
        Assert.Throws<MySqlException>(() => forbidden.ExecuteScalar());
    }
}
