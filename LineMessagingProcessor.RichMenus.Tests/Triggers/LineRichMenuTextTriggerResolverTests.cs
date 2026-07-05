// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineRichMenuTextTriggerResolverTests
// 主要成員：TryResolve_uses_trimmed_ordinal_trigger_mapping
// 引用命名空間：FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Triggers;

/// <summary>
/// 驗證文字觸發 resolver 的輸入正規化與 exact-match 行為。
/// 這些測試鎖住「使用者輸入前後空白不影響判斷」與「產品設定文字可直接對應 menu key」的契約。
/// </summary>
public sealed class LineRichMenuTextTriggerResolverTests
{
    /// <summary>
    /// 確認 resolver 會先 trim 使用者收到的文字，再用 options 內的對照表解析 menu key。
    /// 這能避免 LINE webhook payload 因使用者輸入空白而無法切換 RichMenu。
    /// </summary>
    [Fact]
    public void TryResolve_uses_trimmed_ordinal_trigger_mapping()
    {
        var resolver = new LineRichMenuTextTriggerResolver(new LineRichMenuTextTriggerOptions
        {
            ExactTextToMenuKey =
            {
                ["主選單"] = "member-main"
            }
        });

        resolver.TryResolve(" 主選單 ", out var menuKey).Should().BeTrue();
        menuKey.Should().Be("member-main");
        resolver.TryResolve("主選單", out _).Should().BeTrue();
        resolver.TryResolve("主選單1", out _).Should().BeFalse();
    }
}
