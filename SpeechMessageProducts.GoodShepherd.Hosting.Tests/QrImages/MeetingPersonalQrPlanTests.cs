/*
 * 檔案整體說明
 * 目的與定位：本檔由 SpeechMessageProducts.GoodShepherd.Hosting.Tests 擁有，
 * 保護 P5-09 主日與個人 QR 的封閉五欄 payload、正式身分及內容候選。
 * 主要流程：以合成 GUID、invalid 網域及同名聯絡人測試真正 Host factory 與候選；
 * 故障包含未知欄位、重複 schema、錯數量與錯 contact。任何錯誤必須在 scope 前拒絕。
 * 使用邊界與維護：不讀正式設定、不連資料庫、不畫圖。每案建立及釋放自己的 DI
 * 容器，沒有背景 worker、Session 或共用可變資料；字串只存活於測試呼叫。
 * 驗證方式：固定欄位順序與既有 codec 的逐字契約為決定性斷言，不宣稱實機驗收。
 */
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.QrImages;

/// <summary>驗證單一 QR consumer 能區分 schema，且拒絕任何未核定的擴充資料。</summary>
public sealed class MeetingPersonalQrPlanTests
{
    /// <summary>真 Core 規則與共用 numeric-only 命令的 canonical JSON 可直接消費，防止雙方手寫格式自證。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_ConsumesActualCoreWriterCommandWithoutExpandingSharedContract(bool personal)
    {
        var capture = new CommandCapture();
        var church = Guid.NewGuid();
        var id = Guid.NewGuid();
        QrImageTaskRule.Enqueue(capture, church, id, personal);
        Assert.NotNull(capture.Command);
        var command = capture.Command;
        var payload = MeetingPersonalQrPayload.Parse(command.PayloadJson);
        Assert.Equal(personal ? "personal" : "meeting", payload.Kind);
        Assert.Equal(id, payload.RecordId);
        payload.ValidateWorkItem(new(Guid.NewGuid(), "1", command.ChurchId, command.ContactId,
            (OutboundWorkType)command.Type, command.PayloadJson, command.IdempotencyKey,
            OutboundWorkStatus.Processing, 0, null, DateTime.UtcNow));
    }

    /// <summary>共用命令只允許 numeric kind；拒絕看似同義的字串、浮點及範圍外整數。</summary>
    [Theory]
    [InlineData("\"meeting\"")]
    [InlineData("\"0\"")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("null")]
    [InlineData("0.0")]
    public void Payload_RejectsNonIntegerKind(string kind)
    {
        Assert.Throws<ArgumentException>(() => MeetingPersonalQrPayload.Parse(
            $"{{\"schema\":2,\"kind\":{kind},\"recordId\":\"{Guid.NewGuid():D}\",\"contentVersion\":1,\"imageCount\":38}}"));
    }

    /// <summary>多餘、重複、錯版本、錯數量都不得生成任何候選或保留原 JSON。</summary>
    [Theory]
    [InlineData(",\"name\":\"不可採用\"", 1, 38)]
    [InlineData(",\"schema\":2", 1, 38)]
    [InlineData("", 2, 38)]
    [InlineData("", 1, 1)]
    public void Payload_RejectsUnapprovedShape(string extra, int version, int count)
    {
        var json = $"{{\"schema\":2,\"kind\":0,\"recordId\":\"{Guid.NewGuid():D}\"," +
            $"\"contentVersion\":{version},\"imageCount\":{count}{extra}}}";
        Assert.Throws<ArgumentException>(() => MeetingPersonalQrPayload.Parse(json));
    }

