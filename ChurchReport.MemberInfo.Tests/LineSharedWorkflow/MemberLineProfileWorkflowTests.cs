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
            "ChurchReport",
            "Controllers",
            "MemberInfoController.cs"));

        var source = File.ReadAllText(controllerPath);

        source.Should().Contain("LineMessagingProcessorClass(token)");
        source.Should().Contain("lineProcessor.GetUserProfileAsync(lineId)");
        source.Should().NotContain("new Line.Messaging.LineMessagingClient(token)");
    }
}
