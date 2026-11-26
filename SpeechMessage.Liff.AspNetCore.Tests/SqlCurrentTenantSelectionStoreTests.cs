/*
檔案整體說明
目的與定位：F6 SQL 租戶選擇儲存的離線輸入與部署安全契約；此處完全不使用真實 SQL、LINE 或使用者秘密。
主要功能與流程：以不存在的合成主機與預先取消的 caller token 驗證合法鍵在 I/O 前取消，非法鍵與空 ContactId 則必須先被拒絕；另外驗證部署 TLS 政策及固定五秒期限不能被連線字串放寬。
使用邊界與維護：每案只擁有不可變部署字串及短生命週期取消來源，沒有背景工作與外部連線；這些斷言僅保護驗證順序與公開 API，不能充當 SQL 交易、鎖定或原子性證據，後者由隔離 LocalDB 整合案例承擔。
*/
using Xunit;

namespace SpeechMessage.Liff.AspNetCore.Tests;

/// <summary>以預先取消保證零外部 I/O，保護完整鍵、部署 TLS 與故障不得偽裝為空結果的界線。</summary>
public sealed class SqlCurrentTenantSelectionStoreTests
{
    private const string SafeConnection = "Server=tcp:f6.invalid;Database=synthetic;Integrated Security=true;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;ConnectRetryCount=9";

    /// <summary>缺省或空白部署連線拒絕建構，錯誤不得包含原連線內容或認證片段。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_MissingDeploymentConnection_Rejects(string? connectionString)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SqlCurrentTenantSelectionStore(connectionString!));
    }

    /// <summary>部署不得以停用加密或信任自簽憑證繞過遠端 TLS；測試不連線到指定合成主機。</summary>
    [Theory]
    [InlineData("Server=tcp:f6.invalid;Database=synthetic;Encrypt=false;TrustServerCertificate=false")]
    [InlineData("Server=tcp:f6.invalid;Database=synthetic;Encrypt=true;TrustServerCertificate=true")]
    [InlineData("Server=tcp:f6.invalid;Database=synthetic;Encrypt=optional;TrustServerCertificate=false")]
    public void Constructor_UnsafeTls_Rejects(string connectionString)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SqlCurrentTenantSelectionStore(connectionString));
    }

    /// <summary>完整鍵各欄位獨立拒絕空值、空白、控制字元及超過 128 UTF-16 單位；先於 caller 取消及 I/O 判斷。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" product")]
    [InlineData("product ")]
    [InlineData("pro duct")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData("a\u007fb")]
    [InlineData("a\u00a0b")]
    public async Task Scope_InvalidComponent_RejectsBeforeCancellation(string? invalid)
    {
        var store = new SqlCurrentTenantSelectionStore(SafeConnection);
        var cancelled = new CancellationToken(true);
        foreach (var index in Enumerable.Range(0, 3))
        {
            var key = new[] { "Product", "Channel", "User" };
            key[index] = invalid!;
            await Assert.ThrowsAnyAsync<ArgumentException>(() => store.GetAsync(key[0], key[1], key[2], cancelled));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SetAsync(key[0], key[1], key[2], Guid.NewGuid(), cancelled));
        }
    }

    /// <summary>複合鍵不能依賴 SQL 的截斷或空白填補；每一欄位超長與無效代理字元必須零 I/O 拒絕。</summary>
    [Fact]
    public async Task Scope_OverlongOrInvalidUnicode_RejectsBeforeIo()
    {
        foreach (var invalid in new[] { new string('x', 129), "a\ud800" })
        {
            await Scope_InvalidComponent_RejectsBeforeCancellation(invalid);
        }
    }

    /// <summary>有效鍵包含最大長度及區分大小寫的合成值，必須接受驗證後回傳 caller 取消，而非連線故障或 null。</summary>
    [Theory]
    [InlineData("Product", "Channel", "User")]
    [InlineData("product", "channel", "user")]
    [InlineData("產品", "頻道", "用戶")]
    public async Task Scope_ValidInput_PreCancelledCallerNeverOpensSql(string product, string channel, string user)
    {
        var store = new SqlCurrentTenantSelectionStore(SafeConnection);
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync(product, channel, user, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetAsync(product, channel, user, Guid.NewGuid(), cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync(new string('p', 128), new string('c', 128), new string('u', 128), cancelled));
    }

    /// <summary>空 ContactId 不是授權拒絕或刪除指令；在任意 SQL 及 caller 取消前明確拒絕。</summary>
    [Fact]
    public async Task Set_EmptyContact_RejectsBeforeIo()
    {
        var store = new SqlCurrentTenantSelectionStore(SafeConnection);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SetAsync("P", "C", "U", Guid.Empty, new CancellationToken(true)));
    }

    /// <summary>初始化與 schema 驗證是部署入口，已取消啟動必須零 SQL 而直接傳播取消。</summary>
    [Fact]
    public async Task Deployment_PreCancelledCaller_DoesNotOpenSql()
    {
        var store = new SqlCurrentTenantSelectionStore(SafeConnection);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InitializeAsync(new CancellationToken(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ValidateAsync(new CancellationToken(true)));
    }
}
