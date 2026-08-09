// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementEvidenceSerializationTests.cs
// 用途：保護 P7.2 Slice C child evidence 與 PowerShell strict parser 之間的 JSON 欄位命名契約。
//
// 邊界與隔離：本測試只在記憶體建立 private evidence record 的 reflection instance，絕不建立 Data8
// runtime、讀取 Credential Manager、連線 CE 或寫入 temporary evidence。它驗證 child 送到 parent 的
// operation 欄位必須是 camelCase；若使用預設 System.Text.Json 的 PascalCase，parent 必須 fail closed，
// 而此測試會先失敗以阻止實機 handoff 把可解析 evidence 誤判為 unavailable。
// ============================================================================

using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 Slice C temporary evidence 的 JSON 欄位命名。這是 child/parent 的跨 process 信任邊界：parent
/// 只能接受 lower camelCase 的 fixed schema，且不應嘗試猜測或寬鬆轉換 PascalCase、GUID、例外或其他
/// payload。測試不保留任何外部資源，因此完成後沒有 session、credential 或 managed resource cleanup 工作。
/// </summary>
public sealed class P72Data8ListManagementEvidenceSerializationTests
{
    /// <summary>
    /// 保護 operation evidence 的 camelCase wire contract。故障注入是直接序列化 child 使用的 private
    /// record；決定性 assertion 是輸出必須含 <c>operationId</c> 而非 <c>OperationId</c>，使嚴格
    /// PowerShell parser 不會因 CLR property naming 預設值而把安全、去識別化 evidence 誤判為遺失。
    /// </summary>
    [Fact]
    public void Slice_c_operation_evidence_serializes_with_the_parent_camel_case_contract()
    {
        var operationType = typeof(LivePackage02Data8ListManagementEvidenceTests).GetNestedType(
            "SliceCOperationEvidence",
            BindingFlags.NonPublic);
        operationType.Should().NotBeNull();

        var operation = Activator.CreateInstance(
            operationType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                "list.members.add.many",
                "not-run",
                "baseline-unprovable",
                false,
                "baseline-absent",
                "not-applicable"
            ],
            culture: null);
        operation.Should().NotBeNull();

        var optionsField = typeof(LivePackage02Data8ListManagementEvidenceTests).GetField(
            "EvidenceJsonOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        optionsField.Should().NotBeNull();
        var options = optionsField!.GetValue(null) as JsonSerializerOptions;
        options.Should().NotBeNull(
            because: "the child writer must use its fixed, shared naming options rather than the CLR default serializer contract");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(operation, options));
        document.RootElement.TryGetProperty("operationId", out _).Should().BeTrue(
            because: "the PowerShell parent accepts only the lower camelCase operation schema");
        document.RootElement.TryGetProperty("OperationId", out _).Should().BeFalse(
            because: "PascalCase would be a parser-contract mismatch, not a value that parent may normalize");
    }
}
