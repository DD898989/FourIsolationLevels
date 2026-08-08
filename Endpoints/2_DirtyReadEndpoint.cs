using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;
using System.Data;
using System.Runtime.CompilerServices;

namespace MyApiApp.Endpoints;

public static class DirtyReadEndpoint
{
    public static string Description => $"**[點擊此處下載原始碼](/api/download/{GetFileName()})**";

    private static string GetFileName([CallerFilePath] string path = "") => Path.GetFileName(path);

    public static IResult Handle(DemoIsolationLevel isolationLevel, IDbContextFactory<AppDbContext> dbFactory)
    {
        var logs = new ConcurrentList<string>();

        using (var db = dbFactory.CreateDbContext())
        {
            EndpointsHelper.ResetTableData(db, new List<Account>
            {
                new Account { Id = 1, Balance = 1000 }
            });
        }

        var mres1 = new ManualResetEventSlim(false);
        var mres2 = new ManualResetEventSlim(false);
        var mres3 = new ManualResetEventSlim(false);

        var taskA = Task.Run(() =>
        {
            using (var db = dbFactory.CreateDbContext())
            {
                using (var tx = db.Database.BeginTransaction((IsolationLevel)isolationLevel))
                {
                    var acc1 = db.Accounts.AsNoTracking().First(a => a.Id == 1);
                    logs.Add($"[交易 A] 第一次查詢: 帳戶 1 餘額 = {acc1.Balance}");

                    mres1.Set();
                    if (!mres2.Wait(4000))
                    {
                        logs.Add($"[交易 A] 等待交易 B 更新逾時（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止 交易B update該筆資料）。");
                        logs.Close();
                        mres3.Set();
                        return;
                    }

                    // 第二次查詢 (檢查髒讀)
                    var acc2 = db.Accounts.AsNoTracking().First(a => a.Id == 1);
                    logs.Add($"[交易 A] 第二次查詢 (髒讀檢查): 帳戶 1 餘額 = {acc2.Balance}");

                    mres3.Set();
                    Thread.Sleep(500);

                    var acc3 = db.Accounts.AsNoTracking().First(a => a.Id == 1);
                    logs.Add($"[交易 A] 第三次查詢: 帳戶 1 餘額 = {acc3.Balance}");

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
                    var acc = db.Accounts.First(a => a.Id == 1);
                    acc.Balance = 800;
                    db.SaveChanges();
                    logs.Add("[交易 B] 已將帳戶 1 的餘額修改為 800 (未提交)");

                    mres2.Set();
                    mres3.Wait(30000);

                    tx.Rollback();
                    logs.Add("[交易 B] 交易執行回滾 (Rollback)。");
                }
            }
        });

        Task.WaitAll(taskA, taskB);

        bool hasDirtyRead = logs.ToList().Any(l => l.Contains("第二次查詢") && l.Contains("800"));
        if (hasDirtyRead)
            logs.Add("--> 結論：髒讀 (Dirty Read) 發生了！交易 A 成功讀取到交易 B 尚未提交的暫時性資料 (800)。");
        else
            logs.Add("--> 結論：髒讀 (Dirty Read) 已成功被防止！交易 A 僅能讀取到已被 Commit 提交的正確資料 (1000)。");

        return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
    }
}
