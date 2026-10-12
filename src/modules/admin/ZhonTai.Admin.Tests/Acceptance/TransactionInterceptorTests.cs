using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using FreeSql;
using FreeSql.DataAnnotations;
using Xunit;
using ZhonTai.Admin.Core.Attributes;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Db.Transaction;
using ZhonTai.Admin.Core.Dto;
using ZhonTai.Admin.Core.Repositories;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class TransactionInterceptorTests
{
    [Fact]
    public async Task SynchronousThrowFromTaskMethodRollsBackAndReleasesTransaction()
    {
        using var fixture = new TransactionFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proxy.ThrowBeforeReturningTask());

        Assert.Null(fixture.Manager.GetUnitOfWorkManager("first").Current);
        Assert.False(fixture.FirstReader.Select<TransactionProbeRecord>().Any());
        await fixture.Proxy.CommitAsync();
        Assert.Equal(1, fixture.FirstReader.Select<TransactionProbeRecord>().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncExceptionsRollBackBothTaskReturnShapes(bool generic)
    {
        using var fixture = new TransactionFixture();
        if (generic)
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proxy.ThrowResultAsync());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proxy.ThrowAsync());

        Assert.False(fixture.FirstReader.Select<TransactionProbeRecord>().Any());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResultFailureRollsBackAndSuccessCommits(bool synchronous)
    {
        using var fixture = new TransactionFixture();
        if (synchronous)
        {
            Assert.False(fixture.Proxy.WriteResult(false).Success);
            Assert.False(fixture.FirstReader.Select<TransactionProbeRecord>().Any());
            Assert.True(fixture.Proxy.WriteResult(true).Success);
        }
        else
        {
            Assert.False((await fixture.Proxy.WriteResultAsync(false)).Success);
            Assert.False(fixture.FirstReader.Select<TransactionProbeRecord>().Any());
            Assert.True((await fixture.Proxy.WriteResultAsync(true)).Success);
        }
        Assert.Equal(1, fixture.FirstReader.Select<TransactionProbeRecord>().Count());
    }

    [Fact]
    public async Task NestedRequiredTransactionRollsBackWithItsOwner()
    {
        using var fixture = new TransactionFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proxy.NestedFailureAsync());

        Assert.Null(fixture.Manager.GetUnitOfWorkManager("first").Current);
        Assert.False(fixture.FirstReader.Select<TransactionProbeRecord>().Any());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingCallsOnOneProxyKeepTheirOwnTransactions(bool generic)
    {
        using var fixture = new TransactionFixture();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = generic
            ? fixture.Proxy.FirstResultAsync(entered, release.Task)
            : fixture.Proxy.FirstAsync(entered, release.Task);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (generic)
                await fixture.Proxy.SecondResultAsync();
            else
                await fixture.Proxy.SecondAsync();
        }
        finally
        {
            release.TrySetResult(true);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, fixture.FirstReader.Select<TransactionProbeRecord>().Count());
        Assert.Equal(1, fixture.SecondReader.Select<TransactionProbeRecord>().Count());
    }

    private sealed class TransactionFixture : IDisposable
    {
        private readonly List<string> _paths = [];
        private readonly List<IFreeSql> _databases = [];

        public TransactionFixture()
        {
            var cloud = new FreeSqlCloud();
            var first = Register(cloud, "first");
            var second = Register(cloud, "second");
            Manager = new UnitOfWorkManagerCloud(cloud);
            var target = new TransactionProbeService(
                new RepositoryBase<TransactionProbeRecord, long>(first, Manager.GetUnitOfWorkManager("first")),
                new RepositoryBase<TransactionProbeRecord, long>(second, Manager.GetUnitOfWorkManager("second")));
            var interceptor = new TransactionAsyncInterceptor(Manager, new DbConfig { Key = "first" });
            Proxy = new ProxyGenerator().CreateInterfaceProxyWithTarget<ITransactionProbeService>(target, interceptor.ToInterceptor());
            target.Nested = Proxy;
            FirstReader = Build(_paths[0]);
            SecondReader = Build(_paths[1]);
            _databases.Add(FirstReader);
            _databases.Add(SecondReader);
        }

        public UnitOfWorkManagerCloud Manager { get; }
        public ITransactionProbeService Proxy { get; }
        public IFreeSql FirstReader { get; }
        public IFreeSql SecondReader { get; }

        private IFreeSql Register(FreeSqlCloud cloud, string key)
        {
            var path = Path.Combine(Path.GetTempPath(), $"admincore-interceptor-{Guid.NewGuid():N}.db");
            _paths.Add(path);
            var db = Build(path);
            db.CodeFirst.SyncStructure<TransactionProbeRecord>();
            _databases.Add(db);
            cloud.Register(key, () => db);
            return db;
        }

        private static IFreeSql Build(string path) => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={path};Pooling=false")
            .Build();

        public void Dispose()
        {
            Manager.Dispose();
            foreach (var db in _databases) db.Dispose();
            foreach (var path in _paths) File.Delete(path);
        }
    }
}

