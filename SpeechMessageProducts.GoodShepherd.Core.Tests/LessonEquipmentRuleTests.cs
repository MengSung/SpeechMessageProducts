/*
 * 檔案整體說明
 * 目的與定位：P5-07設備規則的雙宿主離線契約，期望直接閱讀EquipmentUtility.cs:62、
 * 305-334取得，不宣稱舊程式實跑或實機影子驗收；使用八個固定合成階段及正式GUID。
 * 主要流程：正常案例只取完成課程最高階，空集合／未知階名維持尚未裝備；同名不同ID
 * 完整參與判斷。故障注入重複ID、空ID、重複階段及超量資料，必須在發布結果前拒絕。
 * 使用邊界與維護：所有狀態只屬於測試呼叫；沒有SDK、機密、背景Task、連線或共享快取，列舉器由規則
 * 同步關閉。A／B／A呼叫證明沒有上一個會友結果殘留；不把純值檢查當伺服器授權。
 */
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>以獨立閱讀期望保護最高完成階級及逐筆身分契約，不用產品函式重生期望。</summary>
public sealed class LessonEquipmentRuleTests
{
    /// <summary>核定名稱；正常、邊界及身分錯誤皆在同一條業務契約內驗證。</summary>
    [Fact]
    public void Lessons_Equipment_StatusTransitions_MatchLegacy()
    {
        var stages = new[] { "尚未裝備", "門徒學校上", "E1成長營", "門徒學校下", "E2門徒營", "幸福大學上", "E3領袖營", "幸福大學下" };
        var first = new Guid("11111111-1111-1111-1111-111111111111");
        var second = new Guid("22222222-2222-2222-2222-222222222222");
        var third = new Guid("33333333-3333-3333-3333-333333333333");
        var rows = new[] { new LessonCompletion(first, true, "門徒學校下"), new LessonCompletion(second, false, "幸福大學下"), new LessonCompletion(third, true, "E2門徒營") };
        Assert.Equal("E2門徒營", LessonEquipmentRule.Evaluate(rows, stages));
        Assert.Equal("尚未裝備", LessonEquipmentRule.Evaluate(Array.Empty<LessonCompletion>(), stages));
        Assert.Equal("尚未裝備", LessonEquipmentRule.Evaluate(new[] { new LessonCompletion(first, true, "未知階段") }, stages));
        Assert.Equal("E2門徒營", LessonEquipmentRule.Evaluate(rows, stages));
        Assert.Equal("門徒學校下", LessonEquipmentRule.Evaluate(new[] { new LessonCompletion(first, true, "門徒學校下"), new LessonCompletion(second, true, "門徒學校下") }, stages));
        Assert.Throws<InvalidOperationException>(() => LessonEquipmentRule.Evaluate(new[] { rows[0], rows[0] }, stages));
        Assert.Throws<ArgumentException>(() => new LessonCompletion(Guid.Empty, true, "門徒學校下"));
        var duplicateStages = new[] { "尚未裝備", "尚未裝備" };
        Assert.Throws<ArgumentException>(() => LessonEquipmentRule.Evaluate(rows, duplicateStages));
        Assert.Throws<InvalidOperationException>(() => LessonEquipmentRule.Evaluate(Enumerable.Range(1, 4097).Select(i => new LessonCompletion(new Guid(i, 0, 0, new byte[8]), false, null)), stages));
    }
}
