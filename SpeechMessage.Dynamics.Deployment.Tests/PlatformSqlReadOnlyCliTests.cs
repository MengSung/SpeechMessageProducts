/*
檔案整體說明
目的與定位：保護 P0／F2 平台 SQL 唯讀命令的固定測試環境、嚴格 TLS、公鑰釘選與輸出契約，避免診斷工具因任意輸入變成正式環境或資料修改入口。
主要功能與流程：以純合成 organization catalog 與單次查詢委派執行正式 parser，確認合法命令產生固定連線設定及 SELECT 1 或集區 SID／資料庫證據摘要；故障注入包含正式組織、未知選項、缺少參數、錯誤結果及已取消 caller，均檢查零 I/O 或拒絕發布。
使用邊界與維護：本檔只使用不可變合成組織、StringWriter 及有界同步完成的 Task，不建立 SQL／SDK 連線、不載入 User Secrets 或實機憑證。writer 與取消來源由每案 using 釋放；測試不持有共享使用者狀態或背景工作，不把離線成功宣稱為 Strict TLS 實機驗收。
*/
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>固定 SQL 查驗目標與 fail-closed 輸出測試，真實網路由單一借入委派替換。</summary>
public sealed class PlatformSqlReadOnlyCliTests
{
    /// <summary>AppPool 成功必須由同一個 SQL 查詢核對原始登入、有效登入及資料庫；不能以本機 token 標記取代 SSPI 結果。</summary>
    [Fact]
    public void AppPoolQuery_RequiresServerSideIdentityAndDatabaseProof()
    {
        Assert.Equal(@"SELECT CASE WHEN (SELECT original_security_id FROM sys.dm_exec_sessions WHERE session_id = @@SPID) = SUSER_SID(N'IIS APPPOOL\GoodShepherd2Test') THEN 1 ELSE 0 END + CASE WHEN SUSER_SID() = SUSER_SID(N'IIS APPPOOL\GoodShepherd2Test') THEN 2 ELSE 0 END + CASE WHEN DB_NAME() = N'GoodShepherd2Platform' THEN 4 ELSE 0 END",
            PlatformSqlReadOnlyCli.QueryText(appPool: true));
        Assert.Equal("SELECT 1", PlatformSqlReadOnlyCli.QueryText(appPool: false));
    }

