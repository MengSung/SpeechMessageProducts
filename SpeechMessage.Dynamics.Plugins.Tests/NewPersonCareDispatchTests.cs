/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-10 NewPersonCareActivity 的實際分支接線，避免只測 enum
 * 分類卻把 ExecuteWorkflow 的出席服務留成空操作。本檔屬離線外殼整合測試。
 * 主要流程：平台 context 使用合成正式 contact；SDK 在第一次讀取注入唯一故障，
 * 逐一要求 ExecuteWorkflow、Create、Update 均抵達真 Core SDK 路徑並保留原例外。
 * 不支援訊息必須完全零 SDK 操作，不能將空操作誤列為舊入口相容完成。
 * 使用邊界與維護：每案 using 擁有 FakeOrganizationService，context Mock 於 finally
 * 重設，釋放當次集合；沒有網路、真組織、秘密、timer、背景工作或跨案例快取。
 * 真 WorkflowInvoker 與部分信任的完整 Shadow 鏈另由 CareShell/CAS 測試驗證。
 */
extern alias Workflows;
using System;
using Moq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;
using ActivityInput = Workflows::SpeechMessage.Dynamics.Workflows.Infrastructure.ActivityInput;
using PluginServices = Workflows::SpeechMessage.Dynamics.Plugins.Infrastructure.PluginServices;
using NewPersonActivity = Workflows::SpeechMessage.Dynamics.Workflows.Care.NewPersonCareActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>直接驗正式外殼的服務委派，不以相同分類函式自比作為接線證據。</summary>
public sealed class NewPersonCareDispatchTests
{
    /// <summary>真活動不得略過三個可達訊息，SDK 原例外必須原樣送回外殼邊界。</summary>
    [Theory]
    [InlineData("ExecuteWorkflow")]
    [InlineData("Create")]
    [InlineData("Update")]
    public void Care_NewPerson_DispatchesReachableBranch_OriginalFailure(string message)
    {
        using var sdk = new FakeOrganizationService();
        var context = new FakeWorkflowContext(messageName: message, primaryEntityName: "contact");
        var failure = new InvalidOperationException("Synthetic Care SDK failure.");
        sdk.RetrieveHandler = (_, _, _) => throw failure;
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                new NewPersonActivity().ExecuteRule(new ActivityInput(context.Object), new PluginServices(sdk, null, false))));
            Assert.Equal(1, sdk.Calls["Retrieve"]);
        }
        finally { context.Mock.Reset(); }
    }

    /// <summary>非舊可達訊息不得查詢、建立或更新任何資料。</summary>
    [Fact]
    public void Care_NewPerson_UnsupportedMessage_NoSdk()
    {
        using var sdk = new FakeOrganizationService();
        var context = new FakeWorkflowContext(messageName: "Delete", primaryEntityName: "contact");
        try
        {
            new NewPersonActivity().ExecuteRule(new ActivityInput(context.Object), new PluginServices(sdk, null, false));
            Assert.Empty(sdk.Calls);
        }
        finally { context.Mock.Reset(); }
    }
}
