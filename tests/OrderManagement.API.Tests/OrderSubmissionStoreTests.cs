using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Infrastructure.Data;
using OrderManagement.Infrastructure.Repositories;
using Npgsql;

namespace OrderManagement.API.Tests;

public sealed class OrderSubmissionStoreTests : IDisposable
{
    private readonly SqliteConnection? _sqlite;
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly bool _postgres;
    private readonly Clock _clock = new();
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026,11,27,12,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public OrderSubmissionStoreTests()
    {
        var connection = Environment.GetEnvironmentVariable("OMS_SUBMISSION_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(connection))
        {
            var settings = new NpgsqlConnectionStringBuilder(connection);
            if (settings.Host != "127.0.0.1" || settings.Database != "oms_submission_verification")
                throw new InvalidOperationException("Use ONLY the disposable localhost PostgreSQL verification container.");
            settings.Database += "_" + Guid.NewGuid().ToString("N");
            _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(settings.ConnectionString).Options;
            _postgres = true;
        }
        else
        {
            _sqlite = new SqliteConnection("Data Source=:memory:"); _sqlite.Open();
            _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_sqlite).Options;
        }
        using var db = Db();
        if (_postgres) db.Database.Migrate(); else db.Database.EnsureCreated();
    }
    private ApplicationDbContext Db() => new(_options);
    private NewOrderSubmission Request(Guid? key = null, string actor = "account-1", int user = 1, string hash = "A", string snapshot = "{\"price\":25}")
        => new(actor, key ?? Guid.NewGuid(), user, string.Concat(Enumerable.Repeat(hash,64)), snapshot);
    private OrderSubmissionStore Store(ApplicationDbContext db) => new(db, _clock);

    [Fact]
    public async Task SameIdentityKeepsFirstReservationIdAndPriceSnapshotAcrossContexts()
    {
        var request = Request(); Guid id;
        using (var db = Db()) id = (await Store(db).GetOrCreateAsync(request)).Id;
        using (var db = Db())
        {
            var replay = await Store(db).GetOrCreateAsync(request with { SnapshotJson = "{\"price\":99}" });
            Assert.Equal(id,replay.Id); Assert.Equal(request.SnapshotJson,replay.SnapshotJson);
            Assert.Equal(1,await db.OrderSubmissions.CountAsync());
        }
    }
    [Fact]
    public async Task DifferentPayloadOrTargetUserConflictsButDifferentActorHasOwnKeyspace()
    {
        using var db = Db(); var store = Store(db); var request = Request();
        var first = await store.GetOrCreateAsync(request);
        await Assert.ThrowsAsync<DataConflictException>(()=>store.GetOrCreateAsync(request with { RequestHash = new string('B',64) }));
        await Assert.ThrowsAsync<DataConflictException>(()=>store.GetOrCreateAsync(request with { UserId=2 }));
        Assert.NotEqual(first.Id,(await store.GetOrCreateAsync(request with { ActorAccountId="account-2" })).Id);
        Assert.Null(await store.GetOwnedAsync(first.Id,"account-2"));
    }
    [Fact]
    public async Task LeaseExpiresAndOldOwnerCannotAdvanceOrReleaseNewOwnersWork()
    {
        using var db = Db(); var store=Store(db); var id=(await store.GetOrCreateAsync(Request())).Id;
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        Assert.NotNull(await store.TryAcquireAsync(id,first,TimeSpan.FromSeconds(30)));
        Assert.Null(await store.TryAcquireAsync(id,second,TimeSpan.FromSeconds(30)));
        _clock.Now=_clock.Now.AddSeconds(31);
        Assert.False(await store.TransitionAsync(id,first,"Pending","Reserved"));
        Assert.NotNull(await store.TryAcquireAsync(id,second,TimeSpan.FromSeconds(30)));
        Assert.False(await store.TransitionAsync(id,first,"Pending","Reserved"));
        Assert.False(await store.ReleaseLeaseAsync(id,first));
        Assert.True(await store.TransitionAsync(id,second,"Pending","Reserved"));
        Assert.True(await store.TransitionAsync(id,second,"Reserved","Compensating"));
        Assert.True(await store.TransitionAsync(id,second,"Compensating","Compensated"));
        Assert.True(await store.ReleaseLeaseAsync(id,second));
        Assert.Null(await store.TryAcquireAsync(id,Guid.NewGuid(),TimeSpan.FromSeconds(30)));
    }
    [Fact]
    public async Task CannotMarkCompletedWithoutAtomicOrderCommit()
    {
        using var db=Db(); var store=Store(db);var id=(await store.GetOrCreateAsync(Request())).Id;var owner=Guid.NewGuid();
        await store.TryAcquireAsync(id,owner,TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.TransitionAsync(id,owner,"Reserved","Completed"));
        Assert.True(await store.TransitionAsync(id,owner,"Pending","Rejected"));
        await store.ReleaseLeaseAsync(id,owner);
        Assert.Null(await store.TryAcquireAsync(id,Guid.NewGuid(),TimeSpan.FromSeconds(30)));
    }
    [Fact]
    public async Task InvalidIdentityIsRejectedBeforeWriting()
    {
        using var db=Db();var store=Store(db);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.GetOrCreateAsync(Request() with { RequestKey=Guid.Empty }));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.GetOrCreateAsync(Request() with { RequestHash="bad" }));
        Assert.Equal(0,await db.OrderSubmissions.CountAsync());
    }
    [Fact]
    public async Task ConcurrentSameKeyAndLeaseHaveOneWinnerOnPostgres()
    {
        // SQLite's shared in-memory connection cannot model independent simultaneous writers.
        // The disposable PostgreSQL run exercises the true concurrent path below.
        if (!_postgres) return;
        var request=Request();
        var creates=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>
        {
            using var db=Db();return (await Store(db).GetOrCreateAsync(request)).Id;
        }));
        Assert.Single(creates.Distinct());
        var leases=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>
        {
            using var db=Db();return await Store(db).TryAcquireAsync(creates[0],Guid.NewGuid(),TimeSpan.FromSeconds(30));
        }));
        Assert.Single(leases, lease=>lease is not null);
    }
    public void Dispose()
    {
        if (_postgres) { using var db=Db();db.Database.EnsureDeleted(); }
        _sqlite?.Dispose();
    }
}
