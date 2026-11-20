/*
檔案整體說明
目的與定位：P7切換計畫的純離線驗證，以合成步驟／舊活動／流程快照保護操作順序、互斥與不移植流程保留契約。
主要流程：每案建立獨立候選，對缺DLL、缺證據及新舊同模組衝突注入故障；計畫只能輸出固定佔位與metadata。
資源與隔離：不連D365、不執行任何舊程式或流程；假組織與假hash不是實機證據。純值沒有Dispose資源。
決定性斷言：檢查真正planner的操作種類／ID與拒絕行為，未移植或混合活動的流程絕不能出現RepointWorkflow。
*/
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Switching;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以操作集合而非模擬器呼叫計數驗證計畫的安全結果。</summary>
public sealed class SwitchPlanTests
{
    private static readonly string[] LiveOrder = { "SetStepConfiguration", "DisableLegacyStep", "RepointWorkflow", "EnableStep" };
    /// <summary>停舊與改指完成後才啟新；安全設定必須在新步驟停用期間準備。</summary>
    [Fact]
    public void SwitchPlan_ToLive_OrdersConfigLegacyWorkflowEnable()
    {
        var fixture = Fixture();
        var plan = SwitchPlanner.Create(fixture, "live");
        Assert.Equal(LiveOrder, plan.Operations.Select(op => op.Kind));
        Assert.False(plan.Operations.Single(op => op.Kind == "RepointWorkflow").Enabled);
    }

    /// <summary>dormant維持停用與Shadow，即使組織目標Live；既有Live設定會安排清除。</summary>
    [Fact]
    public void SwitchPlan_DormantTier_StaysDisabledShadow()
    {
        var fixture = Fixture();
        fixture = fixture with { Steps = Array.AsReadOnly(new[] { fixture.Steps[0] with
        { Registration = fixture.Steps[0].Registration with { Legacy = Array.Empty<LegacyStepReference>(), Live = new StepLiveRegistration { Tier = "dormant", Reason = "P7-PROP-006" } }, Configuration = "{\"mode\":\"Live\"}" } }), LegacySteps = Array.Empty<SwitchLegacyStep>(), Workflows = Array.Empty<SwitchWorkflow>() };
        var plan = SwitchPlanner.Create(fixture, "live");
        Assert.DoesNotContain(plan.Operations, op => op.Kind == "EnableStep");
        var config = Assert.Single(plan.Operations);
        Assert.Equal("{\"mode\":\"Shadow\"}", config.Configuration);
        Assert.Equal("", config.SecureConfiguration);
        Assert.Contains("P7-PROP-006", Assert.Single(plan.Dormant), StringComparison.Ordinal);
    }

    /// <summary>原本新舊同模組都啟用必須拒絕；不能用後續停舊假裝原況合法。</summary>
    [Fact]
    public void SwitchPlan_LegacyAndNewSameModule_NeverBothEnabled()
    {
        var fixture = Fixture();
        fixture = fixture with { Steps = Array.AsReadOnly(new[] { fixture.Steps[0] with { Enabled = true } }) };
        Assert.Throws<InvalidDataException>(() => SwitchPlanner.Create(fixture, "live"));
    }

    /// <summary>只含dormant舊步驟的模組保持舊狀態，列KeepLegacy供決定，不自動停用或啟用。</summary>
    [Fact]
    public void SwitchPlan_LegacyEnabledForDormantItem_KeptAndListedForDecision()
    {
        var fixture = Fixture();
        fixture = fixture with { Steps = Array.AsReadOnly(new[] { fixture.Steps[0] with { Registration = fixture.Steps[0].Registration with
            { Live = new StepLiveRegistration { Tier = "dormant", Reason = "P7-PROP-006" } } } }), Workflows = Array.Empty<SwitchWorkflow>() };
        var operation = Assert.Single(SwitchPlanner.Create(fixture, "live").Operations);
        Assert.Equal("KeepLegacy", operation.Kind);
        Assert.Equal(fixture.LegacySteps[0].Id, operation.Id);
    }

    /// <summary>合成production的有效live項目沒有pass證據，產生計畫就拒絕，不留給執行時才發現。</summary>
    [Fact]
    public void SwitchPlan_ProductionLiveWithoutEvidence_Rejected()
    {
        var fixture = Fixture();
        fixture = fixture with { Organization = fixture.Organization with { Kind = "production" } };
        Assert.Throws<InvalidDataException>(() => SwitchPlanner.Create(fixture, "live"));
    }

    /// <summary>plan只含固定TokenKey佔位；沒有讀取秘密的委派，無法把實值寫進計畫。</summary>
    [Fact]
    public void SwitchPlan_LiveTokenStep_UsesPlaceholderNeverValue()
    {
        var plan = SwitchPlanner.Create(Fixture(), "live");
        Assert.Equal("${secret:TokenKey}", plan.Operations.Single(op => op.Kind == "SetStepConfiguration").SecureConfiguration);
    }

    /// <summary>缺組件或內容不符都必須先部署；即使其餘操作合法也不能產計畫。</summary>
    [Fact]
    public void SwitchPlan_AssemblyNotDeployedOrContentDiffers_Rejected()
    {
        var fixture = Fixture();
        foreach (var remote in new[] { "", new string('B', 64) })
            Assert.Throws<InvalidDataException>(() => SwitchPlanner.Create(fixture with
                { Assemblies = Array.AsReadOnly(new[] { fixture.Assemblies[0] with { RemoteSha256 = remote } }) }, "live"));
    }

