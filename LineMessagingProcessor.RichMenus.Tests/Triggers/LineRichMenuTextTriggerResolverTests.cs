/*
 * 檔案整體說明
 *
 * 目的與定位：驗證文字觸發 resolver 的輸入正規化與 exact-match 行為。 這些測試鎖住「使用者輸入前後空白不影響判斷」與「產品設定文字可直接對應 menu key」的契約。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuTextTriggerResolverTests。
 * - 確認 resolver 會先 trim 使用者收到的文字，再用 options 內的對照表解析 menu key。 這能避免 LINE webhook payload 因使用者輸入空白而無法切換 RichMenu。
 * 測試案例入口包含 TryResolve_uses_trimmed_ordinal_trigger_mapping。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
