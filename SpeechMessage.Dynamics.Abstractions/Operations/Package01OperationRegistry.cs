// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs
// 用途：集中宣告 Package 0/1 的 server-owned capability、封閉回應種類、有限 page/byte 政策與版本化雜湊。
//
// 安全與生命週期邊界：
// 1. registry 是 process-static immutable allowlist；它不保存 profile、credential、token、session、HTTP
//    client、stream 或 queue item。runtime 只可讀取定義，資源仍由設定檔世代與 request scope 擁有並 dispose。
// 2. 每個作業使用相同的四頁/64 KiB/256 KiB 保守預設。Package 1 尚未開啟流量，任何擴大政策都必須有實測
//    容量證據、matrix/hash 更新與 review，不能用「無上限」或 caller 參數繞過。
// 3. metadata 明確標記 Unsupported；connector 必須在 raw metadata、OData annotation 或 continuation 離開
//    request scope 前失敗關閉，不能把 generic object 當作產品回應。
// ============================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// Package 0/1 capability 的唯一 registry owner。靜態字典在 type 初始化時一次建立，之後只讀；它不形成
/// per-user/per-profile 快取，也不接管 HTTP、認證、取消、timer 或 background task 的生命週期。
/// </summary>
public static class Package01OperationRegistry
{
    /// <summary>
    /// feature-disabled Package 1 在未取得真實容量證據前最多讀取四頁，避免 cyclic continuation 或大量
    /// aggregate record 把 request scope 的記憶體與 credential-bearing request 放大為未界定風險。
    /// </summary>
    private const int ConservativeMaximumPageCount = 4;

    /// <summary>
    /// 單頁 64 KiB 的上限使 connector 可先拒絕 Content-Length，並在 chunked 讀取時持續限制 buffer；實際
    /// buffer/stream 仍由 connector request scope 擁有並在所有退出路徑清理。
    /// </summary>
    private const int ConservativeMaximumPageBytes = 64 * 1024;

    /// <summary>
    /// 四個單頁上限的累積 256 KiB 保守總量。它不是長生命週期 cache 配額，超限必須受控失敗而不是保留 partial
    /// OData document 或啟動下一個 credential-bearing page request。
    /// </summary>
    private const int ConservativeMaximumCumulativeResponseBytes =
        ConservativeMaximumPageCount * ConservativeMaximumPageBytes;

    /// <summary>
    /// 結果列數以 4096 為保守 hard limit；它與每頁、累積位元組上限共同限制 materialized DTO 集合，
    /// 避免小型列或惡意 upstream 回應在 request scope 內造成無界配置。connector 超限時須釋放該頁資源
    /// 並失敗關閉，不能保留 partial records 或把計數器放入跨 request/session 的可變狀態。
    /// </summary>
    private const int ConservativeMaximumResultItemCount = 4096;

    /// <summary>
    /// 認證 contact lookup 的專屬結果列上限。兩個固定 QueryExpression 都以 <c>TopCount = 2</c>
    /// 區分 zero、唯一與 duplicate，因此 registry 不能沿用一般 read 的 4096 列預算；否則未來
    /// connector 或 transport 即使正確投影純值，也可能在 envelope 前 materialize 多餘的個資。
    /// </summary>
    private const int AuthenticationContactReadMaximumResultItemCount = 2;

    private static readonly IReadOnlyDictionary<string, OperationDefinition> Definitions =
        Build().ToDictionary(x => x.CapabilityOperationId, StringComparer.Ordinal);

    /// <summary>
    /// 取得 immutable capability snapshot。呼叫端不得修改其中的定義或以它保存 request/profile 狀態；需要資源
    /// 的 runtime 必須在自己 scope 內依這些有限 policy 建立、drain 與 dispose。
    /// </summary>
    public static IReadOnlyCollection<OperationDefinition> All => Definitions.Values.ToArray();

