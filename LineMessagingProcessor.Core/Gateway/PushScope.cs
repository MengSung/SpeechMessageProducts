/*
檔案整體說明
目的與定位：F5 推播入口的不可變範圍提示，攜帶產品、Channel、租戶及顯示提示，不代表授權。
主要功能與流程：由呼叫端傳入 Gateway，後者必須核對固定產品及權威租戶到 Channel 名額，
品牌實際使用 IRecipientMembership 讀回的事實，不能直接信任此資料內的名稱或圖示。
使用邊界與維護：本檔只是純資料，不建立連線、背景工作或快取；範圍只活到單次送訊結束，
不得存入未隔離的 Session 或 singleton。ToString 固定遮蔽顯示資訊，避免偶然診斷洩漏。
*/
using System;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>呼叫端的範圍提示；必須經固定產品、權威名額及完整收件人驗證後才能傳輸。</summary>
/// <param name="Product">固定產品識別，不能藉此選擇其他組織。</param>
/// <param name="ChannelKey">產品目錄內的正式 Channel 數字鍵。</param>
/// <param name="TenantId">正式 account 主鍵，空值由 Gateway 拒絕。</param>
/// <param name="TenantDisplayName">相容顯示提示，不是實際 sender 權威。</param>
/// <param name="TenantIconUrl">相容圖示提示，不取代資料庫讀回值。</param>
public sealed record PushScope(string Product, string ChannelKey, Guid TenantId, string TenantDisplayName, string? TenantIconUrl)
{
    /// <summary>不讓記錄自動展開租戶顯示資訊；此摘要不可用作事件或資料庫識別。</summary>
    public override string ToString() => "PushScope(redacted)";
}
