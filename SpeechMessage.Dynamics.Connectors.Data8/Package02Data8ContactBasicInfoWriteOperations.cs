// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package02Data8ContactBasicInfoWriteOperations.cs
// 用途：實作 P7.2 唯一已審查的 Data8 contact basic-info 寫入模板與 bounded read-back。
//
// 安全與生命週期邊界：
// 1. 此檔只接受 memberinfo.contact.update.basic.info；Entity logical name、可寫欄位、read-back 欄位、
//    CE version 與 response discriminator 都由程式常數與 registry 決定，不能由產品或 HTTP payload 指定。
// 2. connector 只在同一個 Data8 lease scope 內短暫建立 update Entity、呼叫一次 Update、讀回兩個欄位並投影
//    成安全 enum；不保存 Entity、IOrganizationService、endpoint、credential、token、cookie、baseline 或 session。
// 3. 更新後若 read-back 不完全相符，就不把結果當成功；例外傳回 lease owner，使其淘汰可能不可信的 client 並
//    由上層 idempotency/reconciliation contract 停止，而不是盲目重送寫入。
// ============================================================================

using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// P7.2 會友基本資料寫入的 Data8 connector-internal template owner。這個類別故意不是 generic CRUD API：
/// 它只理解一個固定 capability、固定 CE 9.1、固定 <c>contact</c> entity 與兩個字串欄位。任何未知 operation、
/// CE 8.2、OptionSet、額外參數、空 GUID、過長／不合法字串、Entity identity 不符或 read-back 不符都在安全邊界
/// 失敗關閉；產品端永遠不會接觸 CRM SDK 型別或 raw response。
/// </summary>
internal static class Package02Data8ContactBasicInfoWriteOperations
{
    private const string ContactEntityName = "contact";
    private const string ContactIdAttribute = "contactid";
    private const string MobilePhoneAttribute = "mobilephone";
    private const string AddressLine1Attribute = "address2_line1";
    private const int MaximumStringParameterBytes = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 執行唯一已核准的 P7.2 contact basic-info template。正常產品流程已由 executor 驗證 immutable profile、
    /// Data8 connector 與 operation schema；本方法仍重驗所有直接 connector caller 可偽造的值。當兩個可寫字串
    /// 都不存在時，它只回傳 <see cref="ContactBasicInfoUpdateDisposition.NoChange"/>，不呼叫 service；有值時
    /// 則只執行一次 Update 和一次固定 ColumnSet 的 Retrieve，成功前必須確認回讀值完全一致。
    /// </summary>
    /// <param name="service">目前 connector lease 唯一擁有的 Data8 organization service；呼叫端必須在 lease dispose 時釋放它。</param>
    /// <param name="operation">已正規化的 capability operation；不得包含 endpoint、credential、profile 或任意 CRM SDK request。</param>
    /// <param name="ceVersion">immutable resolved profile 的 CE version；此 slice 只允許 9.1。</param>
    /// <returns>僅包含 changed/no-change 與固定 correlation category 的安全 response envelope。</returns>
    internal static OperationResponseData Execute(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(operation.OperationId, OperationIds.MemberInfoContactUpdateBasicInfo, StringComparison.Ordinal) ||
            !string.Equals(ceVersion, "9.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 contact basic-info operation is not permitted.");
        }

        GetDefinition(operation.OperationId);
        RejectUnexpectedParameters(operation.Parameters);
        var contactId = ReadRequiredContactId(operation.Parameters);
        var phone = ReadOptionalBoundedString(operation.Parameters, "phone");
        var address = ReadOptionalBoundedString(operation.Parameters, "address");
        if (phone is null && address is null)
        {
            return OperationResponseData.ForContactBasicInfoUpdate(
                operation.OperationId,
                ceVersion,
                ContactBasicInfoUpdateDisposition.NoChange,
                ContactBasicInfoUpdateCorrelationCategory.NoDispatch);
        }

        var update = new Entity(ContactEntityName, contactId);
        if (phone is not null)
        {
            update[MobilePhoneAttribute] = phone;
        }

        if (address is not null)
        {
            update[AddressLine1Attribute] = address;
        }

