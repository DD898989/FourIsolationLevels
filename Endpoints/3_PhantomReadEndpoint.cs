using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;

namespace MyApiApp.Endpoints;

public static class PhantomReadEndpoint
{
    public static void MapPhantomReadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/concurrency/phantom-read", (DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbContextFactory) =>
        {
            var dbIsolationLevel = (System.Data.IsolationLevel)isolationLevel;
            var logs = new ConcurrentList<string>();
            logs.Add($"=== 啟動幻讀 (Phantom Read) 演示 (隔離層級: {isolationLevel}) ===");

            // 重置資料表資料
            using (var initContext = dbContextFactory.CreateDbContext())
            {
                EndpointsHelper.ResetTableData(initContext, new List<Account>
                {
                    new Account { Id = 1, Balance = 6000.00m },
                    new Account { Id = 5, Balance = 1000.00m }
                });
            }
            logs.Add("資料表重置成功：帳戶 1 餘額 = 6000.00、帳戶 5 餘額 = 1000.00。");
            logs.Add("查詢範圍：餘額在 5000 到 10000 之間的帳戶數量");

            var mres1 = new ManualResetEventSlim(false);
            var mres2 = new ManualResetEventSlim(false);


            Task taskA = Task.Run(() =>
            {
                using (var context = dbContextFactory.CreateDbContext())
                {
                    using (var transaction = context.Database.BeginTransaction(dbIsolationLevel))
                    {
                        // 第一次查詢
                        var count1 = context.Database.SqlQueryRaw<int>(
                            "SELECT COUNT(*) AS Value FROM Accounts WHERE Balance >= 5000 AND Balance <= 10000"
                        ).AsEnumerable().First();
                        logs.Add($"[交易 A] 第一次查詢: 符合範圍的帳戶數量 = {count1}");

                        mres1.Set(); // 通知交易 B 進行寫入

                        if (!mres2.Wait(10000))
                        {
                            logs.Add("[交易 A] 等待交易 B 提交超時，可能發生死鎖！（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止 交易B 讀取該筆資料）。");
                            logs.Close();
                            throw new Exception();
                        }

                        // 第二次查詢 (在 MySQL RepeatableRead 隔離層級下，一般的 SELECT 藉由 MVCC 快照讀可防範幻讀。若要演示幻讀發生，需使用 FOR SHARE 進行當前讀以讀取最新提交的資料)
                        var count2 = context.Database.SqlQueryRaw<int>(
                            "SELECT COUNT(*) AS Value FROM Accounts WHERE Balance >= 5000 AND Balance <= 10000 FOR SHARE"
                        ).AsEnumerable().First();
                        logs.Add($"[交易 A] 第二次查詢: 符合範圍的帳戶數量 = {count2}");

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
                        context.Accounts.Add(new Account { Id = 2, Balance = 9999.00m });
                        context.SaveChanges();
                        transaction.Commit();
                        logs.Add("[交易 B] 成功寫入新帳戶 2 (餘額 = 9999.00) 並已提交 Commit。");

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

            bool hasPhantomRead = logs.ToList().Any(l => l.Contains("第一次查詢") && l.Contains("1")) &&
                                 logs.ToList().Any(l => l.Contains("第二次查詢") && l.Contains("2"));
            if (hasPhantomRead)
                logs.Add("--> 結論：幻讀 (Phantom Read) 發生了！交易 A 在同一個交易內重複查詢該範圍，卻看見了交易 B 新寫入並 Commit 的幻影行資料，數量從 1 筆變為 2 筆。");
            else
                logs.Add("--> 結論：幻讀 (Phantom Read) 已成功被防止！即使交易 B 已提交新列，交易 A 讀取到的數量依然維持一致不變。");

            return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
        })
        .WithSummary("3. 幻讀 (Phantom Read) 演示")
        .WithDescription("### 幻讀 (Phantom Read) 演示\n\n此端點用於演示在不同隔離層級下的幻讀現象。\n\n**[點擊此處下載原始碼 (3_PhantomReadEndpoint.cs)](/api/download/3_PhantomReadEndpoint.cs)**");
    }
}
