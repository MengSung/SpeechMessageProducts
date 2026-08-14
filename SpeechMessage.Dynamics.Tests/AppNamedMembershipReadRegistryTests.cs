// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedMembershipReadRegistryTests.cs
// 用途：在不建立 CE、Data8 connector、連線池或任何外部資源前，守護 ORG-CALL-00057 的封閉名單成員讀取契約。
//
// 本測試僅讀取 process-static registry、反射型別與 repository source。固定 contact GUID、operation ID 與模板
// 名稱都是不含使用者、Profile、credential、session、Entity 或可重用 connection 的合約資料；案例結束後不保留
// cancellation registration、timer、stream、handle 或跨案例 mutable state。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 ORG-CALL-00057「依 contact 讀取 App-named 名單成員關係」先形成獨立、server-owned、單一定位器及有限
/// 資料面的封閉邊界。此類別不派送 operation；紅燈只代表編譯後 allowlist 或 wire response contract 尚未建立，
/// 不代表 CE、Data8、deployment、consumer 或 feature gate 已啟用。未來 consumer 必須在 dispatch 前另行完成
/// principal-derived authorization，且不能將 browser/session/legacy mutable state 當作此 contactId 的授權依據。
/// </summary>
public sealed class AppNamedMembershipReadRegistryTests
{
    private const string OperationId = "list.membership.retrieve.appnamed.by.contact";
    private const string TemplateId = "list.membership.appnamed.by.contact.v1";
    private const string ResponseKindName = "AppNamedMembershipRecords";

    /// <summary>
    /// 保護 registry、封閉 response union 與 wire row 的完整 ORG-CALL-00057 契約。故障注入是尚未登錄的
    /// operation、branch、factory 與 row type；決定性斷言要求唯一 contact GUID parameter、固定 QueryExpression
    /// template、單頁 32 列／32 KiB 預算，以及僅含 list GUID 與 nullable 名稱的純量列。如此呼叫端不能注入
    /// 篩選、排序、profile、endpoint、credential、CRM Entity 或可變集合，避免跨 capability、跨 request 或跨 profile
    /// 資料混合。此測試不建立任何 I/O owner，反射與來源文字皆只存活到目前案例結束。
    /// </summary>
    [Fact]
    public void ORG_CALL_00057_registry_and_response_declare_the_exact_bounded_appnamed_membership_contract()
    {
        Package01OperationRegistry.TryGet(OperationId, out var definition).Should().BeTrue(
            because: "ORG-CALL-00057 必須是固定 capability，不能退回 generic CRM relationship query");

        definition.Should().NotBeNull();
        definition!.OperationKind.Should().Be("read");
        definition.TemplateKind.Should().Be("queryexpression");
        definition.TemplateId.Should().Be(TemplateId);
        definition.ResponseKind.ToString().Should().Be(ResponseKindName);
        definition.DataClassification.Should().Be("personal-data");
        definition.AuditRequirement.Should().Be("read-audit");
        definition.IdempotencyClass.Should().Be("read-only");
        definition.MaximumPageCount.Should().Be(1);
        definition.MaximumPageBytes.Should().Be(32 * 1024);
        definition.MaximumCumulativeResponseBytes.Should().Be(32 * 1024);
        definition.MaximumResultItemCount.Should().Be(32);
        definition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        }).Should().BeEquivalentTo(
            [
                new
                {
                    Name = "contactId",
                    Type = "guid",
                    Required = true,
                    EncodingContext = "queryexpression-condition"
                }
            ],
            options => options.WithStrictOrdering());

        Enum.GetNames<OperationResponseKind>().Should().Contain(ResponseKindName);
        var constructor = typeof(OperationResponseData).GetConstructors().Should().ContainSingle().Subject;
        constructor.GetParameters().Select(parameter => parameter.Name).Should().Contain("appNamedMembershipRecords");
        typeof(OperationResponseData).GetMethods()
            .Select(method => method.Name)
            .Should()
            .Contain("ForAppNamedMembershipRecords");

        var recordType = typeof(OperationResponseData).Assembly.GetType(
            "SpeechMessage.Dynamics.Abstractions.Operations.AppNamedMembershipRecord");
        recordType.Should().NotBeNull(
            because: "membership row 必須有只含 allowlisted pure scalar 的獨立 wire record");
        recordType!
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new { property.Name, property.PropertyType })
            .Should()
            .Equal(
                new { Name = "ListId", PropertyType = typeof(Guid) },
                new { Name = "ListName", PropertyType = typeof(string) });

        var responseSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Abstractions",
            "Operations",
            "OperationResponseData.cs");
        responseSource.Should().Contain(
            "OperationResponseKind.AppNamedMembershipRecords => branchCount == 1 && appNamedMembershipRecords is not null");
    }

    /// <summary>
    /// 從 test output 向上定位 repository 並在 using scope 內讀取來源。檔案 bytes 不會進入產品 cache、session 或
    /// background work；這個 helper 沒有持久 handle，故不會把測試路徑或內容保留到另一個 profile/request。
    /// </summary>
    /// <param name="segments">由 solution root 起算的安全相對路徑片段。</param>
    /// <returns>本次 assertion 專屬的 UTF-8 source text。</returns>
    private static string ReadRepositorySource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return File.ReadAllText(Path.Combine([directory.FullName, .. segments]));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到含有 SpeechMessageProducts.sln 的 repository root。");
    }
}
