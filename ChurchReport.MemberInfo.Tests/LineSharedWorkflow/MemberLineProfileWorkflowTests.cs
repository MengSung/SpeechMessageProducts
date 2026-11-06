/*
 * 檔案整體說明
 *
 * 目的與定位：以控制器來源契約確認LINE profile查閱委派給共用processor，防止產品端重新分叉provider操作。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberLineProfileWorkflowTests。
 * 測試案例入口包含 MemberInfoController_uses_processor_for_line_profile_lookup。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class MemberLineProfileWorkflowTests
{
    [Fact]
    public void MemberInfoController_uses_processor_for_line_profile_lookup()
    {
        var controllerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "MemberInfoController.cs"));

        var source = File.ReadAllText(controllerPath);

        source.Should().Contain("LineMessagingProcessorClass(token)");
        source.Should().Contain("lineProcessor.GetUserProfileAsync(lineId)");
        source.Should().NotContain("new Line.Messaging.LineMessagingClient(token)");
    }
}
