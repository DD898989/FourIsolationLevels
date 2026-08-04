using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;

namespace MyApiApp.Endpoints;

public static class DirtyReadEndpoint
{
    public static void MapDirtyReadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/concurrency/dirty-read", (DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbContextFactory) =>
        {
            var dbIsolationLevel = (System.Data.IsolationLevel)isolationLevel;
            var logs = new ConcurrentList<string>();
            logs.Add($"=== 啟動髒讀 (Dirty Read) 演示 (隔離層級: {isolationLevel}) ===");

            // 重置資料表資料
            using (var initContext = dbContextFactory.CreateDbContext())
            {
                EndpointsHelper.ResetTableData(initContext, new List<Account>
                {
                    new Account { Id = 1, Balance = 1000.00m }
                });
            }
            logs.Add("資料表重置成功：帳戶 1 初始餘額為 1000.00。");

            var mres1 = new ManualResetEventSlim(false);
            var mres2 = new ManualResetEventSlim(false);
            var mres3 = new ManualResetEventSlim(false);

            Task taskA = Task.Run(() =>
            {
                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(dbIsolationLevel))
                    {
                        // 第一次查詢
                        var acc1 = context.Accounts.AsNoTracking().Single(a => a.Id == 1);
                        logs.Add($"[交易 A] 第一次查詢: 帳戶 1 餘額 = {acc1.Balance:F2}");

                        mres1.Set(); // 通知交易 B 進行更新
                        if (!mres2.Wait(10000))
                        {
                            logs.Add($"[交易 A] 等待交易 B 更新逾時（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止 交易B 讀取該筆資料）。");
                            logs.Close();
                            throw new Exception();
                        }

                        // 第二次查詢 (檢查髒讀)
                        var acc2 = context.Accounts.AsNoTracking().Single(a => a.Id == 1);
                        logs.Add($"[交易 A] 第二次查詢 (髒讀檢查): 帳戶 1 餘額 = {acc2.Balance:F2}");

                        mres3.Set(); // 通知交易 B 執行回滾
                        Thread.Sleep(500); // 延遲以確保交易 B 完成回滾

                        // 第三次查詢
                        var acc3 = context.Accounts.AsNoTracking().Single(a => a.Id == 1);
                        logs.Add($"[交易 A] 第三次查詢: 帳戶 1 餘額 = {acc3.Balance:F2}");

                        transaction.Commit();
                    }
                }
            });


            var taskB = Task.Run(() =>
            {
                mres1.Wait();

                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        var acc = context.Accounts.Single(a => a.Id == 1);
                        acc.Balance = 800.00m;
                        context.SaveChanges();
                        logs.Add("[交易 B] 已將帳戶 1 的餘額修改為 800.00 (未提交)");

                        mres2.Set(); // 通知交易 A 進行第二次查詢
                        mres3.Wait();

                        transaction.Rollback();
                        logs.Add("[交易 B] 交易執行回滾 (Rollback)。");
                    }
                }
            });

            try
            {
                Task.WaitAll(taskA, taskB);
            }
            catch
            {
                return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
            }

            bool hasDirtyRead = logs.ToList().Any(l => l.Contains("第二次查詢") && l.Contains("800"));
            if (hasDirtyRead)
                logs.Add("--> 結論：髒讀 (Dirty Read) 發生了！交易 A 成功讀取到交易 B 尚未提交的暫時性資料 (800.00)。");
            else
                logs.Add("--> 結論：髒讀 (Dirty Read) 已成功被防止！交易 A 僅能讀取到已被 Commit 提交的正確資料 (1000.00)。");

            return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
        })
        .WithSummary("2. 髒讀 (Dirty Read) 演示")
        .WithDescription("### 髒讀 (Dirty Read) 演示\n\n此端點用於演示在不同隔離層級下的髒讀現象。\n\n**[點擊此處下載原始碼 (2_DirtyReadEndpoint.cs)](/api/download/2_DirtyReadEndpoint.cs)**");
    }
}
