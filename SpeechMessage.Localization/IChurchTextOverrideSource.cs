/*
 * 檔案整體說明
 *
 * 目的與定位：完整覆寫讀取的雙目標契約。Host 先授權完整 context；來源只回純值，不把連線或 SDK Entity 交給快取。
 *
 * 主要功能與流程：
 * 本檔宣告 IChurchTextOverrideSource。
 * - 讀完本範圍全部頁面才回候選；任何部分失敗向上傳原例外，不將查無資料與讀取故障混同。 呼叫者擁有此次取消及排空責任；實作必須有 I/O 逾時、資料量上限及確定的資源歸還路徑。
 * 資料／選項欄位包含 IChurchTextOverrideSource，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/OverrideCacheExceptionTests.cs、
 * SpeechMessage.Tenancy.Tests/ChurchTextOverrideCacheTests.cs、
 * SpeechMessage.Tenancy.Tests/OverrideCompositionTests.cs、SpeechMessage.Tenancy/ChurchTextOverrideCache.cs、
 * SpeechMessage.Tenancy/OverrideServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechMessage.Localization;

/// <summary>完整覆寫讀取的雙目標契約。Host 先授權完整 context；來源只回純值，不把連線或 SDK Entity 交給快取。</summary>
public interface IChurchTextOverrideSource
{
    /// <summary>
    /// 讀完本範圍全部頁面才回候選；任何部分失敗向上傳原例外，不將查無資料與讀取故障混同。
    /// 呼叫者擁有此次取消及排空責任；實作必須有 I/O 逾時、資料量上限及確定的資源歸還路徑。
    /// </summary>
    /// <param name="context">伺服器已授權的完整組織／產品／教會，不可直接信任 HTTP 路由值。</param>
    /// <param name="cancellationToken">本次操作 owner 的取消；不應直接傳某位共享等待者的 token。</param>
    /// <returns>完整純文字候選；發布前仍須驗證系統鍵、參數及長度。</returns>
    Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken);
}
