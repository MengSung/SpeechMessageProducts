/*
檔案整體說明
目的與定位：F4 一般測試的真實 LocalDB owner，沿用 F6 的隨機合成資料庫與嚴格 TLS 模式，供排程、重試及隔離測試觀察已提交的 SQL 結果。
主要功能與流程：每案只在固定 MSSQLLocalDB 建立 F4Schedule_ 隨機庫，記錄資料檔；以有界獨立連線執行作者提供的 SQL。測試必須先停止所有 runtime 及額外 worker，本 owner 再清自己確切連線池、查 session 歸零、刪庫並核對實體檔案消失。
使用邊界與維護：不讀設定、秘密或既有組織，不連遠端平台。SQL 命令限五至十五秒；清理故障仍嘗試刪除自己的庫並讓案例失敗。證據只包含合成庫名及殘留計數，寫至本工作樹忽略目錄；LocalDB 結果不能代表遠端部署驗收。
*/
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SpeechMessage.Scheduling.Tests;

/// <summary>每案唯一擁有合成庫；不共用其他案例的池或背景工作，所有清理亦在失敗路徑執行。</summary>
internal sealed class SchedulingDatabase : IAsyncDisposable
{
    private const string Master = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=false;Pooling=false;Connect Timeout=5;ConnectRetryCount=0;Enlist=false";
    private readonly string name = "F4Schedule_" + Guid.NewGuid().ToString("N");
    private readonly List<string> files = [];
    private bool created;

    /// <summary>僅供本案 Hangfire 與觀察者使用；強制憑證驗證、有界池且沒有隱含交易或重連。</summary>
    internal string ConnectionString => new SqlConnectionStringBuilder(Master)
    {
        InitialCatalog = name, Pooling = true, MinPoolSize = 0, MaxPoolSize = 64,
        MultipleActiveResultSets = false
    }.ConnectionString;

    /// <summary>從目前測試輸出向上尋找本 checkout；不跨到整合工作樹或其他線寫證據。</summary>
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Test checkout not found.");
        }
    }

    /// <summary>忽略目錄只保存本 worker 的原始測試及合成資源證據，不保存連線字串或部署資料。</summary>
    internal static string Evidence => Path.Combine(Root, ".ccg", "dual-model-runs", "20261126-f4-tests");

    /// <summary>建立資料庫前先登記清理責任；初始化任何位置故障都由同一 owner 嘗試刪除，避免孤兒資料檔。</summary>
    internal static async Task<SchedulingDatabase> CreateAsync()
    {
        Directory.CreateDirectory(Evidence);
        var database = new SchedulingDatabase();
        try
        {
            await using var connection = new SqlConnection(Master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
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

    /// <summary>固定測試 SQL 以獨立連線執行，呼叫結束即釋放；不能接收任何外部 SQL 或真實資料。</summary>
    internal async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>讀取已提交的整數或計數，避免以記憶體探針冒充 SQL 工作狀態。</summary>
    internal async Task<int> ScalarAsync(string sql)
    {
        var values = await QueryAsync(sql, reader => Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
        return Assert.Single(values);
    }

    /// <summary>用短生命週期 reader 將純值候選完全讀完才回傳；reader、連線及命令皆不逃離方法。</summary>
    internal async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> project)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<T>();
        while (await reader.ReadAsync()) values.Add(project(reader));
        return values;
    }

    /// <summary>清確切本案池後，以不入池觀察者等待五秒內 session 歸零；絕不清整個程序的連線池。</summary>
    internal async Task AssertDrainAsync()
    {
        using (var pool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pool);
        await using var observer = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await observer.OpenAsync(deadline.Token);
        await using var command = observer.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID() AND session_id<>@@SPID";
        while (Convert.ToInt32(await command.ExecuteScalarAsync(deadline.Token), CultureInfo.InvariantCulture) != 0)
            await Task.Delay(20, deadline.Token);
    }

    /// <summary>先驗 drain，再於 finally 刪唯一合成庫；任何遺留工作、session、資料庫或資料檔都使測試失敗。</summary>
    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        var drained = false;
        try
        {
            await AssertDrainAsync();
            drained = true;
        }
        finally
        {
            using (var pool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pool);
            await using var connection = new SqlConnection(Master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            command.CommandText = "IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]; END; SELECT COUNT(*) FROM sys.databases WHERE name=@name";
            command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            created = false;
            Assert.All(files, file => Assert.False(File.Exists(file), "Owned LocalDB data file remains."));
            await File.WriteAllTextAsync(Path.Combine(Evidence, name + ".json"),
                JsonSerializer.Serialize(new { database = name, drained, databaseResidual = 0, fileResidual = 0, checkedFiles = files.Count }));
        }
    }

    /// <summary>以單調計時器限制最終一致觀察，逾時保留具體契約訊息；不以無界 delay 等待工作。</summary>
    internal static async Task UntilAsync(Func<Task<bool>> predicate, string contract, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!await predicate())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(seconds), contract);
            await Task.Delay(25);
        }
    }
}
