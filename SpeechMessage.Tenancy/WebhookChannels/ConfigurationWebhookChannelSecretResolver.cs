/*
檔案整體說明

目的與定位：從 Host 擁有的 IConfiguration 解析 LINE Webhook 的非秘密 channel
mapping 與部署 secret，供 P3-03 驗簽器使用；不直接處理 HTTP、JSON、HMAC 或 LINE API。

主要功能與流程：建構時只 materialize 最多一千零二十四筆 channelId→organization mapping，拒絕
非法數字 channel 或設定路徑字元；驗簽時依固定 organization 讀取
Deployment:Organizations:{organization}:TestChannelSecret，驗證 32 碼 ASCII 十六進位
格式後才回傳。啟動檢查逐一驗證全部 mapping，任一缺項即固定失敗，不讀取其他頻道、
access token 或預設組織作後備。

使用邊界與維護：借用 Host 擁有的 IConfiguration，不另存秘密快照；只保存非秘密
mapping，secret 每次操作重新借用且不進公開屬性、ToString、例外或診斷。Host 擁有
設定來源，resolver 沒有 reload watcher、cache、timer、queue 或背景工作；呼叫端
必須在驗簽返回後停止使用 secret。mapping 建構完成後不接受 request 改寫，保護跨
頻道／跨組織隔離。此檔不宣稱 User Secrets 鍵存在以外的實機部署結果。
*/
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace SpeechMessage.Tenancy;

/// <summary>
/// 讀取固定部署 Webhook mapping 的無狀態 resolver；只保留非秘密組織識別。
/// </summary>
public sealed class ConfigurationWebhookChannelSecretResolver : IWebhookChannelSecretResolver
{
    private const string ChannelSection = "LineWebhook:Channels";
    private const string SecretSuffix = "TestChannelSecret";
    /// <summary>單一 Host 可保留的部署映射上限；超額在發布任何 resolver 快照前拒絕。</summary>
    private const int MaximumConfiguredChannels = 1024;
    private readonly IConfiguration configuration;
    private readonly Dictionary<string, string> organizations;

    /// <summary>
    /// 建立部署範圍的 Webhook secret resolver；產品與 mapping 由組合根提供，設定來源仍由 Host 擁有。
    /// </summary>
    /// <param name="configuration">Host 已組合的受信任部署設定。</param>
    /// <param name="product">組合根固定的產品識別，只檢查格式，不代表已執行組織或使用者授權。</param>
    public ConfigurationWebhookChannelSecretResolver(IConfiguration configuration, string product)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        TenantId.RequireIdentifier(product, nameof(product));

        var candidate = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var channel in configuration.GetSection(ChannelSection).GetChildren().Take(MaximumConfiguredChannels + 1))
        {
            if (candidate.Count == MaximumConfiguredChannels)
                throw new InvalidOperationException("Webhook channel mapping exceeds its configured limit.");
            if (!IsValidChannelId(channel.Key))
                throw new InvalidOperationException("Webhook channel mapping is invalid.");
            var organization = channel["Organization"];
            if (string.IsNullOrWhiteSpace(organization))
                throw new InvalidOperationException("Webhook channel mapping is incomplete.");
            candidate.Add(channel.Key, TenantId.RequireIdentifier(organization, nameof(organization)));
        }
        organizations = candidate;
        // IReadOnlyList 本身不能阻止把實際陣列轉型後改寫；包成唯讀集合，
        // 才能保證啟動檢查永遠檢查與 organizations 一致的完整頻道集合。
        ConfiguredChannelIds = Array.AsReadOnly(candidate.Keys.ToArray());
    }

    /// <summary>
    /// 啟動時 materialize 的非秘密 channelId 清單；不含 secret 或 token。
    /// </summary>
    public IReadOnlyList<string> ConfiguredChannelIds { get; }

    /// <summary>
    /// 依固定 mapping 讀取本次 secret；不使用其他頻道或預設組織後備。
    /// </summary>
    /// <param name="channelId">不受信任的 HTTP route channelId。</param>
    /// <param name="secret">合法時的本次借用 secret，呼叫端不得留存。</param>
    /// <returns>解析成功且 secret 格式合法時為 true。</returns>
    public bool TryResolveSecret(string channelId, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        if (!IsValidChannelId(channelId) || !organizations.TryGetValue(channelId, out var organization))
            return false;

        var candidate = configuration[$"Deployment:Organizations:{organization}:{SecretSuffix}"];
        if (candidate is null || !IsValidSecret(candidate))
            return false;
        secret = candidate;
        return true;
    }

    /// <summary>
    /// 驗證啟動時所有已配置 channel 的 secret；任何缺項均固定拒絕，不輸出設定值。
    /// </summary>
    public void ValidateConfiguration()
    {
        foreach (var channelId in ConfiguredChannelIds)
            if (!TryResolveSecret(channelId, out _))
                throw new InvalidOperationException("Webhook channel secret is not configured.");
    }

    /// <summary>限制 channelId 與 LINE 平台資料表邊界一致，不提供任何授權判定。</summary>
    /// <param name="channelId">待檢查的純 ASCII 數字識別。</param>
    /// <returns>符合一至二十位 ASCII 數字時為 true。</returns>
    private static bool IsValidChannelId(string? channelId) =>
        channelId is { Length: > 0 and <= 20 } && channelId.All(char.IsAsciiDigit);

    /// <summary>只接受規劃指定的 32 碼 ASCII 十六進位 secret，不放寬成任意字串。</summary>
    /// <param name="secret">設定來源候選值。</param>
    /// <returns>格式合法時為 true。</returns>
    private static bool IsValidSecret(string? secret)
    {
        if (secret is not { Length: 32 }) return false;
        foreach (var character in secret)
            if (!char.IsAsciiHexDigit(character)) return false;
        return true;
    }
}
