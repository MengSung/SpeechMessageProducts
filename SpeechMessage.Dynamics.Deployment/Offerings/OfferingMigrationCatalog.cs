/*
 * 檔案整體說明
 *
 * 目的與定位：保存只供Deployment plan精確映射的ChurchReport現行奉獻畫面名稱及順序。
 * 主要功能與流程：固定提供各畫面文字與顯示排序；planner僅以正式組織Choice metadata標籤精確比對，
 * 再由metadata提供的值建立plan，絕不把本清單當成組織選項值或執行期字典。
 * 這些文字不會提供給ChurchReport執行期，也不能用來判定D365選項值；對應值及正式名稱
 * 必須由組織1028語系Choice metadata唯一精確匹配。相同文字若對到多個值視為未對應，
 * 不任選或合併不同正式選項身份。
 * 清單來自P8-01a對F10現行畫面選項的測試盤點；來源程式的實際業務對照仍由P8-01b處理。
 * 使用邊界與維護：本檔只提供固定唯讀陣列，不持有組織、使用者、Session、credential、連線或可變快取，
 * 無需Dispose或背景清理。OfferingMigrationPlannerTests保護畫面選項聯集與對應失敗行為。
 */
using System;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>部署時奉獻畫面對照資料；不能作為資料庫選項值的權威來源或ChurchReport runtime API。</summary>
internal static class OfferingMigrationCatalog
{
    /// <summary>由現行畫面盤點的六個固定文字與順序；值只可由D365 metadata映射。</summary>
    internal static readonly OfferingScreenOption[] ScreenOptions =
    {
        new("十一奉獻", 1),
        new("感恩奉獻", 2),
        new("建堂奉獻", 3),
        new("宣教奉獻", 4),
        new("主日奉獻", 5),
        new("愛心奉獻", 6)
    };
}
