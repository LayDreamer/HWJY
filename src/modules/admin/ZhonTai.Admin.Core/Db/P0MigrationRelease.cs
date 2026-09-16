using System.Security.Cryptography;
using System.Text.Json;
using FreeSql;
using MySqlConnector;

namespace ZhonTai.Admin.Core.Db;

/// <summary>Release description. The manifest is the only source for forward migrations.</summary>
public sealed class P0MigrationManifest
{
    public List<P0MigrationManifestEntry> Migrations { get; set; } = [];

    public static P0MigrationManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<P0MigrationManifest>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return manifest ?? throw new InvalidOperationException("P0 migration manifest is empty.");
    }

    public IReadOnlyList<P0MigrationManifestEntry> Forward(string root)
    {
        var entries = Migrations
            .Where(x => string.Equals(x.Provider, "MySql", StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Direction, "Forward", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Version, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("P0 migration manifest has no MySQL forward entries.");
        }

        var versions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            entry.Validate(root);
            if (!versions.Add(entry.Version))
            {
                throw new InvalidOperationException($"P0 migration manifest contains duplicate version {entry.Version}.");
            }
            if (entry.DependsOn.Any(dependency => !versions.Contains(dependency)))
            {
                throw new InvalidOperationException($"P0 migration {entry.Version} has a missing or out-of-order dependency.");
            }
        }

        return entries;
    }
}

public sealed class P0MigrationManifestEntry
{
    public string Version { get; set; }
    public string Name { get; set; }
    public string Provider { get; set; }
    public string Direction { get; set; }
    public string Path { get; set; }
    public string Checksum { get; set; }
    public string RollbackPath { get; set; }
    public string RollbackChecksum { get; set; }
    public List<string> DependsOn { get; set; } = [];

    public void Validate(string root)
    {
        if (string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(Path) || string.IsNullOrWhiteSpace(Checksum))
        {
            throw new InvalidOperationException("A P0 migration entry is missing Version, Path, or Checksum.");
        }

        ValidateFile(root, Path, Checksum, "migration");
    }

    public void ValidateRollback(string root)
    {
        if (string.IsNullOrWhiteSpace(RollbackPath) || string.IsNullOrWhiteSpace(RollbackChecksum))
        {
            throw new InvalidOperationException($"P0 migration {Version} has no approved rollback artifact.");
        }

        ValidateFile(root, RollbackPath, RollbackChecksum, "rollback");
    }

    private void ValidateFile(string root, string relativePath, string expectedChecksum, string artifactKind)
    {
        var fullRoot = System.IO.Path.GetFullPath(root);
        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relativePath));
        if (!file.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            throw new InvalidOperationException($"P0 {artifactKind} file is unavailable: {relativePath}.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        if (!string.Equals(checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"P0 {artifactKind} checksum mismatch for {Version}.");
        }
    }
}

/// <summary>
/// Executes P0 releases through one dedicated MySQL session. MySQL named locks are session scoped,
/// so lock acquisition, history changes, DDL and release must never be split over pooled ADO calls.
/// </summary>
public sealed class P0MigrationExecutor
{
    private const string LockName = "admincore:p0-schema-migration";
    private readonly string _connectionString;
    private readonly string _manifestRoot;

    public P0MigrationExecutor(IFreeSql db, string manifestRoot)
        : this(db?.Ado.ConnectionString ?? throw new ArgumentNullException(nameof(db)), manifestRoot)
    {
    }

