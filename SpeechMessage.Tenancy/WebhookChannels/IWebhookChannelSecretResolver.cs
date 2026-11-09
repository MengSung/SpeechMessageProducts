/*
檔案整體說明

目的與定位：定義 LINE Webhook 頻道 secret 的最小部署解析契約，供 ASP.NET Core
驗簽 adapter 借用；本檔不實作 HMAC、不保存 HTTP request，也不授權 route。

主要功能與流程：組合根先以非秘密 channelId→organization mapping 建立 resolver，
啟動時可列出已配置的 channelId 做 fail-closed 檢查；每次驗簽再向受信任設定來源
取得該頻道的短生命週期 secret。解析失敗只回 false，啟動檢查則以固定錯誤拒絕。

使用邊界與維護：介面不公開可序列化的 secret 屬性，不提供 ToString 或 cache API。
呼叫端不得把 secret 寫入 log、envelope、Session、背景佇列或跨 request 狀態；設定
來源由 Host 擁有，resolver 只借用，沒有 watcher、timer、connection 或 background
task。此契約只描述離線部署設定，不代表已完成 LINE 平台實機驗收。
*/
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpeechMessage.Tenancy;

/// <summary>
/// 以固定部署 mapping 解析 Webhook 頻道 secret；不把不受信任的 route 當作授權。
/// </summary>
public interface IWebhookChannelSecretResolver
{
    /// <summary>
    /// 啟動時已發現的非秘密 channelId 清單；清單唯讀且只包含部署 mapping 的識別。
    /// </summary>
    IReadOnlyList<string> ConfiguredChannelIds { get; }

    /// <summary>
    /// 解析本次驗簽所需的 secret；未知頻道、空值或格式錯誤均 fail closed。
    /// </summary>
    /// <param name="channelId">尚未授權的 HTTP route 頻道識別。</param>
    /// <param name="secret">成功時的本次操作借用值；呼叫端不得保存、記錄或交給背景工作。</param>
    /// <returns>目前部署有完整合法 secret 時為 true。</returns>
    bool TryResolveSecret(string channelId, [NotNullWhen(true)] out string? secret);

    /// <summary>
    /// 驗證所有已配置 mapping 的 secret 都存在且符合部署格式；失敗必須阻止 Host 接收 HTTP。
    /// </summary>
    void ValidateConfiguration();
}
