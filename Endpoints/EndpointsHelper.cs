using System.Data;
using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Models;

namespace MyApiApp.Endpoints;

public enum DemoIsolationLevel
{
    ReadUncommitted = IsolationLevel.ReadUncommitted,
    ReadCommitted = IsolationLevel.ReadCommitted,
    RepeatableRead = IsolationLevel.RepeatableRead,
    Serializable = IsolationLevel.Serializable
}

public static class EndpointsHelper
{
    // 清空資料表並重新寫入初始種子資料
    public static void ResetTableData(AppDbContext context, List<Account> seedData)
    {
        context.Accounts.ExecuteDelete();
        context.Accounts.AddRange(seedData);
        context.SaveChanges();
    }
}

// 執行緒安全的執行日誌清單，用於在併發環境下捕捉真實的即時時間線順序
public class ConcurrentList<T>
{
    private readonly List<T> _list = new();
    private readonly object _lock = new();
    private bool _isClosed = false;

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_isClosed) return;
            _list.Add(item);
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            _isClosed = true;
        }
    }


    public List<T> ToList()
    {
        lock (_lock)
            return new List<T>(_list);
    }
}
