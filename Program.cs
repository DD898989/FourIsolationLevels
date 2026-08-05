using Microsoft.EntityFrameworkCore;
using MyApiApp.Data;
using MyApiApp.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// 設定 JSON 序列化，將 Enum 轉換為字串（在 Swagger/OpenAPI 上會顯示為下拉選單字串而非數字）
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// 註冊 DbContext 與 DbContextFactory
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// 註冊 IDbContextFactory 用於多執行緒背景併發任務
var serverVersion = new MariaDbServerVersion(new Version(10, 11, 0));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));


// 註冊 OpenAPI 服務
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Description = @"### Read uncommitted
這是最低的隔離級別。在這個級別中，一個事務(Transaction)可以讀取另一個還未提交的事務的數據

在 Read uncommitted 還是會發生下列的問題：
- 寫偏斜 (Write Skew)
- 髒讀 (Dirty Reads)
- 幻讀 (Phantom Reads)
- 不可重複讀 (Non-Repeatable Reads)

<br/>

### Read committed
這個級別保證一個事務只能讀取已經提交的事務的數據，這是許多資料庫系統的默認隔離級別

在 Read committed 還是會發生下列的問題：
- 寫偏斜 (Write Skew)
- 幻讀 (Phantom Reads)
- 不可重複讀 (Non-Repeatable Reads)

<br/>

### Repeatable read
在這個級別中，一個事務在整個過程中都可以看到一個一致的數據視圖。即使其他事務已經提交了新的數據，它仍然可以看到數據的舊版本

在 Repeatable read 還是會發生下列的問題：
- 寫偏斜 (Write Skew)
- 幻讀 (Phantom Reads)

<br/>

### Serializable
這是最高的隔離級別。它通過完全封鎖對相同數據的同時訪問來避免所有的並發問題

在這個級別中，可以完全解決上述提到的並行交易(Concurrency Transactions) 帶來的問題，因為他會讓所有交易序列化排程，來確保即使交易是並發執行的，它們也會產生與某種串行執行相同的效果";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// 啟用 OpenAPI 文件與 Swagger UI
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "v1");
});

// 使用 .NET 10 擴充方法樣式映射 Minimal API 端點
app.MapWriteSkewEndpoint();
app.MapDirtyReadEndpoint();
app.MapPhantomReadEndpoint();
app.MapNonRepeatableReadEndpoint();

// 註冊原始碼檔案下載端點
app.MapGet("/api/download/{fileName}", (string fileName) =>
{
    var filePath = Path.Combine(builder.Environment.ContentRootPath, "Endpoints", fileName);
    return Results.File(filePath, "text/plain", fileName);
})
.ExcludeFromDescription();

// 確保資料庫與資料表在啟動時已建立（但不刪除原有結構）
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();
}

app.Run();
