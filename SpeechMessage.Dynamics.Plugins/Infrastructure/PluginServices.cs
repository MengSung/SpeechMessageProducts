/*
 * 檔案整體說明
 *
 * 目的與定位：集中一次 Plugin 規則執行可使用的服務與 Shadow sink，明確標示
 * service 的借用／所有權邊界。此型別本身不擁有 Dataverse service，也不啟動
 * 背景工作；Live 的借用 service 由 Dataverse 擁有，Shadow recorder 由外殼於
 * Execute 期間建立並在 finally 後遺失。
 *
 * 主要功能與流程：PluginShellBase 依模式建立本型別，規則使用 Organization、
 * ShadowPush 與模式旗標；Wait 以單一同步邊界等待規則產生的 Task，避免各規則
 * 自行使用 `.Result` 而形成未排空工作。
 *
 * 使用邊界與維護：不得把本型別、SDK service 或 sink 存進 static、Session、
 * cache 或 queue。取消與逾時由規則／Core 的既有契約處理；本型別不另建 timer
 * 或 cancellation registration。
 */
using System;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>供規則使用的一次執行服務集合。</summary>
internal sealed class PluginServices
{
    /// <summary>建立服務集合；organization 由本次 Execute 借用。</summary>
    public PluginServices(IOrganizationService organization, ShadowPushRecorder? shadowPush, bool isShadow)
    {
        Organization = organization ?? throw new ArgumentNullException(nameof(organization));
        ShadowPush = shadowPush;
        IsShadow = isShadow;
        if (isShadow && shadowPush is null) throw new ArgumentNullException(nameof(shadowPush));
    }

    /// <summary>Live 真服務或 Shadow 記錄器。</summary>
    public IOrganizationService Organization { get; }
    /// <summary>Shadow 模式的推播記錄器；Live 為 null。</summary>
    public ShadowPushRecorder? ShadowPush { get; }
    /// <summary>是否為 Shadow 模式。</summary>
    public bool IsShadow { get; }

    /// <summary>以唯一等待入口排空規則的 Task，不保存工作或 continuation。</summary>
    public static void Wait(Task task)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));
        task.GetAwaiter().GetResult();
    }

    /// <summary>以唯一等待入口取得規則結果。</summary>
    public static TResult Wait<TResult>(Task<TResult> task)
    {
        if (task is null) throw new ArgumentNullException(nameof(task));
        return task.GetAwaiter().GetResult();
    }
}
