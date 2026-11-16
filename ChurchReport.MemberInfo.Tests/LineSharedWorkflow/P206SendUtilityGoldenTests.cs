/*
 * 檔案整體說明
 *
 * 目的與定位：P6-03 以產品通知器重播仍使用的 P2-06 HTTP 契約，原始兩份黃金
 * 檔永遠唯讀；每案不是明列重播，就是明列舊入口與編譯盤點確認的退役理由。
 * 主要功能與流程：先鎖原檔 SHA-256，再以真 SDK 與假 HTTP／CRM 比對 URL、遮罩
 * 標頭、UTF-8 本文與 RetryKey 的完整位元組；另保留共用 legacy 雙頻道與媒體清理案例。
 * 使用邊界與維護：全為離線合成資料，沒有 LINE、D365 或秘密存取。每案 owns 有限
 * HTTP client、SDK、工具與下載回應，全部 await 後逆序 Dispose；捕捉只存遮罩後文字，
 * Dispose 清空集合。退役清單與重播集合必須互斥且聯集精確等於黃金案名，不能用
 * 自動收納剩餘案例掩飾遺漏；媒體故障與尚存共用流程的專用測試由共用測試專案負責。
 */
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChurchReport.Services;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>以不可改寫的原始黃金與明列清冊，保護產品保留的四種送訊入口及共用隔離契約。</summary>
public sealed class P206SendUtilityGoldenTests
{
    private const string UserId = "U11111111111111111111111111111111";
    private const string OtherUserId = "U22222222222222222222222222222222";
    private const string MainGolden = "P206-pre-migration.http.golden";
    private const string SourceGolden = "P206-source-replay.http.golden";

    /// <summary>重播入口逐筆明列，不依舊型別反射自動豁免；只傳合成輸入。</summary>
    public static IEnumerable<object[]> KeptCases()
    {
        yield return new object[] { MainGolden, "push.SendMessage(String,String).default" };
        yield return new object[] { MainGolden, "push.SendMessage(String,List-1).default" };
        yield return new object[] { MainGolden, "push.SendReliableMessageAsync(String,String,String).default" };
        yield return new object[] { MainGolden, "tool.MultiCastTextMessageAsync(IList-1,String).default" };
        yield return new object[] { SourceGolden, "group-push" };
        yield return new object[] { SourceGolden, "multicast-duplicates" };
    }

