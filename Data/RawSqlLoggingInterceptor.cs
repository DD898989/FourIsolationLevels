using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyApiApp.Data;

public class RawSqlLoggingInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        LogCommand(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        LogCommand(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        LogCommand(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        LogCommand(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        LogCommand(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        LogCommand(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void LogCommand(DbCommand command)
    {
        try
        {
            var sql = command.CommandText;

            // 依參數名稱長度降冪排序，避免字首重合干擾 (例如：@p10 先於 @p1 被替換)
            var parameters = command.Parameters
                .Cast<DbParameter>()
                .OrderByDescending(p => p.ParameterName.Length)
                .ToList();

            foreach (var parameter in parameters)
            {
                var value = parameter.Value;
                string valueStr;

                if (value == null || value == DBNull.Value)
                {
                    valueStr = "NULL";
                }
                else if (value is string || value is Guid || value is DateTime || value is DateOnly)
                {
                    var escapedValue = value.ToString()?.Replace("'", "''");
                    valueStr = $"'{escapedValue}'";
                }
                else if (value is bool b)
                {
                    valueStr = b ? "1" : "0";
                }
                else
                {
                    valueStr = value.ToString() ?? "NULL";
                }

                sql = sql.Replace(parameter.ParameterName, valueStr);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"================ [RAW SQL] (Thread ID: {Environment.CurrentManagedThreadId}) ================");
            Console.WriteLine(sql.Trim());
            Console.WriteLine("========================================================================");
            Console.ResetColor();
        }
        catch
        {
            // 發生解析例外時，退回列印原始 CommandText
            Console.WriteLine($"[RAW SQL Log Fail] {command.CommandText}");
        }
    }
}
