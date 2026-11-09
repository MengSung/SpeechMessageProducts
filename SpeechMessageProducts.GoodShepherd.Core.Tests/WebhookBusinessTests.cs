/*
檔案整體說明
目的與定位：P3-06 雙宿主離線契約，保護舊 Golden 路由、原始輸入、真實 CRM 寫入與跨教會拒絕。
主要功能與流程：正式 resolver 逐筆讀 731 筆已驗收嵌入黃金；具體文字 handler 借用只支援測試所需 SDK 操作的假服務，斷言欄位、正式 ID、取消及故障保真。測試不呼叫 D365、LINE 或設定來源，不把假服務結果當實機驗收。
使用邊界與維護：假 SDK 與候選 Entity 每個案例獨占；沒有靜態使用者集合、背景工作、連線或需 Dispose 的來源。嵌入 stream／reader 以 using 關閉；同名不同 ID 不合併，重複 ID／跨教會必須在寫入前拒絕。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Members;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>以窄假 CRM 驗證真正領域入口；不使用會直接回成功的業務 sink。</summary>
public sealed class WebhookBusinessTests
{
    /// <summary>Golden 每個變體都經正式過渡 resolver；保持排序、大小寫及原 matcher 的相容差異。</summary>
    [Fact]
    public void Golden731_UsesRealResolver()
    {
        using var stream = typeof(WebhookBusinessTests).Assembly.GetManifestResourceStream("SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.webhook-routing.json")!;
        using var reader = new StreamReader(stream);
        var cases = (JArray)JObject.Parse(reader.ReadToEnd())["cases"]!;
        Assert.Equal(731, cases.Count);
        foreach (var item in cases) Assert.Equal((string)item["actualIntent"]!, TransitionalIntentResolver.Resolve((string)item["input"]!));
    }

    /// <summary>真入口必須把 RPG 次數累加於本人的當週正式記錄，不可只回意圖或成功代碼。</summary>
    [Fact]
    public void Rpg_UpdatesActualRecordAndOriginalParameters()
    {
        var sdk = new BusinessSdk();
        sdk.Record["new_rpg_number"] = 2;
        var result = new WebhookTextHandler(new ChurchClock()).Handle(sdk, sdk.Actor, "rpg3", CancellationToken.None);
        Assert.Equal(WebhookBusinessStatus.Applied, result.Status);
        var update = Assert.Single(sdk.Updates);
        Assert.Equal(sdk.Record.Id, update.Id);
        Assert.Equal(5, update.GetAttributeValue<int>("new_rpg_number"));
        Assert.Contains(sdk.Queries, q => q.Criteria.Conditions.Any(c => c.AttributeName == "new_contact_new_present_record" && Equals(c.Values[0], sdk.Actor.ContactId)));
    }

    /// <summary>不合理的 RPG 增量必須在查詢後、更新前拒絕，避免負值或溢位污染正式週記錄。</summary>
    [Fact]
    public void Rpg_InvalidIncrement_IsRejectedWithoutUpdate()
    {
        var sdk = new BusinessSdk();

        var result = new WebhookTextHandler(new ChurchClock()).Handle(sdk, sdk.Actor, "RPG-9999", CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.Rejected, result.Status);
        Assert.Equal("InvalidRpgIncrement", result.Code);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>靈修次數不是整數時不得把既有資料覆寫為零，應保留原記錄並具名拒絕。</summary>
    [Fact]
    public void Devotion_InvalidCount_IsRejectedWithoutUpdate()
    {
        var sdk = new BusinessSdk();

        var result = new WebhookTextHandler(new ChurchClock()).Handle(sdk, sdk.Actor, "靈修abc次", CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.Rejected, result.Status);
        Assert.Equal("InvalidDevotionCount", result.Code);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>群組 Join 重送以完整組織／教會／LINE ID 對同一正式 Contact 做原子 Upsert。</summary>
    [Fact]
    public void GroupJoin_ReusesStableDatabaseIdentityOnRedelivery()
    {
        var sdk = new BusinessSdk();
        var groupId = "C00000000000000000000000000000000";

        var first = WebhookMembershipHandler.Join(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, groupId, null,
            "Synthetic group", "https://example.test/group.png", CancellationToken.None);
        var redelivery = WebhookMembershipHandler.Join(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, groupId, null,
            "Synthetic group", "https://example.test/group.png", CancellationToken.None);

        var expectedId = WebhookRecordOperations.BusinessId(sdk.Actor.OrganizationId, sdk.Actor.ChurchId, "line-contact", groupId);
        Assert.Equal(expectedId, Assert.Single(first.RecordIds));
        Assert.Equal(expectedId, Assert.Single(redelivery.RecordIds));
        Assert.Equal(2, sdk.Upserts.Count);
        Assert.All(sdk.Upserts, entity => Assert.Equal(expectedId, entity.Id));
        Assert.Equal("Synthetic group", sdk.Upserts[0].GetAttributeValue<string>("lastname"));
        Assert.Equal("Synthetic group", sdk.Upserts[0].GetAttributeValue<string>("new_line_displayname"));
        Assert.Equal(groupId, sdk.Upserts[0].GetAttributeValue<string>("new_lineid"));
        Assert.Equal(groupId, sdk.Upserts[0].GetAttributeValue<string>("new_lineid_backup"));
        Assert.Equal(sdk.Actor.ChurchId, sdk.Upserts[0].GetAttributeValue<EntityReference>("parentcustomerid")?.Id);
        Assert.False(sdk.Upserts[0].GetAttributeValue<bool>("new_line_register"));
    }

    /// <summary>已存在候選若沒有同一 LINE 綁定，必須在 CRM 寫入前拒絕，不能接管任意 Contact。</summary>
    [Fact]
    public void GroupJoin_RejectsMismatchedExistingBindingBeforeWrite()
    {
        var sdk = new BusinessSdk();
        var groupId = "C00000000000000000000000000000000";
        sdk.Contact["new_lineid"] = "C11111111111111111111111111111111";

        Assert.Throws<InvalidOperationException>(() => WebhookMembershipHandler.Join(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, groupId, sdk.Contact,
            "Synthetic group", string.Empty, CancellationToken.None));

        Assert.Empty(sdk.Upserts);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>既有 Contact 缺少 LINE ID 時注入不完整資料；Join 必須在 CRM 寫入前 fail closed。</summary>
    [Fact]
    public void GroupJoin_RejectsMissingExistingBindingBeforeWrite()
    {
        var sdk = new BusinessSdk();
        var groupId = "C00000000000000000000000000000000";
        sdk.Contact.Attributes.Remove("new_lineid");
        sdk.Contact.RowVersion = "7";

        Assert.Throws<InvalidOperationException>(() => WebhookMembershipHandler.Join(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, groupId, sdk.Contact,
            "Synthetic group", string.Empty, CancellationToken.None));

        Assert.Empty(sdk.Upserts);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>新個人 Follow 重送也須沿同一正式 Contact ID 原子 Upsert，不建立平行重複資料列。</summary>
    [Fact]
    public void Follow_CreateUsesStableDatabaseIdentityOnRedelivery()
    {
        var sdk = new BusinessSdk();

        var first = WebhookMembershipHandler.Follow(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, sdk.Actor.UserId, null,
            "Synthetic user", string.Empty, string.Empty, CancellationToken.None);
        var redelivery = WebhookMembershipHandler.Follow(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, sdk.Actor.UserId, null,
            "Synthetic user", string.Empty, string.Empty, CancellationToken.None);

        var expectedId = WebhookRecordOperations.BusinessId(sdk.Actor.OrganizationId, sdk.Actor.ChurchId, "line-contact", sdk.Actor.UserId);
        Assert.Equal(expectedId, Assert.Single(first.RecordIds));
        Assert.Equal(expectedId, Assert.Single(redelivery.RecordIds));
        Assert.Equal(2, sdk.Upserts.Count);
        Assert.All(sdk.Upserts, entity => Assert.Equal(expectedId, entity.Id));
        Assert.Equal(sdk.Actor.ChurchId, sdk.Upserts[0].GetAttributeValue<EntityReference>("parentcustomerid")?.Id);
        Assert.Equal(sdk.Actor.UserId, sdk.Upserts[0].GetAttributeValue<string>("new_lineid"));
        Assert.Equal("Synthetic user(Line)", sdk.Upserts[0].GetAttributeValue<string>("lastname"));
    }

    /// <summary>注入格式錯誤但長度正確的 LINE actor；Follow 必須在建立任何候選前拒絕。</summary>
    [Fact]
    public void Follow_RejectsInvalidSourceIdentityBeforeWrite()
    {
        var sdk = new BusinessSdk();
        var invalidUserId = "U" + new string('Z', 32);

        Assert.Throws<ArgumentException>(() => WebhookMembershipHandler.Follow(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, invalidUserId, null,
            "Synthetic user", string.Empty, string.Empty, CancellationToken.None));

        Assert.Empty(sdk.Upserts);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>注入不完整的 profile 顯示名稱；Follow 必須拒絕而非因 null dereference 或寫入部分資料。</summary>
    [Fact]
    public void Follow_RejectsIncompleteProfileBeforeWrite()
    {
        var sdk = new BusinessSdk();

        Assert.Throws<ArgumentException>(() => WebhookMembershipHandler.Follow(
            sdk, sdk.Actor.OrganizationId, sdk.Actor.ChurchId, sdk.Actor.UserId, null,
            null!, string.Empty, string.Empty, CancellationToken.None));

        Assert.Empty(sdk.Upserts);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>顯式排除姓名手機註冊，未寫 CRM；不能沿用舊明文綁定或創造替代引導。</summary>
    [Fact]
    public void Registration_IsNamedRejectionWithoutWrites()
    {
        var sdk = new BusinessSdk();
        var result = new WebhookTextHandler(new ChurchClock()).Handle(sdk, sdk.Actor, "//synthetic,000", CancellationToken.None);
        Assert.Equal(WebhookBusinessStatus.Rejected, result.Status);
        Assert.Equal("RegistrationExcluded", result.Code);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>查到的 actor 若屬另一教會即在業務資料查詢與寫入前拒絕，避免路由值冒充授權。</summary>
    [Fact]
    public void ForeignChurch_RejectsBeforeBusinessWrites()
    {
        var sdk = new BusinessSdk();
        sdk.Contact["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => new WebhookTextHandler(new ChurchClock()).Handle(sdk, sdk.Actor, "RPG", CancellationToken.None));
        Assert.Empty(sdk.Updates);
    }

    /// <summary>SDK 故障保持原 Exception，讓外層共用告警保留型別與堆疊；不偽裝查無或成功。</summary>
    [Fact]
    public void RealSdkFailure_IsNotConvertedToSuccess()
    {
        var sdk = new BusinessSdk { Failure = new TimeoutException("synthetic") };
        var failure = Assert.Throws<TimeoutException>(() => new WebhookTextHandler(new ChurchClock()).Handle(sdk, sdk.Actor, "RPG", CancellationToken.None));
        Assert.Same(sdk.Failure, failure);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>窄 SDK owner 只保存本案例候選與請求，未知操作立即失敗；不連網、不共享狀態。</summary>
    private sealed class BusinessSdk : IOrganizationService
    {
        /// <summary>建立合成組織、教會與本人綁定及現有週記錄，避免測試隱含跨租戶預設。</summary>
        public BusinessSdk()
        {
            Actor = new WebhookActor(Guid.NewGuid(), "123", Guid.NewGuid(), Guid.NewGuid(), "U" + new string('0', 32), "Asia/Taipei");
            Contact = new Entity("contact", Actor.ContactId) { ["parentcustomerid"] = new EntityReference("account", Actor.ChurchId), ["new_lineid"] = Actor.UserId };
            Record = new Entity("new_present_record", Guid.NewGuid()) { RowVersion = "7", ["new_contact_new_present_record"] = new EntityReference("contact", Actor.ContactId) };
        }
        /// <summary>測試範圍與真實 actor 相同，僅在本案例存活。</summary>
        public WebhookActor Actor { get; }
        /// <summary>允許故障測試改為外教會，作為決定性隔離注入。</summary>
        public Entity Contact { get; }
        /// <summary>本週記錄候選，不提供其他使用者的共享實體。</summary>
        public Entity Record { get; }
        /// <summary>只保留本案例已完成更新的窄欄位集合。</summary>
        public List<Entity> Updates { get; } = new List<Entity>();
        /// <summary>記錄每次資料庫原子 Upsert 候選，證明重送不產生第二種識別。</summary>
        public List<Entity> Upserts { get; } = new List<Entity>();
        /// <summary>正式查詢的參數證據，不包含真實資料。</summary>
        public List<QueryExpression> Queries { get; } = new List<QueryExpression>();
        /// <summary>真實故障替身，用來驗證 reference identity 不被吞掉。</summary>
        public Exception? Failure { get; set; }
        /// <summary>僅允許讀本人 Contact；其他讀取是測試未核定的資料存取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Failure is not null ? throw Failure : entityName == "contact" && id == Contact.Id ? Contact : throw new InvalidOperationException("Unexpected retrieve.");
        /// <summary>記下查詢並回目前週記錄，實際欄位範圍由測試斷言。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (Failure is not null) throw Failure;
            var expression = (QueryExpression)query; Queries.Add(expression);
            return new EntityCollection(new List<Entity> { Record });
        }
        /// <summary>只允許具 rowversion 的更新，驗證正式 handler 沒有盲目覆寫併行資料。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is UpsertRequest upsert)
            {
                Upserts.Add(upsert.Target);
                var response = new UpsertResponse();
                response.Results["Target"] = new EntityReference(upsert.Target.LogicalName, upsert.Target.Id);
                return response;
            }
            if (request is UpdateRequest update)
            {
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                Assert.Equal("7", update.Target.RowVersion);
                Updates.Add(update.Target); return new UpdateResponse();
            }
            throw new InvalidOperationException("Unexpected request.");
        }
        /// <summary>測試沒有授權任意建立，錯誤路徑必須明確失敗。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected create.");
        /// <summary>盲目更新被拒絕，強制經具併行條件的 Execute。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unconditional update.");
        /// <summary>本包沒有刪除業務，呼叫代表越界。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");
        /// <summary>測試沒有授權關聯建立。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Unexpected associate.");
        /// <summary>測試沒有授權解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Unexpected disassociate.");
    }
}
