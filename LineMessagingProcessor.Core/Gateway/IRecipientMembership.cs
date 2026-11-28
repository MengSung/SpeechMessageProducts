/*
檔案整體說明
目的與定位：F5 的租戶／收件人權威查詢接縫，不讓純 Core 依賴 Dataverse 或產品主機。
主要功能與流程：固定產品 adapter 驗 account 啟用、租戶旗標、已配發名額及營運 Channel，
返回品牌事實；Gateway 再整批核對名單及測試租戶保護，全部成功前不可進行 HTTP。
使用邊界與維護：實作必須綁定已授權的固定組織／產品與有期限的 SDK owner，不保存查詢、
收件人或 HttpContext；取消後須排空已入場 I/O。本介面沒有資源所有權，返回值只含純值。
*/
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>由伺服器資料驗證的當次租戶品牌，不可從 scope 顯示提示直接複製。</summary>
/// <param name="DisplayName">權威 account 名稱，最多 200 UTF-16。</param>
/// <param name="IconUrl">權威圖示，實際輸出仍須經 SenderRules。</param>
/// <param name="IsTestTenant">只有正式選項「是」才為 true。</param>
public sealed record TenantPushFacts(string DisplayName, string? IconUrl, bool IsTestTenant);

/// <summary>每次發送前的固定產品資料驗證，任何讀取失敗必須 fail closed。</summary>
public interface IRecipientMembership
{
    /// <summary>核對整條名額／Channel 路徑後才返回權威品牌；範圍不符回 null。</summary>
    Task<TenantPushFacts?> ReadTenantAsync(PushScope scope, CancellationToken cancellationToken);
    /// <summary>完整有界名單屬於租戶且其名額對應該 Channel 才回 true；不得只驗第一批。</summary>
    Task<bool> AreAllMembersAsync(PushScope scope, IReadOnlyList<string> recipientUserIds, CancellationToken cancellationToken);
    /// <summary>只核對固定組織的 account 正式測試旗標，空值或「否」一律為 false。</summary>
    Task<bool> IsTestTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
