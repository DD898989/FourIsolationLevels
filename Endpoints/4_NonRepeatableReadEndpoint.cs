using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;

namespace MyApiApp.Endpoints;

public static class NonRepeatableReadEndpoint
{
    public static void MapNonRepeatableReadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/concurrency/non-repeatable-read", (DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbContextFactory) =>
        {
            var dbIsolationLevel = (System.Data.IsolationLevel)isolationLevel;
            var logs = new ConcurrentList<string>();
            logs.Add($"=== 啟動不可重複讀 (Non-Repeatable Read) 演示 (隔離層級: {isolationLevel}) ===");

            // 重置資料表資料
            using (var initContext = dbContextFactory.CreateDbContext())
            {
                EndpointsHelper.ResetTableData(initContext, new List<Account>
                {
                    new Account { Id = 5, Balance = 1000.00m }
                });
            }
            logs.Add("資料表重置成功：帳戶 5 初始餘額為 1000.00。");

            var mres1 = new ManualResetEventSlim(false);
            var mres2 = new ManualResetEventSlim(false);

            Task taskA = Task.Run(() =>
            {
                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(dbIsolationLevel))
                    {
                        // 第一次查詢
                        var acc1 = context.Accounts.AsNoTracking().Single(a => a.Id == 5);
                        logs.Add($"[交易 A] 第一次查詢: 帳戶 5 餘額 = {acc1.Balance:F2}");

                        mres1.Set(); // 通知交易 B 進行修改

                        if (!mres2.Wait(10000))
                        {
                            logs.Add("[交易 A] 等待交易 B 提交超時，可能發生死鎖！（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止交易 B 修改該筆資料）。");
                            logs.Close();
                            throw new Exception();
                        }

                        // 第二次查詢 (檢查不可重複讀)
                        var acc2 = context.Accounts.AsNoTracking().Single(a => a.Id == 5);
                        logs.Add($"[交易 A] 第二次查詢: 帳戶 5 餘額 = {acc2.Balance:F2}");

                        transaction.Commit();
                    }
                }
            });

            var taskB = Task.Run(() =>
            {
                mres1.Wait(); // 等待交易 A 的第一次查詢

                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        var acc = context.Accounts.Single(a => a.Id == 5);
                        acc.Balance = 700.00m;
                        context.SaveChanges();
                        transaction.Commit();
                        logs.Add("[交易 B] 已將帳戶 5 的餘額修改為 700.00 並已提交 Commit。");

                        mres2.Set(); // 通知交易 A 進行第二次查詢
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

            bool hasNonRepeatableRead = logs.ToList().Any(l => l.Contains("第一次查詢") && l.Contains("1000")) &&
                                        logs.ToList().Any(l => l.Contains("第二次查詢") && l.Contains("700"));
            if (hasNonRepeatableRead)
                logs.Add("--> 結論：不可重複讀 (Non-Repeatable Read) 發生了！帳戶 5 的餘額在交易 A 的兩次讀取中從 1000.00 變成了 700.00，產生了資料不一致。");
            else
                logs.Add("--> 結論：不可重複讀 (Non-Repeatable Read) 已成功被防止！交易 A 重複查詢得到的餘額保持一致. ");

            return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
        })
        .WithSummary("4. 不可重複讀 (Non-Repeatable Read) 演示")
        .WithDescription("### 不可重複讀 (Non-Repeatable Read) 演示\n\n此端點用於演示在不同隔離層級下的不可重複讀現象。\n\n**[點擊此處下載原始碼 (4_NonRepeatableReadEndpoint.cs)](/api/download/4_NonRepeatableReadEndpoint.cs)**");
    }
}
