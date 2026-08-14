// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoAuthorizationAssignmentRegistryTests.cs
// 用途：先以 RED 契約守護 P7 MemberInfo 伺服器擁有指派證據 operation 的固定 registry 邊界。
//
// 此測試只讀取 process-static registry metadata，不建立 CE、Data8 connector、連線池、租約、Session、
// cache、背景工作或任何可跨使用者保留的資源。它要求 operation 僅接受 server-validated subject GUID；
// 成功前的紅燈代表 capability 尚未被登錄，而不是可以改用 legacy ListManager 或 caller-selected CRM query。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 MemberInfo 指派證據必須有獨立、固定且 bounded 的 registry contract。
/// 這是 P7.4 consumer migration 的前置資料面，不接線 controller、feature gate、traffic 或 CE mutation；
/// registry 不保存 subject、profile、credential、Session 或回應資料，因此任何實際 request 的隔離與資源釋放
/// 仍由後續 executor lease owner 負責。
/// </summary>
public sealed class MemberInfoAuthorizationAssignmentRegistryTests
{
    private const string OperationId = "memberinfo.authorization.assignment.resolve.by.subject";
    private const string TemplateId = "memberinfo.authorization.assignment.by.subject.v1";
    private const string ResponseKindName = "MemberInfoAssignmentEvidence";

    /// <summary>
    /// 保護固定 operation 只能宣告一個 subject GUID，且其結果上限為 512 筆 assignment list。
    /// 故障注入是目前尚未登錄的 capability ID；決定性斷言要求先有 server-owned allowlist，避免未來實作
    /// 偷渡 browser list ID、role、profile、endpoint、credential、FetchXML、日期或排序至 connector／pool。
    /// </summary>
    [Fact]
    public void Registry_declares_the_exact_bounded_subject_assignment_evidence_operation()
    {
        Package01OperationRegistry.TryGet(OperationId, out var definition).Should().BeTrue(
            because: "MemberInfo 授權證據必須由固定 operation 取得，不能退回 generic CRM bridge 或 legacy ListManager");

        definition.Should().NotBeNull();
        definition!.OperationKind.Should().Be("read");
        definition.TemplateKind.Should().Be("queryexpression");
        definition.TemplateId.Should().Be(TemplateId);
        definition.ResponseKind.ToString().Should().Be(ResponseKindName);
        definition.IdempotencyClass.Should().Be("read-only");
        definition.MaximumPageCount.Should().Be(1);
        definition.MaximumResultItemCount.Should().Be(512);
        definition.Parameters.Should().ContainSingle();
        definition.Parameters[0].Name.Should().Be("subjectContactId");
        definition.Parameters[0].Type.Should().Be("guid");
        definition.Parameters[0].Required.Should().BeTrue();
        definition.Parameters[0].EncodingContext.Should().Be("queryexpression-condition");
    }

    /// <summary>
    /// 保護 operation 回應必須使用獨立的 assignment evidence union branch，而不是借用 membership、catalog、
    /// Entity 或通用 object。故障注入是目前不存在的 discriminator、constructor parameter、property 與 factory；
    /// 決定性斷言防止 Church-wide 結論、subject 或 list allowlist 被錯誤混入另一 capability 的 response。
    /// </summary>
    [Fact]
    public void Response_union_exposes_an_exclusive_memberinfo_assignment_evidence_branch()
    {
        Enum.GetNames<OperationResponseKind>().Should().Contain(ResponseKindName);

        var constructor = typeof(OperationResponseData).GetConstructors().Should().ContainSingle().Subject;
        constructor.GetParameters()
            .Select(parameter => parameter.Name)
            .Should()
            .Contain("memberInfoAuthorizationAssignmentEvidence");
        typeof(OperationResponseData).GetProperty("MemberInfoAuthorizationAssignmentEvidence")
            .Should().NotBeNull();
        typeof(OperationResponseData).GetMethods()
            .Select(method => method.Name)
            .Should()
            .Contain("ForMemberInfoAuthorizationAssignmentEvidence");
    }
}
