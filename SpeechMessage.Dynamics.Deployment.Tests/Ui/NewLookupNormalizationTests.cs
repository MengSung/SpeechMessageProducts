/*
檔案整體說明
目的與定位：FS 22:05決策的新建lookup伺服器正規化契約；使用實際唯讀node-differences.json，
不連D365、不讀秘密或建立業務列。真fixture含四次觀察共110節點，保留原基準缺口false。
主要流程：由寫前SDK候選取得完整純值，獨立套用fixture列出的伺服器預設，呼叫真護欄
WithAdditions核對完整預期；固定account Merge=Cascade，其他目標仍NoCascade。
故障注入：未明列舊CaptureLookup、未知新節點、錯預設值、來源身分及舊baseline漂移不得被吸收。
決定性斷言：完整合法預期必須等於actual，未授權預期必須仍不相等或拒絕，不能只測無例外。
資源與隔離：JsonDocument由本案using釋放，字典皆獨立；沒有SDK client、timer、背景工作或共享可變狀態。
尚無新方法時以反射明確斷言缺契約形成行為RED，絕不把編譯失敗或離線結果寫成實機成功。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>以真差異清單保護新關聯的精確白名單；所有候選與fixture生命週期只屬本案。</summary>
public sealed class NewLookupNormalizationTests
{
    /// <summary>四個真觀察必須全部通過，account的固定Merge必須在寫前明列；不重新信任寫後子樹。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlannedNewRelationship_Real110NodeFixture_MatchesExactExpected(int observation)
    {
        var (baseline, delta, actual) = Build(observation, true);
        Assert.Equal(actual.OrderBy(x => x.Key), Compute(baseline, delta, actual).OrderBy(x => x.Key));
    }

    /// <summary>舊捕捉入口、其他節點、錯預設值與舊label變更仍拒絕，白名單不可用於既有元件。</summary>
    [Theory]
    [InlineData("unapproved")]
    [InlineData("extra")]
    [InlineData("wrong-default")]
    [InlineData("old-label")]
    public void NonAuthorizedChange_IsNeverAbsorbed(string fault)
    {
        var (baseline, delta, actual) = Build(0, fault != "unapproved");
        var root = actual.Keys.Single(key => key.EndsWith("].SchemaName", StringComparison.Ordinal))[..^11];
        if (fault == "extra") actual[root + ".Unapproved"] = new("System.Boolean", true);
        if (fault == "wrong-default") actual[root + ".IsCustomRelationship"] = new("System.Boolean", false);
        if (fault == "old-label") actual["account.Description"] = new("System.String", "changed-old");
        var result = Compute(baseline, delta, actual);
        Assert.False(result.Count == actual.Count && result.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value));
    }

    /// <summary>三個分層組織所有account/contact主實體lookup都必須在清單明列Cascade，不靠guard吞掉錯請求。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void LayeredManifest_AccountAndContactMerge_IsExplicitCascade(string organization)
    {
        var property = typeof(SpeechMessage.Dynamics.Deployment.Schema.SchemaField).GetProperty("CascadeMerge");
        Assert.NotNull(property);
        var definition = SpeechMessage.Dynamics.Deployment.Schema.SchemaFiles.LoadForOrganization(DeploymentCli.FindRepositoryRoot(), organization);
        var fields = definition.Entities.SelectMany(entity => entity.Fields).Where(field => field.Type == "lookup" && field.Target is "account" or "contact").ToArray();
        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.Equal("Cascade", property.GetValue(field)));
    }

    /// <summary>本案讀完整fixture並保留實際正式ID；它只證明新關聯子樹，不宣稱原完整baseline已復原。</summary>
    private static (UiMetadataPublicationGuard.Snapshot, UiMetadataPublicationGuard.SchemaDelta,
        Dictionary<string, UiMetadataPublicationGuard.Scalar>) Build(int index, bool approved)
    {
        var path = Path.Combine(DeploymentCli.FindRepositoryRoot(), "SpeechMessage.Dynamics.Deployment.Tests", "Fixtures", "fs-new-lookup-node-differences.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.False(document.RootElement.GetProperty("fullOriginalBaselineAvailable").GetBoolean());
        var all = document.RootElement.GetProperty("observations").EnumerateArray().ToArray();
        Assert.Equal(110, all.Sum(row => row.GetProperty("differences").GetArrayLength()));
        var observation = all[index];
        var target = observation.GetProperty("entity").GetString()!;
        var field = target == "account" ? "new_tenant" : "new_role";
        var relationId = observation.GetProperty("metadataId").GetGuid();
        var relation = new OneToManyRelationshipMetadata
        {
            SchemaName = "new_new_testinvite_" + field, ReferencingEntity = "new_testinvite",
            ReferencedEntity = target, ReferencedAttribute = target + "id", ReferencingAttribute = field,
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict,
                Merge = target == "account" ? CascadeType.Cascade : CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
            }
        };
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        var attribute = new LookupAttributeMetadata { SchemaName = field, DisplayName = new Label("測試", 1028) };
        UiMetadataPublicationGuard.LookupAddition candidate;
        if (approved)
        {
            var method = typeof(UiMetadataPublicationGuard.SchemaDelta).GetMethod("CapturePlannedLookup", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            candidate = (UiMetadataPublicationGuard.LookupAddition)method.Invoke(delta, ["new_testinvite", attribute, relation, CancellationToken.None])!;
        }
        else candidate = delta.CaptureLookup("new_testinvite", attribute, relation, default);
        delta.AddLookup(candidate, new Guid("31000000-0000-0000-0000-000000000001"), relationId);
        var addition = delta.Additions.Single(item => item.Entity == target && item.Collection == "OneToManyRelationships");
        var basis = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(StringComparer.Ordinal)
        {
            [target] = new("Contract", "Microsoft.Xrm.Sdk.Metadata.EntityMetadata"),
            [target + ".OneToManyRelationships"] = new("Collection", 0),
            [target + ".Description"] = new("System.String", "original-old")
        };
        var root = target + ".OneToManyRelationships[" + addition.Identity + "]";
        var actual = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(basis, StringComparer.Ordinal)
        { [target + ".OneToManyRelationships"] = new("Collection", 1) };
        foreach (var pair in addition.Values) actual.Add(root + pair.Key, pair.Value);
        foreach (var item in observation.GetProperty("normalizationDefaults").EnumerateObject())
        {
            var type = item.Value.GetProperty("type").GetString()!;
            var value = item.Value.GetProperty("value");
            object? pure = type switch
            {
                "null" => null, "System.Boolean" => value.GetBoolean(), "Collection" or "System.Int32" => value.GetInt32(),
                "System.Guid" => value.GetGuid(), "System.String" or "Contract" => value.GetString(), _ => value.GetInt64()
            };
            actual[root + item.Name] = new(type, pure);
        }
        return (new UiMetadataPublicationGuard.Snapshot(basis), delta, actual);
    }

    /// <summary>執行真正完整護欄的增量函式，僅解開反射包裝，原故障不轉換為成功。</summary>
    private static Dictionary<string, UiMetadataPublicationGuard.Scalar> Compute(UiMetadataPublicationGuard.Snapshot baseline,
        UiMetadataPublicationGuard.SchemaDelta delta, Dictionary<string, UiMetadataPublicationGuard.Scalar> actual)
    {
        var method = typeof(UiMetadataPublicationGuard).GetMethod("WithAdditions", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (Dictionary<string, UiMetadataPublicationGuard.Scalar>)method.Invoke(null, [baseline, delta.Additions, actual, true, CancellationToken.None])!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
}
