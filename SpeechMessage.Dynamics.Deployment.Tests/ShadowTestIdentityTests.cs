/*
 * 檔案整體說明
 *
 * 目的與定位：保護影子執行前的組織與測試身分權威核對；合成 SDK 明確回傳組織、
 * D34 範圍及精確 LINE 綁定，不依賴名稱或真實秘密，也不連線或寫入外部服務。
 * 主要流程：注入其他組織、分頁、重複綁定、父教會錯配或設定 GUID 錯誤，要求
 * 完整拒絕；成功只發布三個正式 GUID，不能保留 LINE ID 或服務物件。
 * 使用邊界與維護：測試 owner 每案獨占替身與輸入，沒有檔案、計時器或待釋放背景工作。
 * 決定性斷言包含查詢條件、有限欄位、零寫入及固定錯誤，離線成功不是實機證據。
 */
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以命令區域替身驗證測試身分的完整權威來源，禁止用顯示名稱建立授權。</summary>
public sealed class ShadowTestIdentityTests
{
    /// <summary>發現綁定及已設定 GUID 核對都應回傳相同正式身分；所有查詢唯讀且有界。</summary>
    [Fact]
    public void Identity_UsesExactBindingAndAuthoritativeParent()
    {
        var service = new IdentityService();
        var found = ShadowTestIdentity.Discover(service, "synthetic-recipient", CancellationToken.None);
        Assert.Equal(service.Organization, found.OrganizationId);
        Assert.Equal(service.Church, found.ChurchId);
        Assert.Equal(service.Contact, found.ContactId);
        Assert.Equal(found, ShadowTestIdentity.Verify(service, "synthetic-recipient",
            service.Church.ToString("D"), service.Contact.ToString("D"), CancellationToken.None));
        Assert.DoesNotContain("synthetic-recipient", found.ToString(), StringComparison.Ordinal);
    }

    /// <summary>不完整頁面、重複身分、其他組織及權威父關聯錯誤都必須拒絕，不挑第一筆。</summary>
    [Theory]
    [InlineData("organization")]
    [InlineData("more-d34")]
    [InlineData("more-binding")]
    [InlineData("duplicate")]
    [InlineData("parent")]
    [InlineData("account")]
    [InlineData("missing")]
    public void Identity_RejectsAmbiguousOrUnauthorizedEvidence(string fault)
    {
        var service = new IdentityService { Fault = fault };
        var failure = Assert.Throws<InvalidDataException>(() =>
            ShadowTestIdentity.Discover(service, "synthetic-recipient", CancellationToken.None));
        Assert.DoesNotContain("synthetic-recipient", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>設定 GUID 只能與權威結果一致；取消與畸形設定不得查詢，更不可使用後備名稱。</summary>
    [Fact]
    public void Identity_RejectsConfiguredMismatchAndCancellation()
    {
        var service = new IdentityService();
        Assert.Throws<InvalidDataException>(() => ShadowTestIdentity.Verify(service, "synthetic-recipient",
            Guid.NewGuid().ToString("D"), service.Contact.ToString("D"), CancellationToken.None));
        var untouched = new IdentityService();
        Assert.Throws<InvalidDataException>(() => ShadowTestIdentity.Verify(untouched, "synthetic-recipient",
            "bad", service.Contact.ToString("D"), CancellationToken.None));
        Assert.Equal(0, untouched.Calls);
        Assert.Throws<OperationCanceledException>(() =>
            ShadowTestIdentity.Discover(untouched, "synthetic-recipient", new CancellationToken(true)));
        Assert.Equal(0, untouched.Calls);
    }

    /// <summary>單案無資源 SDK 替身；所有寫入口拒絕，查詢以明確條件斷言產品實際行為。</summary>
    private sealed class IdentityService : IOrganizationService
    {
        internal readonly Guid Organization = Guid.NewGuid();
        internal readonly Guid Church = Guid.NewGuid();
        internal readonly Guid Contact = Guid.NewGuid();
        internal string Fault = "";
        internal int Calls;

        /// <summary>只提供真正的組織識別訊息，無額外業務或秘密讀取。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Calls++;
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            return new RetrieveCurrentOrganizationResponse
            {
                Results = new ParameterCollection
                {
                    ["Detail"] = new OrganizationDetail
                    { OrganizationId = Organization, UniqueName = Fault == "organization" ? "production" : "goodshepherdback" }
                }
            };
        }
        /// <summary>兩次 contact 查詢分別驗完整 D34 及精確測試綁定，TopCount 固定二。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Calls++;
            var expression = Assert.IsType<QueryExpression>(query);
            Assert.Equal("contact", expression.EntityName);
            Assert.Equal(2, expression.TopCount);
            Assert.False(expression.ColumnSet.AllColumns);
            var binding = expression.Criteria.Conditions.Any(condition =>
                condition.AttributeName == "new_lineid" && condition.Operator == ConditionOperator.Equal &&
                Equals(condition.Values.Single(), "synthetic-recipient"));
            if (!binding)
            {
                Assert.Contains(expression.Criteria.Conditions, condition => condition.Operator == ConditionOperator.NotNull);
                Assert.Contains(expression.Criteria.Conditions, condition => condition.Operator == ConditionOperator.NotEqual &&
                    Equals(condition.Values.Single(), ""));
            }
            var result = new EntityCollection { MoreRecords = Fault == (binding ? "more-binding" : "more-d34") };
            if (Fault == "missing") return result;
            result.Entities.Add(new Entity("contact", Contact)
            { ["parentcustomerid"] = new EntityReference(Fault == "parent" ? "contact" : "account", Church) });
            if (Fault == "duplicate") result.Entities.Add(new Entity("contact", Guid.NewGuid()));
            return result;
        }
        /// <summary>只確認父教會正式 ID 存在，不取名稱或其他業務欄位。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Calls++;
            Assert.Equal("account", entityName);
            Assert.Equal(Church, id);
            Assert.Empty(columnSet.Columns);
            Assert.False(columnSet.AllColumns);
            return new Entity("account", Fault == "account" ? Guid.NewGuid() : Church);
        }
        /// <summary>此權威核對不可建立資料。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected write.");
        /// <summary>此權威核對不可修改資料。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected write.");
        /// <summary>此權威核對不可刪除資料。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected write.");
        /// <summary>此權威核對不可新增關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected write.");
        /// <summary>此權威核對不可移除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected write.");
    }
}
