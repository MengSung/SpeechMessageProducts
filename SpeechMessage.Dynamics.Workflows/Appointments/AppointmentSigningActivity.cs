/*
 * 檔案整體說明
 *
 * 目的與定位：P5-08 預約簽核的新版 CodeActivity 外殼，保留退役活動的
 * InputParameter、InputAppointment 與 OutputResult 名稱／型別，並由
 * ActivityShellBase 提供選填 ExecutionMode。
 *
 * 主要功能與流程：活動只在本次 CodeActivityContext 求值引數，驗證可省略的
 * appointment 參照與平台主記錄相符，再把純值交共用規則。Shadow 才記錄通知
 * 意圖；Live 依 P5-PROP-006 在任何安全金鑰查詢前固定拒絕。活動實例不保存
 * context、SDK、Entity、token、Session、cache 或背景工作。
 *
 * 維護與驗證：離線 WorkflowInvoker／CAS 測試檢查引數相容、A/B/A 隔離、Shadow
 * 影子記錄與 Live fail-closed；不宣稱活動流程已在 D365 實機送達。
 */
using System;
using System.Activities;
using System.Threading;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;

namespace SpeechMessage.Dynamics.Workflows.Appointments;

/// <summary>預約簽核活動；只保留 WF expression，不保留執行資料。</summary>
public sealed class AppointmentSigningActivity : ActivityShellBase
{
    /// <summary>保留舊活動的輸入參數；只供固定業務分派，不提供授權。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>保留舊活動的可省略來源 appointment 參照，提供一致性檢查。</summary>
    [Input("InputAppointment")]
    [ReferenceTarget("appointment")]
    public InArgument<EntityReference> InputAppointment { get; set; } = new();

    /// <summary>保留舊輸出宣告；未把預設零解釋為已送達。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>Live 在任何 secure key 查詢前拒絕；Shadow 才使用當次 recorder。</summary>
    internal override void ExecuteWithArguments(CodeActivityContext context, ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "appointment" ||
            input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        if (!services.IsShadow)
            throw new InvalidPluginExecutionException("P5-PROP-006: Appointment signing activity Live notifications are not implemented.");
        var parameter = InputParameter.Get(context);
        var target = InputAppointment.Get(context);
        if (target is not null && (target.LogicalName != "appointment" ||
            target.Id == Guid.Empty || target.Id != input.PrimaryEntityId || target.KeyAttributes.Count != 0))
            throw new InvalidPluginExecutionException("Appointment signing activity source is invalid.");
        PluginServices.Wait(AppointmentSigningRule.NotifyAsync(services.Organization, new LinePushClient(),
            input.PrimaryEntityId, input.OrganizationId, input.MessageName, parameter,
            services.ShadowPush!, null, CancellationToken.None));
    }

    /// <summary>沒有當次 WF context 無法安全求值 InputParameter，因此固定拒絕。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services) =>
        throw new InvalidPluginExecutionException("Appointment signing activity requires evaluated workflow arguments.");
}
