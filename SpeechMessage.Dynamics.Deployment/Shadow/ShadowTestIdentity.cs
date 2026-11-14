/*
 * 檔案整體說明
 *
 * 目的與定位：影子 CLI 的唯讀權威身分關卡，只允許 goodshepherdback 中唯一測試
 * LINE 綁定及其 account 父關聯，供設定 User Secrets 或執行前核對正式 GUID。
 * 主要流程：讀取服務端組織識別、完整 D34 非空綁定範圍，再以 TestLineUserId
 * 精確篩選同一 contact，驗證 parentcustomerid 並確認 account 存在；不查名稱。
 * 信任邊界：設定中的 GUID 只是候選，必須與當下權威結果完全一致才可通過。
 * 每個查詢最多兩列；分頁、空值、重複或其他組織一律拒絕，不將第一筆當唯一。
 * 擁有權：SDK 與秘密字串由單一命令借入，沒有寫入、快取、背景工作或跨命令狀態；
 * 結果只含正式 GUID，不保留 LINE ID、原始列或服務。每次 I/O 前後檢查取消，
 * 同步呼叫返回後才允許外層釋放租約；錯誤採固定訊息，不回顯身分或秘密。
 * 驗證以合成 SDK 的權威關聯、分頁及錯配測試為準，不宣稱已完成實機前置。
 */
using System;
using System.IO;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>命令區域的核定測試身分；純正式 GUID，不持有秘密、SDK 或可變資料列。</summary>
internal sealed record ShadowVerifiedIdentity(Guid OrganizationId, Guid ChurchId, Guid ContactId);

/// <summary>唯讀核對組織及測試綁定，不以顯示名稱、檔案別名或呼叫端 GUID 建立授權。</summary>
internal static class ShadowTestIdentity
{
    /// <summary>
    /// 發現唯一權威測試身分；供明確設定流程使用，不會自行變更 Secrets 或業務資料。
    /// 必須同時證明 D34 範圍只有一列，且精確 TestLineUserId 查詢找到同一 contact。
    /// </summary>
    internal static ShadowVerifiedIdentity Discover(IOrganizationService service, string testLineUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (string.IsNullOrWhiteSpace(testLineUserId) || testLineUserId.Length > 256) throw Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveCurrentOrganizationRequest());
        cancellationToken.ThrowIfCancellationRequested();
        var organization = (response as RetrieveCurrentOrganizationResponse)?.Detail;
        if (organization is null || organization.OrganizationId == Guid.Empty ||
            !string.Equals(organization.UniqueName, "goodshepherdback", StringComparison.OrdinalIgnoreCase))
            throw Invalid();
        var scope = new QueryExpression("contact") { ColumnSet = new ColumnSet(false), TopCount = 2 };
        scope.Criteria.AddCondition("new_lineid", ConditionOperator.NotNull);
        scope.Criteria.AddCondition("new_lineid", ConditionOperator.NotEqual, "");
        var onlyMember = ReadSingle(service, scope, cancellationToken);
        var exact = new QueryExpression("contact") { ColumnSet = new ColumnSet("parentcustomerid"), TopCount = 2 };
        exact.Criteria.AddCondition("new_lineid", ConditionOperator.Equal, testLineUserId);
        var contact = ReadSingle(service, exact, cancellationToken);
        if (contact.Id != onlyMember.Id ||
            contact.GetAttributeValue<EntityReference>("parentcustomerid") is not { LogicalName: "account" } parent ||
            parent.Id == Guid.Empty) throw Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        var account = service.Retrieve("account", parent.Id, new ColumnSet(false));
        cancellationToken.ThrowIfCancellationRequested();
        if (account is null || account.LogicalName != "account" || account.Id != parent.Id) throw Invalid();
        return new ShadowVerifiedIdentity(organization.OrganizationId, account.Id, contact.Id);
    }

    /// <summary>先驗設定 GUID，再以當下權威綁定核對；錯配不修正資料、不使用名稱後備。</summary>
    internal static ShadowVerifiedIdentity Verify(IOrganizationService service, string testLineUserId,
        string configuredChurchId, string configuredContactId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(configuredChurchId, "D", out var church) || church == Guid.Empty ||
            !Guid.TryParseExact(configuredContactId, "D", out var contact) || contact == Guid.Empty) throw Invalid();
        var identity = Discover(service, testLineUserId, cancellationToken);
        return identity.ChurchId == church && identity.ContactId == contact ? identity : throw Invalid();
    }

    /// <summary>單頁且恰一筆正式 contact 才發布，保留有界查詢與取消的失敗語意。</summary>
    private static Entity ReadSingle(IOrganizationService service, QueryExpression query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = service.RetrieveMultiple(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null || result.MoreRecords || result.Entities.Count != 1 ||
            result.Entities[0].LogicalName != "contact" || result.Entities[0].Id == Guid.Empty) throw Invalid();
        return result.Entities[0];
    }

    /// <summary>固定拒絕訊息不包含組織 ID、LINE ID、父關聯、設定值或原始列。</summary>
    private static InvalidDataException Invalid() => new("Shadow test identity could not be verified.");
}