    /// <summary>
    /// 以 capability ID 查詢已登錄定義。未知 ID 只回傳 false，呼叫端必須在取得 endpoint、token 或 outbound
    /// 資源前失敗關閉，不能退回 generic URL/template。
    /// </summary>
    public static bool TryGet(string capabilityOperationId, out OperationDefinition? definition)
        => Definitions.TryGetValue(capabilityOperationId, out definition);

    /// <summary>
    /// 判斷 capability 是否已登錄。此方法無 I/O、無配置且不保存 caller input，適合在 authorization 前後的
    /// allowlist guard 使用；實際 profile/resource 所有權不在此型別。
    /// </summary>
    public static bool Contains(string capabilityOperationId)
        => Definitions.ContainsKey(capabilityOperationId);

    /// <summary>
    /// 建立目前二十一個作業的 immutable 定義。每一列同時指定產品安全回應種類，因而 connector 不必猜測 JSON
    /// shape；新增 capability 時必須同步更新 matrix、projection、有限政策與 hash agreement test。
    /// </summary>
    private static IEnumerable<OperationDefinition> Build()
    {
        // Package 0 runtime：兩個 WhoAmI 都只允許最小 GUID projection，metadata 無產品回應 branch。
        yield return Def(
            OperationIds.RuntimeHealthWhoAmI,
            package: "package-0-runtime",
            kind: "function",
            templateKind: "odata-function",
            templateId: "WhoAmI",
            responseKind: OperationResponseKind.WhoAmI,
            data: "internal",
            audit: "security-audit",
            idempotency: "read-only");

        yield return Def(
            OperationIds.RuntimePoolValidateConnection,
            package: "package-0-runtime",
            kind: "connection-runtime",
            templateKind: "odata-function",
            templateId: "WhoAmI",
            responseKind: OperationResponseKind.WhoAmI,
            data: "internal",
            audit: "security-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("logicalProfileId", "string", required: true, encoding: "none")
            ]);

        yield return Def(
            OperationIds.MetadataOptionSetByAttribute,
            package: "package-3-special-resources",
            kind: "metadata",
            templateKind: "odata-route",
            templateId: "metadata.optionset.by.attribute.v2",
            responseKind: OperationResponseKind.OptionSetOptions,
            data: "internal",
            audit: "none",
            idempotency: "read-only",
            parameters:
            [
                Param("target", "metadata-optionset-target", required: true, encoding: "server-metadata-target")
            ]);

