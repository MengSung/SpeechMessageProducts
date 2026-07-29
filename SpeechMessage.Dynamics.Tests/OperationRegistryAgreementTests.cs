using System.Text.Json;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 將 Phase 0 Organization-call matrix 與實際編譯進產品的 operation registry 綁成可執行契約。
/// 此 gate 防止只修改 C# registry、只修改 JSON matrix，或在不更新證據/稽核分類的情況下悄悄改變伺服器範本；
/// 任一漂移都必須在 CI 失敗，而不是等到 Gateway 或 Embedded 執行錯誤的查詢形狀才被發現。
/// </summary>
public sealed class OperationRegistryAgreementTests
{
    /// <summary>
    /// 每個已註冊操作必須在 matrix 恰有一列，且完整比對 template、具型別參數、encoding、audit、idempotency
    /// 與兩個 CE 版本的證據狀態。測試只讀 repository artifact，不建立快取或保留 JsonDocument 到測試之外。
    /// </summary>
    [Fact]
    public void Compiled_registry_exactly_matches_enforced_phase0_matrix_rows()
    {
        var root = FindRepositoryRoot();
        var matrixPath = Path.Combine(
            root,
            ".trellis",
            "tasks",
            "07-23-dynamics-connection-compatibility",
            "phase0-organization-call-matrix.json");
        using var matrix = JsonDocument.Parse(File.ReadAllBytes(matrixPath));
        var document = matrix.RootElement;

        document.GetProperty("operationRegistryAgreementGate")
            .GetProperty("status")
            .GetString()
            .Should().Be("enforced");

        var rows = document.GetProperty("normalizedCallSites")
            .EnumerateArray()
            .Select(row => row.Clone())
            .ToArray();

        foreach (var definition in Package01OperationRegistry.All)
        {
            var matchingRows = rows
                .Where(row => string.Equals(
                    row.GetProperty("capabilityOperationId").GetString(),
                    definition.CapabilityOperationId,
                    StringComparison.Ordinal))
                .ToArray();

            matchingRows.Should().ContainSingle(
                $"registry operation {definition.CapabilityOperationId} 必須在 matrix 恰有一列");
            AssertDefinitionMatchesRow(definition, matchingRows[0]);
        }
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
            $"{definition.CapabilityOperationId} 的參數名稱、順序、型別、必要性與 encoding 必須一致");

        // Matrix schema 以明確的 "none" sentinel 表示零參數操作沒有任何 caller-controlled 編碼位置；
        // 有參數操作仍從編譯契約推導精確 encoding set，不能用 none 或空集合掩蓋欄位漂移。
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

    private static void AssertEvidenceDeclared(JsonElement evidence, string ceVersion, string operationId)
    {
        evidence.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace(
            $"{operationId} 必須明確宣告 {ceVersion} 證據狀態，即使目前仍是 metadata-only 或 blocked");
        evidence.GetProperty("notes").GetString().Should().NotBeNullOrWhiteSpace(
            $"{operationId} 的 {ceVersion} 證據必須說明限制，不能以空欄位冒充相容性");
    }

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

        throw new InvalidOperationException("無法從測試輸出目錄定位 SpeechMessageProducts.sln。");
    }

    private sealed record ParameterContract(
        string Name,
        string Type,
        bool Required,
        string EncodingContext);
}
