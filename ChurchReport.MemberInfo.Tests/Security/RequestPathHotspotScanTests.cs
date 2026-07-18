using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class RequestPathHotspotScanTests
{
    [Fact]
    public void HomeController_TestCachePerformance_DoesNotBlockOnAsyncInvalidation()
    {
        var source = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Controllers", "HomeController.cs");

        source.Should().Contain("public async Task<IActionResult> TestCachePerformance()");
        source.Should().Contain("await cacheService.InvalidateAsync(");
        source.Should().NotContain(".InvalidateAsync($\"list_query_{testContactId}_vice_family_leader\").Wait()");
    }

    [Fact]
    public void AuthenticationLineLoginOAuth_UsesHttpClientFactory()
    {
        var source = ReadRepositoryFile(
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "AuthenticationController",
            "AuthenticationController.LineLoginOAuth.cs");

        source.Should().Contain("IHttpClientFactory");
        source.Should().Contain("CreateClient(\"LineLoginOAuth\")");
        source.Should().NotContain("new HttpClient(");

        var startup = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Startup.cs");
        startup.Should().Contain("AddHttpClient(\"LineLoginOAuth\"");
    }

    [Fact]
    public void SmallGroupLineLogin_DoesNotUseTaskRunForRequestStateMutation()
    {
        var source = ReadRepositoryFile(
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "SmallGroupController",
            "SmallGroupController.LineLogin.cs");

        source.Should().NotContain("Task.Run");
        source.Should().Contain("SetupViewBagForSmallGroup();");
        source.Should().Contain("EnsureIntegrateDataLoaded(lineUserId);");
    }

    [Fact]
    public void Dedication_SetupUserLineId_DoesNotUseTaskRunForCrmOrModelMutation()
    {
        var source = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs");
        var method = ExtractSourceSection(source, "public async Task<IActionResult> SetupUserLineId", "[Route(\"/Home/DediationLineLoginView");

        method.Should().Contain("ToolUtility.RetrieveContactByLineId(UserLineId)");
        method.Should().Contain("InMemoryContext.DonationPaymentManager.SetDonationPaymentModel(loginContact);");
        method.Should().NotContain("Task.Run");
    }

    [Fact]
    public void SmallGroupCrud_UpdatePresentRecord_DoesNotUseTaskRunForMutableListUpdates()
    {
        var source = ReadRepositoryFile(
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "SmallGroupController",
            "SmallGroupController.Crud.cs");
        var method = ExtractSourceSection(source, "public IActionResult UpdateSmallGroupPresentRecord", "public IActionResult DeletePresentRecord");

        method.Should().Contain("dataList.m_SmallGroupData.UpdateMember(key, values);");
        method.Should().Contain("dataList.m_AllMemeberData.UpdateMember(key, values);");
        method.Should().NotContain("Task.Run");
        method.Should().NotContain("Task.WhenAll");
    }

    [Fact]
    public void ContactService_GetLoginContactAsync_DoesNotWrapSynchronousCrmLookupInTaskRun()
    {
        var source = ReadRepositoryFile(
            "SpeechMessageProducts.ChurchReport",
            "Services",
            "Contact",
            "Impl",
            "ContactService.cs");
        var method = ExtractSourceSection(source, "private Task<Entity> GetLoginContactAsync", "public Entity GetContactCurrentGroup");

        method.Should().Contain("return Task.FromResult(loginContact);");
        method.Should().NotContain("Task.Run");
    }

    [Fact]
    public void DonationPaymentProcessor_KeyInContactLookup_IsBoundedAndNarrow()
    {
        var source = ReadRepositoryFile(
            "SpeechMessageProducts.ChurchReport",
            "WebServiceConnector",
            "DonationPaymentProcessor",
            "DonationPaymentProcessor.FeeManagement.cs");
        var method = ExtractSourceSection(source, "private Entity GetContactForKeyIn", "private static readonly TimeSpan");

        method.Should().Contain("TopCount = 1");
        method.Should().Contain("AddOrder(\"contactid\", OrderType.Ascending)");
        method.Should().Contain("new ColumnSet(");
        method.Should().Contain("\"new_lineid_backup\"");
        method.Should().NotContain("new ColumnSet(true)");
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. segments]));
    }

    private static string ExtractSourceSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}
