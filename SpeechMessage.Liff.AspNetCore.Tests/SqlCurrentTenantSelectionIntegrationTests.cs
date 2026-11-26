/*
檔案整體說明
目的與定位：F6 目前租戶選擇的真實 SQL 語意測試，獨立於離線驗證，保護完整二進位主鍵、資料庫原子更新與故障排空。
主要功能與流程：每案建立固定本機 MSSQLLocalDB 上的隨機專用庫，再呼叫正式 store 或正式部署腳本；以六十四筆競爭、觸發器拋錯及另一條持鎖連線注入真實故障，從獨立 SQL 查詢斷言唯一列、完整鍵隔離、回滾及取消後可重試。
使用邊界與維護：Category=SqlIntegration 預設排除，不能把未選取當通過；不讀部署秘密、不連遠端 SQL 或 LINE。案例 owner 最後只清本案連線池、強制回滾並刪除自己的庫，查 sys.databases 與資料檔確認零殘留。所有命令有期限，原始生命週期證據只存工作者的忽略目錄；LocalDB 結果不代表遠端 TLS 或平台部署驗收。
*/
using System.Data;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SpeechMessage.Liff.AspNetCore.Tests;

/// <summary>使用真本機 SQL；缺少環境直接失敗而不 skip，每案建立與清除獨立合成資料庫。</summary>
[Trait("Category", "SqlIntegration")]
public sealed class SqlCurrentTenantSelectionIntegrationTests
{
    /// <summary>正式建表與部署腳本兩條路徑都須符合相同 schema，重複初始化不改寫資料或多建索引。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialize_ExactSchema_IsIdempotent(bool useScript)
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        if (useScript)
        {
            await database.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(SelectionDatabase.Root, "SpeechMessage.Liff.AspNetCore", "line_current_selection.sql")));
        }
        await store.InitializeAsync(default);
        var contact = Guid.NewGuid();
        await store.SetAsync("P", "C", "U", contact, default);
        await store.InitializeAsync(default);
        await store.ValidateAsync(default);
        Assert.Equal(contact, await store.GetAsync("P", "C", "U", default));
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.line_current_selection')"));
    }

    /// <summary>舊表即使名稱相同，只要鍵定序、長度、額外欄位或主鍵不符，都必須拒絕而非默認採用。</summary>
    [Theory]
    [InlineData("wrong-collation")]
    [InlineData("wrong-length")]
    [InlineData("extra-column")]
    [InlineData("wrong-key")]
    public async Task Initialize_IncompatibleExistingSchema_FailsClosed(string fault)
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var sql = "CREATE TABLE dbo.line_current_selection(product nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,channel_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,line_user_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,contact_id uniqueidentifier NOT NULL,CONSTRAINT PK_line_current_selection PRIMARY KEY(product,channel_key,line_user_id))";
        sql = fault switch
        {
            "wrong-collation" => sql.Replace("Latin1_General_100_BIN2", "Latin1_General_100_CI_AS", StringComparison.Ordinal),
            "wrong-length" => sql.Replace("nvarchar(128)", "nvarchar(127)", StringComparison.Ordinal),
            "extra-column" => sql.Replace("contact_id uniqueidentifier NOT NULL,", "contact_id uniqueidentifier NOT NULL,extra nvarchar(10) NULL,", StringComparison.Ordinal),
            "wrong-key" => sql.Replace("PRIMARY KEY(product,channel_key,line_user_id)", "PRIMARY KEY(product,channel_key)", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("Unknown synthetic fault.")
        };
        await database.ExecuteAsync(sql);
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        var failure = await Assert.ThrowsAsync<SqlException>(() => store.InitializeAsync(default));
        Assert.Equal(51060, failure.Number);
        await Assert.ThrowsAsync<SqlException>(() => store.ValidateAsync(default));
    }

    /// <summary>三輪各六十四筆真正 SQL 操作同時送入相同鍵；每輪只有一列且 ContactId 必為本輪完整已提交值。</summary>
    [Fact]
    public async Task Set_64ConcurrentWriters_SingleCompleteSelection()
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        await store.InitializeAsync(default);
        for (var round = 0; round < 3; round++)
        {
            var candidates = Enumerable.Range(0, 64).Select(_ => Guid.NewGuid()).ToArray();
            await Task.WhenAll(candidates.Select(contact => store.SetAsync("Product", "Channel", "User", contact, default)));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.line_current_selection"));
            var selected = await store.GetAsync("Product", "Channel", "User", default);
            Assert.NotNull(selected);
            Assert.Contains(selected.Value, candidates);
        }
        await database.AssertDrainAsync();
    }

    /// <summary>相同顯示內容或大小寫不同的完整穩定鍵均不能混用；修改其中一列不影響其他產品、頻道或主體。</summary>
    [Fact]
    public async Task Set_64FullScopesAndCaseVariants_RemainIsolated()
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        await store.InitializeAsync(default);
        var rows = Enumerable.Range(0, 64).Select(index => new
        {
            Product = (index & 1) == 0 ? "Product" : "product",
            Channel = (index & 2) == 0 ? "Channel" : "channel",
            User = ((index & 4) == 0 ? "User" : "user") + (index / 8).ToString(CultureInfo.InvariantCulture),
            Contact = Guid.NewGuid()
        }).ToArray();
        await Task.WhenAll(rows.Select(row => store.SetAsync(row.Product, row.Channel, row.User, row.Contact, default)));
        foreach (var row in rows)
        {
            Assert.Equal(row.Contact, await store.GetAsync(row.Product, row.Channel, row.User, default));
        }
        var replacement = Guid.NewGuid();
        await store.SetAsync(rows[0].Product, rows[0].Channel, rows[0].User, replacement, default);
        Assert.Equal(replacement, await store.GetAsync(rows[0].Product, rows[0].Channel, rows[0].User, default));
        Assert.Equal(rows[1].Contact, await store.GetAsync(rows[1].Product, rows[1].Channel, rows[1].User, default));
        Assert.Equal(64, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.line_current_selection"));
        Assert.Null(await store.GetAsync("Other", "Channel", "User", default));
        await database.AssertDrainAsync();
    }

    /// <summary>真正 SQL 觸發器在 INSERT 或 UPDATE 後拋錯，整個選擇交易必須回滾且原始 SqlException 向上傳遞，解除故障後可重試。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Set_RealSqlFailure_RollsBackAndRetries(bool existing)
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        await store.InitializeAsync(default);
        var before = Guid.NewGuid();
        if (existing) await store.SetAsync("P", "C", "U", before, default);
        await database.ExecuteAsync("CREATE TRIGGER dbo.F6_Fail ON dbo.line_current_selection AFTER INSERT,UPDATE AS BEGIN THROW 51061,'Synthetic selection failure.',1; END");
        var error = await Assert.ThrowsAsync<SqlException>(() => store.SetAsync("P", "C", "U", Guid.NewGuid(), default));
        Assert.Equal(51061, error.Number);
        Assert.Equal(existing ? before : (Guid?)null, await store.GetAsync("P", "C", "U", default));
        await database.ExecuteAsync("DROP TRIGGER dbo.F6_Fail");
        var after = Guid.NewGuid();
        await store.SetAsync("P", "C", "U", after, default);
        Assert.Equal(after, await store.GetAsync("P", "C", "U", default));
        await database.AssertDrainAsync();
    }

    /// <summary>另一條真連線持有目標範圍鎖，以 DMV 看見等待後才取消；接受取消例外或 SQL 的零號取消錯誤，其餘 SQL 錯誤不得冒充取消，之後驗回滾、重試與 drain。</summary>
    [Fact]
    public async Task Set_CancelBlockedSql_DrainsAndCanRetry()
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        await store.InitializeAsync(default);
        var before = Guid.NewGuid();
        await store.SetAsync("P", "C", "U", before, default);
        await using var blocker = new SqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using var held = blocker.CreateCommand();
        held.Transaction = transaction;
        held.CommandTimeout = 5;
        held.CommandText = "UPDATE dbo.line_current_selection SET contact_id=contact_id WHERE product=N'P' AND channel_key=N'C' AND line_user_id=N'U'";
        await held.ExecuteNonQueryAsync();
        using var caller = new CancellationTokenSource();
        Task? waiting = null;
        try
        {
            waiting = store.SetAsync("P", "C", "U", Guid.NewGuid(), caller.Token);
            var watch = Stopwatch.StartNew();
            while (await database.ScalarAsync("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id>0") == 0)
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "No real SQL lock waiter observed.");
                await Task.Delay(10);
            }
            await caller.CancelAsync();
            var cancelled = await Record.ExceptionAsync(() => waiting);
            if (cancelled is SqlException sqlFailure)
            {
                await File.WriteAllTextAsync(Path.Combine(SelectionDatabase.Root, ".ccg", "dual-model-runs", "20261126-f6-sql", "cancel-error-codes.json"), System.Text.Json.JsonSerializer.Serialize(sqlFailure.Errors.Cast<SqlError>().Select(error => new { error.Number, error.Class, error.State })));
            }
            Assert.True(caller.IsCancellationRequested);
            Assert.NotNull(cancelled);
            Assert.True(cancelled is OperationCanceledException || cancelled is SqlException { Number: 0 }, "Cancellation must remain a provider failure, not a successful write or an unrelated SQL error.");
            if (cancelled is SqlException sqlCancelled)
            {
                Assert.All(sqlCancelled.Errors.Cast<SqlError>(), error => Assert.Equal(0, error.Number));
            }
        }
        finally
        {
            await caller.CancelAsync();
            await transaction.RollbackAsync();
            if (waiting is not null && !waiting.IsCompleted)
            {
                // 若故障發生在等待探針，仍先 drain 真正 SQL 工作再讓 fixture 刪除自己的庫。
                await Record.ExceptionAsync(() => waiting);
            }
        }
        await blocker.CloseAsync();
        Assert.Equal(before, await store.GetAsync("P", "C", "U", default));
        await store.SetAsync("P", "C", "U", Guid.NewGuid(), default);
        await database.AssertDrainAsync();
    }

    /// <summary>未初始化或已遺失資料表的正常查詢必須傳播 SQL 故障，不能回 null 冒充「尚未選擇」。</summary>
    [Fact]
    public async Task Get_MissingTable_PropagatesFailure()
    {
        await using var database = await SelectionDatabase.CreateAsync();
        var store = new SqlCurrentTenantSelectionStore(database.ConnectionString);
        await Assert.ThrowsAsync<SqlException>(() => store.GetAsync("P", "C", "U", default));
        await Assert.ThrowsAsync<SqlException>(() => store.ValidateAsync(default));
        await database.AssertDrainAsync();
    }

    /// <summary>單一案例 SQL owner；固定 LocalDB、無部署設定，每條測試連線／命令自己釋放，最後只移除本案庫及本案池。</summary>
    private sealed class SelectionDatabase : IAsyncDisposable
    {
        private const string Master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=false;Pooling=false;Connect Timeout=5;ConnectRetryCount=0;Enlist=false";
        private readonly string name = "F6Selection_" + Guid.NewGuid().ToString("N");
        private readonly List<string> files = [];
        private bool created;

        /// <summary>合成庫使用目前 Windows 主體及強制 TLS；不存取既有業務資料庫。</summary>
        internal string ConnectionString => new SqlConnectionStringBuilder(Master) { InitialCatalog = name, Pooling = true, MaxPoolSize = 64, MinPoolSize = 0, MultipleActiveResultSets = false }.ConnectionString;

        /// <summary>只向上定位目前 checkout 的方案根，證據及正式腳本均限同一工作樹。</summary>
        internal static string Root
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) directory = directory.Parent;
                return directory?.FullName ?? throw new InvalidOperationException("Worktree not found.");
            }
        }

        /// <summary>建立隨機合成庫並登記實際資料檔；初始化故障也先刪除自己的庫，絕不遺留待下次測試清理。</summary>
        internal static async Task<SelectionDatabase> CreateAsync()
        {
            Directory.CreateDirectory(Path.Combine(Root, ".ccg", "dual-model-runs", "20261126-f6-sql"));
            var database = new SelectionDatabase();
            try
            {
                await using var connection = new SqlConnection(Master);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 10;
                command.CommandText = "CREATE DATABASE [" + database.name + "]";
                database.created = true;
                await command.ExecuteNonQueryAsync();
                command.CommandText = "SELECT physical_name FROM sys.master_files WHERE database_id=DB_ID(@name)";
                command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = database.name });
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) database.files.Add(reader.GetString(0));
                Assert.Equal(2, database.files.Count);
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        /// <summary>只執行測試作者擁有的固定 SQL；操作完成即關閉連線，不接受外部輸入或真實個資。</summary>
        internal async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>使用獨立真連線觀察已提交資料與 SQL DMV，不用記憶體替身推論鎖或交易。</summary>
        internal async Task<int> ScalarAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        /// <summary>先清本案確切連線字串的池，再用不入池觀察者確認其他 sessions 五秒內歸零；不使用 ClearAllPools。</summary>
        internal async Task AssertDrainAsync()
        {
            using (var pool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pool);
            var observerString = new SqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString;
            await using var observer = new SqlConnection(observerString);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await observer.OpenAsync(deadline.Token);
            await using var command = observer.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID() AND session_id<>@@SPID";
            while (Convert.ToInt32(await command.ExecuteScalarAsync(deadline.Token), CultureInfo.InvariantCulture) != 0)
            {
                await Task.Delay(10, deadline.Token);
            }
        }

        /// <summary>只刪此 owner 隨機建立的資料庫；強制回滾後查資料庫與資料檔零殘留，清理失敗不能當作通過。</summary>
        public async ValueTask DisposeAsync()
        {
            if (!created) return;
            using (var pool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pool);
            await using var connection = new SqlConnection(Master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            command.CommandText = "IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]; END; SELECT COUNT(*) FROM sys.databases WHERE name=@name";
            command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            created = false;
            Assert.All(files, path => Assert.False(File.Exists(path), "Owned LocalDB file remains."));
            var evidence = Path.Combine(Root, ".ccg", "dual-model-runs", "20261126-f6-sql", name + ".json");
            await File.WriteAllTextAsync(evidence, System.Text.Json.JsonSerializer.Serialize(new { database = name, databaseResidual = 0, fileResidual = 0, checkedFiles = files.Count }));
        }
    }
}