        service.Update(update);
        var readBack = service.Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet(MobilePhoneAttribute, AddressLine1Attribute));
        ValidateReadBack(readBack, contactId, phone, address);

        return OperationResponseData.ForContactBasicInfoUpdate(
            operation.OperationId,
            ceVersion,
            ContactBasicInfoUpdateDisposition.Changed,
            ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 取得並重驗 operation registry 定義。registry 的存在不構成 generic 寫入授權；這個方法只允許目前
    /// capability 的 template、write kind、ContactBasicInfoUpdate discriminator、write audit 與 caller-owned
    /// idempotency key policy 全部一致，避免 registry 漂移時仍以舊 template 操作資料。
    /// </summary>
    private static void GetDefinition(string operationId)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "write", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.ContactBasicInfoUpdate ||
            !string.Equals(definition.TemplateId, "memberinfo.contact.basic.info.patch.v1", StringComparison.Ordinal) ||
            !string.Equals(definition.AuditRequirement, "write-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "caller-idempotency-key-required", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 contact basic-info registry definition is invalid.");
        }
    }

    /// <summary>
    /// 拒絕固定三個 scalar 以外的任何 connector parameter。registry 保留的兩個 OptionSet scalar 必須等到
    /// connector-internal metadata allowlist 與 fixture baseline 俱備才可啟用；目前即使直接呼叫 connector，
    /// 也不能以 integer、欄位 map、Entity、FetchXML、endpoint 或其他未知輸入越過這個 P7.2 slice 邊界。
    /// </summary>
    private static void RejectUnexpectedParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters is null || parameters.Count is < 1 or > 3)
        {
            throw new InvalidOperationException("The Data8 contact basic-info parameters are invalid.");
        }

        foreach (var parameter in parameters.Keys)
        {
            if (parameter is not "contactId" and not "phone" and not "address")
            {
                throw new InvalidOperationException("The Data8 contact basic-info parameters are invalid.");
            }
        }
    }

    /// <summary>
    /// 讀取唯一必填的 contact GUID。空值、文字、JsonElement、EntityReference 或任何其他 SDK／物件型別都被拒絕，
    /// 所以只會將 executor 已正規化的 immutable Guid 帶入固定 URI／Entity identity，而不會形成 caller-selected
    /// query 或跨 contact 的 mutable session state。
    /// </summary>
    private static Guid ReadRequiredContactId(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("contactId", out var value) || value is not Guid contactId || contactId == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 contact basic-info contact ID is invalid.");
        }

        return contactId;
    }

    /// <summary>
    /// 讀取可選的 allowlisted 字串。缺少欄位表示本次不覆寫；存在時必須是非空、trim 後 UTF-8 最多 256 bytes 的
    /// <see cref="string"/>。不採用 <c>ToString()</c>、不接受 JSON object／array，也不把空字串視為清除請求，
    /// 使直連 connector 的安全語意與 executor 的 no-change contract 保持一致。
    /// </summary>
    private static string? ReadOptionalBoundedString(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value is not string text)
        {
            throw new InvalidOperationException("The Data8 contact basic-info string parameter is invalid.");
        }

        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("The Data8 contact basic-info string parameter is invalid.");
        }

        try
        {
            if (StrictUtf8.GetByteCount(normalized) > MaximumStringParameterBytes)
            {
                throw new InvalidOperationException("The Data8 contact basic-info string parameter is invalid.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidOperationException("The Data8 contact basic-info string parameter is invalid.");
        }

        return normalized;
    }

    /// <summary>
    /// 驗證 Update 後的 bounded read-back。CRM 必須回傳相同的 contact identity；只比較這次實際更新的兩個
    /// allowlisted 欄位，不觀察或序列化其他屬性。任何型別不符、identity 不符、值不符或 service 回傳 null 都視為
    /// ambiguous，呼叫端不得重試寫入，而應釋放／淘汰 lease 後交給上層 fixture reconciliation policy。
    /// </summary>
    private static void ValidateReadBack(Entity? entity, Guid expectedContactId, string? expectedPhone, string? expectedAddress)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != expectedContactId ||
            !HasMatchingPrimaryId(entity, expectedContactId) ||
            (expectedPhone is not null && !string.Equals(ReadOptionalString(entity, MobilePhoneAttribute), expectedPhone, StringComparison.Ordinal)) ||
            (expectedAddress is not null && !string.Equals(ReadOptionalString(entity, AddressLine1Attribute), expectedAddress, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The Data8 contact basic-info read-back is invalid.");
        }
    }

    /// <summary>
    /// 驗證 CRM 若回傳 primary ID attribute，其值必須與 Entity.Id 完全相等。省略 attribute 可與既有 Data8
    /// Retrieve projection 相容；若存在卻是空值、錯型別或另一個 GUID，則拒絕，避免錯誤 entity graph 被誤當作
    /// 成功 read-back。
    /// </summary>
    private static bool HasMatchingPrimaryId(Entity entity, Guid expectedContactId)
    {
        if (!entity.Attributes.TryGetValue(ContactIdAttribute, out var value))
        {
            return true;
        }

        return value is Guid id && id == expectedContactId;
    }

    /// <summary>
    /// 從 read-back Entity 取出可選純文字欄位。欄位不存在代表 null；存在時只允許 <see cref="string"/>，防止
    /// OptionSet、AliasedValue、EntityReference 或其他 SDK graph 透過錯誤型別進入比對或產品回應。
    /// </summary>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string
            ?? throw new InvalidOperationException("The Data8 contact basic-info read-back attribute is invalid.");
    }
}
