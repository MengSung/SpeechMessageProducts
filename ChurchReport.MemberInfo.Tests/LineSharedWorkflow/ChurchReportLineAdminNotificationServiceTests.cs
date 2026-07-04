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