    /// <summary>同流程有live與dormant舊活動時整體保留，不能僅改其中可切的活動。</summary>
    [Fact]
    public void SwitchPlan_WorkflowMixingRepointAndKeptActivities_ListedAsKeepLegacy()
    {
        var fixture = Fixture();
        var kept = new ActivityRegistration { Module = "care", Type = "New.Kept", Live = new LiveRegistration { Tier = "dormant", Reason = "P7-PROP-006" },
            Legacy = new[] { new LegacyActivityReference { Assembly = "Old.Assembly", Type = "Old.Kept" } } };
        fixture = fixture with { Activities = Array.AsReadOnly(fixture.Activities.Append(new SwitchActivity(kept, "New.Assembly")).ToArray()),
            Workflows = Array.AsReadOnly(new[] { fixture.Workflows[0] with { Xaml = "<Activity><A AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/><B AssemblyQualifiedName=\"Old.Kept, Old.Assembly\"/></Activity>" } }) };
        Assert.DoesNotContain(SwitchPlanner.Create(fixture, "live").Operations, op => op.Kind == "RepointWorkflow");
        Assert.Contains(SwitchPlanner.Create(fixture, "live").Operations, op => op.Kind == "KeepLegacy" && op.Id == fixture.Workflows[0].Id);
    }

    /// <summary>未移植JoinMainSmallGroupList活動即使停用且同時含可改指活動，整流程完全KeepLegacy。</summary>
    [Fact]
    public void SwitchPlan_WorkflowReferencingUnportedActivity_ListedAsKeepLegacy()
    {
        var fixture = Fixture();
        fixture = fixture with { Workflows = Array.AsReadOnly(new[] { fixture.Workflows[0] with
        { Xaml = "<Activity><A AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/><B AssemblyQualifiedName=\"JoinMainSmallGroupList.JoinMainSmallGroupListPlugIn, JoinMainSmallGroupList\"/></Activity>" } }) };
        var plan = SwitchPlanner.Create(fixture, "live");
        Assert.Contains(plan.Operations, op => op.Kind == "KeepLegacy" && op.Id == fixture.Workflows[0].Id);
        Assert.DoesNotContain(plan.Operations, op => op.Kind == "RepointWorkflow");
    }

    /// <summary>catalog仍off而plan目標Live，verify --plan必須按目標判定，不受catalog期望蓋過。</summary>
    [Fact]
    public void Verify_WithSwitchPlan_ComparesAgainstPlanTarget()
    {
        var source = Fixture();
        var approved = SwitchPlanner.Create(source, "live");
        var current = source with { Steps = Array.AsReadOnly(new[] { source.Steps[0] with { Enabled = true,
            Configuration = "{\"mode\":\"Live\"}", SecureId = Guid.NewGuid() } }),
            LegacySteps = Array.AsReadOnly(new[] { source.LegacySteps[0] with { Enabled = false } }),
            Workflows = Array.Empty<SwitchWorkflow>() };
        Assert.Empty(SwitchPlanner.Verify(current, approved));
        Assert.NotEmpty(SwitchPlanner.Verify(current));
    }

    /// <summary>catalog期望Shadow但實況停用時須報狀態差異，不用manifestoff假裝一致。</summary>
    [Fact]
    public void Verify_OrganizationsState_MismatchReportsDifference()
    {
        var source = Fixture();
        source = source with { Organization = source.Organization with { SwitchState = "shadow" },
            LegacySteps = Array.AsReadOnly(new[] { source.LegacySteps[0] with { Enabled = false } }), Workflows = Array.Empty<SwitchWorkflow>() };
        Assert.Contains(SwitchPlanner.Verify(source), op => op.Kind == "EnableStep");
    }

    /// <summary>每次建立獨立純值候選，正式ID合成但穩定；不使用真組織或任何真實憑證。</summary>
    internal static SwitchSnapshot Fixture()
    {
        var oldId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var step = new StepRegistration { Module = "care", Type = "New.Plugin", UnsecureConfig = "{\"mode\":\"Shadow\"}",
            Live = new StepLiveRegistration { Tier = "live", TokenKey = true },
            Legacy = new[] { new LegacyStepReference { Assembly = "Old.Assembly", Type = "Old.Plugin", StepId = oldId.ToString("D") } } };
        var activity = new ActivityRegistration { Module = "care", Type = "New.Activity", Live = new LiveRegistration(),
            Legacy = new[] { new LegacyActivityReference { Assembly = "Old.Assembly", Type = "Old.Activity" } } };
        return new SwitchSnapshot(new OrganizationDefinition("synthetic", "test", "synthetic.invalid", "off"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"), "manifest", "catalog", "fingerprint",
            Array.AsReadOnly(new[] { new SwitchAssembly("New.Assembly", new string('A', 64), new string('A', 64)) }),
            Array.AsReadOnly(new[] { new SwitchStep(Guid.Parse("00000000-0000-0000-0000-000000000003"), step, false, "{\"mode\":\"Shadow\"}", null) }),
            Array.AsReadOnly(new[] { new SwitchActivity(activity, "New.Assembly") }),
            Array.AsReadOnly(new[] { new SwitchLegacyStep(oldId, "Old.Assembly", "Old.Plugin", true) }),
            Array.AsReadOnly(new[] { new SwitchWorkflow(Guid.Parse("00000000-0000-0000-0000-000000000004"), 0, "<Activity><A AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>") }),
            Array.AsReadOnly(new[] { new WorkflowActivityReference("Old.Assembly", "Old.Activity"), new WorkflowActivityReference("JoinMainSmallGroupList", "JoinMainSmallGroupList.JoinMainSmallGroupListPlugIn"), new WorkflowActivityReference("Old.Assembly", "Old.Kept") }));
    }
}
