/*
檔案整體說明
目的與定位：P3-04 真 LocalDB 案例的唯一資料庫 owner，僅建立具隨機識別的本機合成資料庫，不讀部署設定或秘密。
主要功能與流程：連到固定 MSSQLLocalDB 的 master，以安全字元庫名建立庫並執行正式 A.8 腳本；每個查詢自己擁有連線及命令。失敗及正常結束均以 finally 關閉測試 owner，再 SINGLE_USER 回滾殘留交易並 DROP，查詢 sys.databases 及本庫實際 mdf／ldf 決定性確認零殘留，原始結果只寫 ignored evidence。
使用邊界與維護：所有測試連線停用 pooling 且命令有界，資料只存本案例庫至 DisposeAsync。實例原狀由執行腳本記錄及還原；本 fixture 不擅自停止共用實例。測試不宣稱遠端部署成功，資料庫與 SQL 錯誤不得包含真實個資。
*/
using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>每案例唯一臨時 SQL 資料庫；沒有 static 連線或跨案例資料集合。</summary>
internal sealed class WebhookSqlTestDatabase : IAsyncDisposable
{
    private const string Master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=false;Pooling=false;Connect Timeout=5;ConnectRetryCount=0";
    private readonly string name = "P304_" + Guid.NewGuid().ToString("N");
    private bool created;
    private readonly List<string> physicalFiles = [];

    /// <summary>合成庫使用 Windows 本機身分；此值絕非產品部署秘密。</summary>
    internal string ConnectionString => new SqlConnectionStringBuilder(Master) { InitialCatalog = name }.ConnectionString;

    /// <summary>從目前工作樹定位正式腳本，不讀父工作樹或機密組態。</summary>
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Worktree not found.");
        }
    }

    /// <summary>先建立唯一庫再套正式四欄腳本；任何初始化故障也必須先刪庫才傳出原錯誤。</summary>
    internal static async Task<WebhookSqlTestDatabase> CreateAsync()
    {
        var database = new WebhookSqlTestDatabase();
        try
        {
            await using var master = new SqlConnection(Master);
            await master.OpenAsync();
            await using var command = master.CreateCommand(); command.CommandTimeout = 10;
            command.CommandText = "CREATE DATABASE [" + database.name + "]";
            database.created = true; await command.ExecuteNonQueryAsync();
            await database.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(Root,
                "SpeechMessageProducts.LineWebhook", "Database", "001_LineWebhookEvent.sql")));
            command.CommandText = "SELECT physical_name FROM sys.master_files WHERE database_id=DB_ID(@name)";
            command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = database.name });
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) database.physicalFiles.Add(reader.GetString(0));
            Assert.Equal(2, database.physicalFiles.Count);
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }

    /// <summary>執行本測試固定 SQL；不接收 HTTP 值，每次命令與連線使用後立即釋放。</summary>
    internal async Task<int> ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandTimeout = 10; command.CommandText = sql;
        command.Parameters.AddRange(parameters); return await command.ExecuteNonQueryAsync();
    }

    /// <summary>以獨立連線讀回已提交整數結果，避免只檢查記憶體計數就宣稱 SQL 寫入成功。</summary>
    internal async Task<int> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandTimeout = 10; command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>插入精確毫秒 UTC 時間，驗七天邊界時不依賴 server 現在時間或排程速度。</summary>
    internal Task InsertAsync(string id, DateTime utc) => ExecuteAsync(
        "INSERT dbo.LineWebhookEvent(ChannelId,WebhookEventId,ReceivedUtc,IsRedelivery) VALUES ('123',@id,@utc,0)",
        new SqlParameter("@id", SqlDbType.VarChar, 64) { Value = id },
        new SqlParameter("@utc", SqlDbType.DateTime2) { Scale = 3, Value = utc });

    /// <summary>只刪本 owner 建立的安全庫名；強制斷開測試連線後確認 sys.databases 為零，失敗不能假稱清乾淨。</summary>
    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        await using var connection = new SqlConnection(Master); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandTimeout = 15;
        command.CommandText = "IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]; END; SELECT COUNT(*) FROM sys.databases WHERE name=@name";
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        created = false;
        Assert.All(physicalFiles, path => Assert.False(File.Exists(path), "Owned LocalDB file remains after DROP."));
        var evidence = Path.Combine(Root, ".trellis", "tasks", "11-09-p3-04-sql-dedupe", "evidence", "database-lifecycle");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, name + ".json"), System.Text.Json.JsonSerializer.Serialize(new
        { database = name, databaseResidual = 0, fileResidual = 0, checkedFiles = physicalFiles.Count }));
        physicalFiles.Clear();
    }
}
