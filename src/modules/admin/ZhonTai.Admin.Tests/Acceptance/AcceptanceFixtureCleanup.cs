using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>Owns only fixture tables and API registrations added during one acceptance run.</summary>
internal sealed class AcceptanceFixtureCleanup : IDisposable
{
    private static readonly string[] FixtureTables =
    [
        "acc_host_business_record", "acc_p0_data_permission_fixture", "acceptance_data_scope",
        "transaction_interceptor_probe", "p0_t2_owner_detail", "p0_t2_owner_header"
    ];

    private readonly IFreeSql _db;
    private readonly bool _ownsDatabase;
    private readonly HashSet<string> _originalTables;
    private readonly HashSet<long> _originalFixtureApis;
    private readonly List<Action<IFreeSql>> _cleanup = [];
    private bool _disposed;

    internal AcceptanceFixtureCleanup(IFreeSql db, bool ownsDatabase = false)
    {
        _db = db;
        _ownsDatabase = ownsDatabase;
        try
        {
            _originalTables = FixtureTables.Where(table => db.DbFirst.ExistsTable(table)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _originalFixtureApis = ReadFixtureApis().ToHashSet();
        }
        catch
        {
            if (ownsDatabase) db.Dispose();
            throw;
        }
    }

    internal static AcceptanceFixtureCleanup ForMySql(string connectionString) => new(
        new FreeSqlBuilder().UseConnectionString(DataType.MySql, P0MySqlAcceptanceEnvironment.VerifyConnection(connectionString)).UseAutoSyncStructure(false).Build(),
        ownsDatabase: true);

    internal void Register(Action<IFreeSql> cleanup) => _cleanup.Add(cleanup);

    internal void RegisterHostRun(string recordCode, string apiPath, string apiLabel)
    {
        var parameters = new { code = recordCode, rollback = recordCode + "-ROLLBACK", path = apiPath, label = apiLabel };
        if ((_db.DbFirst.ExistsTable("base_api") && _db.Ado.QuerySingle<int>(
                "select count(*) from base_api where Path = @path", parameters) > 0)
            || (_db.DbFirst.ExistsTable("acc_host_business_record") && _db.Ado.QuerySingle<int>(
                "select count(*) from acc_host_business_record where RecordCode in (@code,@rollback)", parameters) > 0))
            throw new InvalidOperationException("Host acceptance identifiers already exist; refusing to modify pre-existing records.");

        Register(db => db.Ado.Transaction(() =>
        {
            if (db.DbFirst.ExistsTable("base_api"))
            {
                var ids = db.Ado.Query<long>("select Id from base_api where Path = @path and Label = @label", parameters);
                DeleteApis(ids.ToArray());
            }
            if (db.DbFirst.ExistsTable("acc_host_business_record"))
                db.Ado.ExecuteNonQuery("delete from acc_host_business_record where RecordCode in (@code,@rollback)", parameters);
        }));
    }

    private List<long> ReadFixtureApis() => _db.DbFirst.ExistsTable("base_api")
        ? _db.Ado.Query<long>("select Id from base_api where Path like '/api/admin/p0-data-permission-fixture/%'")
        : [];

    private void DeleteApis(long[] added)
    {
        if (added.Length == 0) return;
        var ids = string.Join(',', added);
        if ((_db.DbFirst.ExistsTable("base_permission_api") && _db.Ado.QuerySingle<int>(
                $"select count(*) from base_permission_api where ApiId in ({ids})") > 0)
            || _db.Ado.QuerySingle<int>($"select count(*) from base_api where ParentId in ({ids}) and Id not in ({ids})") > 0)
            throw new InvalidOperationException("Other records reference newly added acceptance APIs; cleanup refused.");
        _db.Ado.ExecuteNonQuery($"delete from base_api where Id in ({ids})");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var errors = new List<Exception>();
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception exception) { errors.Add(exception); }
        }

        try
        {
            foreach (var cleanup in _cleanup) Attempt(() => cleanup(_db));
            Attempt(() =>
            {
                var added = ReadFixtureApis().Where(id => !_originalFixtureApis.Contains(id)).ToArray();
                if (added.Length == 0) return;
                _db.Ado.Transaction(() => DeleteApis(added));
            });
            foreach (var table in FixtureTables.Where(table => !_originalTables.Contains(table)))
                Attempt(() => _db.Ado.ExecuteNonQuery($"drop table if exists `{table}`"));
        }
        finally
        {
            if (_ownsDatabase) _db.Dispose();
        }

        if (errors.Count > 0) throw new AggregateException("Acceptance fixture cleanup failed.", errors);
    }
}
