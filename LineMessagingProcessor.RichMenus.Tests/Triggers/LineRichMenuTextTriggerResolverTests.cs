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
