/*
檔案整體說明
目的與定位：提供 P3-01 產生器測試專用的完全合成舊來源與暫存 owner，不攜帶實際舊方案、設定、私人 ID 或 CRM 資料。
主要功能與流程：建立和真來源同形狀的四階段路由與單一 helper，靜態回覆檔只有語法而不編譯外部服務。各案例可改 helper 或回覆來驗證工具確實消費來源；以 .NET CP950 寫入受控檔案，讓真 P0-05 轉碼也可測。
使用邊界與維護：每個 fixture 擁有唯一暫存根與全部子檔，Dispose 先由案例等待任何工作完成，再刪根；不保留背景 task、外部連線或共用狀態。合成 matcher 是測試輸入，不代替正式產生器的真實舊來源；所有故障與決定性斷言由使用案例明列，不能將離線輸出冒稱實機。
*/
using System.Text;
using LegacyGolden;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>合成測試輸入與目錄的唯一 owner；呼叫端須等待 reader 結束後 Dispose。</summary>
internal sealed class LegacyGoldenFixture : IDisposable
{
    /// <summary>本案例唯一根，不與產品設定、其他案例或另一登入使用者共用。</summary>
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "legacy-golden-test-" + Guid.NewGuid().ToString("N"));
    /// <summary>合成來源根；只有白名單的十個 .cs 檔會被建立。</summary>
    internal string Source => Path.Combine(Root, "source");
    /// <summary>測試自有暫存與原始轉碼 manifest 目錄，隨 fixture 結束一併刪除。</summary>
    internal string Scratch => Path.Combine(Root, "scratch");
    /// <summary>發布路徑與來源、暫存互不包含，保護錯誤路徑的拒絕契約。</summary>
    internal string Output => Path.Combine(Root, "Golden");

    /// <summary>建立獨立合成根；寫入過程若失敗立即清理，不留半建構 owner。</summary>
    internal LegacyGoldenFixture()
    {
        try
        {
            Directory.CreateDirectory(Scratch);
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
            foreach (var source in Sources())
            {
                var path = Path.Combine(Source, source.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, encoding.GetBytes(source.Text));
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>取得目前工作樹的正式 P0-05 腳本；只沿父目錄查固定相對路徑，不掃描外部設定。</summary>
    internal static string Converter
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "tools", "RepoQuality", "Convert-TextEncoding.ps1");
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException("Repository converter missing.");
        }
    }

    /// <summary>回傳十份新資料，每次皆可獨立改動；hash 是這些合成 CP950 bytes 的真實 hash。</summary>
    internal static List<SourceDocument> Sources()
    {
        var texts = LegacySourceReader.AllowedPaths.ToDictionary(path => path, _ => "// synthetic static source\r\n", StringComparer.Ordinal);
        texts[LegacySourceReader.RoutingPath] = """
            using System;
            using System.Collections.Generic;
            using System.Collections.Frozen;
            namespace LineMessagePlugIn.Processors.Keywords;
            public static class MessageRoutingKeywords
            {
                public enum MessageHandlerType { Unknown, Help, Registration, Enrollment }
                private static readonly FrozenDictionary<string, MessageHandlerType> ExactMatchTable =
                    new Dictionary<string, MessageHandlerType>(StringComparer.OrdinalIgnoreCase)
                    { { "Seed", MessageHandlerType.Help } }.ToFrozenDictionary();
                private static readonly (string Prefix, MessageHandlerType HandlerType)[] PrefixMatchTable =
                    { ("pre", MessageHandlerType.Registration) };
                private static readonly (string Keyword, MessageHandlerType HandlerType)[] ContainsMatchTable =
                    { ("tail", MessageHandlerType.Help) };
                public static MessageHandlerType GetHandlerType(string message) => GetHandlerType(message.AsSpan());
                public static MessageHandlerType GetHandlerType(ReadOnlySpan<char> message)
                {
                    var trimmedMessage = message.Trim();
                    var messageStr = trimmedMessage.ToString();
                    if (ExactMatchTable.TryGetValue(messageStr, out var exactMatch)) return exactMatch;
                    foreach (var (prefix, handlerType) in PrefixMatchTable)
                        if (trimmedMessage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return handlerType;
                    if (MessageKeywordMatcher.IsEnrollmentKeyword(trimmedMessage)) return MessageHandlerType.Enrollment;
                    foreach (var (keyword, handlerType) in ContainsMatchTable)
                        if (trimmedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return handlerType;
                    return MessageHandlerType.Unknown;
                }
            }
            """;
        texts[LegacySourceReader.MatcherPath] = """
            using System;
            namespace LineMessagePlugIn.Processors.Keywords;
            public static class MessageKeywordMatcher
            {
                public static bool IsEnrollmentKeyword(ReadOnlySpan<char> description)
                {
                    return description.StartsWith("Group");
                }
            }
            """;
        texts["LineMessagePlugIn/Processors/Core/IncomingMessageProcessor.Routing.cs"] = """
            partial class IncomingMessageProcessor
            {
                void RouteMessageAsync(string description) { if (description == "") SendReplyAsync("empty"); RouteToHandlerAsync(description); }
                void RouteToHandlerAsync(Intent intent)
                {
                    switch (intent)
                    {
                        case Intent.Help: ShowHelpMessageAsync(); return;
                        case Intent.Registration: HandleRegistrationAsync(); return;
                        case Intent.Enrollment: HandleEnrollmentAsync(); return;
                        case Intent.Unknown: default: return;
                    }
                }
            }
            """;
        texts["LineMessagePlugIn/Processors/Core/IncomingMessageProcessor.Reply.cs"] = """
            partial class IncomingMessageProcessor
            {
                void SendReplyAsync(string message) { output.ReplyMessageAsync("{replyToken}", message); }
                void ShowHelpMessageAsync() { string answer = "synthetic-help" + Environment.NewLine + "end"; output.ReplyMessageAsync("{replyToken}", answer); }
            }
            """;
        texts["LineMessagePlugIn/Processors/Handlers/IncomingMessageProcessor.Enrollment.cs"] = """
            partial class IncomingMessageProcessor
            {
                void HandleEnrollmentAsync() { using (var processor = new SyntheticData(client)) { processor.Display(description, lineId); } }
                void HandleRegistrationAsync() { output.ReplyMessageAsync(replyToken, $"synthetic-id: {lineId}"); }
            }
            """;
        texts["LineApiWebhook/Handlers/LineBotMessageHandler.cs"] = """
            class LineBotMessageHandler
            {
                void HandleTextMessageAsync() { processor.RouteMessageAsync(description, lineId); }
                void HandleImageMessageAsync() { /* no connected image handler */ }
                void HandleAudioMessageAsync() { /* no connected audio handler */ }
                void HandleLocationMessageAsync() { /* no connected location handler */ }
                void HandleStickerMessageAsync() { /* no connected sticker handler */ }
            }
            """;
        texts["LineApiWebhook/Processors/WebhookMessageProcessor.cs"] = """
            class WebhookMessageProcessor { void RouteMessageAsync() { incoming.RouteMessageAsync(description, lineId); } }
            """;
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
        return texts.Select(pair =>
        {
            var text = LegacySourceReader.Normalize(pair.Value);
            return new SourceDocument(pair.Key, text, LegacySourceReader.Hash(encoding.GetBytes(text)),
                LegacySourceReader.Hash(Encoding.UTF8.GetBytes(text)), LegacySourceReader.CountLines(text));
        }).ToList();
    }

    /// <summary>owner 只刪自己的唯一根；所有 reader process 已由等待的操作排空，重複釋放安全。</summary>
    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
