/*
 * 檔案整體說明
 *
 * 目的與定位：以 Moq 包裝 IPluginExecutionContext，集中建立單次 Plugin 測試所需
 * 的固定輸入、深度、訊息、組織與影像。它不模擬未宣告的 Dataverse 行為，也不
 * 保存跨案例狀態。
 *
 * 主要功能與流程：建構式建立 Mock 並設定必要唯讀屬性；測試可再以 Mock 直接
 * 注入特殊輸入或故障。所有 Entity／集合均由案例建立與清理，不連 D365、不保存
 * credential、Session 或背景工作。
 *
 * 使用邊界與維護：這個替身只供離線外殼測試，不能作為實機影子證據；測試應明確
 * 斷言外殼在深度、模式與例外邊界的決定性行為。
 */
using System;
using Microsoft.Xrm.Sdk;
using Moq;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>Plugin execution context 的最小可配置包裝。</summary>
internal sealed class FakePluginExecutionContext
{
    /// <summary>建立常用 context 欄位。</summary>
    public FakePluginExecutionContext(
        int depth = 1,
        string messageName = "Create",
        string primaryEntityName = "account",
        Guid? primaryEntityId = null,
        Guid? correlationId = null,
        Guid? organizationId = null,
        Guid? userId = null)
    {
        Mock = new Mock<IPluginExecutionContext>(MockBehavior.Loose);
        Mock.SetupGet(x => x.Depth).Returns(depth);
        Mock.SetupGet(x => x.MessageName).Returns(messageName);
        Mock.SetupGet(x => x.PrimaryEntityName).Returns(primaryEntityName);
        Mock.SetupGet(x => x.PrimaryEntityId).Returns(primaryEntityId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.CorrelationId).Returns(correlationId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.OrganizationId).Returns(organizationId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.UserId).Returns(userId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.InputParameters).Returns(new ParameterCollection());
        Mock.SetupGet(x => x.PreEntityImages).Returns(new EntityImageCollection());
        Mock.SetupGet(x => x.PostEntityImages).Returns(new EntityImageCollection());
    }

    /// <summary>可進一步設定的 Moq context。</summary>
    public Mock<IPluginExecutionContext> Mock { get; }
    /// <summary>供 service provider 使用的 context 介面。</summary>
    public IPluginExecutionContext Object => Mock.Object;
}
