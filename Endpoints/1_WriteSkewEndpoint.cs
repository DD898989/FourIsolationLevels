using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;

namespace MyApiApp.Endpoints;

public static class WriteSkewEndpoint
{
    public static void MapWriteSkewEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/concurrency/write-skew", (DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbContextFactory) =>
        {
            var dbIsolationLevel = (System.Data.IsolationLevel)isolationLevel;
            var logs = new ConcurrentList<string>();
            logs.Add($"=== 啟動寫偏斜 (Write Skew) 演示 (隔離層級: {isolationLevel}) ===");

            // 初始化/重置 Table 資料
            using (var initContext = dbContextFactory.CreateDbContext())
            {
                EndpointsHelper.ResetTableData(initContext, new List<Account>
                {
                    new Account { Id = 1, Balance = 150.00m }
                });
            }
            logs.Add("資料表重置成功：帳戶 1 初始餘額為 150.00。");
            logs.Add("業務邏輯規則：帳戶餘額不可以為負數。最低提款門檻為 100.00。");

            var mres1 = new ManualResetEventSlim(false);
            var mres2 = new ManualResetEventSlim(false);
            var mres3 = new ManualResetEventSlim(false);

            Task taskA = Task.Run(() =>
            {
                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(dbIsolationLevel))
                    {
                        logs.Add("[交易 A] 啟動交易...");
                        var acc = context.Accounts.Single(a => a.Id == 1);
                        logs.Add($"[交易 A] 讀取帳戶 1 餘額 = {acc.Balance:F2}");

                        bool canWithdraw = acc.Balance >= 100.00m;
                        logs.Add($"[交易 A] 檢查餘額 >= 100 是否成立: {canWithdraw}");

                        mres1.Set(); // 通知交易 B 啟動並讀取餘額
                        mres2.Wait();

                        if (canWithdraw)
                        {
                            context.Database.ExecuteSqlRaw("UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1");
                            context.Entry(acc).Reload();
                            logs.Add($"[交易 A] 已成功扣款，更新餘額為 {acc.Balance:F2}");
                        }

                        transaction.Commit();
                        logs.Add("[交易 A] 交易提交成功！");

                        mres3.Set(); // 通知交易 B，交易 A 已提交完成
                    }
                }
            });

            var taskB = Task.Run(() =>
            {
                mres1.Wait();

                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(dbIsolationLevel))
                    {
                        logs.Add("[交易 B] 啟動交易...");
                        var acc = context.Accounts.Single(a => a.Id == 1);
                        logs.Add($"[交易 B] 讀取帳戶 1 餘額 = {acc.Balance:F2}");

                        bool canWithdraw = acc.Balance >= 100.00m;
                        logs.Add($"[交易 B] 檢查餘額 >= 100 是否成立: {canWithdraw}");

                        mres2.Set(); // 通知交易 A，交易 B 已讀取完畢
                        if (!mres3.Wait(10000))
                        {
                            logs.Add("[交易 B] 等待交易 A 提交超時（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止 交易B 讀取該筆資料）。");
                            logs.Close();
                            throw new Exception();
                        }

                        if (canWithdraw)
                        {
                            context.Database.ExecuteSqlRaw("UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1");
                            context.Entry(acc).Reload();
                            logs.Add($"[交易 B] 已成功扣款，更新餘額為 {acc.Balance:F2}");
                        }

                        transaction.Commit();
                        logs.Add("[交易 B] 交易提交成功！");
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

            using (var resultContext = dbContextFactory.CreateDbContext())
            {
                var acc = resultContext.Accounts.Single(a => a.Id == 1);
                logs.Add($"[最終結果] 帳戶 1 的最終餘額 = {acc.Balance:F2}");
                if (acc.Balance < 0)
                    logs.Add("--> 結論：寫偏斜 (Write Skew) 發生了！兩個交易都成功提交，導致最終餘額變為不合法的負數。");
                else
                    logs.Add("--> 結論：寫偏斜 (Write Skew) 已成功被防止！其中一個交易被鎖定阻擋或發生異常回滾，確保了餘額不為負數。");
            }

            return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
        })
        .WithSummary("1. 寫偏斜 (Write Skew) 演示")
        .WithDescription("### 寫偏斜 (Write Skew) 演示\n\n此端點用於演示在不同隔離層級下的寫偏斜現象。\n\n**[點擊此處下載原始碼 (1_WriteSkewEndpoint.cs)](/api/download/1_WriteSkewEndpoint.cs)**");
    }
}
