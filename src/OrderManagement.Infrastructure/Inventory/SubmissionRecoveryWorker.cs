using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Inventory;

public sealed class SubmissionRecoveryWorker(IServiceScopeFactory scopes, ILogger<SubmissionRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(10));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Guid[] ids;
                using(var scope=scopes.CreateScope())
                {
                    var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var now=DateTime.UtcNow;
                    ids=await db.OrderSubmissions.AsNoTracking()
                        .Where(row=>(row.State=="Pending" || row.State=="Reserved" || row.State=="Compensating")
                            && (row.LeaseOwner==null || row.LeaseUntilUtc<=now))
                        .OrderBy(row=>row.UpdatedAtUtc).Take(10).Select(row=>row.Id).ToArrayAsync(stoppingToken);
                }
                foreach(var id in ids)
                {
                    using var scope=scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<OrderSubmissionCoordinator>().ProcessAsync(id,stoppingToken);
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception error){logger.LogWarning("Order recovery iteration failed. ErrorType: {ErrorType}",error.GetType().Name);}
        }
    }
}
