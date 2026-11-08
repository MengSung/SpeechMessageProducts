/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04 的 Core 會友入口，先驗來源教會／會友／頻道指派，才允許共用
 * 傳輸管線碰觸 LINE；頻道池內多教會共用憑證不會共用可變 sender 或訊息。
 * 主要功能與流程：單次輸入最多五則 Line.Messaging 訊息，建立有界 JSON 快照，
 * 覆寫每則 sender 為伺服器教會名稱，Retry Key 原樣沿用來源正式記錄 ID。
 * 使用邊界與維護：不保存 caller 集合、訊息、credential 或教會資料；client 只借用
 * 無狀態 HttpClient。HTTP request／response、逾時及連結取消來源全部由單次傳輸
 * using 擁有，完成後釋放；正常 caller 取消與內部逾時沿用既有契約，不建立重試背景。
 * 永久／暫時失敗交主機共用告警或核定重試 owner，本檔不自行發管理者 LINE。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LineMessagingProcessor.Core;

/// <summary>共用十秒有界傳輸的會友授權核對外觀，不持有操作資料。</summary>
public sealed partial class LinePushClient
{
    /// <summary>先核對兩條教會條件，拒絕時不得觸發 HTTP；成功時凍結訊息及指定 sender。</summary>
    public async Task<LinePushResult> PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid sourceChurchId,
        IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rejection = ValidateMember(recipient, channel, sourceChurchId);
        if (rejection is not null) return rejection;
        if (retryKey == Guid.Empty) throw new ArgumentException("An event retry key is required.", nameof(retryKey));
        var frozen = FreezeMessages(messages, channel.SenderName);
        var body = new JObject { ["to"] = recipient.LineUserId, ["messages"] = frozen }.ToString(Formatting.None);
        return (await SendCoreAsync(HttpMethod.Post, "message/push", channel.Credential, body, retryKey, null, ct)
            .ConfigureAwait(false)).Result;
    }

    /// <summary>解析指定 alias 並直接換綁宮格，不產生圖片；缺少 alias 是永久拒絕。</summary>
    public async Task<LinePushResult> LinkRichMenuToUserAsync(MemberRecipient recipient, ChurchChannel channel,
        Guid sourceChurchId, string richMenuAlias, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rejection = ValidateMember(recipient, channel, sourceChurchId);
        if (rejection is not null) return rejection;
        if (string.IsNullOrEmpty(richMenuAlias) || richMenuAlias.Length > 32)
            throw new ArgumentException("A bounded rich menu alias is required.", nameof(richMenuAlias));
        foreach (var character in richMenuAlias)
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and
                not (>= '0' and <= '9') and not '_' and not '-')
                throw new ArgumentException("Invalid rich menu alias.", nameof(richMenuAlias));
        var lookup = await SendCoreAsync(HttpMethod.Get, "richmenu/alias/" + richMenuAlias,
            channel.Credential, null, null, richMenuAlias, ct).ConfigureAwait(false);
        if (!lookup.Result.Success || lookup.MenuId is null) return lookup.Result;
        return (await SendCoreAsync(HttpMethod.Post, "user/" + recipient.LineUserId + "/richmenu/" + lookup.MenuId,
            channel.Credential, null, null, null, ct).ConfigureAwait(false)).Result;
    }

    /// <summary>核對教會後解除當次單一會友的宮格綁定，不保留會友或頻道狀態。</summary>
    public async Task<LinePushResult> UnlinkRichMenuAsync(MemberRecipient recipient, ChurchChannel channel,
        Guid sourceChurchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rejection = ValidateMember(recipient, channel, sourceChurchId);
        if (rejection is not null) return rejection;
        return (await SendCoreAsync(HttpMethod.Delete, "user/" + recipient.LineUserId + "/richmenu",
            channel.Credential, null, null, null, ct).ConfigureAwait(false)).Result;
    }

    /// <summary>兩條教會條件及正式ID先驗證；同頻道共享不等於同教會授權。</summary>
    private static LinePushResult? ValidateMember(MemberRecipient recipient, ChurchChannel channel, Guid sourceChurchId)
    {
#if NETFRAMEWORK
        if (recipient is null) throw new ArgumentNullException(nameof(recipient));
        if (channel is null) throw new ArgumentNullException(nameof(channel));
#else
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(channel);
#endif
        if (sourceChurchId == Guid.Empty || recipient.ContactId == Guid.Empty || recipient.ChurchId != sourceChurchId ||
            channel.ChurchId != sourceChurchId || channel.ChannelId == Guid.Empty ||
            channel.AssignedChannelId != channel.ChannelId)
            return LinePushResult.Reject("ChurchMismatch");
        if (string.IsNullOrWhiteSpace(recipient.LineUserId)) return LinePushResult.Reject("NotBound");
        LinePushRequest.RequireUserId(recipient.LineUserId);
        return null;
    }

    /// <summary>序列化有界快照並覆寫sender，不修改caller模型；只接受正式SDK型別而非自訂序列化物件。</summary>
    private static JArray FreezeMessages(IReadOnlyList<ISendMessage> messages, string senderName)
    {
#if NETFRAMEWORK
        if (messages is null) throw new ArgumentNullException(nameof(messages));
#else
        ArgumentNullException.ThrowIfNull(messages);
#endif
        if (messages.Count is < 1 or > 5) throw new ArgumentException("One through five messages are required.", nameof(messages));
        if (string.IsNullOrWhiteSpace(senderName) || senderName.Length > 20)
            throw new ArgumentException("A server-owned sender name of at most 20 characters is required.", nameof(senderName));
        foreach (var message in messages)
            if (message is null || message.GetType().Assembly != typeof(ISendMessage).Assembly)
                throw new ArgumentException("Only SDK message models are accepted.", nameof(messages));
        var serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };
        serializer.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        using var text = new BoundedMessageWriter();
        using (var writer = new JsonTextWriter(text) { CloseOutput = false }) serializer.Serialize(writer, messages);
        var snapshot = JArray.Parse(text.ToString());
        foreach (var message in snapshot)
        {
            if (message is not JObject row || row["type"]?.Type != JTokenType.String)
                throw new ArgumentException("Invalid message model.", nameof(messages));
            if (string.Equals(row["type"]!.Value<string>(), "text", StringComparison.Ordinal) &&
                (row["text"]?.Type != JTokenType.String || row["text"]!.Value<string>()!.Length is < 1 or > 5000))
                throw new ArgumentException("Invalid text message size.", nameof(messages));
            row["sender"] = new JObject { ["name"] = senderName };
        }
        return snapshot;
    }

    /// <summary>alias成功回應最多讀4096 bytes、深度8，只接受同alias及安全richmenu ID，不保存原本文。</summary>
    private static async Task<string?> ReadMenuIdAsync(HttpResponseMessage response, string expectedAlias, CancellationToken ct)
    {
        if (response.Content is null || response.Content.Headers.ContentLength > 4096) return null;
#if NETFRAMEWORK
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#endif
        var buffer = new byte[4097];
        var total = 0;
        while (total < buffer.Length)
        {
#if NETFRAMEWORK
            var read = await stream.ReadAsync(buffer, total, buffer.Length - total, ct).ConfigureAwait(false);
#else
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
#endif
            if (read == 0) break;
            total += read;
        }
        if (total > 4096) return null;
        try
        {
            using var text = new StringReader(new UTF8Encoding(false, true).GetString(buffer, 0, total));
            using var reader = new JsonTextReader(text) { MaxDepth = 8 };
            var row = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read() || row["richMenuAliasId"]?.Type != JTokenType.String ||
                row["richMenuId"]?.Type != JTokenType.String || row["richMenuAliasId"]!.Value<string>() != expectedAlias) return null;
            var id = row["richMenuId"]?.Value<string>();
            if (id is null || id.Length != 41 || !id.StartsWith("richmenu-", StringComparison.Ordinal)) return null;
            for (var index = 9; index < id.Length; index++)
                if (id[index] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return null;
            return id;
        }
        catch (JsonException) { return null; }
        catch (DecoderFallbackException) { return null; }
    }

    /// <summary>單次序列化的唯一字串owner；寫入前限制容量，避免先配置無界JSON再檢查長度。</summary>
    private sealed class BoundedMessageWriter : StringWriter
    {
        /// <summary>使用Invariant格式與小型初始配置，結束由FreezeMessages的using釋放。</summary>
        internal BoundedMessageWriter() : base(new StringBuilder(1024), CultureInfo.InvariantCulture) { }
        /// <summary>寫入一字元前檢查當次固定容量。</summary>
        public override void Write(char value) { RequireSpace(1); base.Write(value); }
        /// <summary>寫入字串前檢查容量，不儲存來源字串參照。</summary>
        public override void Write(string? value) { RequireSpace(value?.Length ?? 0); base.Write(value); }
        /// <summary>串列寫入沿用相同容量預算，不以分段繞過上限。</summary>
        public override void Write(char[] buffer, int index, int count) { RequireSpace(count); base.Write(buffer, index, count); }
#if NET10_0_OR_GREATER
        /// <summary>net10 span寫入也先受同一容量預算約束，不能繞過字串／陣列守門。</summary>
        public override void Write(ReadOnlySpan<char> buffer) { RequireSpace(buffer.Length); base.Write(buffer); }
#endif
        /// <summary>最多256Ki字元的操作快照，超限直接拒絕且不輸出任何訊息內容。</summary>
        private void RequireSpace(int count)
        {
            if (count < 0 || GetStringBuilder().Length > 262144 - count)
                throw new ArgumentException("Message snapshot budget exceeded.");
        }
    }
}
