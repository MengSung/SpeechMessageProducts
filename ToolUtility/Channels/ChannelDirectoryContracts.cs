/*
檔案整體說明
目的與定位：定義共通層 F3 固定產品目錄的離線注入與主機整合邊界；本檔的資料型別不執行授權或建立網路連線。
主要功能與流程：來源先提供不含密文的正式資料列，再依目錄要求取得完整候選；健康結果僅以已驗證資料列回寫。診斷入口借用既有共用 owner，保留原 Exception 而不讀其敏感本文。
使用邊界與維護：Product 與 OrganizationId 由組合根固定，路由不可選擇產品；資料與解密字串由一次操作或最多十分鐘快照持有。字串只能清參照，不能保證原地清零。來源、時鐘及診斷依賴都由外部 owner 管理，必須先排空目錄再釋放。離線 SDK／HTTP 替身驗證不代表實機驗收。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.Channels;

/// <summary>與 F3 Dataverse choice 精確對應；只有營運中及已滿仍可服務已配發租戶。</summary>
public enum DirectoryChannelStatus
{
    /// <summary>僅登錄、不提供憑證。</summary>
    Registered = 100000000,
    /// <summary>可服務且可配發。</summary>
    Operating = 100000001,
    /// <summary>不可新配發，既有租戶仍可服務。</summary>
    Full = 100000002,
    /// <summary>憑證無效或由管理者停用。</summary>
    Disabled = 100000003,
    /// <summary>永久退出服務。</summary>
    Retired = 100000004
}

/// <summary>來源回傳的不可變正式資料列；秘密不得序列化或記錄，不能以顯示名稱替代 Id。</summary>
public sealed class ChannelDirectoryRecord
{
    /// <summary>建立單次讀取值；來源須限制欄位長度，目錄仍逐列重新驗證產品、唯一身分及版本。</summary>
    public ChannelDirectoryRecord(Guid id, string? product, string channelKey, DirectoryChannelStatus status,
        string revision, DateTime modifiedOn, string? tokenEncrypted = null, string? secretEncrypted = null)
    {
        Id = id; Product = product; ChannelKey = channelKey; Status = status;
        Revision = revision; ModifiedOn = modifiedOn; TokenEncrypted = tokenEncrypted; SecretEncrypted = secretEncrypted;
    }
    /// <summary>資料庫正式主鍵，必須非空且在消費集合中唯一。</summary>
    public Guid Id { get; }
    /// <summary>精確產品字串；空白靜默忽略，他產品不能讀密文。</summary>
    public string? Product { get; }
    /// <summary>伺服器保存的 Channel 數字鍵，路由僅作索引。</summary>
    public string ChannelKey { get; }
    /// <summary>資料庫 choice，未知數值拒絕整份候選。</summary>
    public DirectoryChannelStatus Status { get; }
    /// <summary>用於提早失效及樂觀併行回寫的資料庫版本。</summary>
    public string Revision { get; }
    /// <summary>資料庫 UTC 修改時間，與版本一起比較。</summary>
    public DateTime ModifiedOn { get; }
    /// <summary>僅本產品完整讀取可有的加密 token；禁止記錄或序列化本物件。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? TokenEncrypted { get; }
    /// <summary>僅本產品完整讀取可有的加密 secret；生命週期不超過目錄快照。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SecretEncrypted { get; }
    /// <summary>固定遮罩，避免插值輸出任何資料或密文。</summary>
    public override string ToString() => "ChannelDirectoryRecord(***)";
}

/// <summary>不含敏感資料的健康檢查結果；false 表示 HTTP 401，並非推播結果。</summary>
public sealed record ChannelHealthResult(bool Healthy, int MonthlyUsage, int RichMenuCount, DateTimeOffset CheckedAt);

/// <summary>固定部署來源；一次方法借一個 SDK lease，呼叫返回前必須排空並歸還。</summary>
public interface IChannelDirectorySource
{
    /// <summary>組合根已驗證的固定組織，不接受 caller 覆寫。</summary>
    Guid OrganizationId { get; }
    /// <summary>組合根固定的精確產品。</summary>
    string Product { get; }
    /// <summary>建立本次單一 SDK lease；metadata 與密文兩階段查詢須共用該 lease。</summary>
    ValueTask<IChannelDirectorySourceLease> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>一個操作唯一的來源 lease；關閉時歸還借入 SDK，不處置 pool 或 manager。</summary>
public interface IChannelDirectorySourceLease : IAsyncDisposable
{
    /// <summary>false 只取不含秘密的 metadata；true 先完成 metadata 驗證，再逐 ID 讀密文。</summary>
    Task<IReadOnlyList<ChannelDirectoryRecord>> ReadAsync(bool includeCredentials, CancellationToken cancellationToken);
    /// <summary>重驗正式 Id、產品及版本後以 CAS 更新；不接受 caller 指定任意更新 target。</summary>
    Task WriteHealthAsync(ChannelDirectoryRecord channel, ChannelHealthResult result, bool disable, CancellationToken cancellationToken);
}

/// <summary>可注入的共用診斷邊界；實作不能記錄 Message、Data、密文、token 或動態路由。</summary>
public interface IChannelDiagnostics
{
    /// <summary>保留真 Exception 與固定操作名稱；正常 caller 取消不應呼叫。</summary>
    void Report(Exception exception, string operation, CancellationToken cancellationToken);
}

/// <summary>借用既有集中式落檔／LINE owner，不自行建立通知 client、佇列或工作。</summary>
public sealed class CommonChannelDiagnostics : IChannelDiagnostics
{
    private readonly ExceptionDiagnostics owner;
    /// <summary>注入由主機管理的診斷 owner；主機必須最後才 Dispose 此 owner。</summary>
    public CommonChannelDiagnostics(ExceptionDiagnostics owner) => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    /// <summary>原例外交共用去重及敏感資訊白名單；不自行讀取例外本文。</summary>
    public void Report(Exception exception, string operation, CancellationToken cancellationToken) => owner.Report(exception, operation, cancellationToken);
}
