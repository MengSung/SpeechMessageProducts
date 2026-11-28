/*
檔案整體說明
目的與定位：P0／F2 平台 SQL 的方案內唯讀診斷入口，依 02A §10.4 取代臨時 PowerShell 載入 SqlClient 或另建診斷專案，供測試 VM 驗證嚴格 TLS 憑證釘選及 SELECT 1。
主要功能與流程：先經 ProductionGuard 檢查組織，再以精確語法只接受 goodshepherd20 測試範圍、master 或 GoodShepherd2Platform，以及三個固定 pin 案例。一般模式使用目前 Windows 整合身分；app-pool 模式只允許平台資料庫，並在固定測試 VM 借用唯一 GoodShepherd2Test 工作程序的 Windows 身分，不接受 PID／帳密且不得退回管理員。SQL 主機固定 D365APP01、Encrypt=Strict、TrustServerCertificate=false；正確 pin 指向已核定公鑰，wrong 指向原 fallback 公鑰，missing 指向固定刻意不存在的檔案。命令只執行 SELECT 1，不變更憑證、信任庫、資料庫、角色或 D365 資料。
使用邊界與維護：外層 Program 擁有共用例外診斷及取消 owner；本檔只借用 writer、catalog 與 caller token。每個 SQL 連線停用池化及自動重連，連線八秒、查詢五秒、整次十五秒；所有 SqlConnection、SqlCommand 及 linked cancellation source 都在同一 awaited 操作內確定釋放，不建立背景工作、共用 mutable 狀態或保留憑證／使用者資訊。命令摘要只輸出固定白名單資料庫、pin 案例與數值結果；真正故障將原 Exception 交回 Program 記錄，SQL 只補數值錯碼，不展開訊息、連線字串或例外本文。
驗證方式：PlatformSqlReadOnlyCliTests 使用合成 catalog 與單次純值查詢委派驗 parser、固定 TLS 設定、正式與任意資料庫拒絕、取消及錯誤 scalar 不發布；離線測試後才在已核對 VM 執行真實命令。SQL 登入失敗不能當作錯誤 pin 成功拒絕的證據，三個 pin 案例須以同身分、同資料庫逐一核對。
*/
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>固定測試 VM 的唯讀 SQL 子命令；命令列不接受任意主機、SQL、路徑或密碼。</summary>
internal static class PlatformSqlReadOnlyCli
{
    /// <summary>完整驗語法與測試組織後，借用單次查詢執行器；只在 SELECT 1 與取消檢查通過後輸出成功。</summary>
    /// <param name="args">固定順序的七個參數，不回顯未知值。</param>
    /// <param name="catalog">Program 本次載入的核定組織 catalog，不跨命令快取。</param>
    /// <param name="output">由 Program 或測試擁有的有限摘要 writer。</param>
    /// <param name="query">離線測試使用的查詢接縫；正式入口傳 null，使用本檔唯一的真 SQL SELECT 1。</param>
    /// <param name="cancellationToken">明確 caller 取消；內部十五秒逾時仍交外層當真正故障記錄。</param>
    /// <param name="runAsAppPool">僅供離線測試替換 Windows 身分範圍；正式入口傳 null，由唯一短期 token owner 執行。</param>
    /// <returns>成功為零、語法或組織拒絕為二；其他故障保持真實例外供外層記錄。</returns>
    internal static async Task<int> RunAsync(string[] args, OrganizationCatalog catalog, TextWriter output,
        Func<SqlConnectionStringBuilder, CancellationToken, Task<int>>? query, CancellationToken cancellationToken,
        Func<Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>? runAsAppPool = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParse(args, catalog, cancellationToken, out var database, out var pin))
        {
            output.WriteLine("Platform SQL read-only arguments rejected.");
            return 2;
        }
        var options = CreateOptions(database, pin);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        int scalar;
        try
        {
            if (args[0] == "platform-sql-verify-app-pool")
            {
                Func<CancellationToken, Task<int>> operation = token => (query ?? ReadOneAsync)(options, token);
                scalar = runAsAppPool is null
                    ? await PlatformSqlAppPoolIdentity.RunAsync(operation, deadline.Token).ConfigureAwait(false)
                    : await runAsAppPool(operation, deadline.Token).ConfigureAwait(false);
            }
            else
                scalar = await (query ?? ReadOneAsync)(options, deadline.Token).ConfigureAwait(false);
        }
        catch (SqlException exception)
        {
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PLATFORM-SQL FAILURE number={0}; state={1}; class={2}",
                exception.Number, exception.State, exception.Class));
            throw;
        }
        deadline.Token.ThrowIfCancellationRequested();
        if (scalar != 1)
            throw new InvalidOperationException("Platform SQL SELECT 1 returned an unexpected value.");
        output.WriteLine("PLATFORM-SQL SELECT1=1; database=" + database + "; pin-case=" + pin +
            (args[0] == "platform-sql-verify-app-pool" ? "; identity=GoodShepherd2Test" : ""));
        return 0;
    }

    /// <summary>先用現有正式組織守門，再拒絕非核定測試目標及任何未列入固定語法的參數。</summary>
    private static bool TryParse(string[] args, OrganizationCatalog catalog, CancellationToken cancellationToken,
        out string database, out string pin)
    {
        database = ""; pin = "";
        if (args.Length != 7 || args[0] is not ("platform-sql-verify" or "platform-sql-verify-app-pool") || args[1] != "--org" ||
            args[3] != "--database" || args[5] != "--pin" ||
            args[4] is not ("master" or "GoodShepherd2Platform") || args[6] is not ("correct" or "wrong" or "missing"))
            return false;
        if (args[0] == "platform-sql-verify-app-pool" && args[4] != "GoodShepherd2Platform")
            return false;
        try
        {
            var target = ProductionGuard.Inspect(args, catalog, cancellationToken).Organization;
            if (target.Kind != "test" || target.Name != "goodshepherd20")
                return false;
        }
        catch (InvalidDataException) { return false; }
        database = args[4]; pin = args[6];
        return true;
    }

    /// <summary>只從固定白名單建立單次連線參數；wrong／missing 不會改寫或替代正確公鑰檔。</summary>
    private static SqlConnectionStringBuilder CreateOptions(string database, string pin) => new()
    {
        DataSource = "D365APP01",
        InitialCatalog = database,
        IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Strict,
        TrustServerCertificate = false,
        ServerCertificate = pin switch
        {
            "correct" => @"C:\ProgramData\SpeechMessage\certs\d365-9.1-sql-strict-pin.cer",
            "wrong" => @"C:\ProgramData\SpeechMessage\certs\d365-9.1-sql.cer",
            "missing" => @"C:\ProgramData\SpeechMessage\certs\d365-9.1-sql-intentionally-missing.cer",
            _ => throw new InvalidOperationException("An approved certificate case is required.")
        },
        Pooling = false,
        Enlist = false,
        ConnectTimeout = 8,
        ConnectRetryCount = 0,
        ApplicationName = "3P-PlatformSql-ReadOnly"
    };

    /// <summary>唯一真實 I/O：以 await using 關閉未池化連線與五秒命令；不執行任何任意 SQL 或重試。</summary>
    private static async Task<int> ReadOneAsync(SqlConnectionStringBuilder options, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = 5;
        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is int value ? value : throw new InvalidOperationException("Platform SQL SELECT 1 returned an invalid scalar.");
    }
}
