/*
檔案整體說明
目的與定位：FS 完整 metadata 發布護欄的純合成 SDK 增量契約；直接執行
UiMetadataPublicationGuard，不使用引擎、CLI、檔案輸出、組織連線或秘密設定。
主要功能與流程：先以完整且一致的兩來源建立純值基準，再捕捉寫前 lookup／關聯、
替代鍵及局部選項候選，以獨立建構的 SDK 正式回應驗證精確新增。新表不查詢，但新表
指向既有表時，既有表的 OneToMany 仍受審；舊 scalar 與新增原子樹都不得被忽略。
保護契約與故障注入：合法增量必須成功；正式 ID、目標、cascade、鍵欄位、完整雙語
Label、舊選項或舊描述任一漂移都拒絕。只明列新鍵 Pending／InProgress／Active，
Failed 及未知值不能放行。published 可缺新增項，unpublished 不得缺少必要增量。
測試邊界：尚未實作的新 API 由測試層反射呼叫，缺方法會明確斷言失敗，不建立產品
stub、不以編譯失敗充當行為 RED；方法存在後立即執行真護欄。反射僅解除呼叫包裝，
原 Exception 與 caller token 不變。一般 attribute 控制仍直接使用既有強型別 API。
資源與隔離：每案服務唯一擁有兩來源建構回呼及讀取紀錄，SDK 回應每次重新建立；
Dispose 清除回呼與紀錄，沒有背景工作、計時器、連線或共用可變 SDK。候選保留期間
以弱參照驗證來源可回收，取消來源由 using 釋放；所有證據僅屬離線測試，不代表實機。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>
/// 保護完整基準加精確 schema 增量的離線契約；正向控制防止一律拒絕，負向注入防止重信寫後來源。
/// 所有可變 SDK、服務與取消來源只屬本案，不使用主代理正在修改的引擎／store。
/// </summary>
public sealed class SchemaMetadataDeltaTests
{
    private static readonly Guid AttributeId = new("31000000-0000-0000-0000-000000000001");
    private static readonly Guid RelationshipId = new("31000000-0000-0000-0000-000000000002");
    private static readonly Guid KeyId = new("31000000-0000-0000-0000-000000000003");
    private static readonly Guid OtherId = new("31000000-0000-0000-0000-000000000099");

    /// <summary>
    /// 既有 CaptureAttribute／Add 必須保留；兩來源的精確新欄可以通過，但兩來源一起改新欄標籤仍拒絕。
    /// 不經尚未存在的新 API，故可獨立辨別合成完整回應是否本來就有效。
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Attribute_ExistingApi_PreservesExactCandidate(bool publishedToo, bool drift)
    {
        using var fixture = new BoundaryFixture(metadata =>
        {
            var attribute = StringAttribute();
            attribute.MetadataId = AttributeId;
            attribute.LogicalName = "new_added";
            Set(attribute, "IsManaged", false);
            Set(metadata, "Attributes", metadata.Attributes.Append(attribute).ToArray());
            if (drift) attribute.DisplayName = new Label("未審新欄", 1028);
        }, publishedToo, "account");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        delta.Add(delta.CaptureAttribute("account", StringAttribute(), default), AttributeId);

        if (drift) Assert.Throws<InvalidDataException>(() => fixture.Check(delta));
        else Assert.NotNull(fixture.Check(delta));
    }

    /// <summary>
    /// 查閱新增必須同時核准欄位與兩端關聯；只讀基準既有表，包含新表指向舊表及自我關聯。
    /// Targets 由 relationship.ReferencedEntity 明確補齊，不以捕捉時為 null 略過該子樹。
    /// </summary>
    [Theory]
    [InlineData("account", "contact", false)]
    [InlineData("account", "contact", true)]
    [InlineData("new_child", "account", false)]
    [InlineData("new_child", "account", true)]
    [InlineData("account", "new_parent", false)]
    [InlineData("account", "new_parent", true)]
    [InlineData("account", "account", false)]
    [InlineData("account", "account", true)]
    public void Lookup_ExactDelta_UpdatesOnlyExistingEndpoints(string referencing, string referenced, bool publishedToo)
    {
        var names = new[] { referencing, referenced }.Where(name => name is "account" or "contact")
            .Distinct(StringComparer.Ordinal).ToArray();
        using var fixture = new BoundaryFixture(metadata => ApplyLookup(metadata, referencing, referenced), publishedToo, names);
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var candidate = Capture(delta, "CaptureLookup", referencing, Lookup(), Relationship(referencing, referenced), CancellationToken.None);
        Invoke(delta, "AddLookup", candidate, AttributeId, RelationshipId);

        Assert.NotNull(fixture.Check(delta));

        Assert.All(fixture.Service.Reads, read => Assert.Contains(read.Entity, names));
        Assert.Equal(names.Length * 4, fixture.Service.Reads.Count);
    }

    /// <summary>
    /// 新欄、雙端關聯或任一舊 scalar 漂移，即使兩來源相同也必須對原基準拒絕；
    /// 缺其中一端關聯不能以只驗 lookup 欄位取代，額外伺服器預設亦不可自動信任。
    /// </summary>
    [Theory]
    [InlineData("attribute-id", false)]
    [InlineData("attribute-id", true)]
    [InlineData("relationship-id", false)]
    [InlineData("relationship-id", true)]
    [InlineData("target", false)]
    [InlineData("target", true)]
    [InlineData("lookup-label", false)]
    [InlineData("lookup-label", true)]
    [InlineData("cascade", false)]
    [InlineData("cascade", true)]
    [InlineData("missing-many-to-one", false)]
    [InlineData("missing-many-to-one", true)]
    [InlineData("missing-one-to-many", false)]
    [InlineData("missing-one-to-many", true)]
    [InlineData("old-label", false)]
    [InlineData("old-label", true)]
    [InlineData("old-cascade", false)]
    [InlineData("old-cascade", true)]
    [InlineData("old-key", true)]
    [InlineData("unapproved-default", true)]
    [InlineData("duplicate-relationship", true)]
    public void Lookup_DriftOrIncompleteEndpoint_Refuses(string change, bool bothSources)
    {
        using var fixture = new BoundaryFixture(metadata => ApplyLookup(metadata, "account", "contact"), bothSources, "account", "contact");
        fixture.Service.Mutate = metadata => MutateLookup(metadata, change);
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddLookup", Capture(delta, "CaptureLookup", "account", Lookup(),
            Relationship("account", "contact"), CancellationToken.None), AttributeId, RelationshipId);

        Assert.Throws<InvalidDataException>(() => fixture.Check(delta));
    }

    /// <summary>SDK 可以修改 request；捕捉後修改 lookup 標籤與 relationship cascade，不得改變已核准純值候選。</summary>
    [Fact]
    public void Lookup_RequestMutationAfterCapture_DoesNotChangeApproval()
    {
        using var fixture = new BoundaryFixture(metadata => ApplyLookup(metadata, "account", "contact"), true, "account", "contact");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var lookup = Lookup();
        var relationship = Relationship("account", "contact");
        var candidate = Capture(delta, "CaptureLookup", "account", lookup, relationship, CancellationToken.None);
        lookup.DisplayName.LocalizedLabels[0].Label = "request 被修改";
        relationship.CascadeConfiguration.Delete = CascadeType.Cascade;
        relationship.ReferencedEntity = "new_other";
        Invoke(delta, "AddLookup", candidate, AttributeId, RelationshipId);

        Assert.NotNull(fixture.Check(delta));
    }

    /// <summary>
    /// 新鍵索引非同步狀態只核准官方明列的三種讀值；兩來源可各處於不同合法狀態，
    /// 但不改任何原有鍵的狀態、欄位集合或其他 metadata。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    public void Key_ExactDelta_AllowsOnlyDeclaredAsyncStates(int publishedStatus, int unpublishedStatus)
    {
        using var fixture = new BoundaryFixture(metadata => ApplyKey(metadata, 0), true, "account");
        fixture.Service.MutateBySource = (metadata, unpublished) =>
            Set(metadata.Keys.Single(key => key.MetadataId == KeyId), "EntityKeyIndexStatus",
                (EntityKeyIndexStatus)(unpublished ? unpublishedStatus : publishedStatus));
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddKey", Capture(delta, "CaptureKey", "account", Key(), CancellationToken.None), KeyId);

        Assert.NotNull(fixture.Check(delta));
    }

    /// <summary>尚未發布的新鍵可以僅出現在 unpublished；其正式 ID 與完整欄位及 Label 仍必須核對。</summary>
    [Fact]
    public void Key_OnlyUnpublishedContainsApprovedKey_Succeeds()
    {
        using var fixture = new BoundaryFixture(metadata => ApplyKey(metadata, 1), false, "account");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddKey", Capture(delta, "CaptureKey", "account", Key(), CancellationToken.None), KeyId);

        Assert.NotNull(fixture.Check(delta));
    }

    /// <summary>Failed、未知狀態、錯正式 ID 或鍵原子樹漂移都拒絕；舊鍵亦不得被新鍵的狀態例外涵蓋。</summary>
    [Theory]
    [InlineData("failed")]
    [InlineData("unknown-negative")]
    [InlineData("unknown-positive")]
    [InlineData("id")]
    [InlineData("attributes")]
    [InlineData("label")]
    [InlineData("managed")]
    [InlineData("old-status")]
    [InlineData("old-label")]
    public void Key_UnapprovedStateOrMetadata_Refuses(string change)
    {
        using var fixture = new BoundaryFixture(metadata => ApplyKey(metadata, 0), true, "account");
        fixture.Service.Mutate = metadata =>
        {
            var key = metadata.Keys.Single(item => item.MetadataId == KeyId);
            switch (change)
            {
                case "failed": Set(key, "EntityKeyIndexStatus", (EntityKeyIndexStatus)3); break;
                case "unknown-negative": Set(key, "EntityKeyIndexStatus", (EntityKeyIndexStatus)(-1)); break;
                case "unknown-positive": Set(key, "EntityKeyIndexStatus", (EntityKeyIndexStatus)4); break;
                case "id": key.MetadataId = OtherId; break;
                case "attributes": key.KeyAttributes = ["new_name"]; break;
                case "label": key.DisplayName = new Label("未審替代鍵", 1028); break;
                case "managed": Set(key, "IsManaged", true); break;
                case "old-status": Set(metadata.Keys[0], "EntityKeyIndexStatus", (EntityKeyIndexStatus)2); break;
                case "old-label": metadata.Attributes[0].DisplayName = new Label("未審舊欄", 1028); break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        };
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddKey", Capture(delta, "CaptureKey", "account", Key(), CancellationToken.None), KeyId);

        Assert.Throws<InvalidDataException>(() => fixture.Check(delta));
    }

    /// <summary>新選項以 Value 定位，完整雙語 Label 精確符合；相同標籤但不同值的舊選項必須全數保留。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Option_ExactValueAndFullLabel_PreservesEveryOldOption(bool publishedToo)
    {
        using var fixture = new BoundaryFixture(ApplyOption, publishedToo, "account");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddOption", Capture(delta, "CaptureOption", "account", "new_choice", Option(), CancellationToken.None));

        Assert.NotNull(fixture.Check(delta));
    }

    /// <summary>
    /// 新選項完整 Label、舊選項、舊欄或其他集合任何變動都拒絕；相等的兩份寫後來源不是新基準。
    /// 重複 Value 即使標籤相同亦是衝突，不可靜默 Distinct、覆蓋或刪除舊選項。
    /// </summary>
    [Theory]
    [InlineData("value")]
    [InlineData("second-language")]
    [InlineData("user-label")]
    [InlineData("description")]
    [InlineData("old-label")]
    [InlineData("remove-old")]
    [InlineData("extra-option")]
    [InlineData("duplicate-value")]
    [InlineData("old-attribute")]
    public void Option_AnyUnapprovedSubtreeChange_Refuses(string change)
    {
        using var fixture = new BoundaryFixture(ApplyOption, true, "account");
        fixture.Service.Mutate = metadata =>
        {
            var options = Choice(metadata).OptionSet.Options;
            var option = options.Single(item => item.Value == 100000003);
            switch (change)
            {
                case "value": option.Value = 100000004; break;
                case "second-language": option.Label.LocalizedLabels.Single(label => label.LanguageCode == 1033).Label = "Unapproved"; break;
                case "user-label": option.Label.UserLocalizedLabel.Label = "未審使用者標籤"; break;
                case "description": option.Description = new Label("未知伺服器預設", 1028); break;
                case "old-label": options[0].Label.LocalizedLabels[0].Label = "被偷改的舊值"; break;
                case "remove-old": options.RemoveAt(1); break;
                case "extra-option": options.Add(new OptionMetadata(new Label("未核准", 1028), 100000004)); break;
                case "duplicate-value": options.Add(Option()); break;
                case "old-attribute": metadata.Description = new Label("被偷改的舊描述", 1028); break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        };
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddOption", Capture(delta, "CaptureOption", "account", "new_choice", Option(), CancellationToken.None));

        Assert.Throws<InvalidDataException>(() => fixture.Check(delta));
    }

    /// <summary>替代鍵欄位陣列及選項翻譯在 capture 後被 request 持有人修改，純值候選仍維持寫前內容。</summary>
    [Fact]
    public void KeyAndOption_RequestMutationAfterCapture_DoesNotChangeApproval()
    {
        using var fixture = new BoundaryFixture(metadata => { ApplyKey(metadata, 2); ApplyOption(metadata); }, true, "account");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var key = Key();
        var option = Option();
        var keyCandidate = Capture(delta, "CaptureKey", "account", key, CancellationToken.None);
        var optionCandidate = Capture(delta, "CaptureOption", "account", "new_choice", option, CancellationToken.None);
        key.KeyAttributes[0] = "new_other";
        key.DisplayName.LocalizedLabels[0].Label = "request 被修改";
        option.Value = 100000004;
        option.Label.LocalizedLabels[1].Label = "request 被修改";
        Invoke(delta, "AddKey", keyCandidate, KeyId);
        Invoke(delta, "AddOption", optionCandidate);

        Assert.NotNull(fixture.Check(delta));
    }

    /// <summary>完整 guard 必須合併同次 lookup／key／option 的精確增量，不得每次用寫後來源覆蓋舊基準。</summary>
    [Fact]
    public void CombinedDelta_PreservesOriginalBaselineAcrossAllThreeKinds()
    {
        using var fixture = new BoundaryFixture(metadata =>
        {
            ApplyLookup(metadata, "account", "contact");
            if (metadata.LogicalName == "account") { ApplyKey(metadata, 2); ApplyOption(metadata); }
        }, false, "account", "contact");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddLookup", Capture(delta, "CaptureLookup", "account", Lookup(),
            Relationship("account", "contact"), CancellationToken.None), AttributeId, RelationshipId);
        Invoke(delta, "AddKey", Capture(delta, "CaptureKey", "account", Key(), CancellationToken.None), KeyId);
        Invoke(delta, "AddOption", Capture(delta, "CaptureOption", "account", "new_choice", Option(), CancellationToken.None));
        Assert.NotNull(fixture.Check(delta));

        fixture.Service.Mutate = metadata => metadata.Description = new Label("兩來源共同漂移", 1028);
        fixture.Service.PublishedToo = true;

        Assert.Throws<InvalidDataException>(() => fixture.Check(delta));
    }

    /// <summary>兩個正式 lookup ID 及新鍵 ID 都不可為空；拒絕必須發生在加入候選時，不能留待 SDK 來源冒充。</summary>
    [Theory]
    [InlineData("attribute")]
    [InlineData("relationship")]
    [InlineData("key")]
    public void Add_EmptyFormalIdentity_Refuses(string missing)
    {
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        if (missing == "key")
        {
            var candidate = Capture(delta, "CaptureKey", "account", Key(), CancellationToken.None);
            Assert.Throws<InvalidDataException>(() => Invoke(delta, "AddKey", candidate, Guid.Empty));
        }
        else
        {
            var candidate = Capture(delta, "CaptureLookup", "account", Lookup(), Relationship("account", "contact"), CancellationToken.None);
            Assert.Throws<InvalidDataException>(() => Invoke(delta, "AddLookup", candidate,
                missing == "attribute" ? Guid.Empty : AttributeId, missing == "relationship" ? Guid.Empty : RelationshipId));
        }
    }

    /// <summary>capture 遇 caller 已取消必須保留相同 token；反射測試層不能把它變成 TargetInvocationException。</summary>
    [Theory]
    [InlineData("lookup")]
    [InlineData("key")]
    [InlineData("option")]
    public void Capture_CanceledCaller_PreservesToken(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var delta = new UiMetadataPublicationGuard.SchemaDelta();

        var error = Assert.Throws<OperationCanceledException>(() =>
        {
            _ = kind switch
            {
                "lookup" => Capture(delta, "CaptureLookup", "account", Lookup(), Relationship("account", "contact"), cancellation.Token),
                "key" => Capture(delta, "CaptureKey", "account", Key(), cancellation.Token),
                "option" => Capture(delta, "CaptureOption", "account", "new_choice", Option(), cancellation.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        });

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    /// <summary>capture 的文字預算對新選項亦生效；三倍 UTF-8 大小的合成標籤不能流入無界候選。</summary>
    [Fact]
    public void Option_OversizedLabel_RefusesBeforeAddingCandidate()
    {
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var option = new OptionMetadata(new Label(new string('測', 1024 * 1024), 1028), 100000003);

        Assert.Throws<InvalidDataException>(() => Capture(delta, "CaptureOption", "account", "new_choice", option, CancellationToken.None));
    }

    /// <summary>鍵集合原已滿 32 項時不能再新增；基準先實際通過完整讀取，避免用損壞回應取得假陽性。</summary>
    [Fact]
    public void Key_ThirtyThirdKey_RefusesWithoutDiscardingOldKeys()
    {
        using var fixture = new BoundaryFixture(metadata => ApplyKey(metadata, 2), true, ["account"], metadata =>
        {
            Set(metadata, "Keys", Enumerable.Range(0, 32).Select(index => new EntityKeyMetadata
            {
                MetadataId = new Guid(index + 1, 1, 1, new byte[8]),
                LogicalName = "new_oldkey" + index.ToString(CultureInfo.InvariantCulture),
                KeyAttributes = ["new_name"]
            }).ToArray());
        });
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        Invoke(delta, "AddKey", Capture(delta, "CaptureKey", "account", Key(), CancellationToken.None), KeyId);

        Assert.Throws<InvalidDataException>(() => fixture.Check(delta));
    }

    /// <summary>guard 與純值候選仍存活時，capture 的 SDK 物件必須可回收；不能把整個 request 留作往後基準。</summary>
    [Fact]
    public void Capture_AllKinds_DoNotRetainSdkObjects()
    {
        var captured = CaptureEphemeralSdk();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(4, captured.Sources.Length);
        Assert.All(captured.Sources, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(captured.Delta);
        GC.KeepAlive(captured.Candidates);
    }

    /// <summary>同步 SDK 讀取故障不能被增量驗證覆蓋；服務返回後 caller 取消仍保留同一 token 並停止後續讀取。</summary>
    [Fact]
    public void Check_SdkFailureAndCallerCancellation_PreserveOriginalFault()
    {
        using var fixture = new BoundaryFixture(_ => { }, false, "account");
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var failure = new IOException("Synthetic complete metadata failure.");
        fixture.Service.Failure = failure;
        Assert.Same(failure, Assert.Throws<IOException>(() => fixture.Check(delta)));
        fixture.Service.Failure = null;
        using var cancellation = new CancellationTokenSource();
        fixture.Service.AfterRead = cancellation.Cancel;
        var reads = fixture.Service.Reads.Count;

        var error = Assert.Throws<OperationCanceledException>(() => fixture.Check(delta, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(reads + 1, fixture.Service.Reads.Count);
    }

    /// <summary>建立不含唯讀正式身分的完整一般欄請求；伺服器控制獨立建立另一份相同輸入。</summary>
    private static StringAttributeMetadata StringAttribute() => new()
    {
        SchemaName = "new_added", MaxLength = 100, DisplayName = new Label("核准一般欄", 1028),
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
    };

    /// <summary>模擬現有引擎送出的 lookup；Targets 尚未由 server 補上，不能因此視為可忽略。</summary>
    private static LookupAttributeMetadata Lookup() => new()
    {
        SchemaName = "new_parentid", DisplayName = new Label("核准查閱", 1028),
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
        IsSecured = false, IsAuditEnabled = new BooleanManagedProperty(false)
    };

    /// <summary>以明確雙端與 cascade 建立寫前關聯；ReferencingAttribute 由 lookup.SchemaName 明確補齊。</summary>
    private static OneToManyRelationshipMetadata Relationship(string referencing, string referenced) => new()
    {
        SchemaName = "new_approved_parent", ReferencingEntity = referencing, ReferencedEntity = referenced,
        ReferencedAttribute = referenced + "id",
        CascadeConfiguration = new CascadeConfiguration
        {
            Delete = CascadeType.Restrict, Assign = CascadeType.NoCascade, Merge = CascadeType.NoCascade,
            Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
        }
    };

    /// <summary>鍵請求沒有正式 ID／唯讀索引狀態；兩欄及顯示名均屬受審候選，不能由 server 任意改寫。</summary>
    private static EntityKeyMetadata Key() => new()
    {
        SchemaName = "new_approved_key", KeyAttributes = new[] { "new_name", "new_number" },
        DisplayName = new Label("核准複合鍵", 1028)
    };

    /// <summary>完整核准標籤含兩語及獨立 UserLocalizedLabel，不讓只驗第一個 LocalizedLabel 的實作通過。</summary>
    private static OptionMetadata Option()
    {
        var label = new Label("核准選項", 1028) { UserLocalizedLabel = new LocalizedLabel("核准選項", 1028) };
        label.LocalizedLabels.Add(new LocalizedLabel("Approved option", 1033));
        return new OptionMetadata(label, 100000003);
    }

    /// <summary>伺服器回應獨立 materialize 正式 lookup ID、Targets 與雙端關聯；不呼叫任何 guard 建構答案。</summary>
    private static void ApplyLookup(EntityMetadata metadata, string referencing, string referenced)
    {
        if (metadata.LogicalName == referencing)
        {
            var attribute = Lookup();
            attribute.MetadataId = AttributeId;
            attribute.LogicalName = "new_parentid";
            attribute.Targets = new[] { referenced };
            Set(attribute, "IsManaged", false);
            Set(metadata, "Attributes", metadata.Attributes.Append(attribute).ToArray());
            Set(metadata, "ManyToOneRelationships", metadata.ManyToOneRelationships.Append(FormalRelationship(referencing, referenced)).ToArray());
        }
        if (metadata.LogicalName == referenced)
            Set(metadata, "OneToManyRelationships", metadata.OneToManyRelationships.Append(FormalRelationship(referencing, referenced)).ToArray());
    }

    /// <summary>每次回應建立獨立關聯物件；兩端相同正式 ID，不共用可變 SDK 參照。</summary>
    private static OneToManyRelationshipMetadata FormalRelationship(string referencing, string referenced)
    {
        var relationship = Relationship(referencing, referenced);
        relationship.MetadataId = RelationshipId;
        relationship.ReferencingAttribute = "new_parentid";
        Set(relationship, "IsManaged", false);
        return relationship;
    }

    /// <summary>模擬 CreateKey 回應後的完整 metadata，只填明列正式 ID、LogicalName、unmanaged 及索引狀態。</summary>
    private static void ApplyKey(EntityMetadata metadata, int status)
    {
        var key = Key();
        key.MetadataId = KeyId;
        key.LogicalName = "new_approved_key";
        Set(key, "IsManaged", false);
        Set(key, "EntityKeyIndexStatus", (EntityKeyIndexStatus)status);
        Set(metadata, "Keys", metadata.Keys.Append(key).ToArray());
    }

    /// <summary>只補單一核准選項；不改父欄、OptionSet 或任何舊選項。</summary>
    private static void ApplyOption(EntityMetadata metadata) => Choice(metadata).OptionSet.Options.Add(Option());

    /// <summary>固定合成 choice 的正式欄位身分，不按顯示名選欄或去重選項。</summary>
    private static PicklistAttributeMetadata Choice(EntityMetadata metadata) =>
        Assert.IsType<PicklistAttributeMetadata>(metadata.Attributes.Single(attribute => attribute.LogicalName == "new_choice"));

    /// <summary>精確修改單一新／舊原子樹，兩表的故障各在所屬端注入，不改 published 狀態決策。</summary>
    private static void MutateLookup(EntityMetadata metadata, string change)
    {
        if (metadata.LogicalName == "contact")
        {
            if (change == "missing-one-to-many")
                Set(metadata, "OneToManyRelationships", metadata.OneToManyRelationships.Where(item => item.MetadataId != RelationshipId).ToArray());
            return;
        }
        var attribute = Assert.IsType<LookupAttributeMetadata>(metadata.Attributes.Single(item => item.LogicalName == "new_parentid"));
        var relationship = metadata.ManyToOneRelationships.Single(item => item.MetadataId == RelationshipId);
        switch (change)
        {
            case "attribute-id": attribute.MetadataId = OtherId; break;
            case "relationship-id": relationship.MetadataId = OtherId; break;
            case "target": attribute.Targets = new[] { "new_other" }; break;
            case "lookup-label": attribute.DisplayName.LocalizedLabels[0].Label = "未審查閱"; break;
            case "cascade": relationship.CascadeConfiguration.Delete = CascadeType.Cascade; break;
            case "missing-many-to-one": Set(metadata, "ManyToOneRelationships", Array.Empty<OneToManyRelationshipMetadata>()); break;
            case "missing-one-to-many": break;
            case "old-label": metadata.Attributes[0].DisplayName = new Label("未審舊標籤", 1028); break;
            case "old-cascade": metadata.OneToManyRelationships[0].CascadeConfiguration.Delete = CascadeType.Cascade; break;
            case "old-key": metadata.Keys[0].KeyAttributes = new[] { "new_number" }; break;
            case "unapproved-default": attribute.Description = new Label("未知 server 預設", 1028); break;
            case "duplicate-relationship": Set(metadata, "ManyToOneRelationships", new[] { relationship, FormalRelationship("account", "contact") }); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>隔開 SDK 區域生命週期，僅回傳候選與弱參照；NoInlining 避免測試堆疊延長 request 壽命。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UiMetadataPublicationGuard.SchemaDelta Delta, object[] Candidates, WeakReference[] Sources) CaptureEphemeralSdk()
    {
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var lookup = Lookup();
        var relationship = Relationship("account", "contact");
        var key = Key();
        var option = Option();
        var lookupCandidate = Capture(delta, "CaptureLookup", "account", lookup, relationship, CancellationToken.None);
        var keyCandidate = Capture(delta, "CaptureKey", "account", key, CancellationToken.None);
        var optionCandidate = Capture(delta, "CaptureOption", "account", "new_choice", option, CancellationToken.None);
        Invoke(delta, "AddLookup", lookupCandidate, AttributeId, RelationshipId);
        Invoke(delta, "AddKey", keyCandidate, KeyId);
        Invoke(delta, "AddOption", optionCandidate);
        return (delta, new[] { lookupCandidate, keyCandidate, optionCandidate },
            new[] { new WeakReference(lookup), new WeakReference(relationship), new WeakReference(key), new WeakReference(option) });
    }

    /// <summary>新 capture API 必須實際存在且回傳候選；沒有 legacy fallback，不以空候選繞過核准。</summary>
    private static object Capture(UiMetadataPublicationGuard.SchemaDelta delta, string name, params object[] arguments)
    {
        var result = Invoke(delta, name, arguments);
        Assert.NotNull(result);
        return result;
    }

    /// <summary>
    /// 僅測試層以方法名稱與完整參數數量定位新增 API；不存在即明確斷言失敗，保持舊 guard 可編譯。
    /// 產品例外以 ExceptionDispatchInfo 重拋保留原物件及堆疊，不把反射包裝當成安全拒絕。
    /// </summary>
    private static object? Invoke(UiMetadataPublicationGuard.SchemaDelta delta, string name, params object[] arguments)
    {
        var method = typeof(UiMetadataPublicationGuard.SchemaDelta)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(item => item.Name == name && item.GetParameters().Length == arguments.Length);
        Assert.True(method is not null, "Required schema delta API is not implemented: " + name);
        try { return method!.Invoke(delta, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    /// <summary>只用於合成伺服器 metadata 的唯讀 setter，產品不得依賴此測試入口修改 SDK 唯讀契約。</summary>
    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    /// <summary>唯一擁有本案服務與純值基準；建立基準時尚未注入增量，所有同步工作結束後才清除 owner。</summary>
    private sealed class BoundaryFixture : IDisposable
    {
        private readonly string[] names;
        private readonly UiMetadataPublicationGuard.Snapshot baseline;

        /// <summary>本案服務只提供完整 metadata；不與引擎／UI fixture 共用 store 或工作目錄。</summary>
        internal MetadataService Service { get; }

        /// <summary>先建立合法兩來源基準，之後 Check 才啟用核准增量；一般案例無額外基準故障。</summary>
        internal BoundaryFixture(Action<EntityMetadata> apply, bool publishedToo, params string[] names)
            : this(apply, publishedToo, names, null) { }

        /// <summary>僅容量案例可改基準；基準仍先經完整 guard 成功驗證，不能用損壞來源取得預期失敗。</summary>
        internal BoundaryFixture(Action<EntityMetadata> apply, bool publishedToo, string[] names, Action<EntityMetadata>? prepare)
        {
            this.names = names;
            Service = new MetadataService(names, apply, prepare) { PublishedToo = publishedToo };
            baseline = UiMetadataPublicationGuard.Check(Service, names, default);
        }

        /// <summary>每次都對最初純值基準比較，不把前一次成功的寫後回應回灌 baseline。</summary>
        internal UiMetadataPublicationGuard.Snapshot Check(UiMetadataPublicationGuard.SchemaDelta delta, CancellationToken ct = default)
        {
            Service.Applied = true;
            return UiMetadataPublicationGuard.Check(Service, names, ct, baseline, delta);
        }

        /// <summary>同步完成或斷言失敗皆清除服務回呼與讀取清單；沒有外部連線需要接管。</summary>
        public void Dispose() => Service.Dispose();
    }

    /// <summary>單案合成 SDK，完整回應每次重建；只允許已明列的基準表及 RetrieveEntity(All)，其餘立即暴露測試錯誤。</summary>
    private sealed class MetadataService : IOrganizationService, IDisposable
    {
        private readonly HashSet<string> names;
        private Action<EntityMetadata>? apply;
        private Action<EntityMetadata>? prepare;
        private bool disposed;

        /// <summary>只保存純值查詢紀錄，Dispose 清除，不保存 SDK request 或 response。</summary>
        internal List<(string Entity, bool Unpublished)> Reads { get; } = new();
        /// <summary>基準完成後才啟用核准增量，不讓增量在 capture 前冒充原有受審結構。</summary>
        internal bool Applied { get; set; }
        /// <summary>是否兩來源都已顯示新增；false 代表 published 保持舊結構。</summary>
        internal bool PublishedToo { get; set; }
        /// <summary>寫後來源故障注入，僅存在本案同步呼叫期間。</summary>
        internal Action<EntityMetadata>? Mutate { get; set; }
        /// <summary>新鍵不同合法非同步索引狀態的來源專屬注入。</summary>
        internal Action<EntityMetadata, bool>? MutateBySource { get; set; }
        /// <summary>模擬同步 SDK 返回前取消，Dispose 清除以免保留 CancellationTokenSource。</summary>
        internal Action? AfterRead { get; set; }
        /// <summary>本案原始 SDK 故障參照，沒有真組織錯誤本文。</summary>
        internal Exception? Failure { get; set; }

        /// <summary>接管本案固定名稱白名單及回應建構回呼，無連線／timer／背景工作。</summary>
        internal MetadataService(IEnumerable<string> names, Action<EntityMetadata> apply, Action<EntityMetadata>? prepare)
        {
            this.names = new HashSet<string>(names, StringComparer.Ordinal);
            this.apply = apply;
            this.prepare = prepare;
        }

        /// <summary>完整 metadata 每次重新建立，額外同標籤不同值選項永久保留；未知表與 SDK 寫入一律測試失敗。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.False(disposed);
            var retrieve = Assert.IsType<RetrieveEntityRequest>(request);
            Assert.Equal(EntityFilters.All, retrieve.EntityFilters);
            Assert.Contains(retrieve.LogicalName, names);
            Assert.Equal(Guid.Empty, retrieve.MetadataId);
            Reads.Add((retrieve.LogicalName, retrieve.RetrieveAsIfPublished));
            if (Failure is not null) throw Failure;
            var metadata = UiImportMetadataBoundaryTests.Metadata(retrieve.LogicalName);
            Choice(metadata).OptionSet.Options.Add(new OptionMetadata(new Label("合成選項", 1028), 100000099));
            prepare?.Invoke(metadata);
            if (Applied && (PublishedToo || retrieve.RetrieveAsIfPublished))
            {
                apply?.Invoke(metadata);
                Mutate?.Invoke(metadata);
                MutateBySource?.Invoke(metadata, retrieve.RetrieveAsIfPublished);
            }
            AfterRead?.Invoke();
            return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = metadata } };
        }

        /// <summary>唯一 owner 同步停止後清除所有可變回呼、故障及讀取紀錄，可重複 Dispose 而不保留 SDK。</summary>
        public void Dispose()
        {
            disposed = true;
            apply = null; prepare = null; Mutate = null; MutateBySource = null; AfterRead = null; Failure = null;
            Reads.Clear(); names.Clear();
        }

        /// <summary>純 guard 不建立業務資料，任何呼叫皆代表超出測試契約。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>純 guard 不更新業務資料，也不能以寫入修正讀取差異。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>純 guard 不刪除業務資料或未知 metadata。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>純 guard 只讀完整 metadata，禁止改以業務列投影代替。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>沒有業務查詢或分頁資源；未知查詢立即暴露。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>不建立業務關聯；新增關聯僅由合成 metadata 回應表達。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>不解除業務關聯，測試不含自動 rollback。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
