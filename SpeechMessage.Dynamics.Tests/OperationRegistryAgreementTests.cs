// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OperationRegistryAgreementTests.cs
// 用途：驗證 Phase 0 matrix 與編譯後 registry 的 operation、模板、回應 discriminator 及資源政策逐列一致。
//
// 安全與生命週期邊界：
// 1. matrix 是部署前可稽核的 machine-readable contract；本檔保證它不能在 registry 之外自行宣告新的
//    response branch、無界 paging 或不同的 template revision。
// 2. JSON 文件只在 test method 範圍內以 using 持有並立即 dispose；測試不建立 HTTP、認證、Token、queue
//    或背景 work，因此不會把輸入檔案內容跨測試或跨設定檔世代保存。
// 3. registry policy 可來自 immutable 70-row source call-site 或其明確可追溯的 derived mapping；兩者合併後
//    每個 capability 仍只能有一列，避免把 CRM metadata graph、URL 或 OData extension 資料誤標為產品可回傳資料。
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 將 immutable C# registry 和版本化 JSON matrix 視為同一個安全契約的兩種表示，避免產品授權、connector
/// projection、queue revision 與稽核資料各自採用不同 response kind 或容量上限。
/// </summary>
public sealed class OperationRegistryAgreementTests
{
    /// <summary>
    /// 每個 compiled registry 作業必須在 matrix 中恰有一列，並逐項比對 template、參數、版本證據、audit、
    /// idempotency、response kind 與三個有限上限。這是在任何 credential-bearing request 前執行的純檔案
    /// 檢查，發現 drift 時以測試失敗關閉。
    /// </summary>
    [Fact]
    public void Compiled_registry_exactly_matches_enforced_phase0_matrix_rows()
    {
        using var matrix = OpenMatrix();
        var document = matrix.RootElement;

        document.GetProperty("operationRegistryAgreementGate")
            .GetProperty("status")
            .GetString()
            .Should().Be("enforced");

        var rows = GetRegistryAgreementRows(document);

        foreach (var definition in Package01OperationRegistry.All)
        {
            var matchingRows = rows
                .Where(row => string.Equals(
                    row.GetProperty("capabilityOperationId").GetString(),
                    definition.CapabilityOperationId,
                    StringComparison.Ordinal))
                .ToArray();

            matchingRows.Should().ContainSingle(
                $"registry operation {definition.CapabilityOperationId} 必須在 matrix 有且只有一列");
            AssertDefinitionMatchesRow(definition, matchingRows[0]);
        }
    }