    public P0MigrationExecutor(string connectionString, string manifestRoot)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A MySQL connection string is required.", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(manifestRoot)) throw new ArgumentException("A manifest root is required.", nameof(manifestRoot));
        _connectionString = connectionString;
        _manifestRoot = System.IO.Path.GetFullPath(manifestRoot);
    }

    /// <summary>Reads release state only; it never creates the migration history table.</summary>
    public P0MigrationPlan Plan(string manifestPath)
    {
        var entries = LoadForward(manifestPath);
        using var connection = OpenConnection();
        if (!HistoryExists(connection))
        {
            return new P0MigrationPlan(entries.Select(x => x.Version).ToArray(), [], entries[^1].Version, entries[^1].Checksum, false);
        }

        var history = ReadHistory(connection);
        ValidateHistory(entries, history);
        var pending = entries.Where(entry => !history.TryGetValue(entry.Version, out var item) || !IsSucceeded(item)).Select(x => x.Version).ToArray();
        return new P0MigrationPlan(entries.Select(x => x.Version).ToArray(), pending, entries[^1].Version, entries[^1].Checksum, true);
    }

    /// <summary>Reads history and verifies the manifest target without mutating the database.</summary>
    public void Verify(string manifestPath)
    {
        var plan = Plan(manifestPath);
        if (!plan.HistoryExists || plan.PendingVersions.Count != 0)
        {
            throw new InvalidOperationException("P0 migration verification failed: the manifest target is not fully applied.");
        }
    }

    public P0MigrationApplyResult Apply(string manifestPath, string appliedBy)
    {
        var entries = LoadForward(manifestPath);
        using var connection = OpenConnection();
        using var lease = AcquireLock(connection);
        EnsureHistory(connection);
        var history = ReadHistory(connection);
        ValidateHistory(entries, history);

        var applied = new List<string>();
        foreach (var entry in entries)
        {
            if (history.TryGetValue(entry.Version, out var existing) && IsSucceeded(existing))
            {
                continue;
            }

            InsertRunning(connection, entry, appliedBy);
            try
            {
                ExecuteSqlFile(connection, entry.Path);
                MarkSucceeded(connection, entry.Version, appliedBy);
                applied.Add(entry.Version);
            }
            catch (Exception exception)
            {
                MarkFailed(connection, entry.Version, exception.Message);
                throw;
            }
        }

        return new P0MigrationApplyResult(applied, entries[^1].Version, entries[^1].Checksum, applied.Count == 0);
    }

    public void Rollback(string manifestPath, string version, string appliedBy)
    {
        var entry = LoadForward(manifestPath).SingleOrDefault(item => string.Equals(item.Version, version, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"P0 migration {version} is not an approved forward entry.");
        entry.ValidateRollback(_manifestRoot);

        using var connection = OpenConnection();
        using var lease = AcquireLock(connection);
        EnsureHistory(connection);
        var history = ReadHistory(connection);
        ValidateHistory([entry], history);
        if (!history.TryGetValue(version, out var current) || !IsSucceeded(current))
        {
            throw new InvalidOperationException($"P0 migration {version} is not eligible for rollback.");
        }

        ExecuteSqlFile(connection, entry.RollbackPath);
        ExecuteNonQuery(connection, "update p0_schema_migration set Status = 'RolledBack', FinishedAt = utc_timestamp(), AppliedBy = @appliedBy, Error = null where Version = @version", ("@version", version), ("@appliedBy", appliedBy));
    }

    /// <summary>Controlled recovery replays an approved forward artifact after a recorded rollback.</summary>
    public void Recover(string manifestPath, string version, string appliedBy)
    {
        var entry = LoadForward(manifestPath).SingleOrDefault(item => string.Equals(item.Version, version, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"P0 migration {version} is not an approved forward entry.");

        using var connection = OpenConnection();
        using var lease = AcquireLock(connection);
        EnsureHistory(connection);
        var history = ReadHistory(connection);
        if (!history.TryGetValue(version, out var current)
            || !string.Equals(current.Checksum, entry.Checksum, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.Status, "RolledBack", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"P0 migration {version} is not eligible for controlled recovery.");
        }

        try
        {
            ExecuteSqlFile(connection, entry.Path);
            MarkSucceeded(connection, version, appliedBy);
        }
        catch (Exception exception)
        {
            MarkFailed(connection, version, exception.Message);
            throw;
        }
    }

    private IReadOnlyList<P0MigrationManifestEntry> LoadForward(string manifestPath) => P0MigrationManifest.Load(manifestPath).Forward(_manifestRoot);

    private MySqlConnection OpenConnection()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static IDisposable AcquireLock(MySqlConnection connection)
    {
        if (Convert.ToInt64(ExecuteScalar(connection, "select get_lock(@name, 0)", ("@name", LockName))) != 1)
        {
            throw new InvalidOperationException("Another P0 migration executor currently owns the database lock.");
        }
        return new MigrationLockLease(connection);
    }

    private static bool HistoryExists(MySqlConnection connection) => Convert.ToInt64(ExecuteScalar(connection, "select count(*) from information_schema.tables where table_schema = database() and table_name = 'p0_schema_migration'")) == 1;
    private static void EnsureHistory(MySqlConnection connection) => ExecuteNonQuery(connection, "create table if not exists p0_schema_migration (Id bigint not null auto_increment primary key, Version varchar(100) not null, Name varchar(200) not null, Checksum varchar(128) not null, Status varchar(20) not null, StartedAt datetime not null, FinishedAt datetime null, AppliedBy varchar(200) null, Error text null, unique key UX_p0_schema_migration_version (Version)) engine=InnoDB default charset=utf8mb4");

    private static Dictionary<string, P0MigrationHistory> ReadHistory(MySqlConnection connection)
    {
        using var command = new MySqlCommand("select Version, Checksum, Status from p0_schema_migration", connection);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, P0MigrationHistory>(StringComparer.Ordinal);
        while (reader.Read()) result.Add(reader.GetString(0), new P0MigrationHistory(reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static void ValidateHistory(IReadOnlyList<P0MigrationManifestEntry> entries, IReadOnlyDictionary<string, P0MigrationHistory> history)
    {
        foreach (var record in history)
        {
            var manifestEntry = entries.SingleOrDefault(x => string.Equals(x.Version, record.Key, StringComparison.Ordinal));
            if (manifestEntry is null)
            {
                if (!string.Equals(record.Value.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"P0 migration {record.Key} is {record.Value.Status} and requires controlled recovery.");
                }
                continue;
            }
            if (!string.Equals(record.Value.Checksum, manifestEntry.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"P0 migration checksum changed for applied version {record.Key}.");
            }
            if (!IsSucceeded(record.Value))
            {
                throw new InvalidOperationException($"P0 migration {record.Key} is {record.Value.Status} and requires controlled recovery.");
            }
        }
    }

    private static bool IsSucceeded(P0MigrationHistory item) => string.Equals(item.Status, "Succeeded", StringComparison.OrdinalIgnoreCase);
    private void ExecuteSqlFile(MySqlConnection connection, string relativePath) => ExecuteNonQuery(connection, File.ReadAllText(System.IO.Path.Combine(_manifestRoot, relativePath)));
    private static void InsertRunning(MySqlConnection connection, P0MigrationManifestEntry entry, string appliedBy) => ExecuteNonQuery(connection, "insert into p0_schema_migration (Version, Name, Checksum, Status, StartedAt, AppliedBy) values (@version, @name, @checksum, 'Running', utc_timestamp(), @appliedBy)", ("@version", entry.Version), ("@name", entry.Name), ("@checksum", entry.Checksum), ("@appliedBy", appliedBy));
    private static void MarkSucceeded(MySqlConnection connection, string version, string appliedBy) => ExecuteNonQuery(connection, "update p0_schema_migration set Status = 'Succeeded', FinishedAt = utc_timestamp(), AppliedBy = @appliedBy, Error = null where Version = @version", ("@version", version), ("@appliedBy", appliedBy));
    private static void MarkFailed(MySqlConnection connection, string version, string error) => ExecuteNonQuery(connection, "update p0_schema_migration set Status = 'Failed', FinishedAt = utc_timestamp(), Error = @error where Version = @version", ("@version", version), ("@error", error[..Math.Min(error.Length, 1900)]));

    private static object ExecuteScalar(MySqlConnection connection, string commandText, params (string Name, object Value)[] parameters)
    {
        using var command = CreateCommand(connection, commandText, parameters);
        return command.ExecuteScalar() ?? 0;
    }

    private static void ExecuteNonQuery(MySqlConnection connection, string commandText, params (string Name, object Value)[] parameters)
    {
        using var command = CreateCommand(connection, commandText, parameters);
        command.ExecuteNonQuery();
    }

    private static MySqlCommand CreateCommand(MySqlConnection connection, string commandText, IEnumerable<(string Name, object Value)> parameters)
    {
        var command = new MySqlCommand(commandText, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private sealed class MigrationLockLease(MySqlConnection connection) : IDisposable
    {
        public void Dispose()
        {
            try { ExecuteScalar(connection, "select release_lock(@name)", ("@name", LockName)); }
            catch { /* Do not hide the original migration failure. */ }
        }
    }

    private sealed record P0MigrationHistory(string Checksum, string Status);
}

public sealed record P0MigrationPlan(IReadOnlyList<string> ManifestVersions, IReadOnlyList<string> PendingVersions, string TargetVersion, string TargetChecksum, bool HistoryExists);
public sealed record P0MigrationApplyResult(IReadOnlyList<string> AppliedVersions, string TargetVersion, string TargetChecksum, bool AlreadyApplied);
