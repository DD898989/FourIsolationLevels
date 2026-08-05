using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;
using System.Data;

namespace MyApiApp.Endpoints;

public static class PhantomReadEndpoint
{
    public static void MapPhantomReadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/phantom-read", (DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbFactory) =>
        {
            var logs = new ConcurrentList<string>();

            using (var db = dbFactory.CreateDbContext())
            {
                EndpointsHelper.ResetTableData(db, new List<Account>
                {
                    new Account { Id = 1, Balance = 6000 },
                    new Account { Id = 5, Balance = 1000 }
                });
            }

            var mres1 = new ManualResetEventSlim(false);
            var mres2 = new ManualResetEventSlim(false);


            var taskA = Task.Run(() =>
            {
                using (var db = dbFactory.CreateDbContext())
                {
                    using (var tx = db.Database.BeginTransaction((IsolationLevel)isolationLevel))
                    {
                        var count1 = db.Database.SqlQueryRaw<int>(
                            "SELECT COUNT(*) AS Value FROM Accounts WHERE Balance >= 5000 AND Balance <= 10000"
                        ).AsEnumerable().First();
                        logs.Add($"[交易 A] 第一次查詢: 符合範圍的帳戶數量 = {count1}");

                        mres1.Set();

                        if (!mres2.Wait(4000))
                        {
                            logs.Add("[交易 A] 等待交易 B 提交超時，可能發生死鎖！（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止 交易B 讀取該筆資料）。");
                            logs.Close();
                            return;
                        }

                        // 第二次查詢 (在 MySQL/MariaDB RepeatableRead 隔離層級下，一般的 SELECT 藉由 MVCC 快照讀可防範幻讀。若要演示幻讀發生，需使用 LOCK IN SHARE MODE 進行當前讀以讀取最新提交的資料)
                        var count2 = db.Database.SqlQueryRaw<int>(
                            "SELECT COUNT(*) AS Value FROM Accounts WHERE Balance >= 5000 AND Balance <= 10000 LOCK IN SHARE MODE"
                        ).AsEnumerable().First();
                        logs.Add($"[交易 A] 第二次查詢: 符合範圍的帳戶數量 = {count2}");

                        tx.Commit();
                    }
                }
            });





            var taskB = Task.Run(() =>
            {
                mres1.Wait(30000);

                using (var db = dbFactory.CreateDbContext())
                {
                    using (var tx = db.Database.BeginTransaction())
                    {
                        db.Accounts.Add(new Account { Id = 2, Balance = 9999 });
                        db.SaveChanges();
                        tx.Commit();
                        logs.Add("[交易 B] 成功寫入新帳戶 2 (餘額 = 9999) 並已提交 Commit。");

                        mres2.Set();
                    }
                }
            });

            Task.WaitAll(taskA, taskB);

            bool hasPhantomRead = logs.ToList().Any(l => l.Contains("第一次查詢") && l.Contains("1")) &&
                                 logs.ToList().Any(l => l.Contains("第二次查詢") && l.Contains("2"));
            if (hasPhantomRead)
                logs.Add("--> 結論：幻讀 (Phantom Read) 發生了！交易 A 在同一個交易內重複查詢該範圍，卻看見了交易 B 新寫入並 Commit 的幻影行資料，數量從 1 筆變為 2 筆。");
            else
                logs.Add("--> 結論：幻讀 (Phantom Read) 已成功被防止！即使交易 B 已提交新列，交易 A 讀取到的數量依然維持一致不變。");

            return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
        })
        .WithSummary("3. 幻讀 (Phantom Read) 演示")
        .WithDescription("**[點擊此處下載原始碼](/api/download/3_PhantomReadEndpoint.cs)**");
    }
}
