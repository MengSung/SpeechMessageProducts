/*
檔案整體說明
目的與定位：保護 P0／F2 平台 SQL 唯讀命令的固定測試環境、嚴格 TLS、公鑰釘選與輸出契約，避免診斷工具因任意輸入變成正式環境或資料修改入口。
主要功能與流程：以純合成 organization catalog 與單次查詢委派執行正式 parser，確認合法命令產生固定連線設定及 SELECT 1 摘要；故障注入包含正式組織、未知選項、缺少參數、錯誤結果及已取消 caller，均檢查零 I/O 或拒絕發布。
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
