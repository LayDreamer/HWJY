using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Xunit;
using ZhonTai.Admin.Core;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class ConfigCenterConfigurationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("P0T4HostIsolation")]
    public void LoadsBaseThenOnlyCurrentEnvironmentInStableOrder(string environment)
    {
        using var scope = new ConfigScope();
        // Creation order deliberately differs from file name and configuration precedence.
        scope.Write("alpha.Development.json", "{\"Marker\":\"Development\",\"DevOnly\":true}");
        scope.Write("zeta.json", "{\"Marker\":\"base-z\",\"BaseOnly\":true}");
        scope.Write("alpha.json", "{\"Marker\":\"base-a\"}");
        scope.Write("alpha.Production.json", "{\"Marker\":\"Production\"}");
        scope.Write("alpha.P0T4HostIsolation.json", "{\"Marker\":\"P0T4HostIsolation\"}");
        scope.Write("broken.OtherEnvironment.json", "not valid JSON");

        using var configuration = Load(scope.Directory, environment);
        Assert.Equal(environment == "Staging" ? "base-z" : environment, configuration["Marker"]);
        Assert.True(configuration.GetValue<bool>("BaseOnly"));
        Assert.Equal(environment == "Development", configuration.GetValue<bool>("DevOnly"));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("development", true)]
    [InlineData("Production", false)]
    [InlineData("P0T4HostIsolation", false)]
    public void ProcessConnectionOverridesAreAppliedLastOnlyInDevelopment(string environment, bool isDevelopment)
    {
        using var scope = new ConfigScope();
        scope.Write("dbconfig.json", "{\"DbConfig\":{\"ConnectionString\":\"base-main\",\"Dbs\":[{\"ConnectionString\":\"base-log\"}]}}");
        scope.Write("dbconfig.Development.json", "{\"DbConfig\":{\"Type\":\"MySql\",\"ConnectionString\":\"dev-main\",\"Dbs\":[{\"ConnectionString\":\"dev-log\"}]}}");
        scope.Set("DbConfig__ConnectionString", "process-main");
        scope.Set("DbConfig__Dbs__0__ConnectionString", "process-log");
        scope.Set("DbConfig__Type", "must-not-override-json");

        using var configuration = Load(scope.Directory, environment);
        Assert.Equal(isDevelopment ? "process-main" : "base-main", configuration["DbConfig:ConnectionString"]);
        Assert.Equal(isDevelopment ? "process-log" : "base-log", configuration["DbConfig:Dbs:0:ConnectionString"]);
        Assert.Equal(isDevelopment ? "MySql" : null, configuration["DbConfig:Type"]);
    }

    [Fact]
    public void EmptyConnectionOverridesKeepDevelopmentFileValues()
    {
        using var scope = new ConfigScope();
        scope.Write("dbconfig.Development.json", "{\"DbConfig\":{\"ConnectionString\":\"dev-main\",\"Dbs\":[{\"ConnectionString\":\"dev-log\"}]}}");
        scope.Set("DbConfig__ConnectionString", "   ");
        scope.Set("DbConfig__Dbs__0__ConnectionString", "");

        using var configuration = Load(scope.Directory, "Development");
        Assert.Equal("dev-main", configuration["DbConfig:ConnectionString"]);
        Assert.Equal("dev-log", configuration["DbConfig:Dbs:0:ConnectionString"]);
    }

    [Fact]
    public void IsolatedEnvironmentFileDoesNotRequireABaseFile()
    {
        using var scope = new ConfigScope();
        scope.Write("dbconfig.P0T4HostIsolation.json", "{\"DbConfig\":{\"ConnectionString\":\"isolated-main\"}}");
        scope.Write("dbconfig.Development.json", "{\"DevOnly\":true}");
        scope.Set("DbConfig__ConnectionString", "daily-main-must-not-leak");

        using var configuration = Load(scope.Directory, "P0T4HostIsolation");
        Assert.Equal("isolated-main", configuration["DbConfig:ConnectionString"]);
        Assert.Null(configuration["DevOnly"]);
    }

    [Theory]
    [InlineData("Development", "MySql")]
    [InlineData("Production", "Sqlite")]
    [InlineData("Staging", "Sqlite")]
    [InlineData("P0T4HostIsolation", "Sqlite")]
    public void RepositoryMySqlDefaultsAreRestrictedToDevelopment(string environment, string expectedType)
    {
        using var scope = new ConfigScope();
        var directory = Path.Combine(RepositoryBoundaryTests.RepositoryRoot, "src", "modules", "admin", "ZhonTai.Admin.Host", "ConfigCenter");
        using var configuration = Load(directory, environment);
        foreach (var prefix in new[] { "DbConfig", "DbConfig:Dbs:0" })
        {
            Assert.Equal(expectedType, configuration[prefix + ":Type"]);
            if (environment == "Development")
            {
                foreach (var flag in new[] { "CreateDb", "SyncStructure", "SyncData", "SyncUpdateData", "GenerateData" })
                    Assert.False(configuration.GetValue<bool>(prefix + ":" + flag));
                var connection = configuration[prefix + ":ConnectionString"];
                Assert.Contains("Port=3306", connection);
                Assert.Contains(prefix == "DbConfig" ? "Database=admincore_host;" : "Database=admincore_host_log;", connection);
            }
            else
            {
                Assert.Contains("Data Source=", configuration[prefix + ":ConnectionString"]);
                Assert.True(configuration.GetValue<bool>(prefix + ":CreateDb"));
                Assert.True(configuration.GetValue<bool>(prefix + ":SyncStructure"));
            }
        }
    }

    private static ConfigurationManager Load(string directory, string environment)
    {
        var configuration = new ConfigurationManager();
        var method = typeof(HostApp).GetMethod("AddConfigCenterConfiguration", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, [configuration, environment, directory, true, false]);
        return configuration;
    }

    // Assembly test configuration disables parallel collections; restore every process value we touch.
    private sealed class ConfigScope : IDisposable
    {
        private readonly Dictionary<string, string> original = new();
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "admincore-config-" + Guid.NewGuid().ToString("N"));

        public ConfigScope()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Set("DbConfig__ConnectionString", null);
            Set("DbConfig__Dbs__0__ConnectionString", null);
        }

        public void Write(string fileName, string json) => File.WriteAllText(Path.Combine(Directory, fileName), json);

        public void Set(string name, string value)
        {
            if (!original.ContainsKey(name)) original[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var entry in original) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            foreach (var file in System.IO.Directory.GetFiles(Directory)) File.Delete(file);
            System.IO.Directory.Delete(Directory);
        }
    }
}
