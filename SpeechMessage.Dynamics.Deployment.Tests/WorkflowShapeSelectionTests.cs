/*
檔案整體說明
目的與定位：驗證 workflow-shape 從同一份已完整核對的 snapshot 選取未啟用流程，不另外查詢可能漂移的第二份清單。
主要流程：建立純合成流程列，混合草稿、暫停、啟用、零參照與多參照定義，再確認只返回唯一 ActivityReference 的脫敏投影。
依賴與邊界：測試僅呼叫 Deployment 內部純選擇器與合成 XAML，不建立 SDK、不連線組織、不讀取或輸出任何真實流程內容。
資源與隔離：資料均為測試方法區域不可變字串及純值 record，沒有連線、檔案、背景工作或需釋放的資源。
驗證方式：決定性斷言排除啟用流程、未知活動、多重活動及非舊參照；測試結果只證明來源選擇邏輯，不代替 goodshepherdback 唯讀取樣。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SpeechMessage.Dynamics.Deployment.Live;
using SpeechMessage.Dynamics.Deployment.Switching;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護 workflow-shape 只從已核對 snapshot 選取未啟用單一活動參照流程的契約。</summary>
public sealed class WorkflowShapeSelectionTests
{
    /// <summary>舊流程可有其他活動參照；只投影唯一命中已註冊舊自訂活動的節點與其祖先路徑。</summary>
    [Fact]
    public void WorkflowShape_SelectsUniqueLegacyReferenceFromMultiReferenceWorkflow()
    {
        var expected = Guid.NewGuid();
        var workflows = new[]
        {
            new SwitchWorkflow(expected, 0,
                "<Activity xmlns:x=\"urn:test:xaml\"><Sequence><ActivityReference AssemblyQualifiedName=\"Other.Activity, Other.Assembly\"/><ActivityReference AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"><ActivityReference.Arguments><InArgument x:Key=\"FixedInput\">literal</InArgument><InArgument x:Key=\"RecordInput\">[entity.attribute]</InArgument></ActivityReference.Arguments></ActivityReference></Sequence></Activity>")
        };
        WorkflowActivityReference[] legacy = [new("Old.Assembly", "Old.Activity")];

        var selected = DeploymentCli.SelectWorkflowShapeSource(workflows, legacy, legacy);

        Assert.Equal(expected, selected.Workflow.Id);
        Assert.Equal(1, XDocument.Parse(selected.Shape).Descendants().Count(element => element.Name.LocalName == "ActivityReference"));
        Assert.DoesNotContain("Other.Activity", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("Other.Assembly", selected.Shape, StringComparison.Ordinal);
        Assert.Contains("[FIXED_VALUE]", selected.Shape, StringComparison.Ordinal);
        Assert.Contains("[RECORD_FIELD]", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("literal", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("entity.attribute", selected.Shape, StringComparison.Ordinal);
    }

    /// <summary>身份診斷沿既有 repointer 支援 CLR namespace XAML 型別，不把此格式誤判為缺少身份。</summary>
    [Fact]
    public void WorkflowShape_IdentityDiagnosticsRecognizeClrNamespaceReference()
    {
        const string xaml = "<Activity xmlns:legacy=\"clr-namespace:Old.Namespace;assembly=Old.Assembly\"><legacy:OldActivity /></Activity>";
        WorkflowActivityReference[] legacy = [new("Old.Assembly", "Old.Namespace.OldActivity")];

        var match = WorkflowActivityShapeProjector.CompareLegacyActivityIdentity(xaml, legacy);

        Assert.True(match.HasIdentity);
        Assert.True(match.TypeMatched);
        Assert.True(match.AssemblyMatched);
        Assert.False(match.ReversedMatched);
        Assert.True(match.RegisteredMatch);
    }

    /// <summary>診斷逐階段計數可區分型別、組件或欄位方向不符，且只輸出數量而不暴露身份文字。</summary>
    [Fact]
    public void WorkflowShape_CountsIdentityMatchStagesWithoutExposingValues()
    {
        var workflows = new[]
        {
            new SwitchWorkflow(Guid.NewGuid(), 0, "<Activity><ActivityReference AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>"),
            new SwitchWorkflow(Guid.NewGuid(), 0, "<Activity><ActivityReference AssemblyQualifiedName=\"Old.Activity, Other.Assembly\"/></Activity>"),
            new SwitchWorkflow(Guid.NewGuid(), 0, "<Activity><ActivityReference AssemblyQualifiedName=\"Other.Activity, Old.Assembly\"/></Activity>"),
            new SwitchWorkflow(Guid.NewGuid(), 0, "<Activity><ActivityReference AssemblyQualifiedName=\"Old.Assembly, Old.Activity\"/></Activity>"),
            new SwitchWorkflow(Guid.NewGuid(), 0, "<Activity><ActivityReference /></Activity>")
        };
        WorkflowActivityReference[] legacy = [new("Old.Assembly", "Old.Activity")];

        var counts = DeploymentCli.CountWorkflowShapeCandidates(workflows, legacy);

        Assert.Equal((5, 5, 1, 0, 0, 0, 1, 4, 2, 2, 1, 1), counts);
        Assert.DoesNotContain("Old.Activity", counts.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Old.Assembly", counts.ToString(), StringComparison.Ordinal);
    }

    /// <summary>候選來源排除啟用、零參照及多參照流程，並只回傳已脫敏的結構投影。</summary>
    [Fact]
    public void WorkflowShape_SelectsDisabledLegacyWorkflowFromSnapshot()
    {
        var expected = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var workflows = new[]
        {
            new SwitchWorkflow(Guid.Parse("00000000-0000-0000-0000-000000000001"), 2,
                "<Activity xmlns:x=\"urn:test:xaml\"><ActivityReference AssemblyQualifiedName=\"Old.Activity, Old.Assembly, Version=1.0.0.0\"><InArgument x:Key=\"FixedInput\">fixed</InArgument><InArgument x:Key=\"RecordInput\">[entity.attribute]</InArgument></ActivityReference></Activity>"),
            new SwitchWorkflow(Guid.Parse("00000000-0000-0000-0000-000000000002"), 0,
                "<Activity><ActivityReference AssemblyQualifiedName=\"Other.Activity, Other.Assembly, Version=1.0.0.0\"/></Activity>"),
            new SwitchWorkflow(expected, 1,
                "<Activity><ActivityReference AssemblyQualifiedName=\"Old.Activity, Old.Assembly, Version=1.0.0.0\"/></Activity>"),
            new SwitchWorkflow(Guid.Parse("00000000-0000-0000-0000-000000000004"), 0,
                "<Activity><ActivityReference AssemblyQualifiedName=\"Old.Activity, Old.Assembly, Version=1.0.0.0\"/><ActivityReference AssemblyQualifiedName=\"Old.Activity, Old.Assembly, Version=1.0.0.0\"/></Activity>")
        };
        WorkflowActivityReference[] legacy = [new("Old.Assembly", "Old.Activity")];

        var counts = DeploymentCli.CountWorkflowShapeCandidates(workflows, legacy);
        var selected = DeploymentCli.SelectWorkflowShapeSource(workflows, legacy, legacy);

        Assert.Equal((4, 3, 1, 1, 1, 1, 1, 3, 2, 2, 0, 2), counts);
        Assert.Equal(expected, selected.Workflow.Id);
        Assert.Contains("ActivityReference", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("Old.Activity", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("Old.Assembly", selected.Shape, StringComparison.Ordinal);
    }

    /// <summary>指定新活動時只可選取其清單明確對應的舊活動流程，不能被其他已登錄活動搶先匹配。</summary>
    [Fact]
    public void WorkflowShape_SelectsOnlyRequestedActivityLegacyMapping()
    {
        var expected = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var unrelated = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var workflows = new[]
        {
            new SwitchWorkflow(unrelated, 0,
                "<Activity xmlns:x=\"urn:test:xaml\"><ActivityReference AssemblyQualifiedName=\"Other.Activity, Other.Assembly\"><InArgument x:Key=\"FixedInput\">fixed</InArgument><InArgument x:Key=\"RecordInput\">[entity.attribute]</InArgument></ActivityReference></Activity>"),
            new SwitchWorkflow(expected, 0,
                "<Activity xmlns:x=\"urn:test:xaml\"><ActivityReference AssemblyQualifiedName=\"Requested.Activity, Requested.Assembly\"><InArgument x:Key=\"FixedInput\">fixed</InArgument><InArgument x:Key=\"RecordInput\">[entity.attribute]</InArgument></ActivityReference></Activity>")
        };
        WorkflowActivityReference[] allLegacy =
        [
            new("Other.Assembly", "Other.Activity"),
            new("Requested.Assembly", "Requested.Activity")
        ];
        WorkflowActivityReference[] requestedLegacy = [new("Requested.Assembly", "Requested.Activity")];

        var selected = DeploymentCli.SelectWorkflowShapeSource(workflows, allLegacy, requestedLegacy);

        Assert.Equal(expected, selected.Workflow.Id);
        Assert.DoesNotContain("Other.Activity", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("Other.Assembly", selected.Shape, StringComparison.Ordinal);
    }

    /// <summary>雙繫結形狀不足時仍可輸出指定活動的脫敏診斷候選，但不得把它當成 golden。</summary>
    [Fact]
    public void WorkflowShape_DiagnosticSelectionReturnsSanitizedIncompleteCandidate()
    {
        var expected = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var workflows = new[]
        {
            new SwitchWorkflow(expected, 0,
                "<Activity xmlns:x=\"urn:test:xaml\"><ActivityReference AssemblyQualifiedName=\"Requested.Activity, Requested.Assembly\"><ActivityReference.Arguments><InArgument x:Key=\"RecordInput\">[entity.sensitivefield]</InArgument></ActivityReference.Arguments></ActivityReference></Activity>")
        };
        WorkflowActivityReference[] legacy = [new("Requested.Assembly", "Requested.Activity")];

        var selected = DeploymentCli.SelectWorkflowShapeDiagnosticSource(workflows, legacy, legacy);

        Assert.Equal(expected, selected.Workflow.Id);
        Assert.Contains("[RECORD_FIELD]", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitivefield", selected.Shape, StringComparison.Ordinal);
        Assert.DoesNotContain("Requested.Activity", selected.Shape, StringComparison.Ordinal);
    }
}
