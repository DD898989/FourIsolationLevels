using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace MyApiApp.Data;

public class TransactionLoggingInterceptor : DbTransactionInterceptor
{
    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result)
    {
        LogTransactionStarting(eventData);
        return base.TransactionStarting(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        LogTransactionStarting(eventData);
        return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
    }

    public override void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData)
    {
        LogTransactionEnded(eventData, "COMMITTED");
        base.TransactionCommitted(transaction, eventData);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogTransactionEnded(eventData, "COMMITTED");
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(
        DbTransaction transaction,
        TransactionEndEventData eventData)
    {
        LogTransactionEnded(eventData, "ROLLED BACK");
        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogTransactionEnded(eventData, "ROLLED BACK");
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    private void LogTransactionStarting(TransactionStartingEventData eventData)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"================ [TRANSACTION START] (Thread ID: {Environment.CurrentManagedThreadId}) ================");
        Console.WriteLine($"Action: Begin Transaction");
        Console.WriteLine($"Isolation Level: {eventData.IsolationLevel}");
        Console.WriteLine($"Transaction ID: {eventData.TransactionId}");
        Console.WriteLine("=====================================================================================");
        Console.ResetColor();
    }

    private void LogTransactionEnded(TransactionEndEventData eventData, string status)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"================ [TRANSACTION END] (Thread ID: {Environment.CurrentManagedThreadId}) ================");
        Console.WriteLine($"Action: Transaction {status}");
        Console.WriteLine($"Transaction ID: {eventData.TransactionId}");
        Console.WriteLine($"Duration: {eventData.Duration.TotalMilliseconds} ms");
        Console.WriteLine("===================================================================================");
        Console.ResetColor();
    }
}
