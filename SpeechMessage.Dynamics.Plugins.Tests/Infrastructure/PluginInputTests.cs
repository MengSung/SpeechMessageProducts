/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 PluginInput 擷取 SDK context 後的操作區域隔離契約，防止規則
 * 修改原始 Target、映像或巢狀 SDK 值。測試只使用合成 context，沒有網路或秘密。
 *
 * 主要功能與流程：建立含位元組、巢狀 Entity、Money 與參照的輸入，先擷取快照再
 * 分別改動來源及讀出值；決定性斷言確認兩端均不互相污染，並保留 Delete 目標。
 *
 * 使用邊界與維護：所有值均由單一案例擁有，沒有 static、快取、背景工作或外部
 * 資源；案例結束即失去參照。本測試不宣稱已驗證 Dataverse 的實機註冊或映像。
 */
using System;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Infrastructure;

/// <summary>保護輸入快照與 SDK context 間的深層隔離。</summary>
public sealed class PluginInputTests
{
    /// <summary>來源與讀出值雙向變更時，位元組及巢狀實體均不得回寫快照或 context。</summary>
    [Fact]
    public void Input_MutableSdkValues_AreDetachedOnCaptureAndRead()
    {
        var context = new FakePluginExecutionContext();
        var target = new Entity("account", Guid.NewGuid())
        {
            ["payload"] = new byte[] { 1, 2 },
            ["child"] = new Entity("task", Guid.NewGuid()) { ["amount"] = new Money(10m) }
        };
        context.Object.InputParameters["Target"] = target;
        context.Object.PreEntityImages["PreImage"] = target;
        var input = new PluginInput(context.Object);

        ((byte[])target["payload"])[0] = 9;
        ((Money)((Entity)target["child"])["amount"]).Value = 99m;
        var read = input.Target!;
        Assert.Equal(1, ((byte[])read["payload"])[0]);
        Assert.Equal(10m, ((Money)((Entity)read["child"])["amount"]).Value);
        ((byte[])read["payload"])[1] = 8;
        Assert.Equal(2, ((byte[])input.Target!["payload"])[1]);
        Assert.Equal(1, ((byte[])input.PreImage!["payload"])[0]);
    }

    /// <summary>Delete 的 EntityReference 必須保留穩定身分，且不能與 context 共用參照。</summary>
    [Fact]
    public void Input_DeleteReference_PreservesDetachedIdentity()
    {
        var context = new FakePluginExecutionContext(messageName: "Delete");
        var id = Guid.NewGuid();
        var reference = new EntityReference("account", id);
        context.Object.InputParameters["Target"] = reference;
        var input = new PluginInput(context.Object);

        reference.Id = Guid.NewGuid();

        Assert.NotNull(input.Target);
        Assert.Equal("account", input.Target!.LogicalName);
        Assert.Equal(id, input.Target.Id);
    }

    /// <summary>前後映像與平台路由資訊分開擷取；來源變更不得污染已建立的快照。</summary>
    [Fact]
    public void Input_ImagesAndExecutionMetadata_PreserveSnapshot()
    {
        var context = new FakePluginExecutionContext(depth: 2);
        context.Mock.SetupGet(x => x.OrganizationName).Returns("synthetic-org");
        context.Mock.SetupGet(x => x.Stage).Returns(40);
        context.Mock.SetupGet(x => x.Mode).Returns(1);
        var pre = new Entity("account", Guid.NewGuid()) { ["versionnumber"] = 1L };
        var post = new Entity("account", pre.Id) { ["versionnumber"] = 2L };
        context.Object.PreEntityImages["PreImage"] = pre;
        context.Object.PostEntityImages["PostImage"] = post;

        var input = new PluginInput(context.Object);
        pre["versionnumber"] = 9L;
        post["versionnumber"] = 9L;

        Assert.Equal(1L, input.PreImage!["versionnumber"]);
        Assert.Equal(2L, input.PostImage!["versionnumber"]);
        Assert.Equal(2, input.Depth);
        Assert.Equal(40, input.Stage);
        Assert.Equal(1, input.Mode);
        Assert.Equal("synthetic-org", input.OrganizationName);
    }
}