[Table(Name = "transaction_interceptor_probe")]
public sealed class TransactionProbeRecord
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }
}

public interface ITransactionProbeService
{
    Task ThrowBeforeReturningTask();
    Task ThrowAsync();
    Task<ResultOutput<string>> ThrowResultAsync();
    Task CommitAsync();
    ResultOutput<string> WriteResult(bool success);
    Task<ResultOutput<string>> WriteResultAsync(bool success);
    Task NestedFailureAsync();
    Task FirstAsync(TaskCompletionSource<bool> entered, Task release);
    Task SecondAsync();
    Task<ResultOutput<string>> FirstResultAsync(TaskCompletionSource<bool> entered, Task release);
    Task<ResultOutput<string>> SecondResultAsync();
}

public sealed class TransactionProbeService(
    RepositoryBase<TransactionProbeRecord, long> first,
    RepositoryBase<TransactionProbeRecord, long> second) : ITransactionProbeService
{
    public ITransactionProbeService Nested { get; set; }

    [Transaction]
    public Task ThrowBeforeReturningTask()
    {
        first.Insert(new TransactionProbeRecord());
        throw new InvalidOperationException("Synchronous failure before returning Task.");
    }

    [Transaction]
    public async Task ThrowAsync()
    {
        await first.InsertAsync(new TransactionProbeRecord());
        await Task.Yield();
        throw new InvalidOperationException("Asynchronous failure.");
    }

    [Transaction]
    public async Task<ResultOutput<string>> ThrowResultAsync()
    {
        await first.InsertAsync(new TransactionProbeRecord());
        await Task.Yield();
        throw new InvalidOperationException("Asynchronous result failure.");
    }

    [Transaction]
    public async Task CommitAsync() => await first.InsertAsync(new TransactionProbeRecord());

    [Transaction]
    public ResultOutput<string> WriteResult(bool success)
    {
        first.Insert(new TransactionProbeRecord());
        return success ? new ResultOutput<string>().Ok("ok") : new ResultOutput<string>().NotOk("rejected");
    }

    [Transaction]
    public async Task<ResultOutput<string>> WriteResultAsync(bool success)
    {
        await first.InsertAsync(new TransactionProbeRecord());
        return success ? new ResultOutput<string>().Ok("ok") : new ResultOutput<string>().NotOk("rejected");
    }

    [Transaction]
    public async Task NestedFailureAsync()
    {
        await first.InsertAsync(new TransactionProbeRecord());
        await Nested.CommitAsync();
        throw new InvalidOperationException("Owner failure after nested call.");
    }

    [Transaction]
    public async Task FirstAsync(TaskCompletionSource<bool> entered, Task release)
    {
        await first.InsertAsync(new TransactionProbeRecord());
        entered.TrySetResult(true);
        await release;
    }

    [Transaction("second")]
    public async Task SecondAsync() => await second.InsertAsync(new TransactionProbeRecord());

    [Transaction]
    public async Task<ResultOutput<string>> FirstResultAsync(TaskCompletionSource<bool> entered, Task release)
    {
        await first.InsertAsync(new TransactionProbeRecord());
        entered.TrySetResult(true);
        await release;
        return new ResultOutput<string>().Ok("first");
    }

    [Transaction("second")]
    public async Task<ResultOutput<string>> SecondResultAsync()
    {
        await second.InsertAsync(new TransactionProbeRecord());
        return new ResultOutput<string>().Ok("second");
    }
}
