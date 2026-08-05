using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;
using System.Data;

namespace MyApiApp.Endpoints;

public static class NonRepeatableReadEndpoint
{
    public static void MapNonRepeatableReadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/non-repeatable-read", (DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbFactory) =>
        {
            var logs = new ConcurrentList<string>();

            using (var db = dbFactory.CreateDbContext())
            {
                EndpointsHelper.ResetTableData(db, new List<Account>
                {
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
                        var acc1 = db.Accounts.AsNoTracking().First(a => a.Id == 5);
                        logs.Add($"[交易 A] 第一次查詢: 帳戶 5 餘額 = {acc1.Balance}");

                        mres1.Set();

                        if (!mres2.Wait(4000))
                        {
                            logs.Add("[交易 A] 等待交易 B 提交超時，可能發生死鎖！（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止交易 B 修改該筆資料）。");
                            logs.Close();
                            return;
                        }

                        var acc2 = db.Accounts.AsNoTracking().First(a => a.Id == 5);
                        logs.Add($"[交易 A] 第二次查詢: 帳戶 5 餘額 = {acc2.Balance}");

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
                        var acc = db.Accounts.First(a => a.Id == 5);
                        acc.Balance = 700;
                        db.SaveChanges();
                        tx.Commit();
                        logs.Add("[交易 B] 已將帳戶 5 的餘額修改為 700 並已提交 Commit。");

                        mres2.Set();
                    }
                }
            });

            Task.WaitAll(taskA, taskB);

            bool hasNonRepeatableRead = logs.ToList().Any(l => l.Contains("第一次查詢") && l.Contains("1000")) &&
                                        logs.ToList().Any(l => l.Contains("第二次查詢") && l.Contains("700"));
            if (hasNonRepeatableRead)
                logs.Add("--> 結論：不可重複讀 (Non-Repeatable Read) 發生了！帳戶 5 的餘額在交易 A 的兩次讀取中從 1000 變成了 700，產生了資料不一致。");
            else
                logs.Add("--> 結論：不可重複讀 (Non-Repeatable Read) 已成功被防止！交易 A 重複查詢得到的餘額保持一致. ");

            return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
        })
        .WithSummary("4. 不可重複讀 (Non-Repeatable Read) 演示")
        .WithDescription("**[點擊此處下載原始碼](/api/download/4_NonRepeatableReadEndpoint.cs)**");
    }
}
