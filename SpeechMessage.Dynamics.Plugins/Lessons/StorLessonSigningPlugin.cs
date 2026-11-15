/*
 * 檔案整體說明
 *
 * 目的與定位：P5-07 課程簽核的 PostOperation Plugin 薄外殼，取代舊
 * StorLessonSigningLibrary 的 static／寫死收件者入口。Create 通知課程本人，
 * Update 通知本人所屬小組的正式組長；訊息組合委派 GoodShepherd.Core，
 * 收件者、教會、語系、Token 與傳輸委派共用 MemberNotificationService。
 *
 * 主要功能與流程：只接受 new_stor_lessons 的 Create／Update、PostOperation、
 * 非同步模式及舊入口 Depth 大於 1 的返回政策；重讀最小來源列、課程圖片、
 * member→list→leader 正式 lookup 及來源 account，先以伺服器資料決定收件者，
 * 再在 Shadow 或 Live 模式組合一次課程簽核 Flex。Live 的 secure key 只在
 * 單次 Execute 內解碼並 finally 清零，Shadow 不解密、不推播。
 *
 * 使用邊界與維護：Plugin 欄位只有不可變外殼設定；SDK、Entity、Token、context
 * 及 recorder 均屬單次執行。不得以組織名稱、DEVELOPER_LINE_ID、資料列 token
 * 或 caller 提供的教會值回落；lookup 缺失、錯表或圖片 URL 非 HTTPS 直接拒絕。
 * 不建立 Session、cache、timer、背景 Task 或自有網路連線；共用通知 source
 * 由 Core 於單次通知排空後 Dispose。
 *
 * 驗證方式：離線 Shadow shell 測試覆蓋 Create／Update、正式 recipient chain、
 * Flex action 及零真寫；Live 只驗 secure key 邊界，不代表 goodshepherdback
 * 已啟用或已完成實機影子驗收。
 */
using System;
using System.Threading;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;

namespace SpeechMessage.Dynamics.Plugins.Lessons;

/// <summary>一次執行的課程簽核通知入口，不保存任何來源資料或 credential。</summary>
public sealed class StorLessonSigningPlugin : PluginShellBase
{
    /// <summary>建立固定模式及 Depth 大於一返回的舊入口政策，不預讀任何資料或 credential。</summary>
    public StorLessonSigningPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(1)) { }

    /// <summary>
    /// 依舊 Plugin 的硬性參數 Create=新增、Update=更新呼叫共用分派，平台來源 ID
    /// 與使用者服務維持授權邊界；借用 key 在通知同步排空後 finally 清零。
    /// </summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_stor_lessons" || input.MessageName is not ("Create" or "Update")) return;
        if (input.Stage != 40 || input.Mode != 1)
            throw new InvalidPluginExecutionException("Lesson signing requires asynchronous PostOperation.");
        byte[]? key = null;
        try
        {
            if (!services.IsShadow) key = DecodeKey(Configuration.SecureConfig);
            PluginServices.Wait(LessonSigningDispatch.NotifyAsync(services.Organization, new LinePushClient(), input.PrimaryEntityId,
                input.OrganizationId, input.MessageName, LessonSigningDispatch.DefaultParameter(input.MessageName),
                services.IsShadow ? services.ShadowPush : null, key, CancellationToken.None));
        }
        finally { if (key is not null) Array.Clear(key, 0, key.Length); }
    }

    /// <summary>Live 專用 64-byte secure key，失敗資料先清零且不回顯秘密；成功由呼叫端 finally 清零。</summary>
    private static byte[] DecodeKey(string secure)
    {
        if (string.IsNullOrWhiteSpace(secure) || secure.Length > 128)
            throw new InvalidPluginExecutionException("Lesson signing secure configuration is invalid.");
        byte[] key;
        try { key = Convert.FromBase64String(secure); }
        catch (FormatException) { throw new InvalidPluginExecutionException("Lesson signing secure configuration is invalid."); }
        if (key.Length == 64) return key;
        Array.Clear(key, 0, key.Length);
        throw new InvalidPluginExecutionException("Lesson signing secure configuration is invalid.");
    }
}
