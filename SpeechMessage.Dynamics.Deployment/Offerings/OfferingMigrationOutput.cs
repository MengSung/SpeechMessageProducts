/*
 * 檔案整體說明
 *
 * 目的與定位：將plan與verify純值投影成可供人工核對的安全摘要，不執行SDK I/O或授權判斷。
 * 輸出只包含教會正式ID、選項metadata標籤、筆數、必要等級及類別ID關係，不輸出教會名稱、
 * 會友姓名、來源記錄主鍵、付款內容、例外本文或秘密。未解析教會是獨立計數，不被宣稱為成功回填。
 * 主要功能與流程：依呼叫要求格式化plan分組或verify差異；輸出集合由呼叫端持有，
 * 逐筆摘要只存在本次同步呼叫，不快取、不啟動背景工作。
 * Verify的0/1結果由CLI依缺lookup及跨教會參照決定；本類格式化本身不代表實機通過。
 * 使用邊界與維護：合成測試應斷言資料分組及敏感欄位排除；TextWriter由命令owner持有與關閉。
 */
using System;
using System.Globalization;
using System.IO;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>奉獻遷移的純摘要格式化；只讀operation-local snapshot，不保留writer或輸出資料。</summary>
internal static class OfferingMigrationOutput
{
    /// <summary>輸出plan中的類別身份、每組來源筆數、未對應標籤與兩欄RequiredLevel摘要。</summary>
    internal static void WritePlan(OfferingMigrationPlan plan, TextWriter output)
    {
        output.WriteLine("Organization: " + plan.Organization);
        output.WriteLine("OrganizationId: " + plan.OrganizationId.ToString("D"));
        output.WriteLine("Source records: " + plan.SourceRecordCount.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("Unresolved church records: " + plan.UnresolvedChurchRecordCount.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("new_fee.new_category RequiredLevel: " + plan.FeeCategoryRequiredLevel);
        output.WriteLine("new_dedication_booking.new_dedication_category RequiredLevel: " + plan.BookingCategoryRequiredLevel);
        output.WriteLine("Planned categories:");
        foreach (var category in plan.Categories)
        {
            output.WriteLine("church=" + category.ChurchId.ToString("D")
                + ";code=" + category.Code
                + ";name=" + category.Name
                + ";legacyOptionValue=" + category.LegacyOptionValue!.Value.ToString(CultureInfo.InvariantCulture)
                + ";sortOrder=" + category.SortOrder.ToString(CultureInfo.InvariantCulture)
                + ";sourceCount=" + category.RecordCount.ToString(CultureInfo.InvariantCulture));
        }
        output.WriteLine("Unmatched screen labels:");
        foreach (var label in plan.UnmatchedScreenLabels) output.WriteLine("label=" + label);
    }

    /// <summary>按教會與舊選項值輸出缺lookup計數，另列無法判定及跨教會參照，不靠全域總數掩蓋問題。</summary>
    internal static void WriteVerification(OfferingVerificationResult result, TextWriter output)
    {
        output.WriteLine("Missing lookup by church and option:");
        foreach (var item in result.MissingLookupByChurch)
            output.WriteLine("church=" + item.Key.ChurchId.ToString("D")
                + ";option=" + item.Key.OptionValue.ToString(CultureInfo.InvariantCulture)
                + ";count=" + item.Value.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("Unresolved church records: " + result.UnresolvedChurchRecordCount.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("Lookup to another church category: " + result.ForeignChurchLookups.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var item in result.ForeignChurchLookups)
            output.WriteLine("entity=" + item.Entity
                + ";expectedChurch=" + item.ExpectedChurchId.ToString("D")
                + ";actualChurch=" + item.ActualChurchId.ToString("D"));
    }
}
