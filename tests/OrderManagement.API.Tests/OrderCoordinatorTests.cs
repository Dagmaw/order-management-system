using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;
using OrderManagement.Infrastructure.Inventory;
using OrderManagement.Infrastructure.Repositories;

namespace OrderManagement.API.Tests;

public sealed class OrderCoordinatorTests : IDisposable
{
    private readonly SqliteConnection _connection=new("Data Source=:memory:");
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly FakeInventory _inventory=new();
    public OrderCoordinatorTests()
    {
        _connection.Open();_options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        using var db=Db();db.Database.EnsureCreated();
        db.DomainUsers.Add(new User {Id=1,Name="Test",Email="coordinator@example.com"});
        db.Products.Add(new Product {Id=1,Name="Test",Description="Test",Price=30});db.SaveChanges();
    }
    private ApplicationDbContext Db()=>new(_options);
    private OrderSubmissionCoordinator Coordinator(ApplicationDbContext db)=>new(db,new OrderSubmissionStore(db,TimeProvider.System),
        new OrderService(new OrderRepository(db),new UserRepository(db),new ProductRepository(db)),_inventory,
        TimeProvider.System,NullLogger<OrderSubmissionCoordinator>.Instance);
    private static CreateOrderDto Request(int quantity=2)=>new(){UserId=1,Items=[new(){ProductId=1,Quantity=quantity}]};
    [Fact]
    public async Task RepeatedOrderCreatesOneOrderAndPreservesFirstPriceSnapshot()
    {
        var key=Guid.NewGuid();Guid submission;int? order;
        using(var db=Db()) {var result=await Coordinator(db).SubmitAsync("actor",key,Request(),default);submission=result.Id;order=result.OrderId;Assert.Equal("Completed",result.State);}
        using(var db=Db()){var product=await db.Products.FindAsync(1);product!.Price=99;await db.SaveChangesAsync();}
        using(var db=Db())
        {
            var replay=await Coordinator(db).SubmitAsync("actor",key,Request(),default);
            Assert.Equal(submission,replay.Id);Assert.Equal(order,replay.OrderId);Assert.Equal(1,await db.Orders.CountAsync());
            Assert.Equal(60,(await db.Orders.SingleAsync()).TotalAmount);
            await Assert.ThrowsAsync<DataConflictException>(()=>Coordinator(db).SubmitAsync("actor",key,Request(3),default));
            Assert.Null(await Coordinator(db).GetOwnedAsync(submission,"other",default));
        }
        Assert.Equal(1,_inventory.Deductions);Assert.Equal(0,_inventory.Releases);
    }
    [Fact]
    public async Task LostReserveResponseRecoversWithSameIdentityAfterNewContext()
    {
        var key=Guid.NewGuid();_inventory.LoseResponse=true;Guid id;
        using(var db=Db()){var result=await Coordinator(db).SubmitAsync("actor",key,Request(),default);id=result.Id;Assert.Equal("Pending",result.State);Assert.Empty(db.Orders);}
        using(var db=Db())await Coordinator(db).ProcessAsync(id,default);
        using(var db=Db()){Assert.Equal("Completed",(await db.OrderSubmissions.SingleAsync()).State);Assert.Equal(1,await db.Orders.CountAsync());}
        Assert.Equal(1,_inventory.Deductions);
    }
    [Fact]
    public async Task RejectionIsFencedAndReleaseFailureRecovered()
    {
        _inventory.Reject=true;_inventory.FailRelease=true;Guid id;
        using(var db=Db()){var result=await Coordinator(db).SubmitAsync("actor",Guid.NewGuid(),Request(),default);id=result.Id;Assert.Equal("Compensating",result.State);}
        using(var db=Db())await Coordinator(db).ProcessAsync(id,default);
        using(var db=Db()){Assert.Equal("Compensated",(await db.OrderSubmissions.SingleAsync()).State);Assert.Empty(db.Orders);}
        Assert.Equal("RELEASED",_inventory.State);Assert.Equal(0,_inventory.Deductions);
    }
    private sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        private bool _first=true;
        public override Task TransactionCommittedAsync(DbTransaction transaction,TransactionEndEventData eventData,CancellationToken cancellationToken=default)
        {
            if(_first){_first=false;throw new TimeoutException("Simulated acknowledgement loss after commit.");}
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task LostDatabaseCommitAcknowledgementMustNotReleaseAnExistingOrdersStock()
    {
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection)
            .AddInterceptors(new LostCommitAcknowledgement()).Options;
        SubmissionResult result;
        using(var db=new ApplicationDbContext(options))
            result=await Coordinator(db).SubmitAsync("actor",Guid.NewGuid(),Request(),default);
        Assert.Equal("Completed",result.State);
        Assert.NotNull(result.OrderId);
        Assert.Equal(0,_inventory.Releases);
        using var verify=Db();Assert.Equal(1,await verify.Orders.CountAsync());
    }
    private sealed class FakeInventory:IInventoryClient
    {
        public int Deductions,Releases;public bool LoseResponse,Reject,FailRelease;public string? State;
        private Guid _id;private IReadOnlyList<InventoryReservationItem> _items=[];
        public Task<InventoryReservation> ReserveAsync(Guid id,IReadOnlyList<InventoryReservationItem> items,CancellationToken ct=default)
        {
            if(Reject || State=="RELEASED")throw new InventoryRejectedException(409);
            if(State is null){State="RESERVED";Deductions++;_id=id;_items=items;}
            Assert.Equal(_id,id);
            if(LoseResponse){LoseResponse=false;throw new InventoryUnavailableException(true);}
            return Task.FromResult(new InventoryReservation(id,State,_items));
        }
        public Task<InventoryReservation?> GetReservationAsync(Guid id,CancellationToken ct=default)
            =>Task.FromResult(State is null?null:new InventoryReservation(id,State,_items));
        public Task<InventoryReservation> ReleaseAsync(Guid id,CancellationToken ct=default)
        {
            if(FailRelease){FailRelease=false;throw new InventoryUnavailableException(true);}
            if(State=="RESERVED")Releases++;
            State="RELEASED";return Task.FromResult(new InventoryReservation(id,State,_items));
        }
    }
    public void Dispose()=>_connection.Dispose();
}
