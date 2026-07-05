// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ChurchReportLineAdminNotificationServiceTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class ChurchReportLineAdminNotificationServiceTests、class CapturingWorkflow
// 主要成員：NotifyError_sends_best_effort_admin_notification_through_workflow、NotifyError_keeps_legacy_registration_message_shape_when_category_is_supplied、NotifyError_swallows_workflow_failure_to_preserve_original_exception_flow、SendAsync、SendOrThrowAsync、Requests、SendAsyncException
// 引用命名空間：ChurchReport.Services、FluentAssertions、LineMessagingProcessor.Workflows、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Services;
using FluentAssertions;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class ChurchReportLineAdminNotificationServiceTests
{
    [Fact]
    public void NotifyError_sends_best_effort_admin_notification_through_workflow()
    {
        var workflow = new CapturingWorkflow();
        var service = new ChurchReportLineAdminNotificationService(workflow, "Uadmin");

        service.NotifyError("Product", "CRM failed");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("Uadmin");
        workflow.Requests[0].Content.Text.Should().Be("Product: 錯誤 => CRM failed");
        workflow.Requests[0].Metadata.Should().ContainKey("source")
            .WhoseValue.Should().Be("ChurchReport.LineAdminErrorNotification");
        workflow.Requests[0].Metadata.Should().ContainKey("productSource")
            .WhoseValue.Should().Be("Product");
        workflow.Requests[0].Metadata.Should().ContainKey("category")
            .WhoseValue.Should().Be("錯誤");
    }

    [Fact]
    public void NotifyError_keeps_legacy_registration_message_shape_when_category_is_supplied()
    {
        var workflow = new CapturingWorkflow();
        var service = new ChurchReportLineAdminNotificationService(workflow, "Uadmin");

        service.NotifyError("好牧人", "註冊錯誤", "Register failed");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Content.Text.Should().Be("好牧人 : 註冊錯誤 => Register failed");
        workflow.Requests[0].Metadata.Should().ContainKey("productSource")
            .WhoseValue.Should().Be("好牧人");
        workflow.Requests[0].Metadata.Should().ContainKey("category")
            .WhoseValue.Should().Be("註冊錯誤");
    }

    [Fact]
    public void NotifyError_swallows_workflow_failure_to_preserve_original_exception_flow()
    {
        var workflow = new CapturingWorkflow
        {
            SendAsyncException = new InvalidOperationException("LINE unavailable")
        };
        var service = new ChurchReportLineAdminNotificationService(workflow, "Uadmin");

        var action = () => service.NotifyError("Product", "CRM failed");

        action.Should().NotThrow();
        workflow.Requests.Should().ContainSingle();
    }

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public Exception? SendAsyncException { get; set; }

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            if (SendAsyncException != null)
            {
                throw SendAsyncException;
            }

            return Task.FromResult(LineNotificationResult.Success(request));
        }

        public Task SendOrThrowAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
