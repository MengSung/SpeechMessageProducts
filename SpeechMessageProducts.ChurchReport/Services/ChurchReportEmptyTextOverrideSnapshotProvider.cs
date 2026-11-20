/*
檔案整體說明

目的與定位：提供 ChurchReport 在 P8-02 尚未接入教會覆寫服務前使用的空文字快照 provider，
供 Startup 將 GoodShepherdTextCatalog 註冊為可解析的產品文字目錄。

主要功能與流程：每次 GetSnapshot 都接收已由伺服器建立的完整 TextContext，立即建立只屬於該次
呼叫的空 ChurchTextOverrideSnapshot 並回傳；不查詢 D365、不接受 request 傳入的教會識別，
也不把不同使用者或教會的覆寫狀態合併。

使用邊界與維護：此實作是無狀態 singleton，可由 Host 長期持有；它不擁有連線、快取、
背景工作、計時器或 disposable 資源，沒有跨呼叫資料保留。P8-03a 接上真實覆寫來源時，
Startup 必須以真實 provider 取代本服務，不能讓空與真實 provider 同時註冊。

失敗處理與驗證：空 context 依跨目標慣例立即拒絕；快照建構失敗原樣傳遞，不發布其他 scope
或部分狀態。ChurchReport.MemberInfo.Tests 驗證回傳 context 一致且 Role.* 不存在於空快照。
*/
using System;
using SpeechMessage.Localization;

namespace ChurchReport;

/// <summary>
/// 在 ChurchReport 尚未接入實際教會覆寫資料來源時，為每個已授權文字範圍提供空快照。
/// </summary>
internal sealed class ChurchReportEmptyTextOverrideSnapshotProvider : IChurchTextOverrideSnapshotProvider
{
    /// <summary>
    /// 依本次完整文字範圍建立獨立空快照，不保存範圍、使用者或任何來源狀態。
    /// </summary>
    /// <param name="context">由伺服器授權後建立的教會、組織、產品與語系範圍。</param>
    /// <returns>只屬於指定 context 且不含覆寫資料的不可變快照。</returns>
    public IChurchTextOverrideSnapshot GetSnapshot(TextContext context)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif
        return new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
