// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoPresentRecordReadData8Tests.cs
// 用途：保護 ORG-CALL-00026 的 Data8 固定查詢邊界，不讓後續修改重新開放呼叫端自選查詢、排序或續頁。
//
// 測試安全與生命週期契約：
// 1. 這些測試不建立 CRM fixture、不連線 CE，僅驗證已簽入的固定模板文字；因此不保存 profile、憑證、Session、
//    IOrganizationService、連線或背景工作。
// 2. 決定性斷言同時要求實作檔與 Data8 dispatch 出現，確保只有 server-owned operation ID 能進入該固定讀取路徑。
// 3. 後續的行為測試會覆蓋 row materialization；本測試特別防止執行器漏接固定 operation 而悄悄落入 generic fallback。
// ============================================================================

using FluentAssertions;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 MemberInfo 出席紀錄讀取的 Data8 固定查詢與 dispatch 邊界。
/// 此類別只讀取目前組件旁的原始碼；它不持有 CRM SDK、Data8 pool、連線、使用者資料或可跨請求重用的狀態。
/// </summary>
public sealed class MemberInfoPresentRecordReadData8Tests
{
    /// <summary>
    /// 保護的契約：ORG-CALL-00026 必須由唯一的 Data8 helper 擁有，並且 OnPremise client 只對精確 operation ID
    /// 呼叫該 helper。故障注入為 helper 尚未建立或 dispatch 尚未接線；決定性斷言要求兩者皆存在，否則測試失敗，
    /// 以避免操作落入 Package01 generic read 或接受呼叫端可控制的查詢。
    /// </summary>
    [Fact]
    public void Present_record_read_has_a_dedicated_fixed_query_executor_and_exact_dispatch()
    {
        var repositoryRoot = FindRepositoryRoot();
        var helperPath = Path.Combine(
            repositoryRoot,
            "SpeechMessage.Dynamics.Connectors.Data8",
            "Package02Data8PresentRecordReadOperations.cs");
        var clientPath = Path.Combine(
            repositoryRoot,
            "SpeechMessage.Dynamics.Connectors.Data8",
            "OnPremiseData8ConnectorClientFactory.cs");

        File.Exists(helperPath).Should().BeTrue(
            "ORG-CALL-00026 must have its own fixed-query executor rather than a generic CRM read fallback");

        var helperSource = File.ReadAllText(helperPath);
        var clientSource = File.ReadAllText(clientPath);

        helperSource.Should().Contain("memberinfo.present.retrieve.by.contact");
        helperSource.Should().Contain("new_present_record");
        helperSource.Should().Contain("new_present_recordid");
        helperSource.Should().Contain("new_sunday_present_this_week");
        helperSource.Should().Contain("new_group_present_this_week");
        helperSource.Should().Contain("new_explanation");
        helperSource.Should().Contain("new_sunday_date");
        helperSource.Should().Contain("new_contact_new_present_record");
        helperSource.Should().Contain("ContactEntityName = \"contact\"");
        helperSource.Should().Contain("ContactFullNameAttribute = \"fullname\"");
        helperSource.Should().Contain("ContactAlias = \"presentcontact\"");
        helperSource.Should().Contain("JoinOperator.Inner");
        helperSource.Should().Contain("ReadRequiredAliasedBoundedString");
        helperSource.Should().Contain("ContactAlias + \".\" + ContactFullNameAttribute");
        helperSource.Should().Contain("OrderType.Descending");
        helperSource.Should().Contain("Count = MaximumRowsInFixedPage");
        helperSource.Should().Contain("PageNumber = 1");
        helperSource.Should().Contain("MoreRecords");
        helperSource.Should().Contain("parameters.Count != 1");
        helperSource.Should().Contain("value is not Guid contactId");
        helperSource.Should().Contain("definition.TemplateKind, \"queryexpression\"");
        helperSource.Should().NotContain("ContactFullName = null");
        clientSource.Should().Contain("OperationIds.MemberInfoPresentRetrieveByContact");
        clientSource.Should().Contain("Package02Data8PresentRecordReadOperations.Execute");
    }

    /// <summary>
    /// 由測試輸出目錄向上尋找 solution root。搜尋只使用本機檔案系統，沒有建立 cache 或背景掃描；找到後立刻返回，
    /// 所有暫存路徑只在目前測試方法的 stack frame 存活。
    /// </summary>
    /// <returns>含有 Data8 專案資料夾的 repository root。</returns>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Connectors.Data8")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root for the Data8 source-contract test was not found.");
    }
}