        // P7.3 image 與 weekly reporting 都是固定 capability；產品不得把 raw Stream、IFormFile、FetchXML、
        // entity/schema/field 或 paging token 包裝成參數。image payload 的實際 bytes、format、dimension 與
        // pixel 限制由 executor/connector 在取得 lease 前與 request scope 中各自重驗。
        yield return Def(
            OperationIds.MemberInfoContactRetrieveImage,
            package: "package-3-special-resources",
            kind: "read",
            templateKind: "odata-route",
            templateId: "memberinfo.contact.entityimage.retrieve.v1",
            responseKind: OperationResponseKind.ContactImage,
            data: "personal-data",
            audit: "read-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment")
            ]);

        yield return Def(
            OperationIds.MemberInfoContactUpdateImage,
            package: "package-3-special-resources",
            kind: "write",
            templateKind: "odata-route",
            templateId: "memberinfo.contact.entityimage.update.v1",
            responseKind: OperationResponseKind.ContactImageUpdate,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("imagePayload", "image-payload", required: true, encoding: "bounded-image-payload")
            ]);

        yield return Def(
            OperationIds.NewPersonContactUpdateImage,
            package: "package-3-special-resources",
            kind: "write",
            templateKind: "odata-route",
            templateId: "newperson.contact.entityimage.update.v1",
            responseKind: OperationResponseKind.ContactImageUpdate,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("imagePayload", "image-payload", required: true, encoding: "bounded-image-payload")
            ]);

        yield return Def(
            OperationIds.StatsMeetingRetrieveBySunday,
            package: "package-3-special-resources",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "stats.meeting.by.sunday.v1",
            responseKind: OperationResponseKind.MeetingStatistics,
            data: "internal",
            audit: "read-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("sundayDate", "date-time", required: true, encoding: "fetchxml-attribute-value")
            ]);

        // Package 1 fee reads：雖然 feature gate 仍為 false，所有未來允許的資料都先被鎖定為安全 wire record。
        yield return Def(
            OperationIds.FeeDedicationRetrieveByContact,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "fee.dedication.bycontact.v1",
            responseKind: OperationResponseKind.Package01FeeRecords,
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("contactName", "string", required: false, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.PaymentsDedicationRetrieveByContact,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "payments.dedication.by.contact.v1",
            responseKind: OperationResponseKind.Package01DedicationBookingRecords,
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
            responseKind: OperationResponseKind.Package01FeeRecords,
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
            responseKind: OperationResponseKind.Package01FeeRecords,
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("dedicationBookingId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("dedicationBookingName", "string", required: false, encoding: "fetchxml-attribute-value"),
                Param("paidPeriod", "string", required: true, encoding: "fetchxml-attribute-value")
            ]);

        // Fee editor 與 stor lesson lookup 都回傳同一個 ProductClient-compatible stor branch，而非混合 entity JSON。
        yield return Def(
            OperationIds.FeesEditorLoadByDiscipleLesson,
            package: "package-1-fee-reads",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "fees.editor.load.disciplelesson.v1",
            responseKind: OperationResponseKind.Package01StorLessonRecords,
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
            responseKind: OperationResponseKind.Package01StorLessonRecords,
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
            responseKind: OperationResponseKind.Package01StorLessonRecords,
            data: "financial-data",
            audit: "financial-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("discipleLessonId", "guid", required: true, encoding: "fetchxml-attribute-value"),
                Param("lessonName", "string", required: false, encoding: "fetchxml-attribute-value")
            ]);

        // P7.4 authentication contact lookup：兩個 ID 均只允許一個 bounded lookup scalar。它們保持 local-only；
        // registry 宣告不會啟用 deployment gate、建立 host/pool/handler 或將 typed API 接入既有登入 consumer。
        // 帳號密碼驗證仍屬未來獨立 credential policy，故 password、hash、token、cookie、profile 與 query text
        // 都不能成為 parameter；secret detection 只以空資料的安全分類傳達，不能把秘密投影到 wire/DTO。
        yield return Def(
            OperationIds.AuthenticationContactRetrieveByAccount,
            package: "package-4-authentication-contact-reads-local-only",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "auth.contact.by.account.v1",
            responseKind: OperationResponseKind.AuthenticationContactReadRecords,
            data: "credential-or-secret",
            audit: "security-audit",
            idempotency: "read-only",
            maximumResultItemCount: AuthenticationContactReadMaximumResultItemCount,
            parameters:
            [
                Param("accountLookupValue", "string", required: true, encoding: "fetchxml-attribute-value")
            ]);

        yield return Def(
            OperationIds.AuthenticationContactRetrieveByLineId,
            package: "package-4-authentication-contact-reads-local-only",
            kind: "read",
            templateKind: "fetchxml",
            templateId: "auth.contact.by.lineid.v1",
            responseKind: OperationResponseKind.AuthenticationContactReadRecords,
            data: "personal-data",
            audit: "security-audit",
            idempotency: "read-only",
            maximumResultItemCount: AuthenticationContactReadMaximumResultItemCount,
            parameters:
            [
                Param("lineIdLookupValue", "string", required: true, encoding: "fetchxml-attribute-value")
            ]);

        // Package 2 member-info write：僅宣告固定五個 scalar，不開放 Entity、欄位 map 或 caller 選擇 template。
        // Data8 executor 尚未完成 operation template 時，registry 宣告本身不構成寫入權限；其 allowlist 仍會 fail closed。
        yield return Def(
            OperationIds.MemberInfoContactUpdateBasicInfo,
            package: "package-2-memberinfo-writes",
            kind: "write",
            templateKind: "odata-route",
            templateId: "memberinfo.contact.basic.info.patch.v1",
            responseKind: OperationResponseKind.ContactBasicInfoUpdate,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("phone", "string", required: false, encoding: "json-body"),
                Param("address", "string", required: false, encoding: "json-body"),
                Param("membershipStatusValue", "integer", required: false, encoding: "json-body"),
                Param("spiritualIdentityValue", "integer", required: false, encoding: "json-body")
            ]);

        // Slice B1：LINE credential/profile retrieval 留在產品，Gateway 只允許三個固定欄位的 set/clear/preserve scalar。
        yield return Def(
            OperationIds.MemberInfoContactUpdateLineProfile,
            package: "package-2-memberinfo-writes",
            kind: "write",
            templateKind: "odata-route",
            templateId: "memberinfo.contact.line.profile.patch.v1",
            responseKind: OperationResponseKind.ContactLineProfileUpdate,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("pictureMode", "enum", required: true, encoding: "server-enum"),
                Param("pictureUrl", "string", required: false, encoding: "json-body"),
                Param("statusMode", "enum", required: true, encoding: "server-enum"),
                Param("statusMessage", "string", required: false, encoding: "json-body"),
                Param("displayNameMode", "enum", required: true, encoding: "server-enum"),
                Param("displayName", "string", required: false, encoding: "json-body")
            ]);

        // Slice B2：只允許 bounded search；closed status、metadata matches、list scope 與 FetchXML 由 connector 固定產生。
        yield return Def(
            OperationIds.MemberInfoContactCountUngroupedCommitment,
            package: "package-2-memberinfo-functions",
            kind: "function",
            templateKind: "fetchxml",
            templateId: "memberinfo.contact.ungrouped.commitment.count.v1",
            responseKind: OperationResponseKind.UngroupedCommitmentCounts,
            data: "personal-data",
            audit: "read-audit",
            idempotency: "read-only",
            parameters:
            [
                Param("search", "string", required: false, encoding: "fetchxml-attribute-value")
            ]);

        // Slice C：static-list association 以固定 action 表達；memberIds 的 1,000 筆／distinct／non-empty
        // 限制由 canonical dispatch 與 connector 再驗證，不能把它退化為 caller 提供的 listmember Entity 集合。
        yield return Def(
            OperationIds.ListMembersAddMany,
            package: "package-2-list-management-writes",
            kind: "action",
            templateKind: "odata-action",
            templateId: "list.members.add.many.v1",
            responseKind: OperationResponseKind.StaticListMembershipMutation,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("listId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("memberIds", "guid-array", required: true, encoding: "guid-array-canonical")
            ]);

        yield return Def(
            OperationIds.ListMembersRemoveOne,
            package: "package-2-list-management-writes",
            kind: "action",
            templateKind: "odata-action",
            templateId: "list.members.remove.one.v1",
            responseKind: OperationResponseKind.StaticListMembershipMutation,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("listId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("memberId", "guid", required: true, encoding: "odata-uri-segment")
            ]);

        // 兩個 mode 固定為 change-race-leader / change-area-leader；connector 擁有六個欄位與關聯查詢，
        // 因此產品不能透過 JSON object 改寫共同區牧、代理區長或其他非此契約允許的欄位。
        yield return Def(
            OperationIds.ListManagementSmallGroupUpdateFields,
            package: "package-2-list-management-writes",
            kind: "write",
            templateKind: "odata-route",
            templateId: "listmanagement.smallgroup.fixed.fields.v1",
            responseKind: OperationResponseKind.SmallGroupFixedFieldsMutation,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("listId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("mode", "enum", required: true, encoding: "server-enum"),
                Param("targetLeaderContactId", "guid", required: true, encoding: "odata-uri-segment")
            ]);

        yield return Def(
            OperationIds.ContactAssignOwner,
            package: "package-2-list-management-writes",
            kind: "action",
            templateKind: "odata-action",
            templateId: "contact.assign.owner.v1",
            responseKind: OperationResponseKind.ContactOwnerAssignment,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("ownerSystemUserId", "guid", required: true, encoding: "odata-uri-segment")
            ]);

        // transfer 的週起日須由 caller 以明確 UTC offset 提供；connector 固定解析 target list weekly report，
        // 不讀取主機時區，也不接受 callers 直接指定 present-record、weekly-report 或任意欄位 map。
        yield return Def(
            OperationIds.NewPersonContactTransferBetweenLists,
            package: "package-2-list-management-writes",
            kind: "write",
            templateKind: "composite",
            templateId: "newperson.contact.transfer.between.lists.v1",
            responseKind: OperationResponseKind.ContactListTransfer,
            data: "personal-data",
            audit: "write-audit",
            idempotency: "caller-idempotency-key-required",
            parameters:
            [
                Param("contactId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("sourceListId", "guid", required: false, encoding: "odata-uri-segment"),
                Param("targetListId", "guid", required: true, encoding: "odata-uri-segment"),
                Param("weekStartDate", "date-time", required: true, encoding: "fetchxml-attribute-value"),
                Param("ownerSystemUserId", "guid", required: false, encoding: "odata-uri-segment")
            ]);
    }

    /// <summary>
    /// 建立一筆 immutable definition，並把 template、回應 discriminator 與全部有限政策一同納入 revision hash。
    /// 這個唯一工廠避免九個作業各自遺漏 policy；它不配置 client/stream，也不保存實際 parameter value。
    /// </summary>
    private static OperationDefinition Def(
        string id,
        string package,
        string kind,
        string templateKind,
        string templateId,
        OperationResponseKind responseKind,
        string data,
        string audit,
        string idempotency,
        int maximumResultItemCount = ConservativeMaximumResultItemCount,
        IReadOnlyList<OperationParameterDefinition>? parameters = null)
    {
        var material = string.Join("|",
            templateKind,
            templateId,
            id,
            responseKind.ToString(),
            ConservativeMaximumPageCount.ToString(CultureInfo.InvariantCulture),
            ConservativeMaximumPageBytes.ToString(CultureInfo.InvariantCulture),
            ConservativeMaximumCumulativeResponseBytes.ToString(CultureInfo.InvariantCulture),
            maximumResultItemCount.ToString(CultureInfo.InvariantCulture));
        return new OperationDefinition
        {
            CapabilityOperationId = id,
            Package = package,
            OperationKind = kind,
            TemplateKind = templateKind,
            TemplateId = templateId,
            TemplateHash = Sha256Hex(material),
            ResponseKind = responseKind,
            MaximumPageCount = ConservativeMaximumPageCount,
            MaximumPageBytes = ConservativeMaximumPageBytes,
            MaximumCumulativeResponseBytes = ConservativeMaximumCumulativeResponseBytes,
            MaximumResultItemCount = maximumResultItemCount,
            DataClassification = data,
            AuditRequirement = audit,
            IdempotencyClass = idempotency,
            Parameters = parameters ?? Array.Empty<OperationParameterDefinition>()
        };
    }

    /// <summary>
    /// 建立一筆最小 parameter schema。輸入是 compile-time registry metadata 而非 caller value，因此不需要
    /// cache/dispose；實際值的編碼與暫存所有權一律留在單次 connector request scope。
    /// </summary>
    private static OperationParameterDefinition Param(string name, string type, bool required, string encoding)
        => new()
        {
            Name = name,
            Type = type,
            Required = required,
            EncodingContext = encoding
        };

    /// <summary>
    /// 產生小寫 SHA-256 revision。輸入只含固定 policy metadata，不含 CRM payload、endpoint、credential 或 caller
    /// identity；雜湊陣列在方法結束後不再保留，沒有可跨 request 的 mutable state。
    /// </summary>
    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