    // 每筆理由保留原入口；此陣列不從黃金檔推導，新增或少列都會使集合契約失敗。
    private static readonly (string File, string Key, string Reason)[] RetiredCases =
    {
        ("P206-pre-migration.http.golden", "line.AddRichMenuMessage(String).default", "ChurchReport.Tools.LineUtilityClass.AddRichMenuMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.DeleteRichMenuMessage(String).default", "ChurchReport.Tools.LineUtilityClass.DeleteRichMenuMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.MultiCastTextMessageAsync(IList-1,String).default", "ChurchReport.Tools.LineUtilityClass.MultiCastTextMessageAsync(IList-1,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.PostSerializedConfirm(String,String,String,List-1).default", "ChurchReport.Tools.LineUtilityClass.PostSerializedConfirm(String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.PostSerializedFlex(String,FlexMessage).default", "ChurchReport.Tools.LineUtilityClass.PostSerializedFlex(String,FlexMessage).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default", "ChurchReport.Tools.LineUtilityClass.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.PostSerializedTemplate(Entity,String,String,String,String,String,List-1).default", "ChurchReport.Tools.LineUtilityClass.PostSerializedTemplate(Entity,String,String,String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.PostSerializedTemplate(String,String,String,String,String,List-1).default", "ChurchReport.Tools.LineUtilityClass.PostSerializedTemplate(String,String,String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.ReplyImage(String,String,String).default", "ChurchReport.Tools.LineUtilityClass.ReplyImage(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.ReplyMessage(String,List-1).default", "ChurchReport.Tools.LineUtilityClass.ReplyMessage(String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.ReplyTextMessage(String,String).default", "ChurchReport.Tools.LineUtilityClass.ReplyTextMessage(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendAudeo(String,String,Int64).default", "ChurchReport.Tools.LineUtilityClass.SendAudeo(String,String,Int64).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendImage(String,String,String).default", "ChurchReport.Tools.LineUtilityClass.SendImage(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendLocation(String,String,String,Decimal,Decimal).default", "ChurchReport.Tools.LineUtilityClass.SendLocation(String,String,String,Decimal,Decimal).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendMessage(String,List-1).default", "ChurchReport.Tools.LineUtilityClass.SendMessage(String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendMessage(String,String).default", "ChurchReport.Tools.LineUtilityClass.SendMessage(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendMessageAsync(String,String).default", "ChurchReport.Tools.LineUtilityClass.SendMessageAsync(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendSticker(String,Int32,Int32).default", "ChurchReport.Tools.LineUtilityClass.SendSticker(String,Int32,Int32).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "line.SendVideo(String,String,String).default", "ChurchReport.Tools.LineUtilityClass.SendVideo(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.AddRichMenuMessage(String).default", "ChurchReport.Tools.PushUtility.AddRichMenuMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.CarouselMessage(String).default", "ChurchReport.Tools.PushUtility.CarouselMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.ChurchCarouselMessage(String).default", "ChurchReport.Tools.PushUtility.ChurchCarouselMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.ConfirmMessage(String).default", "ChurchReport.Tools.PushUtility.ConfirmMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.DeleteRichMenuMessage(String).default", "ChurchReport.Tools.PushUtility.DeleteRichMenuMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.PostSerializedConfirm(String,String,String,List-1).default", "ChurchReport.Tools.PushUtility.PostSerializedConfirm(String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default", "ChurchReport.Tools.PushUtility.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.PostSerializedTemplate(String,String,String,String,String,List-1).default", "ChurchReport.Tools.PushUtility.PostSerializedTemplate(String,String,String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendAudeo(String,String,Int64).default", "ChurchReport.Tools.PushUtility.SendAudeo(String,String,Int64).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendImage(String,String,String).default", "ChurchReport.Tools.PushUtility.SendImage(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendLocation(String,String,String,Decimal,Decimal).default", "ChurchReport.Tools.PushUtility.SendLocation(String,String,String,Decimal,Decimal).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendMessageOrThrowAsync(String,String).default", "ChurchReport.Tools.PushUtility.SendMessageOrThrowAsync(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendMessagesOrThrowAsync(String,IReadOnlyList-1).default", "ChurchReport.Tools.PushUtility.SendMessagesOrThrowAsync(String,IReadOnlyList-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendSticker(String,Int32,Int32).default", "ChurchReport.Tools.PushUtility.SendSticker(String,Int32,Int32).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "push.SendVideo(String,String,String).default", "ChurchReport.Tools.PushUtility.SendVideo(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.EchoAsync(String,String).default", "ChurchReport.Tools.ReplyUtility.EchoAsync(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.EchoAsyncProcessor(MessageEvent).default", "ChurchReport.Tools.ReplyUtility.EchoAsyncProcessor(MessageEvent).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.EchoAsyncProcessor(MessageEvent).group", "ChurchReport.Tools.ReplyUtility.EchoAsyncProcessor(MessageEvent).group: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.EchoAsyncProcessor(MessageEvent).room", "ChurchReport.Tools.ReplyUtility.EchoAsyncProcessor(MessageEvent).room: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.EchoImageAsync(String,String,String).default", "ChurchReport.Tools.ReplyUtility.EchoImageAsync(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.PostSerializedConfirm(String,String,String,List-1).default", "ChurchReport.Tools.ReplyUtility.PostSerializedConfirm(String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default", "ChurchReport.Tools.ReplyUtility.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.ReplyMessage(String,List-1).default", "ChurchReport.Tools.ReplyUtility.ReplyMessage(String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.ReplyMessageAsync(String,String).default", "ChurchReport.Tools.ReplyUtility.ReplyMessageAsync(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.ReplyRandomStickerAsync(String).default", "ChurchReport.Tools.ReplyUtility.ReplyRandomStickerAsync(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "reply.UploadMediaContentAsync(String,String,String,String).default", "ChurchReport.Tools.ReplyUtility.UploadMediaContentAsync(String,String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.CarouselMessage(String).default", "ToolUtility.PushUtility.CarouselMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.CarouselMessageAsync(String).default", "ToolUtility.PushUtility.CarouselMessageAsync(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.ChurchCarouselMessage(String).default", "ToolUtility.PushUtility.ChurchCarouselMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.ChurchCarouselMessageAsync(String).default", "ToolUtility.PushUtility.ChurchCarouselMessageAsync(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.ConfirmMessage(String).default", "ToolUtility.PushUtility.ConfirmMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.ConfirmMessageAsync(String).default", "ToolUtility.PushUtility.ConfirmMessageAsync(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.DeleteRichMenuMessage(String).default", "ToolUtility.PushUtility.DeleteRichMenuMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.PostSerializedConfirm(String,String,String,List-1).default", "ToolUtility.PushUtility.PostSerializedConfirm(String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default", "ToolUtility.PushUtility.PostSerializedImageMap(String,String,String,Int32,Int32,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.PostSerializedTemplate(String,String,String,String,String,List-1).default", "ToolUtility.PushUtility.PostSerializedTemplate(String,String,String,String,String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendAudeo(String,String,Int64).default", "ToolUtility.PushUtility.SendAudeo(String,String,Int64).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendImage(String,String,String).default", "ToolUtility.PushUtility.SendImage(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendLocation(String,String,String,Decimal,Decimal).default", "ToolUtility.PushUtility.SendLocation(String,String,String,Decimal,Decimal).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendMessage(String,List-1).default", "ToolUtility.PushUtility.SendMessage(String,List-1).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendMessage(String,String).default", "ToolUtility.PushUtility.SendMessage(String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendSticker(String,Int32,Int32).default", "ToolUtility.PushUtility.SendSticker(String,Int32,Int32).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.SendVideo(String,String,String).default", "ToolUtility.PushUtility.SendVideo(String,String,String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-pre-migration.http.golden", "tool.AddRichMenuMessage(String).default", "ToolUtility.PushUtility.AddRichMenuMessage(String).default: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-source-replay.http.golden", "reply-buttons", "ChurchReport.Tools.ReplyUtility.ReplyMessage (Buttons): 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-source-replay.http.golden", "reply-sender", "ChurchReport.Tools.ReplyUtility.ReplyMessage (Sender): 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
        ("P206-source-replay.http.golden", "menu-add", "ToolUtility.PushUtility.AddRichMenuMessage: 9b7ac0a0 沒有正式呼叫者，隨相容殼刪除"),
    };

    /// <summary>原始檔 bytes 與開工 SHA 相等，任何換行、案例或本文改寫都失敗。</summary>
    [Fact]
    public void P206Golden_File_Sha256Unchanged()
    {
        Assert.Equal("efd0704ea3817c84a7190bf0354d9a23dd0ebcc8bda477784a0b34a7d03fa2ff",
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(GoldenPath(MainGolden)))));
        Assert.Equal("8c07187be54f850ad2ad919b1cf0a556a18507c83a03a31431fec3b2ef54bc4c",
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(GoldenPath(SourceGolden)))));
    }

    /// <summary>真 SDK 經新通知器產生協定，保留標頭遮罩與原檔 CRLF，再與案例原始 bytes 比對。</summary>
    /// <param name="file">唯讀黃金檔名。</param>
    /// <param name="key">明列保留案例的穩定鍵。</param>
    [Theory]
    [MemberData(nameof(KeptCases))]
    public async Task P206Golden_KeptCases_ReplayedThroughNewEntry_ByteIdentical(string file, string key)
    {
        using var handler = new RecordingHandler("synthetic");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "synthetic");
        using var tracer = new NullToolUtilityTracer();
        using var tool = new ToolUtilityClass(DispatchProxy.Create<IOrganizationService, EmptyCrm>(), tracer,
            new ConfigurationBuilder().Build());
        var notifier = ChurchReportLineNotifier.ForClient(sdk);
        switch (key)
        {
            case "push.SendMessage(String,String).default":
                await notifier.SendTextBestEffortAsync(UserId, "合成訊息");
                break;
            case "push.SendMessage(String,List-1).default":
                await notifier.SendMessagesBestEffortAsync(UserId,
                    new ISendMessage[] { new TextMessage("合成訊息"), new StickerMessage("1", "5") });
                break;
            case "push.SendReliableMessageAsync(String,String,String).default":
                await notifier.SendReliableTextAsync(UserId, "合成訊息", "33333333-3333-3333-3333-333333333333");
                break;
            case "group-push":
                await notifier.SendMessagesBestEffortAsync("Cgroup-fixed",
                    new ISendMessage[] { new TextMessage("合成訊息"), new AudioMessage("https://example.test/audio", 1000) });
                break;
            case "tool.MultiCastTextMessageAsync(IList-1,String).default":
            case "multicast-duplicates":
                notifier = new ChurchReportLineNotifier(new LineLegacySendWorkflow(new LineMessagingProcessorClass(sdk)), tool);
                await notifier.MulticastTextAsync(key == "multicast-duplicates"
                    ? new[] { UserId, OtherUserId, UserId } : new[] { UserId, OtherUserId }, "合成訊息");
                break;
            default: throw new InvalidOperationException("Unlisted replay case.");
        }
        var original = ReadGoldenBytes(file, key);
        var actual = Encoding.UTF8.GetBytes(handler.Transcript(false).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(original, actual);
    }

    /// <summary>兩份黃金的 72 案必須精確分成 6 重播與 66 退役，逐案有入口與理由，零交集、零遺漏。</summary>
    [Fact]
    public void P206Golden_EveryCase_IsEitherReplayedOrRetiredWithReason()
    {
        var replayed = KeptCases().Select(row => ((string)row[0], (string)row[1])).ToArray();
        var retired = RetiredCases.Select(row => (row.File, row.Key)).ToArray();
        Assert.Equal(6, replayed.Length);
        Assert.Equal(66, retired.Length);
        Assert.Equal(replayed.Length, replayed.Distinct().Count());
        Assert.Equal(retired.Length, retired.Distinct().Count());
        Assert.Empty(replayed.Intersect(retired));
        Assert.All(RetiredCases, row =>
        {
            Assert.Contains("9b7ac0a0 沒有正式呼叫者，隨相容殼刪除", row.Reason);
            Assert.Contains("Utility", row.Reason);
        });
        foreach (var file in new[] { MainGolden, SourceGolden })
        {
            var keys = File.ReadAllLines(GoldenPath(file)).Where(line => line.StartsWith("@@ ", StringComparison.Ordinal))
                .Select(line => line[3..]).ToArray();
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(keys.Order(StringComparer.Ordinal), replayed.Concat(retired).Where(row => row.Item1 == file)
                .Select(row => row.Item2).Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// 兩頻道並行交替推播 40 次並下載 20 次；handler 嚴格核對 token，本文必須只含該
    /// 操作身分，所有媒體均在 owner 排空後釋放。有限次數不是正式長時間壓測。
    /// </summary>
    [Fact]
    public async Task LegacyAdapter_ConcurrentChannels_IsolatedAndDrained()
    {
        using var a = new RecordingHandler("channel-a");
        using var b = new RecordingHandler("channel-b");
        using var ha = new HttpClient(a) { Timeout = TimeSpan.FromSeconds(5) };
        using var hb = new HttpClient(b) { Timeout = TimeSpan.FromSeconds(5) };
        using var sa = new LineMessagingClient(ha, "channel-a");
        using var sb = new LineMessagingClient(hb, "channel-b");
        var pa = new LineMessagingProcessorClass(sa);
        var pb = new LineMessagingProcessorClass(sb);
        var wa = new LineLegacySendWorkflow(pa);
        var wb = new LineLegacySendWorkflow(pb);
        await Task.WhenAll(Enumerable.Range(0, 20).SelectMany(i => new[]
        {
            wa.SendAsync("Cgroup-a", new ISendMessage[] { new TextMessage("only-a-" + i) }),
            wb.SendAsync("Cgroup-b", new ISendMessage[] { new TextMessage("only-b-" + i) })
        }));
        await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            pa.UseContentAsync("media-a", stream => Assert.True(stream.CanRead)),
            pb.UseContentAsync("media-b", stream => Assert.True(stream.CanRead))
        }));
        Assert.DoesNotContain("only-b", a.Transcript(false));
        Assert.DoesNotContain("only-a", b.Transcript(false));
        Assert.Equal(10, a.Media.Count);
        Assert.Equal(10, b.Media.Count);
        Assert.All(a.Media.Concat(b.Media), media => Assert.True(media.Released));
    }


    /// <summary>從方案根目錄定位唯讀黃金，不使用固定個人路徑或寫入測試資料。</summary>
    private static string GoldenPath(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
                return Path.Combine(directory.FullName, "ChurchReport.MemberInfo.Tests", "LineSharedWorkflow", "Golden", file);
        throw new InvalidOperationException("Fixture repository root not found.");
    }

    /// <summary>只切出唯一原始區段，不正規化黃金 bytes；UTF-8 往返無損，重複或缺鍵立即失敗。</summary>
    private static byte[] ReadGoldenBytes(string file, string key)
    {
        var sections = File.ReadAllText(GoldenPath(file), new UTF8Encoding(false, true))
            .Split("@@ ", StringSplitOptions.RemoveEmptyEntries);
        var marker = key + "\r\n";
        var section = Assert.Single(sections.Where(part => part.StartsWith(marker, StringComparison.Ordinal)));
        return Encoding.UTF8.GetBytes(section[marker.Length..]);
    }

    /// <summary>合成 CRM 回傳固定 contact 或空統計查詢；任何寫入或未預期操作失敗，不連線且無外部資源。</summary>
    public class EmptyCrm : DispatchProxy
    {
        /// <summary>僅允許空統計查詢及固定 contact 讀取；未預期動作失敗，不偷偷建立真實連線。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IOrganizationService.RetrieveMultiple) => new EntityCollection(),
            nameof(IOrganizationService.Retrieve) => new Entity("contact", (Guid)args![1]!) { ["new_lineid"] = UserId },
            _ => throw new InvalidOperationException("Unexpected synthetic CRM operation.")
        };
    }

    /// <summary>追蹤內容owner只記錄釋放旗標，不保存 request 或憑證；handler finally 回收紅燈遺漏。</summary>
    private sealed class TrackingContent(byte[] bytes) : ByteArrayContent(bytes)
    {
        /// <summary>確定性釋放斷言，不依賴 GC／終結器判斷洩漏。</summary>
        internal bool Released { get; private set; }
        /// <summary>先記錄再交基底關閉內容與串流；重複 Dispose 仍安全。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Released = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>固定合成回應，僅保存遮罩 wire 字串；每案例最多數筆，Dispose 清掉記錄並關閉所有媒體owner。</summary>
    private sealed class RecordingHandler(string expectedToken, bool invalidImage = false, Exception? uploadFailure = null) : HttpMessageHandler
    {
        private readonly List<string> requests = new();
        /// <summary>只保留本案例回應內容以斷言 owner；案例 Dispose 清掉引用。</summary>
        internal List<TrackingContent> Media { get; } = new();

        /// <summary>捕捉真實 SDK request；回應無外部網路，所有 token 都是案例擁有的合成字串。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);
            var headers = request.Headers.Select(h => h.Key + ": " + (h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? "Bearer ***" : string.Join(",", h.Value)))
                .Concat(request.Content?.Headers.Select(h => h.Key + ": " + string.Join(",", h.Value)) ?? Enumerable.Empty<string>())
                .OrderBy(h => h, StringComparer.Ordinal);
            var binary = request.Content?.Headers.ContentType?.MediaType == "image/png";
            var body = request.Content == null ? "" : binary
                ? "base64:" + Convert.ToBase64String(await request.Content.ReadAsByteArrayAsync(cancellationToken))
                : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (requests) requests.Add(request.Method + " " + request.RequestUri + "\n" + string.Join("\n", headers) + "\n\n" + body + "\n");
            if (binary && uploadFailure != null) throw uploadFailure;
            HttpContent content;
            if (request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
            {
                using var image = new Image<Rgba32>(8, 8);
                using var memory = new MemoryStream();
                image.SaveAsPng(memory);
                var media = new TrackingContent(invalidImage ? new byte[] { 1, 2, 3 } : memory.ToArray());
                media.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                lock (Media) Media.Add(media);
                content = media;
            }
            else
            {
                var json = request.RequestUri.AbsolutePath.Contains("/member/", StringComparison.Ordinal)
                    ? "{\"displayName\":\"合成會友\",\"userId\":\"" + UserId + "\"}"
                    : "{\"richMenuId\":\"richmenu-fixed\"}";
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        /// <summary>隨機貼圖先驗範圍與原 Content-Length，再遮罩 ID 及衍生長度；其他請求不正規化。</summary>
        internal string Transcript(bool randomSticker)
        {
            var transcript = string.Join("---\n", requests);
            if (randomSticker)
            {
                var offset = transcript.IndexOf("{", StringComparison.Ordinal);
                var body = JObject.Parse(transcript[offset..]);
                var id = int.Parse(body["messages"]![0]!["stickerId"]!.Value<string>()!, System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(id is >= 1 and <= 17 or 21 or >= 100 and <= 139 or >= 401 and <= 430);
                var lengthLine = transcript.Split('\n').Single(line => line.StartsWith("Content-Length: ", StringComparison.Ordinal));
                Assert.Equal(Encoding.UTF8.GetByteCount(transcript[offset..].TrimEnd('\n')),
                    int.Parse(lengthLine["Content-Length: ".Length..], System.Globalization.CultureInfo.InvariantCulture));
                transcript = transcript.Replace(lengthLine, "Content-Length: <random-body-length>", StringComparison.Ordinal);
                offset = transcript.IndexOf("{", StringComparison.Ordinal);
                body["messages"]![0]!["stickerId"] = "<random-existing-range>";
                transcript = transcript[..offset] + body.ToString(Formatting.None) + "\n";
            }
            return requests.Count == 0 ? "NO HTTP\n" : transcript;
        }

        /// <summary>只在該案例排空後關閉尚未釋放的媒體，並清空捕捉集合；不與其他案例共用。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var content in Media) content.Dispose();
                Media.Clear();
                requests.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
