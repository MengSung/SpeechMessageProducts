/*
檔案整體說明
目的與定位：保護 F12 平台測試邀請實體的跨產品結構契約，確保邀請資料可由唯一雜湊查找並以資料庫樂觀並行防止重用。
主要功能與流程：每案建立唯一暫存 repository fixture，經正式分層 SchemaFiles 載入 goodshepherd20、construction 與 association 的平台 schema，使用獨立固定期望值核對 new_testinvite 擁有權、欄位、到期語意、RowVersion 支援與唯一鍵。
依賴與輸出：只讀 repo 內 JSON schema 與純 parser；不載入 User Secrets、不建立 SDK client、不連線 D365／LINE、不輸出個資或秘密。fixture 由本測試唯一擁有，using 結束時清除。
使用邊界與維護：只有 SHA-256 token hash 可持久化，原始邀請憑證不屬 schema；缺欄、型別、lookup、並行或鍵契約不符時測試必須失敗，不能由 schema 自身推導期望值。
驗證方式：先確認 F12 schema 尚未實作時本測試因缺少 new_testinvite 而失敗；實作 schema 後三組織離線 parser 測試均須通過。這些斷言不代表 D365 實機部署或發布。
*/
using System;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Schema;

/// <summary>以獨立固定契約驗證 F12 邀請 schema；只使用暫存 fixture，不接觸任何實機服務。</summary>
public sealed class TestTenantSchemaTests
{
    /// <summary>邀請實體須在三產品共享平台層以 organization ownership 與 row version 保護單次兌換。</summary>
    /// <param name="organization">固定三個測試組織之一，不接受測試外輸入。</param>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void TestInviteSchema_IsSharedAndSingleUseSafe(string organization)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var entity = Assert.Single(schema.Entities, candidate => candidate.LogicalName == "new_testinvite");

        Assert.Equal("OrganizationOwned", entity.OwnershipType);
        Assert.Equal("new_name", entity.PrimaryName);
        Assert.True(entity.OptimisticConcurrency);
        AssertField(entity, "new_name", "string", 100, required: true);
        AssertField(entity, "new_tenant", "lookup", required: true, target: "account");
        AssertField(entity, "new_role", "lookup", required: true, target: "new_role");
        AssertField(entity, "new_tokenhash", "string", 64, required: true);
        var expires = AssertField(entity, "new_expireson", "datetime", required: true);
        Assert.Equal("UserLocal", expires.Behavior);
        Assert.Equal("DateAndTime", expires.Format);
        var used = AssertField(entity, "new_usedon", "datetime");
        Assert.Equal("UserLocal", used.Behavior);
        Assert.Equal("DateAndTime", used.Format);

        var key = Assert.Single(entity.Keys, candidate => candidate.LogicalName == "new_ak_testinvite");
        Assert.Equal("new_tokenhash", Assert.Single(key.Attributes));
    }

    /// <summary>欄位斷言來自 F12 契約，驗證缺漏、型別、容量及 lookup 不會被 schema 自我佐證。</summary>
    /// <param name="entity">正式 parser 完整解析出的邀請實體。</param>
    /// <param name="name">核定的欄位邏輯名稱。</param>
    /// <param name="type">核定的 D365 欄位型別。</param>
    /// <param name="maxLength">字串容量；非字串欄位固定為零。</param>
    /// <param name="required">欄位是否必要。</param>
    /// <param name="target">lookup 的唯一目標，非 lookup 欄位固定為空。</param>
    private static SchemaField AssertField(SchemaEntity entity, string name, string type,
        int maxLength = 0, bool required = false, string target = "")
    {
        var field = Assert.Single(entity.Fields, candidate => candidate.LogicalName == name);
        Assert.Equal(type, field.Type);
        Assert.Equal(1, field.Phase);
        Assert.Equal(maxLength, field.MaxLength);
        Assert.Equal(required, field.Required);
        Assert.Equal(target, field.Target);
        return field;
    }
}