    /// <summary>固定順序 On9／Off9、動態交錯二十張；固定使用實際 URL 欄位而非猜名稱。</summary>
    [Fact]
    public void Meeting_ExpandsCompleteThirtyEightCandidate()
    {
        var id = Guid.NewGuid();
        var payload = Parse("meeting", id);
        var plan = MeetingPersonalQrPlan.Create(payload, new("https://qr.invalid/?QrCodeId=",
            "https://personal.invalid/?QrCodeId="), "");
        Assert.Equal(38, plan.Count);
        Assert.Equal(38, plan.Select(p => p.FieldName).Distinct().Count());
        Assert.Equal("new_sunday_first_qr_on_url", plan[0].FieldName);
        Assert.Equal("new_leaders_gather_qr_on_url", plan[8].FieldName);
        Assert.Equal("new_sunday_first_qr_off_url", plan[9].FieldName);
        Assert.Equal("new_leaders_gather_qr_off_url", plan[17].FieldName);
        Assert.Equal($"https://qr.invalid/?QrCodeId={id:D}@new_saturday_worship_on@On", plan[2].Content);
        Assert.Equal($"https://qr.invalid/?QrCodeId={id:D}_1_On", plan[18].Content);
        Assert.Equal($"https://qr.invalid/?QrCodeId={id:D}_10_Off", plan[37].Content);
    }

    /// <summary>個人尾欄原樣保留；同名不同正式 ID 必須得到不同的圖片內容與附件主鍵。</summary>
    [Fact]
    public void Personal_PreservesSameNamesWithDistinctOfficialIds()
    {
        var a = Parse("personal", Guid.NewGuid());
        var b = Parse("personal", Guid.NewGuid());
        var options = new MeetingPersonalQrOptions("https://qr.invalid/?QrCodeId=",
            "https://personal.invalid/?QrCodeId=");
        var left = Assert.Single(MeetingPersonalQrPlan.Create(a, options, "同名甲 @&?"));
        var right = Assert.Single(MeetingPersonalQrPlan.Create(b, options, "同名甲 @&?"));
        Assert.Equal($"https://personal.invalid/?QrCodeId={a.RecordId:D}@同名甲 @&?", left.Content);
        Assert.NotEqual(left.Content, right.Content);
        Assert.NotEqual(a.AnnotationId, b.AnnotationId);
        Assert.Equal(a.AnnotationId, Parse("personal", a.RecordId).AnnotationId);
    }

    /// <summary>只建構合成 schema2 輸入，所有 case 都走產品 parser。</summary>
    internal static MeetingPersonalQrPayload Parse(string kind, Guid id) =>
        MeetingPersonalQrPayload.Parse($"{{\"schema\":2,\"kind\":{(kind == "meeting" ? 0 : 1)},\"recordId\":\"{id:D}\"," +
            $"\"contentVersion\":1,\"imageCount\":{(kind == "meeting" ? 38 : 1)}}}");

    /// <summary>schema2 必須走新分支；沒有正式 scoped store 時應報依賴缺失而非誤判 lesson。</summary>
    [Fact]
    public async Task Factory_RoutesSchemaTwoToItsScopedDependencies()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var factory = new LessonQrBundleHostFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var id = Guid.NewGuid();
        var church = Guid.NewGuid();
        var source = DeterministicGuid.Create("qrcodes.meeting", id, 1);
        var item = new OutboundWorkItem(Guid.NewGuid(), "1", church, null,
            OutboundWorkType.QrImageGenerate,
            $"{{\"schema\":2,\"kind\":0,\"recordId\":\"{id:D}\",\"contentVersion\":1,\"imageCount\":38}}",
            $"{church:N}-{(int)OutboundWorkType.QrImageGenerate}-{source:N}",
            OutboundWorkStatus.Processing, 0, null, DateTime.UtcNow);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.OpenAsync(item, CancellationToken.None).AsTask());
        Assert.Contains("No service", error.Message);
    }

    /// <summary>只保存本次純值命令供跨層斷言，不連 SDK、沒有快取或背景資源。</summary>
    private sealed class CommandCapture : IOutboundEnqueueStore
    {
        /// <summary>本次 Core 真正交出的不可變命令；測試返回後不保留。</summary>
        public OutboundTaskCommand? Command { get; private set; }
        /// <summary>記錄入列接縫，回合成正式待辦 ID；不宣稱遠端已建立資料。</summary>
        public Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Command = command;
            return Guid.NewGuid();
        }
    }
}
