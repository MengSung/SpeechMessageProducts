/*
 * 檔案整體說明
 * 目的與定位：P5-10 正式 Care 活動的離線外殼契約，保護 WF 參數求值、深度、
 * 例外原實例及重用活動的使用者隔離；不把 Core 純規則測試冒充活動驗收。
 * 主要流程：使用 CareCasFixture 的三列合成權威資料與真 WorkflowInvoker，逐欄比較
 * 生日 sentinel、年資及新朋友 Create 分組的完整 Shadow；故障於首次讀取注入，
 * end 計數及安全追蹤須正確，非法種類須在 SDK 讀取前拒絕，不能回顯 caller 內容。
 * 資源與隔離：每案 using 擁有 SDK、回呼及追蹤，執行同步排空後清理並驗零基線；
 * A/B/A 共用活動但不共用來源、教會或平台身分。無網路、舊 DLL、秘密或背景工作。
 * 使用邊界與維護：只證明離線平台鏈與有界反覆執行；不宣稱實機 Dataverse 或長期 soak。
 */
extern alias Workflows;
using System;
using System.Activities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using Xunit;
using AnniversaryActivity = Workflows::SpeechMessage.Dynamics.Workflows.Care.ContactAnniversaryActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>兩個實際 Care 外殼與三個明確 reading 分支的同步平台契約。</summary>
public sealed class CareShellTests
{
    /// <summary>三種分支必須真正執行完整單筆 patch，不能僅驗型別或方法存在。</summary>
    [Theory]
    [InlineData("birthday")]
    [InlineData("onboarding")]
    [InlineData("newperson")]
    public void Care_ShadowMode_MatchesExpected(string entrypoint)
    {
        using var fixture = new CareCasFixture(entrypoint);
        var result = fixture.Invoke(CareCasFixture.CreateActivity(entrypoint));
        if (entrypoint == "newperson") Assert.Equal(0, Assert.IsType<int>(result["OutputResult"]));
        fixture.Verify();
    }

    /// <summary>盤點舊分支沒有共同深度早退；多深度仍須執行，生日／年資並驗三個可達訊息。</summary>
    [Theory]
    [InlineData("birthday")]
    [InlineData("onboarding")]
    [InlineData("newperson")]
    public void Care_DepthPolicy_MatchesLegacy(string entrypoint)
    {
        var activity = CareCasFixture.CreateActivity(entrypoint);
        foreach (var depth in new[] { 1, 2, 3, 99 })
        foreach (var message in entrypoint == "newperson" ? new[] { "Create" } : new[] { "Create", "Update", "ExecuteWorkflow" })
        {
            using var fixture = new CareCasFixture(entrypoint, depth, message);
            fixture.Invoke(activity);
            fixture.Verify();
        }
    }

