using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using MySqlConnector;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

internal static class P0MySqlAcceptanceEnvironment
{
    internal static readonly IReadOnlyDictionary<string, string> Schemas = new Dictionary<string, string>
    {
        ["ADMINCORE_T4_MYSQL"] = "admincore_p0_t4_acceptance",
        ["ADMINCORE_HOST_MYSQL"] = "admincore_host",
        ["ADMINCORE_HOST_MYSQL_LOG"] = "admincore_log",
        ["ADMINCORE_ACCEPTANCE_MYSQL"] = "admincore_acceptance"
    };

    public static string Missing(params string[] variables) => $"Set {string.Join(" / ", variables)} to dedicated MySQL 8 acceptance schemas.";

    public static bool IsMissing(params string[] variables) => variables.Any(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)));

    internal static MySqlConnectionStringBuilder ValidateTarget(string connectionString, string expectedSchema = null)
    {
        MySqlConnectionStringBuilder target;
        try { target = new MySqlConnectionStringBuilder(connectionString); }
        catch { throw new InvalidOperationException("Invalid acceptance connection configuration; credentials omitted."); }
        if (target.Server != "127.0.0.1" || target.Port != 33306 || target.UserID != "admincore_acceptance"
            || !Schemas.Values.Contains(target.Database, StringComparer.Ordinal)
            || (expectedSchema != null && target.Database != expectedSchema)
            || target.SslMode != MySqlSslMode.Required || target.ConnectionProtocol != MySqlConnectionProtocol.Sockets)
            throw new InvalidOperationException("Acceptance is restricted to 127.0.0.1:33306, its dedicated account and approved schemas; daily databases are forbidden.");
        return target;
    }

    internal static void ValidateIdentity(int port, string uuid, string dataDirectory, string expectedUuid, string expectedDirectory)
    {
        if (port != 33306 || !Guid.TryParse(expectedUuid, out _) || string.IsNullOrWhiteSpace(expectedDirectory)
            || !string.Equals(uuid, expectedUuid, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(dataDirectory).TrimEnd('/', '\\'), Path.GetFullPath(expectedDirectory).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Acceptance MySQL identity differs from the managed instance; no test writes are permitted.");
    }

    internal static string RequireConnection(string variable)
    {
        if (!Schemas.TryGetValue(variable, out var schema)) throw new InvalidOperationException("Unknown acceptance connection key.");
        return VerifyConnection(Environment.GetEnvironmentVariable(variable), schema);
    }

    internal static string VerifyConnection(string connectionString, string expectedSchema = null)
    {
        var target = ValidateTarget(connectionString, expectedSchema); // Reject 3306 before opening any connection.
        var uuid = Environment.GetEnvironmentVariable("ADMINCORE_ACCEPTANCE_SERVER_UUID");
        var directory = Environment.GetEnvironmentVariable("ADMINCORE_ACCEPTANCE_DATA_DIRECTORY");
        if (!Guid.TryParse(uuid, out _) || string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Managed acceptance instance proof is missing; use tools/run-p0-gate.ps1 -Mode Full.");
        try
        {
            target.ConnectionTimeout = 5;
            using var connection = new MySqlConnection(target.ConnectionString);
            connection.Open();
            using var command = new MySqlCommand("select @@port, @@server_uuid, @@datadir", connection);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Acceptance instance identity is unavailable.");
            ValidateIdentity(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), uuid, directory);
        }
        catch (MySqlException)
        {
            throw new InvalidOperationException("Managed acceptance MySQL could not be verified; credentials omitted.");
        }
        return connectionString;
    }
}

public sealed class P0T4MySqlFactAttribute : FactAttribute
{
    public P0T4MySqlFactAttribute()
    {
        if (P0MySqlAcceptanceEnvironment.IsMissing("ADMINCORE_T4_MYSQL"))
            Skip = P0MySqlAcceptanceEnvironment.Missing("ADMINCORE_T4_MYSQL");
    }
}