    /// <summary>原始 SID、有效 SID、資料庫任何位元不符（零至六）均拒絕；只能輸出有界數字，不印寫死的身分成功標記。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Verify_AppPoolServerIdentityMismatch_DoesNotPublishSuccess(int flags)
    {
        using var output = new StringWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify-app-pool --org goodshepherd20 --database GoodShepherd2Platform --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (_, _) => Task.FromResult(flags), CancellationToken.None,
            (query, token) => query(token)));
        Assert.Equal("PLATFORM-SQL identity-proof-flags=" + flags.ToString(System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine,
            output.ToString());
    }

    /// <summary>AppPool 模式仍固定平台資料庫及 Strict 選項，且 SQL 委派必須完整 awaited 在借入身分範圍中。</summary>
    [Fact]
    public async Task Verify_AppPoolMode_AwaitsIdentityScopeBeforePublishing()
    {
        using var output = new StringWriter();
        var active = false;
        var exit = await PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify-app-pool --org goodshepherd20 --database GoodShepherd2Platform --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, async (options, token) =>
            {
                await Task.Yield();
                Assert.True(active);
                Assert.Equal(SqlConnectionEncryptOption.Strict, options.Encrypt);
                Assert.False(options.Pooling);
                return 7;
            }, CancellationToken.None, async (query, token) =>
            {
                active = true;
                try { return await query(token); }
                finally { active = false; }
            });
        Assert.Equal(0, exit);
        Assert.False(active);
        Assert.Contains("IDENTITY-AND-DB-PROOF=7", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>身分取得失敗不得退回管理員 SQL；真正例外保留且成功摘要維持空白。</summary>
    [Fact]
    public async Task Verify_AppPoolIdentityFailure_NoFallbackOrSuccess()
    {
        using var output = new StringWriter();
        var failure = new InvalidOperationException("Synthetic identity failure.");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify-app-pool --org goodshepherd20 --database GoodShepherd2Platform --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (_, _) => throw new InvalidOperationException("Unexpected SQL."),
            CancellationToken.None, (_, _) => throw failure));
        Assert.Same(failure, actual);
        Assert.Equal("", output.ToString());
    }

    /// <summary>固定集區身分不允許連 master；即使有借入替身也必須在開啟身分或 SQL 前拒絕。</summary>
    [Fact]
    public async Task Verify_AppPoolMaster_RejectedBeforeIdentity()
    {
        using var output = new StringWriter();
        var exit = await PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify-app-pool --org goodshepherd20 --database master --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (_, _) => throw new InvalidOperationException("Unexpected SQL."),
            CancellationToken.None, (_, _) => throw new InvalidOperationException("Unexpected identity."));
        Assert.Equal(2, exit);
    }

    /// <summary>唯一身分租約必須涵蓋 await 並在原始故障、取消或成功之後確定 Dispose；合成 owner 不使用 Windows token。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppPoolScope_DrainsAndDisposesAfterAwait(bool fail)
    {
        var scope = new SyntheticIdentityScope();
        var failure = new InvalidOperationException("Synthetic query failure.");
        async Task<int> Query(CancellationToken token)
        {
            await Task.Yield();
            Assert.False(scope.Disposed);
            if (fail) throw failure;
            return 1;
        }
        if (fail)
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PlatformSqlAppPoolIdentity.RunAsync(Query, CancellationToken.None, () => scope)));
        else
            Assert.Equal(1, await PlatformSqlAppPoolIdentity.RunAsync(Query, CancellationToken.None, () => scope));
        Assert.True(scope.Disposed);
        Assert.Equal(1, scope.Executions);
    }

    /// <summary>取得身分後 caller 取消仍要釋放租約，但不得開始 SQL；沒有背景工作可在 Dispose 後續跑。</summary>
    [Fact]
    public async Task AppPoolScope_CancelAfterAcquire_ReleasesWithoutQuery()
    {
        using var cancellation = new CancellationTokenSource();
        var scope = new SyntheticIdentityScope();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlatformSqlAppPoolIdentity.RunAsync(
            _ => throw new InvalidOperationException("Unexpected query."), cancellation.Token,
            () => { cancellation.Cancel(); return scope; }));
        Assert.True(scope.Disposed);
        Assert.Equal(0, scope.Executions);
    }

    /// <summary>測試唯一 owner：只委派單次合成查詢並記錄清理，不建立原生 handle、SQL 連線或共享身分。</summary>
    private sealed class SyntheticIdentityScope : IPlatformSqlIdentityScope
    {
        /// <summary>供決定性斷言確認單次清理已完成；不跨測試案例共享。</summary>
        internal bool Disposed { get; private set; }
        /// <summary>實際執行次數；取消於身分取得後發生時應維持零。</summary>
        internal int Executions { get; private set; }
        /// <summary>借用測試委派及 token，只計次並回傳原 Task，外層必須等待完成。</summary>
        public Task<int> ExecuteAsync(Func<CancellationToken, Task<int>> query, CancellationToken token)
        { Executions++; return query(token); }
        /// <summary>結束此案例唯一身分範圍；沒有任何實機資源需要釋放。</summary>
        public void Dispose() => Disposed = true;
    }

    private const string CatalogJson = """
        {"schemaVersion":1,"organizations":[
        {"name":"goodshepherd20","kind":"test","host":"goodshepherd20.speechmessage.com.tw","switchState":"off","tierOverrides":[]},
        {"name":"synthetic-prod-a","kind":"production","host":"synthetic-prod.invalid","switchState":"off","tierOverrides":[]}]}
        """;

    /// <summary>合法測試命令只能使用 Strict、固定公鑰檔與整合身分，且單次查詢不加入跨命令池。</summary>
    [Theory]
    [InlineData("master", "correct", @"C:\ProgramData\SpeechMessage\certs\d365-9.1-sql-strict-pin.cer")]
    [InlineData("GoodShepherd2Platform", "wrong", @"C:\ProgramData\SpeechMessage\certs\d365-9.1-sql.cer")]
    [InlineData("master", "missing", @"C:\ProgramData\SpeechMessage\certs\d365-9.1-sql-intentionally-missing.cer")]
    public async Task Verify_FixedScope_UsesStrictPinAndBoundedUnpooledConnection(string database, string pin, string path)
    {
        using var output = new StringWriter();
        var calls = 0;
        var exit = await PlatformSqlReadOnlyCli.RunAsync(
            $"platform-sql-verify --org goodshepherd20 --database {database} --pin {pin}".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (options, token) =>
            {
                calls++;
                Assert.Equal("D365APP01", options.DataSource);
                Assert.Equal(database, options.InitialCatalog);
                Assert.Equal(SqlConnectionEncryptOption.Strict, options.Encrypt);
                Assert.False(options.TrustServerCertificate);
                Assert.True(options.IntegratedSecurity);
                Assert.False(options.Pooling);
                Assert.False(options.Enlist);
                Assert.Equal(0, options.ConnectRetryCount);
                Assert.Equal(8, options.ConnectTimeout);
                Assert.Equal(path, options.ServerCertificate);
                Assert.False(token.IsCancellationRequested);
                return Task.FromResult(1);
            }, CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(1, calls);
        Assert.Equal("PLATFORM-SQL SELECT1=1; database=" + database + "; pin-case=" + pin + Environment.NewLine, output.ToString());
    }

    /// <summary>缺欄位、任意資料庫／路徑、未知參數或正式授權都不能到達 SQL I/O；只回固定拒絕訊息。</summary>
    [Theory]
    [InlineData("platform-sql-verify --org goodshepherd20")]
    [InlineData("platform-sql-verify --org goodshepherd20 --database MSCRM_CONFIG --pin correct")]
    [InlineData("platform-sql-verify --org goodshepherd20 --database master --pin C:/secret.cer")]
    [InlineData("platform-sql-verify --org goodshepherd20 --database master --pin correct --apply")]
    [InlineData("platform-sql-verify --org synthetic-prod-a --database master --pin correct --production")]
    public async Task Verify_InvalidOrProductionScope_RejectsBeforeIo(string command)
    {
        using var output = new StringWriter();
        var exit = await PlatformSqlReadOnlyCli.RunAsync(command.Split(' '), OrganizationCatalog.Parse(CatalogJson),
            output, (_, _) => throw new InvalidOperationException("Unexpected SQL I/O."), CancellationToken.None);
        Assert.Equal(2, exit);
        Assert.Equal("Platform SQL read-only arguments rejected." + Environment.NewLine, output.ToString());
    }

    /// <summary>查詢回傳錯誤 scalar 時必須失敗且不發布成功摘要，不把只有連線成功視為驗收。</summary>
    [Fact]
    public async Task Verify_UnexpectedScalar_DoesNotPublishSuccess()
    {
        using var output = new StringWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify --org goodshepherd20 --database master --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (_, _) => Task.FromResult(0), CancellationToken.None));
        Assert.Equal("", output.ToString());
    }

    /// <summary>明確 caller 取消需在 I/O 前拒絕，且不產生成功或假實機結果。</summary>
    [Fact]
    public async Task Verify_CallerCancelled_ZeroIo()
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify --org goodshepherd20 --database master --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (_, _) => throw new InvalidOperationException("Unexpected SQL I/O."),
            cancellation.Token));
        Assert.Equal("", output.ToString());
    }

    /// <summary>查詢成功後取消也不能發布成功，避免把已超時的操作交下一個命令繼續使用。</summary>
    [Fact]
    public async Task Verify_CallerCancelledAfterQuery_DoesNotPublishSuccess()
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlatformSqlReadOnlyCli.RunAsync(
            "platform-sql-verify --org goodshepherd20 --database master --pin correct".Split(' '),
            OrganizationCatalog.Parse(CatalogJson), output, (_, _) => { cancellation.Cancel(); return Task.FromResult(1); },
            cancellation.Token));
        Assert.Equal("", output.ToString());
    }
}
