/*
檔案整體說明
目的與定位：F5 持久發送紀錄的正式分層 schema 離線契約，確保 adapter 依賴能在三組織一致部署。
主要功能與流程：複製真實 schema 至案例獨佔 fixture，經正式 LoadForOrganization 解析後，
核對組織擁有權、optimistic concurrency、batch 唯一鍵、所有安全摘要欄與固定選項。
使用邊界與維護：fixture 唯一擁有臨時目錄並在 using 結束清理；不讀 User Secrets、不建
SDK 連線、不 apply。測試只證明結構候選可解析，不冒稱 key 已 Active 或 LINE 手機收到。
缺欄、非唯一鍵、放入原始訊息／名單或漏產品分區均是契約失敗，不能藉更新 snapshot 隱藏。
*/
using System;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Schema;

/// <summary>固定欄位不是臨時部署測試表，而是 Gateway 事件冪等與 CAS 的資料庫契約。</summary>
public sealed class SendRecordSchemaTests
{
    /// <summary>三個平台組織都必須有相同正式結構；業務內容不能透過原始本文或 userId 落檔。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void SendRecord_AllProducts_ContainAtomicPersistenceContract(string organization)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var entity = Assert.Single(schema.Entities, entity => entity.LogicalName == "new_sendrecord");
        Assert.True(entity.IsNew);
        Assert.Equal("OrganizationOwned", entity.OwnershipType);
        Assert.True(entity.OptimisticConcurrency);
        Assert.Equal("new_name", entity.PrimaryName);
        Assert.False(entity.Audit);
        var key = Assert.Single(entity.Keys);
        Assert.Equal("new_ak_sendrecord", key.LogicalName);
        Assert.Equal<string>(["new_batchkey"], key.Attributes);
        Assert.Equal<string>(
        [
            "new_attempts", "new_batchcount", "new_batchindex", "new_batchkey", "new_category",
            "new_channelkey", "new_correlationid", "new_errorcode", "new_messagecount", "new_name",
            "new_operationhash", "new_payloadhash", "new_product", "new_recipientcount",
            "new_recipienthash", "new_result", "new_retrykey", "new_sendernote", "new_senton", "new_tenant"
        ], entity.Fields.Select(field => field.LogicalName).Order(StringComparer.Ordinal));
        foreach (var name in new[] { "new_batchkey", "new_operationhash", "new_payloadhash", "new_recipienthash" })
        {
            var field = entity.Fields.Single(field => field.LogicalName == name);
            Assert.Equal("string", field.Type);
            Assert.Equal(64, field.MaxLength);
            Assert.True(field.Required);
        }
        Assert.Equal("account", entity.Fields.Single(field => field.LogicalName == "new_tenant").Target);
        Assert.Equal(20, entity.Fields.Single(field => field.LogicalName == "new_channelkey").MaxLength);
        Assert.Equal(128, entity.Fields.Single(field => field.LogicalName == "new_correlationid").MaxLength);
        Assert.Equal(36, entity.Fields.Single(field => field.LogicalName == "new_retrykey").MaxLength);
        Assert.Equal(Enumerable.Range(100000000, 3), entity.Fields.Single(field => field.LogicalName == "new_category").Options.Select(option => option.Value));
        Assert.Equal(Enumerable.Range(100000000, 5), entity.Fields.Single(field => field.LogicalName == "new_result").Options.Select(option => option.Value));
        var senton = entity.Fields.Single(field => field.LogicalName == "new_senton");
        Assert.Equal("datetime", senton.Type);
        Assert.Equal("UserLocal", senton.Behavior);
        Assert.Equal("DateAndTime", senton.Format);
    }
}
