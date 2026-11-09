/*
檔案整體說明
目的與定位：保護 P3-06 Webhook scope resolver 的組織隔離與 Channel token 解密邊界。
主要功能與流程：以合成組織、教會、頻道、Contact 查詢及不具實值意義的測試金鑰建立設定，
再交給正式 resolver；測試刻意提供錯誤根層金鑰，只有 Deployment:Organizations:<organization>:TokenKey
能讓受保護 Channel token 解密成功，避免把所有組織共用一把 key。
使用邊界與維護：所有資料都是測試內建立的純值，不讀取 User Secrets、不輸出金鑰、不連 D365／LINE／SQL。
CRM 替身只擁有本案例資料；每次查詢均回傳獨立 Entity，沒有跨測試快取、背景工作或未釋放資源。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using LineMessagingProcessor.Core.Security;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>驗證 resolver 只採用組織專屬部署金鑰，並完成真實 Channel／Contact 身分核對。</summary>
public sealed class WebhookScopeResolverTests
{
    /// <summary>根層錯誤金鑰不得被採用；組織專屬金鑰成功解密才可發布 scope。</summary>
    [Fact]
    public void Resolve_UsesOrganizationScopedTokenKey()
    {
        var organizationId = Guid.NewGuid();
        var churchId = Guid.NewGuid();
        var channelRecordId = Guid.NewGuid();
        var key = new byte[64];
        for (var index = 0; index < key.Length; index++) key[index] = (byte)(index + 1);
        var encryptedToken = ChannelTokenProtector.Protect("synthetic-token", key);
        var service = new ResolverCrm(organizationId, churchId, channelRecordId, encryptedToken);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LineWebhook:Channels:123:Organization"] = "goodshepherdback",
                ["LineWebhook:Channels:123:OrganizationId"] = organizationId.ToString("D"),
                ["LineWebhook:Channels:123:ChurchId"] = churchId.ToString("D"),
                ["LineWebhook:Channels:123:TimeZone"] = "Asia/Taipei",
                ["TokenKey"] = Convert.ToBase64String(new byte[64]),
                ["Deployment:Organizations:goodshepherdback:TokenKey"] = Convert.ToBase64String(key)
            })
            .Build();

        var scope = new WebhookScopeResolver(service, configuration).Resolve(
            "123", service.UserId, CancellationToken.None);

        Assert.Equal("ResolvedWebhookScope(***)", scope.ToString());
        Assert.Contains(
            service.ContactQuery!.Criteria.Conditions,
            condition => condition.AttributeName == "statecode" &&
                condition.Operator == ConditionOperator.Equal &&
                Equals(condition.Values.Single(), 0));
    }

    /// <summary>只提供本案例固定實體，拒絕未核定的任意資料讀取及寫入。</summary>
    private sealed class ResolverCrm(Guid organizationId, Guid churchId, Guid channelRecordId, string encryptedToken)
        : IOrganizationService
    {
        private readonly Entity church = new("account", churchId)
        {
            ["name"] = "Synthetic church",
            ["new_linechannelid"] = new EntityReference("new_linechannel", channelRecordId),
            ["new_defaultlanguage"] = "zh-TW",
            ["new_enabledlanguages"] = "zh-TW"
        };

        private readonly Entity channel = new("new_linechannel", channelRecordId)
        {
            ["new_channelid"] = "123",
            ["new_accesstokenencrypted"] = encryptedToken
        };

        internal string UserId { get; } = "U00000000000000000000000000000000";
        internal QueryExpression? ContactQuery { get; private set; }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName == "account" && id == church.Id) return church;
            if (entityName == "new_linechannel" && id == channel.Id) return channel;
            throw new InvalidOperationException("Unexpected retrieve.");
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query is QueryExpression expression && expression.EntityName == "contact")
            {
                ContactQuery = expression;
                return new EntityCollection();
            }
            throw new InvalidOperationException("Unexpected query.");
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is WhoAmIRequest)
            {
                var response = new WhoAmIResponse();
                response.Results["OrganizationId"] = organizationId;
                return response;
            }
            throw new InvalidOperationException("Unexpected execute.");
        }

        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected create.");
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected update.");
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Unexpected associate.");
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Unexpected disassociate.");
    }
}
