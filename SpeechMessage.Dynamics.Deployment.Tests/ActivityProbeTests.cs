/*
檔案整體說明
目的與定位：驗證 P5-03b 活動探針的 XML 精準改寫與流程清理契約，所有流程定義與平台委派均為合成資料。
主要功能與流程：確認只替換核定舊活動參照、不改其他 XAML；故障發生於執行中時，測試流程 owner 必須依停用後刪除順序清理。
使用邊界與維護：不連 D365、不執行 XAML、不保存活動輸入或跨案例狀態；每個案例以本地計數器驗證確定性清理，清理失敗仍透過固定例外回報。
*/
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>活動探針的離線改寫與清理回歸案例。</summary>
public sealed class ActivityProbeTests
{
    private static readonly string[] ExpectedCleanupOrder = { "disable", "delete" };

    /// <summary>只改 AssemblyQualifiedName，保留流程名稱、其他活動與自訂屬性。</summary>
    [Fact]
    public void ActivityProbe_RewritesOnlyActivityReference()
    {
        const string source = """
            <Activity x:Class="SMV-TEST-Legacy" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Sequence DisplayName="保留">
                <OldNode AssemblyQualifiedName="Old.Activity, Old.Assembly" Other="unchanged" />
                <OtherNode AssemblyQualifiedName="Other.Activity, Other.Assembly" />
              </Sequence>
            </Activity>
            """;

        var result = ActivityProbe.RewriteOnlyActivityReference(
            source, "Old.Assembly", "Old.Activity", "New.Assembly", "New.Activity");

        Assert.Contains("New.Activity, New.Assembly", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Old.Activity, Old.Assembly", result, StringComparison.Ordinal);
        Assert.Contains("Other.Activity, Other.Assembly", result, StringComparison.Ordinal);
        Assert.Contains("Other=\"unchanged\"", result, StringComparison.Ordinal);
        Assert.Contains("SMV-TEST-Legacy", result, StringComparison.Ordinal);
    }

    /// <summary>執行中途失敗仍必須停用後刪除流程，並將原始執行例外保留給呼叫端。</summary>
    [Fact]
    public void ActivityProbe_FailureMidRun_RemovesTestProcess()
    {
        var calls = new List<string>();
        using var owner = new ProbeProcessOwner(
            () => calls.Add("disable"),
            () => calls.Add("delete"));
        owner.MarkCreated();

        var error = Assert.Throws<InvalidOperationException>(() => owner.Execute(
            () => throw new InvalidOperationException("synthetic-execution-failure")));

        Assert.Equal("synthetic-execution-failure", error.Message);
        Assert.Equal(ExpectedCleanupOrder, calls);
        owner.Dispose();
        Assert.Equal(ExpectedCleanupOrder, calls);
    }
}
