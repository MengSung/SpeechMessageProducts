/*
 * 檔案整體說明
 *
 * 目的與定位：提供活動外殼離線契約測試使用的 IWorkflowContext，以 Moq 建立
 * 每次 WorkflowInvoker 執行自己的組織、使用者、記錄、事件與參數集合。
 * 本檔不模擬 Dataverse 授權、工作流程持久化或實機 plugintracelog 行為。
 *
 * 主要功能與流程：建構式設定平台共同執行欄位及獨立參數／映像集合，Object
 * 作為 WorkflowInvoker 的 extension；Mock 保留案例注入特殊讀取故障的入口。
 * 不把 IPluginExecutionContext 偽裝為活動 context，也不共用其他案例的集合。
 *
 * 使用邊界與維護：只接受合成識別與資料，不含 credential、Session、網路連線、
 * 計時器、取消註冊或背景工作。案例 owner 在同步 Invoke 返回後丟棄本物件，
 * 測試 fixture 收尾時重設 Mock，釋放捕捉的集合；不能將替身保存到 static。
 * 外殼測試驗證輸入 scope 與記錄器排空，不能將這些結果宣稱為實機驗收。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>
/// 單次活動執行的合成平台內容；所有集合屬本實例，沒有跨使用者或組織的共用狀態。
/// 呼叫端應建立新的替身表示另一個 scope，不能中途改寫同一替身模擬隔離。
/// </summary>
internal sealed class FakeWorkflowContext
{
    /// <summary>
    /// 建立具有完整共同執行欄位的合成活動內容；未指定的識別各自產生，
    /// 不提供任何正式組織身分或授權保證。參數與映像集合只保留至案例收尾。
    /// </summary>
    public FakeWorkflowContext(
        int depth = 1,
        string messageName = "Create",
        string primaryEntityName = "task",
        Guid? primaryEntityId = null,
        Guid? correlationId = null,
        Guid? organizationId = null,
        Guid? userId = null,
        string organizationName = "synthetic-workflow")
    {
        var executionUser = userId ?? Guid.NewGuid();
        Mock = new Mock<IWorkflowContext>(MockBehavior.Loose);
        Mock.SetupGet(x => x.Depth).Returns(depth);
        Mock.SetupGet(x => x.MessageName).Returns(messageName);
        Mock.SetupGet(x => x.PrimaryEntityName).Returns(primaryEntityName);
        Mock.SetupGet(x => x.PrimaryEntityId).Returns(primaryEntityId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.CorrelationId).Returns(correlationId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.OrganizationId).Returns(organizationId ?? Guid.NewGuid());
        Mock.SetupGet(x => x.OrganizationName).Returns(organizationName);
        Mock.SetupGet(x => x.UserId).Returns(executionUser);
        Mock.SetupGet(x => x.InitiatingUserId).Returns(executionUser);
        Mock.SetupGet(x => x.Mode).Returns(0);
        Mock.SetupGet(x => x.IsolationMode).Returns(2);
        Mock.SetupGet(x => x.InputParameters).Returns(new ParameterCollection());
        Mock.SetupGet(x => x.OutputParameters).Returns(new ParameterCollection());
        Mock.SetupGet(x => x.SharedVariables).Returns(new ParameterCollection());
        Mock.SetupGet(x => x.PreEntityImages).Returns(new EntityImageCollection());
        Mock.SetupGet(x => x.PostEntityImages).Returns(new EntityImageCollection());
    }

    /// <summary>只供本案例配置故障；fixture 在 Invoke 排空後重設以解除捕捉參照。</summary>
    public Mock<IWorkflowContext> Mock { get; }

    /// <summary>供活動 extension 借用的平台介面，不持有外部服務或可釋放資源。</summary>
    public IWorkflowContext Object => Mock.Object;
}
