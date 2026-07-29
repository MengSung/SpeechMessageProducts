// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs
// 目的：提供 Package 0 + Package 1 的第一版操作註冊表。
//
// 保母教學：
// - 這不是完整 CRM API 目錄，只是第一刀可遷移操作。
// - 之後 CI 會拿 matrix 的 capabilityOperationId / parameters 與這裡比對。
// - 若要新增操作，先補 phase0 matrix，再補這個 registry，不要反過來。
// ============================================================================

using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// Package 0 / Package 1 操作註冊表。
/// </summary>
public static class Package01OperationRegistry
{
    private static readonly IReadOnlyDictionary<string, OperationDefinition> Definitions =
        Build().ToDictionary(x => x.CapabilityOperationId, StringComparer.Ordinal);

    /// <summary>
    /// 取得所有已註冊操作。
    /// </summary>
    public static IReadOnlyCollection<OperationDefinition> All => Definitions.Values.ToArray();

    /// <summary>
    /// 依 capabilityOperationId 查找操作定義。
    /// </summary>
    public static bool TryGet(string capabilityOperationId, out OperationDefinition? definition)
        => Definitions.TryGetValue(capabilityOperationId, out definition);

    /// <summary>
    /// 確認操作是否屬於 Package 0/1 首發範圍。
    /// </summary>
    public static bool Contains(string capabilityOperationId)
        => Definitions.ContainsKey(capabilityOperationId);

    private static IEnumerable<OperationDefinition> Build()
    {
        // Package 0：註冊 runtime 基礎作業；operation ID、參數與稽核分類均為伺服器端不可變契約。
        yield return Def(
            OperationIds.RuntimeHealthWhoAmI,
            package: "package-0-runtime",
            kind: "function",
            templateKind: "odata-function",
            templateId: "WhoAmI",
            data: "internal",
            audit: "security-audit",
            idempotency: "read-only");

        yield return Def(
            OperationIds.RuntimePoolValidateConnection,
            package: "package-0-runtime",
            kind: "connection-runtime",
            templateKind: "odata-function",
            templateId: "WhoAmI",
            data: "internal",
            audit: "security-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("logicalProfileId", "string", required: true, encoding: "none")
            ]);

        yield return Def(
            OperationIds.MetadataOptionSetByAttribute,
            package: "package-0-runtime",
            kind: "metadata",
            templateKind: "odata-route",
            templateId: "metadata.optionset.by.attribute.v1",
            data: "internal",
            audit: "none",
            idempotency: "read-only",
            parameters:
            [
                Param("entityLogicalName", "string", required: true, encoding: "odata-uri-segment"),
                Param("attributeLogicalName", "string", required: true, encoding: "odata-uri-segment")
            ]);

        // Package 1：註冊費用唯讀作業；呼叫端只能提交具型別參數，不能提供任意 OData 或 FetchXML。
        yield return Def(
            OperationIds.FeeDedicationRetrieveByContact,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "fee.dedication.bycontact.v1",
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("contactName", "string", required: false, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "fee.dedication.bycontactdaterange.v1",
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("contactName", "string", required: false, encoding: "fetchxml-attribute-value"),
                Param("startDate", "date-time", required: true, encoding: "fetchxml-attribute-value"),
                Param("endDate", "date-time", required: true, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.FeesRetrieveByDedicationPeriod,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "fees.by.dedication.period.v1",
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("dedicationBookingId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("dedicationBookingName", "string", required: false, encoding: "fetchxml-attribute-value"),
                Param("paidPeriod", "string", required: true, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.FeesEditorLoadByDiscipleLesson,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "fees.editor.load.disciplelesson.v1",
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("discipleLessonId", "guid", required: true, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.LessonsStorRetrieveByContact,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "lessons.stor.by.contact.v1",
            data: "personal-data",
            audit: "read-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("contactName", "string", required: false, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.LessonsStorRetrieveByDiscipleLesson,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "lessons.stor.by.disciplelesson.v1",
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("discipleLessonId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("lessonName", "string", required: false, encoding: "fetchxml-attribute-value")
            ]);
    }

    private static OperationDefinition Def(
        string id,
        string package,
        string kind,
        string templateKind,
        string templateId,
        string data,
        string audit,
        string idempotency,
        IReadOnlyList<OperationParameterDefinition>? parameters = null)
    {
        var material = $"{templateKind}|{templateId}|{id}";
        return new OperationDefinition
        {
            CapabilityOperationId = id,
            Package = package,
            OperationKind = kind,
            TemplateKind = templateKind,
            TemplateId = templateId,
            TemplateHash = Sha256Hex(material),
            DataClassification = data,
            AuditRequirement = audit,
            IdempotencyClass = idempotency,
            Parameters = parameters ?? Array.Empty<OperationParameterDefinition>()
        };
    }

    private static OperationParameterDefinition Param(string name, string type, bool required, string encoding)
        => new()
        {
            Name = name,
            Type = type,
            Required = required,
            EncodingContext = encoding
        };

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
