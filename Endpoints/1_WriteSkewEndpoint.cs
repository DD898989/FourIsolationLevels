using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;
using System.Data;
using System.Runtime.CompilerServices;

namespace MyApiApp.Endpoints;

public static class WriteSkewEndpoint
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
                new Account { Id = 1, Balance = 150 }
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
                    logs.Add("[交易 A] 啟動交易...");
                    var acc = db.Accounts.First(a => a.Id == 1);
                    logs.Add($"[交易 A] 讀取帳戶 1 餘額 = {acc.Balance}");

                    bool canWithdraw = acc.Balance >= 100;
                    logs.Add($"[交易 A] 檢查餘額 >= 100 是否成立: {canWithdraw}");

                    mres1.Set();
                    mres2.Wait(30000);

                    if (canWithdraw)
                    {
                        db.Accounts.Where(a => a.Id == 1).ExecuteUpdate(s => s.SetProperty(
                            a => a.Balance,
                            a => a.Balance - 100
                        ));

                        db.Entry(acc).Reload();

                        logs.Add($"[交易 A] 已成功扣款，更新餘額為 {acc.Balance}");
                    }

                    tx.Commit();
                    logs.Add("[交易 A] 交易提交成功！");

                    mres3.Set();
                }
            }
        });

        var taskB = Task.Run(() =>
        {
            mres1.Wait(30000);

            using (var db = dbFactory.CreateDbContext())
            {
                using (var tx = db.Database.BeginTransaction((IsolationLevel)isolationLevel))
                {
                    logs.Add("[交易 B] 啟動交易...");
                    var acc = db.Accounts.First(a => a.Id == 1);
                    logs.Add($"[交易 B] 讀取帳戶 1 餘額 = {acc.Balance}");

                    bool canWithdraw = acc.Balance >= 100;
                    logs.Add($"[交易 B] 檢查餘額 >= 100 是否成立: {canWithdraw}");

                    mres2.Set();
                    if (!mres3.Wait(4000))
                    {
                        logs.Add("[交易 B] 等待交易 A 提交超時（在 Serializable 隔離層級下，此為正常的鎖定阻塞現象，代表成功阻止 交易A update 該筆資料）。");
                        logs.Close();
                        return;
                    }

                    if (canWithdraw)
                    {
                        db.Accounts.Where(a => a.Id == 1).ExecuteUpdate(s => s.SetProperty(
                            a => a.Balance,
                            a => a.Balance - 100
                        ));

                        db.Entry(acc).Reload();

                        logs.Add($"[交易 B] 已成功扣款，更新餘額為 {acc.Balance}");
                    }

                    tx.Commit();
                    logs.Add("[交易 B] 交易提交成功！");
                }
            }
        });

        Task.WaitAll(taskA, taskB);

        using (var db = dbFactory.CreateDbContext())
        {
            var acc = db.Accounts.First(a => a.Id == 1);
            logs.Add($"[最終結果] 帳戶 1 的最終餘額 = {acc.Balance}");
            if (acc.Balance < 0)
                logs.Add("--> 結論：寫偏斜 (Write Skew) 發生了！兩個交易都成功提交，導致最終餘額變為不合法的負數。");
            else
                logs.Add("--> 結論：寫偏斜 (Write Skew) 已成功被防止！其中一個交易被鎖定阻擋或發生異常回滾，確保了餘額不為負數。");
        }

        return Results.Text(string.Join("\n", logs.ToList()), "text/plain; charset=utf-8");
    }
}