    /// <summary>首次權威讀取失敗應保留原例外，不把敏感訊息寫入 trace，Shadow 仍輸出零意圖結尾。</summary>
    [Theory]
    [InlineData("birthday")]
    [InlineData("onboarding")]
    [InlineData("newperson")]
    public void Care_UnexpectedError_Rethrown(string entrypoint)
    {
        using var fixture = new CareCasFixture(entrypoint);
        var expected = new InvalidOperationException("Synthetic private Care failure.");
        fixture.ReadFailure = expected;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => fixture.Invoke(CareCasFixture.CreateActivity(entrypoint))));
        Assert.Contains(fixture.Trace.Entries, line => line.Contains("ExceptionType=System.InvalidOperationException"));
        Assert.Contains(fixture.Trace.Entries, line => line.Contains("\"end\":true,\"count\":0"));
        Assert.DoesNotContain(fixture.Trace.Entries, line => line.Contains(expected.Message));
        Assert.Empty(fixture.Sdk.WriteRecords);
    }

    /// <summary>不支援訊息、大小寫與錯表應完全零讀取，不可把工廠借用當業務副作用。</summary>
    [Theory]
    [InlineData("birthday")]
    [InlineData("onboarding")]
    [InlineData("newperson")]
    public void Care_UnsupportedTrigger_NoIo(string entrypoint)
    {
        var activity = CareCasFixture.CreateActivity(entrypoint);
        foreach (var message in new[] { "Delete", "Assign", "create", "executeworkflow" })
        {
            using var fixture = new CareCasFixture(entrypoint, message: message);
            fixture.Invoke(activity);
            fixture.Verify(noOperation: true);
        }
        using var wrongTable = new CareCasFixture(entrypoint, table: "account");
        wrongTable.Invoke(activity);
        wrongTable.Verify(noOperation: true);
    }

    /// <summary>同活動依序 A/B/A，各案正式身分不同，A 回復後的完整意圖必須與第一次相同。</summary>
    [Theory]
    [InlineData("birthday")]
    [InlineData("onboarding")]
    [InlineData("newperson")]
    public void Care_ReusedActivity_ABA_Isolated(string entrypoint)
    {
        var activity = CareCasFixture.CreateActivity(entrypoint);
        using var a = new CareCasFixture(entrypoint);
        using var b = new CareCasFixture(entrypoint, depth: 7);
        a.Invoke(activity);
        var first = a.Verify();
        b.Invoke(activity);
        var second = b.Verify();
        Assert.NotEqual(a.RecordId, b.RecordId);
        Assert.NotEqual(a.ChurchId, b.ChurchId);
        Assert.False(JToken.DeepEquals(first, second));
        a.Trace.Entries.Clear(); a.ResetInvocationLease();
        a.Invoke(activity);
        Assert.True(JToken.DeepEquals(first, a.Verify()));
    }

    /// <summary>省略選填種類須只有生日兩欄；中繼資料不能偷偷變成必要引數。</summary>
    [Fact]
    public void Care_AnniversaryKind_OmittedDefaultsToBirthday()
    {
        var property = typeof(AnniversaryActivity).GetProperty("AnniversaryKind")!;
        Assert.NotNull(property);
        Assert.Equal(typeof(InArgument<string>), property.PropertyType);
        Assert.Empty(property.GetCustomAttributes(typeof(RequiredArgumentAttribute), false));
        Assert.Equal("AnniversaryKind", Assert.IsType<InputAttribute>(Assert.Single(property.GetCustomAttributes(typeof(InputAttribute), false))).Name);
        using var fixture = new CareCasFixture("birthday");
        fixture.Invoke(new AnniversaryActivity(), omitKind: true);
        fixture.Verify();
    }

    /// <summary>非法值不能觸發任何 SDK 讀取，也不能把值放進公開錯誤；All 並非舊入口種類。</summary>
    [Theory]
    [InlineData("All")]
    [InlineData("birthday")]
    [InlineData("Onboarding ")]
    [InlineData("caller-secret")]
    [InlineData("")]
    public void Care_AnniversaryKind_InvalidRejectedBeforeSdkRead(string kind)
    {
        using var fixture = new CareCasFixture("birthday");
        var failure = Assert.Throws<InvalidPluginExecutionException>(() => fixture.Invoke(new AnniversaryActivity(), kind));
        if (kind.Length != 0) Assert.DoesNotContain(kind, failure.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Sdk.Calls);
        fixture.Verify(noOperation: true);
    }

    /// <summary>同一活動先生日、再年資、再生日，求值只限當次，不能殘留前次種類或欄位。</summary>
    [Fact]
    public void Care_AnniversaryKind_ReevaluatedPerInvocation()
    {
        var activity = new AnniversaryActivity();
        foreach (var entrypoint in new[] { "birthday", "onboarding", "birthday" })
        {
            using var fixture = new CareCasFixture(entrypoint);
            fixture.Invoke(activity);
            fixture.Verify();
        }
    }

    /// <summary>二十四輪交替成功／讀取失敗後 owner 都須清除所有合成狀態及回呼，不靠 GC 排空。</summary>
    [Theory]
    [InlineData("birthday")]
    [InlineData("onboarding")]
    [InlineData("newperson")]
    public void Care_RepeatedInvocation_DrainsOwnedResources(string entrypoint)
    {
        var activity = CareCasFixture.CreateActivity(entrypoint);
        for (var iteration = 0; iteration < 24; iteration++)
        {
            using var fixture = new CareCasFixture(entrypoint);
            if (iteration % 2 == 0) { fixture.Invoke(activity); fixture.Verify(); }
            else
            {
                var expected = new InvalidOperationException("Synthetic alternating Care failure.");
                fixture.ReadFailure = expected;
                Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => fixture.Invoke(activity)));
            }
            fixture.Dispose();
            Assert.Empty(fixture.Sdk.Calls);
            Assert.Empty(fixture.Trace.Entries);
            Assert.Null(fixture.ReadFailure);
            Assert.Equal(0, fixture.Sdk.EntityCount);
            Assert.Null(fixture.Sdk.RetrieveHandler);
        }
    }
}
