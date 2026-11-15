/*
 * 檔案整體說明
 *
 * 目的與定位：P5-08 預約簽核的新 Dataverse Plugin 薄外殼，取代退役的
 * AppointmentPlugIn／AppointmentSigningLibrary 入口。它只負責平台 context、
 * 舊深度政策與 Shadow／Live 模式路由，業務查詢及訊息組合委派
 * GoodShepherd.Core.AppointmentSigningRule。
 *
 * 流程與邊界：只接受 appointment 的 Create／Update、PostOperation、非同步事件；
 * Depth > 1 依舊入口政策返回。Live 先驗證單次 secure key，Shadow 不讀 Token。
 * SDK、recorder、push client 與 key 都只在一次 Execute 內借用／清理，不保存
 * Session、組織資料、credential、timer 或背景工作。
 *
 * 驗證方式：離線 shell 測試檢查深度、模式、輸入隔離、Shadow 影子投影及
 * Live fail-closed；不啟用正式步驟、不宣稱 D365 或 LINE 實機送達。
 */
using System;
using System.Threading;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;

namespace SpeechMessage.Dynamics.Plugins.Appointments;

/// <summary>預約簽核 Plugin 外殼；不保存任一操作的資料或秘密。</summary>
public sealed class AppointmentPlugin : PluginShellBase
{
    /// <summary>保留退役 AppointmentPlugIn 的 Depth 大於一返回政策。</summary>
    public AppointmentPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(1)) { }

    /// <summary>將平台輸入轉成共用規則，並在 finally 清除 Live key。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "appointment" || input.MessageName is not ("Create" or "Update")) return;
        if (input.Stage != 40 || input.Mode != 1)
            throw new InvalidPluginExecutionException("Appointment signing requires asynchronous PostOperation.");
        byte[]? key = null;
        try
        {
            if (!services.IsShadow) key = DecodeKey(Configuration.SecureConfig);
            PluginServices.Wait(AppointmentSigningRule.NotifyAsync(services.Organization, new LinePushClient(),
                input.PrimaryEntityId, input.OrganizationId, input.MessageName,
                AppointmentSigningRule.DefaultParameter(input.MessageName),
                services.IsShadow ? services.ShadowPush : null, key, CancellationToken.None));
        }
        finally
        {
            if (key is not null) Array.Clear(key, 0, key.Length);
        }
    }

    /// <summary>只接受 64-byte Base64 secure key；錯誤時不回顯組態或解碼內容。</summary>
    private static byte[] DecodeKey(string secure)
    {
        if (string.IsNullOrWhiteSpace(secure) || secure.Length > 128)
            throw new InvalidPluginExecutionException("Appointment signing secure configuration is invalid.");
        byte[] key;
        try { key = Convert.FromBase64String(secure); }
        catch (FormatException) { throw new InvalidPluginExecutionException("Appointment signing secure configuration is invalid."); }
        if (key.Length == 64) return key;
        Array.Clear(key, 0, key.Length);
        throw new InvalidPluginExecutionException("Appointment signing secure configuration is invalid.");
    }
}
