/*
 * 檔案整體說明
 *
 * 目的與定位：驗證課程 QR 圖片組的離線展開契約，確保 P5-07 的待辦消費端
 * 以設定值建立 31 張（Plugin）或 32 張（Workflow 含問卷）圖片，而不是
 * 依組織名稱、檔案路徑或同步網路磁碟分支。
 *
 * 主要功能與流程：測試使用合成課程 ID 與四個明確的 LIFF 前綴，檢查欄位名稱、
 * QR 內容及圖片發布欄位一一對應；所有輸入只在單一測試呼叫內存在，不連 D365、
 * LINE 或檔案系統。問卷必須使用獨立設定前綴，不能把問卷誤當一般簽到圖片。
 *
 * 使用邊界與維護：本檔只驗證純值規劃，不宣稱圖片已產生、URL 已回寫或實機
 * 影子已通過；若內容數量、欄位、前綴或正式 ID 不完整，測試應立即失敗。
 *
 * 驗證方式：測試使用固定合成 ID、注入 renderer 與 publisher，斷言候選數量、
 * 問卷前綴、原子發布與 stream 清理；不連接 D365、LINE 或檔案系統。
 */
using LineMessagingProcessor.RichMenus.QrImages;
using System.IO;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>保護課程 QR 組合的數量、欄位及設定式前綴契約。</summary>
public sealed class LessonQrBundlePlanTests
{
    private static readonly Guid LessonId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>Plugin 只產生十五堂簽到／簽退及報名三十一張，且不包含問卷。</summary>
    [Fact]
    public void Create_WithoutPoll_ExpandsThirtyOneConfiguredImages()
    {
        var plan = LessonQrBundlePlan.Create(LessonId, 1, includePoll: false,
            new LessonQrBundleOptions("https://front.example/qr?QrCodeId=",
                "https://poll.example/qr?QrCodeId="));

        Assert.Equal(31, plan.Count);
        Assert.DoesNotContain(plan.Images, x => x.FieldName == "new_poll_url");
        Assert.Equal("new_enroll_ursl", Assert.Single(plan.Images,
            x => x.FieldName == "new_enroll_ursl").FieldName);
        Assert.Equal("https://front.example/qr?QrCodeId=" +
            LessonId.ToString("D") + "_1_On",
            plan.Images.Single(x => x.FieldName == "new_1_signon_url").Content);
    }

    /// <summary>Workflow 額外產生問卷，且問卷內容使用獨立設定前綴。</summary>
    [Fact]
    public void Create_WithPoll_ExpandsThirtyTwoImagesAndUsesPollPrefix()
    {
        var plan = LessonQrBundlePlan.Create(LessonId, 1, includePoll: true,
            new LessonQrBundleOptions("https://front.example/qr?QrCodeId=",
                "https://poll.example/qr?QrCodeId="));

        Assert.Equal(32, plan.Count);
        Assert.Equal("https://poll.example/qr?QrCodeId=" +
            LessonId.ToString("D") + "_poll",
            plan.Images.Single(x => x.FieldName == "new_poll_url").Content);
    }

    /// <summary>所有圖片完成後才發布，且發布返回後每個暫存stream均已關閉。</summary>
    [Fact]
    public async Task Handler_PublishesOnlyCompleteCandidateAndDisposesStreams()
    {
        var plan = LessonQrBundlePlan.Create(LessonId, 1, includePoll: true,
            new LessonQrBundleOptions("https://front.example/qr?QrCodeId=",
                "https://poll.example/qr?QrCodeId="));
        var renderer = new RecordingRenderer();
        var published = new List<LessonQrRenderedImage>();
        var handler = new LessonQrBundleHandler(plan, (images, _) =>
        {
            published.AddRange(images);
            return Task.CompletedTask;
        }, renderer);

        await handler.ExecuteAsync(default);

        Assert.Equal(32, published.Count);
        Assert.Equal(32, renderer.Contents.Count);
        Assert.All(published, image => Assert.False(image.Png.CanRead));
    }

    /// <summary>中途產圖失敗不得呼叫發布，已產生的stream仍須全部清理。</summary>
    [Fact]
    public async Task Handler_RenderFailureDoesNotPublishPartialCandidate()
    {
        var plan = LessonQrBundlePlan.Create(LessonId, 1, includePoll: false,
            new LessonQrBundleOptions("https://front.example/qr?QrCodeId=",
                "https://poll.example/qr?QrCodeId="));
        var renderer = new RecordingRenderer(failAt: 3);
        var published = false;
        var handler = new LessonQrBundleHandler(plan, (_, _) =>
        {
            published = true;
            return Task.CompletedTask;
        }, renderer);

        await Assert.ThrowsAsync<IOException>(() => handler.ExecuteAsync(default));

        Assert.False(published);
        Assert.Equal(2, renderer.Created.Count);
        Assert.All(renderer.Created, stream => Assert.False(stream.CanRead));
    }

    private sealed class RecordingRenderer : ILessonQrImageRenderer
    {
        private readonly int _failAt;
        internal readonly List<string> Contents = new();
        internal readonly List<MemoryStream> Created = new();
        private int _calls;

        internal RecordingRenderer(int failAt = 0) => _failAt = failAt;

        public Task<Stream> RenderAsync(string content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contents.Add(content);
            _calls++;
            if (_failAt == _calls)
                throw new IOException("Synthetic renderer failure.");
            var stream = new MemoryStream(new byte[] { 137, 80, 78, 71 });
            Created.Add(stream);
            return Task.FromResult<Stream>(stream);
        }
    }
}