    /// <summary>
    /// matrix 只允許目前 registry 所擁有的 capability 宣告 response policy；immutable source call-site 與
    /// derived mapping 合併後仍須一對一，這防止尚未投產的 call site 偽裝成可回傳的 typed payload，也能確保
    /// metadata row 的封閉 pure-value branch 和有限政策受到 CI 鎖定。
    /// </summary>
    [Fact]
    public void Matrix_response_policy_is_present_for_exactly_current_registry_rows()
    {
        using var matrix = OpenMatrix();
        var rows = GetRegistryAgreementRows(matrix.RootElement);

        var responsePolicyRows = rows
            .Where(row => row.TryGetProperty("responseKind", out _))
            .ToArray();
        var registryIds = Package01OperationRegistry.All
            .Select(definition => definition.CapabilityOperationId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var matrixIds = responsePolicyRows
            .Select(row => row.GetProperty("capabilityOperationId").GetString())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        responsePolicyRows.Should().HaveCount(registryIds.Length);
        matrixIds.Should().Equal(registryIds);

        foreach (var row in responsePolicyRows)
        {
            row.TryGetProperty("maximumPageCount", out _).Should().BeTrue();
            row.TryGetProperty("maximumPageBytes", out _).Should().BeTrue();
            row.TryGetProperty("maximumCumulativeResponseBytes", out _).Should().BeTrue();
            row.TryGetProperty("maximumResultItemCount", out _).Should().BeTrue();
        }
    }

    /// <summary>
    /// derived mapping 只能重述既有 immutable source call-site 的另一個封閉 DTO contract：每列必須回指剛好
    /// 一個 source ID、使用不同 capability ID，且不得重複任一 source 或其他 derived capability。故障模型是
    /// 以 derived array 偷渡新的 CRM 呼叫或覆蓋來源 contract；決定性斷言是在任何 connector/CE 互動前拒絕。
    /// </summary>
    [Fact]
    public void Derived_registry_mappings_are_traceable_to_exactly_one_distinct_source_capability()
    {
        using var matrix = OpenMatrix();
        var document = matrix.RootElement;
        var sourceRows = document.GetProperty("normalizedCallSites")
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToArray();
        var derivedRows = document.GetProperty("derivedOperationMappings")
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToArray();

        sourceRows.Should().HaveCount(70, "normalizedCallSites 是 immutable 70-row source inventory");

        var sourceOperationIds = sourceRows
            .Select(row => row.GetProperty("capabilityOperationId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var derivedOperationIds = derivedRows
            .Select(row => row.GetProperty("capabilityOperationId").GetString()!)
            .ToArray();
        derivedOperationIds.Should().OnlyHaveUniqueItems(
            "一個 derived capability 只能有一筆 machine-readable policy");
        derivedOperationIds.Should().NotIntersectWith(sourceOperationIds,
            "derived mapping 不可覆蓋 immutable source capability");

        foreach (var derivedRow in derivedRows)
        {
            var sourceId = derivedRow.GetProperty("id").GetString();
            var matchingSources = sourceRows
                .Where(sourceRow => string.Equals(
                    sourceRow.GetProperty("id").GetString(),
                    sourceId,
                    StringComparison.Ordinal))
                .ToArray();
            matchingSources.Should().ContainSingle(
                $"derived mapping {derivedRow.GetProperty("capabilityOperationId").GetString()} 必須回指唯一 source call-site");
            derivedRow.GetProperty("capabilityOperationId").GetString().Should()
                .NotBe(matchingSources[0].GetProperty("capabilityOperationId").GetString(),
                    "derived mapping 必須是另一個封閉 DTO contract，而非重標來源 capability");
        }
    }

    /// <summary>
    /// schema 必須宣告封閉 discriminator、有限整數範圍及只允許目前 registry capability 填入政策的條件；
    /// derived mapping 必須重用同一個 source-row schema，避免新增未審查的資料外洩或記憶體保留契約。
    /// 此檢查不載入網路 schema，只讀 repository 內版本化檔案。
    /// </summary>
    [Fact]
    public void Matrix_schema_declares_closed_response_policy_contract()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(
            root,
            ".trellis",
            "tasks",
            "07-23-dynamics-connection-compatibility",
            "phase0-organization-call-matrix.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        var normalizedCallSite = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("normalizedCallSite");
        var properties = normalizedCallSite.GetProperty("properties");

        properties.GetProperty("responseKind").GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .BeEquivalentTo(Enum.GetNames<OperationResponseKind>());
        properties.GetProperty("maximumPageCount").GetProperty("minimum").GetInt32().Should().BeGreaterThan(0);
        properties.GetProperty("maximumPageBytes").GetProperty("minimum").GetInt32().Should().BeGreaterThan(0);
        properties.GetProperty("maximumCumulativeResponseBytes").GetProperty("minimum").GetInt32().Should().BeGreaterThan(0);
        properties.GetProperty("maximumResultItemCount").GetProperty("minimum").GetInt32().Should().BeGreaterThan(0);
        normalizedCallSite.TryGetProperty("allOf", out _).Should().BeTrue();

        var derivedMappings = schema.RootElement
            .GetProperty("properties")
            .GetProperty("derivedOperationMappings");
        derivedMappings.GetProperty("items").GetProperty("$ref").GetString()
            .Should().Be("#/$defs/normalizedCallSite");
        schema.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should().Contain("derivedOperationMappings",
                "registry agreement 需要明確區分 immutable source inventory 與 derived mapping");
    }

    private static void AssertDefinitionMatchesRow(OperationDefinition definition, JsonElement row)
    {
        var template = row.GetProperty("serverOwnedTemplate");
        template.GetProperty("templateKind").GetString().Should().Be(definition.TemplateKind);
        template.GetProperty("templateId").GetString().Should().Be(definition.TemplateId);
        template.GetProperty("templateHash").GetString().Should().Be(definition.TemplateHash);
        row.GetProperty("operationKind").GetString().Should().Be(definition.OperationKind);
        row.GetProperty("dataClassification").GetString().Should().Be(definition.DataClassification);
        row.GetProperty("auditRequirement").GetString().Should().Be(definition.AuditRequirement);
        row.GetProperty("idempotencyClass").GetString().Should().Be(definition.IdempotencyClass);
        row.GetProperty("responseKind").GetString().Should().Be(definition.ResponseKind.ToString());
        row.GetProperty("maximumPageCount").GetInt32().Should().Be(definition.MaximumPageCount);
        row.GetProperty("maximumPageBytes").GetInt32().Should().Be(definition.MaximumPageBytes);
        row.GetProperty("maximumCumulativeResponseBytes").GetInt32()
            .Should().Be(definition.MaximumCumulativeResponseBytes);
        row.GetProperty("maximumResultItemCount").GetInt32()
            .Should().Be(definition.MaximumResultItemCount);

        var matrixParameters = row.GetProperty("typedParameters")
            .EnumerateArray()
            .Select(parameter => new ParameterContract(
                parameter.GetProperty("name").GetString()!,
                parameter.GetProperty("type").GetString()!,
                parameter.GetProperty("required").GetBoolean(),
                parameter.GetProperty("encodingContext").GetString()!))
            .ToArray();
        var registryParameters = definition.Parameters
            .Select(parameter => new ParameterContract(
                parameter.Name,
                parameter.Type,
                parameter.Required,
                parameter.EncodingContext))
            .ToArray();
        matrixParameters.Should().Equal(registryParameters,
            $"{definition.CapabilityOperationId} 的 typed parameter 與 encoding 必須由同一 registry owner 宣告");

        var expectedEncodingContexts = definition.Parameters.Count == 0
            ? ["none"]
            : definition.Parameters
                .Select(parameter => parameter.EncodingContext)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        var matrixEncodingContexts = row.GetProperty("encodingContexts")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        matrixEncodingContexts.Should().Equal(expectedEncodingContexts);

        var versionEvidence = row.GetProperty("versionEvidence");
        AssertEvidenceDeclared(versionEvidence.GetProperty("v8_2"), "v8.2", definition.CapabilityOperationId);
        AssertEvidenceDeclared(versionEvidence.GetProperty("v9_1"), "v9.1", definition.CapabilityOperationId);
    }

    /// <summary>
    /// 合併 immutable source call-site 與 derived registry mapping。前者仍是固定 70-row inventory；後者只可
    /// 為同一 legacy call-site 建立不同且封閉的 DTO response contract，不能藉此增加未盤點的 CRM 呼叫。
    /// 每個 JsonElement 都 clone 成 method-owned value，原始 JsonDocument 仍由呼叫端 using 釋放。
    /// </summary>
    private static JsonElement[] GetRegistryAgreementRows(JsonElement document)
    {
        var normalizedRows = document.GetProperty("normalizedCallSites")
            .EnumerateArray()
            .Select(row => row.Clone());
        var derivedRows = document.GetProperty("derivedOperationMappings")
            .EnumerateArray()
            .Select(row => row.Clone());
        return normalizedRows.Concat(derivedRows).ToArray();
    }

    private static void AssertEvidenceDeclared(JsonElement evidence, string ceVersion, string operationId)
    {
        evidence.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace(
            $"{operationId} 必須宣告 {ceVersion} 的 metadata/smoke/blocked 證據狀態");
        evidence.GetProperty("notes").GetString().Should().NotBeNullOrWhiteSpace(
            $"{operationId} 的 {ceVersion} 證據必須保留可稽核說明，而非儲存 credential 或 upstream payload");
    }

    /// <summary>
    /// 將 matrix 檔案讀入一個由呼叫端 using 擁有的 JsonDocument。呼叫端必須在 assertion 完成後 dispose，確保
    /// byte buffer 與 JsonElement 不會跨越 test case 或被誤當成產品回應資料保存。
    /// </summary>
    private static JsonDocument OpenMatrix()
    {
        var root = FindRepositoryRoot();
        var matrixPath = Path.Combine(
            root,
            ".trellis",
            "tasks",
            "07-23-dynamics-connection-compatibility",
            "phase0-organization-call-matrix.json");
        return JsonDocument.Parse(File.ReadAllBytes(matrixPath));
    }

    /// <summary>
    /// 由測試輸出目錄向上尋找 solution root；只傳回 repository 位置，不保留檔案 handle、環境 credential 或
    /// process-wide mutable cache，避免測試工具本身成為跨世代狀態 owner。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到含有 SpeechMessageProducts.sln 的 repository root。");
    }

    private sealed record ParameterContract(
        string Name,
        string Type,
        bool Required,
        string EncodingContext);
}
