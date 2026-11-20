/*
 * 檔案整體說明
 *
 * 目的與定位：P8-01c 唯讀驗收 ChurchReport 的奉獻類別清單與既有收費單 lookup 解析；
 * 唯一實機案例以 Category=P8Live 隔離，一般測試設定預設排除。
 * 主要流程：只從 .NET User Secrets 儲存目錄與環境變數讀取 goodshepherdback URL／帳密及核定測試教會、
 * 測試聯絡人 ID；先核固定 HTTPS host/path 與 server uniqueName，再確認 contact 的
 * parentcustomerid、以 ChurchReport resolver 讀該教會類別、讀取該 contact 已回填收費單，
 * 並以資料庫 lookup ID 找回同教會類別名稱後呼叫產品的奉獻文字解析器。
 * 信任邊界：任何 ID 只用於本次唯讀查詢，不輸出、不持久化、不接受測試參數覆寫；
 * 沒有 Create／Update／Delete、LINE、付款、HTTP Host、Session 或共用可變快取。
 * SDK client 與 IConfigurationRoot 由單一測試方法擁有，finally 先釋放 client 再 Dispose 組態；
 * 連線 timeout 固定 15 秒，所有 failure 僅保留固定型別訊息，不展開例外本文。
 * 使用邊界與維護：只可在核定測試組織明確篩選執行；錯誤只回固定訊息，client 與組態在finally確定釋放。
 * 本案需明確指定 Category=P8Live；離線測試不能取代本案，也不證明 migration verify 的全組織計數。
 */
using System;
using System.Linq;
using ChurchReport.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.ConnectionOperations;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

/// <summary>只讀確認核定測試教會的奉獻清單與收費單正式lookup解析一致。</summary>
public sealed class OfferingCategoriesLiveAcceptanceTests
{
    private const string Organization = "goodshepherdback";
    private const string SecretPrefix = "Deployment:Organizations:goodshepherdback:";
    private const string DeploymentSecretsId = "SpeechMessage.Dynamics.Deployment.P0";
    private const string OrganizationServicePath = "/XRMServices/2011/Organization.svc";

    /// <summary>核對測試聯絡人的教會清單與一筆已回填收費單；全程只讀並抑制所有資料識別值。</summary>
    [Fact]
    [Trait("Category", "P8Live")]
    public void OfferingCategories_Live_ListAndLookupResolve()
    {
        IConfigurationRoot? configuration = null;
        IOrganizationService? service = null;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData)) throw PreconditionFailure();
            var secretsPath = System.IO.Path.Combine(appData, "Microsoft", "UserSecrets",
                DeploymentSecretsId, "secrets.json");
            configuration = new ConfigurationBuilder()
                .AddJsonFile(secretsPath, optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
            var uriText = Required(configuration, "OrganizationUrl");
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath != OrganizationServicePath || !uri.IsDefaultPort
                || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw PreconditionFailure();

            if (!Guid.TryParse(Required(configuration, "ShadowTestChurchId"), out var churchId)
                || churchId == Guid.Empty
                || !Guid.TryParse(Required(configuration, "ShadowTestContactId"), out var contactId)
                || contactId == Guid.Empty)
                throw PreconditionFailure();

            service = new CrmConnectionService().CreateOnPremiseClient(uriText,
                Required(configuration, "Username"), Required(configuration, "Password"));
            if (service is not PowerPlatform.Dataverse.Client.OnPremiseClient client)
                throw PreconditionFailure();
            client.Timeout = TimeSpan.FromSeconds(15);

            var current = service.Execute(new Microsoft.Crm.Sdk.Messages.RetrieveCurrentOrganizationRequest())
                as Microsoft.Crm.Sdk.Messages.RetrieveCurrentOrganizationResponse;
            if (!string.Equals(current?.Detail?.UniqueName, Organization, StringComparison.Ordinal))
                throw PreconditionFailure();

            var contact = service.Retrieve("contact", contactId, new ColumnSet("parentcustomerid"));
            if (contact is null || contact.LogicalName != "contact"
                || contact.GetAttributeValue<EntityReference>("parentcustomerid") is not { LogicalName: "account" } parent
                || parent.Id != churchId)
                throw PreconditionFailure();

            var categories = DonationOfferingCategoryResolver.ReadForContact(service, contact);
            var feeQuery = new QueryExpression("new_fee")
            {
                ColumnSet = new ColumnSet("new_offeringcategoryid"),
                TopCount = 32
            };
            feeQuery.Criteria.AddCondition("new_contact_new_fee", ConditionOperator.Equal, contactId);
            feeQuery.Criteria.AddCondition("new_offeringcategoryid", ConditionOperator.NotNull);
            var fees = service.RetrieveMultiple(feeQuery);
            if (fees is null || fees.MoreRecords || fees.Entities.Count == 0)
                throw PreconditionFailure();

            var resolved = false;
            foreach (var fee in fees.Entities)
            {
                var reference = fee.GetAttributeValue<EntityReference>("new_offeringcategoryid");
                if (fee.LogicalName != "new_fee" || fee.Id == Guid.Empty
                    || reference is not { LogicalName: "new_offeringcategory" } || reference.Id == Guid.Empty)
                    throw PreconditionFailure();
                var category = categories.Categories.SingleOrDefault(item => item.Id == reference.Id);
                if (category is null || category.ChurchId != churchId)
                    throw PreconditionFailure();

                var categoryRow = service.Retrieve("new_offeringcategory", reference.Id,
                    new ColumnSet("new_accountid", "new_name"));
                if (categoryRow is null || categoryRow.LogicalName != "new_offeringcategory"
                    || categoryRow.GetAttributeValue<EntityReference>("new_accountid") is not { LogicalName: "account" } owner
                    || owner.Id != churchId
                    || !string.Equals(categoryRow.GetAttributeValue<string>("new_name"), category.Name, StringComparison.Ordinal))
                    throw PreconditionFailure();

                reference.Name = categoryRow.GetAttributeValue<string>("new_name");
                if (!string.Equals(DonationFeeQueryService.ConvertCategory(fee), category.Name, StringComparison.Ordinal))
                    throw PreconditionFailure();
                resolved = true;
                break;
            }

            if (!resolved) throw PreconditionFailure();
        }
        catch
        {
            throw PreconditionFailure();
        }
        finally
        {
            try { (service as IDisposable)?.Dispose(); }
            finally { (configuration as IDisposable)?.Dispose(); }
        }
    }

    /// <summary>只回傳固定訊息，避免測試錯誤本文攜帶端點、組織列值或個人資料。</summary>
    private static InvalidOperationException PreconditionFailure() =>
        new("P8 live offering acceptance failed; sensitive details suppressed.");

    /// <summary>讀取單一必要 User Secrets 欄位，不回顯鍵值或設定本文。</summary>
    private static string Required(IConfiguration configuration, string name) =>
        configuration[SecretPrefix + name] is { Length: > 0 } value ? value : throw PreconditionFailure();
}
