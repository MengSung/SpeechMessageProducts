/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-07 課程簽核 Flex 訊息的離線契約，保護舊流程的替代文字、
 * 課程／學員／設備文字、圖片連結及同意／退回兩個決策動作。
 *
 * 主要功能與流程：測試以合成正式 ID 與固定輸入建立真正的 Line.Messaging
 * Flex 模型，再直接檢查 bubble 結構與 PostbackTemplateAction 內容；不連 D365、
 * LINE、Token 或檔案系統，也不把測試結果當作實機傳送證據。
 *
 * 使用邊界與維護：每個模型只在單一測試內擁有，不保存 Session、credential、
 * 快取或背景工作。任何欄位遺漏、決策字串改動或跨人資料混入都應立即失敗。
 * 驗證方式：測試檢查 deterministic 的 alt text、body 欄位及兩個正式 action，
 * 並另測空值／非法 URI 在產生前拒絕。
 */
using System;
using System.Linq;
using Line.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Lessons;

/// <summary>保護課程簽核訊息不變條件與決策文字。</summary>
public sealed class LessonSigningMessageFactoryTests
{
    private static readonly Guid StorLessonId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid SignerId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>同意與退回的 action 都指向同一正式課程紀錄及簽核者。</summary>
    [Fact]
    public void Lessons_Signing_ApproveAndReject_MatchLegacy()
    {
        var message = LessonSigningMessageFactory.Create(new LessonSigningMessageInput(
            StorLessonId, SignerId, "課程名稱", "學員姓名", "已完成", "https://image.example/course.png",
            "簽核者"));

        Assert.Equal("有一個課程簽核的動作等待您的決定....", message.AltText);
        var bubble = Assert.IsType<BubbleContainer>(message.Contents);
        var body = Assert.IsType<BoxComponent>(bubble.Body);
        Assert.Equal("課程簽核", Assert.IsType<TextComponent>(body.Contents[0]).Text);
        Assert.Equal(Weight.Bold, Assert.IsType<TextComponent>(body.Contents[0]).Weight);
        var rating = Assert.IsType<BoxComponent>(body.Contents[1]);
        Assert.Equal(5, rating.Contents.OfType<IconComponent>().Count());
        Assert.Equal("5.0", Assert.Single(rating.Contents.OfType<TextComponent>()).Text);
        var bodyText = Assert.Single(Assert.IsType<BoxComponent>(body.Contents[2]).Contents.OfType<TextComponent>()).Text;
        Assert.Equal("課程名稱: 課程名稱\r\n學員姓名: 學員姓名\r\n裝備狀態: 已完成", bodyText);

        var buttons = Assert.IsType<BoxComponent>(bubble.Footer).Contents
            .OfType<ButtonComponent>().ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, button => { Assert.Equal(ButtonStyle.Link, button.Style); Assert.Equal(ButtonHeight.Sm, button.Height); });
        Assert.Equal("您簽核同意", Assert.IsType<PostbackTemplateAction>(buttons[0].Action).DisplayText);
        Assert.Equal("您簽核退回", Assert.IsType<PostbackTemplateAction>(buttons[1].Action).DisplayText);
        Assert.Equal("#C0C8C8", bubble.Styles.Body.BackgroundColor);
        Assert.Equal("#FFFFF0", bubble.Styles.Footer.BackgroundColor);
        Assert.Equal("訊息類別=課程簽核&動作=同意&EntityId=" + StorLessonId.ToString("D") +
            "&簽核者=簽核者", Assert.IsType<PostbackTemplateAction>(buttons[0].Action).Data);
        Assert.Equal("訊息類別=課程簽核&動作=退回&EntityId=" + StorLessonId.ToString("D") +
            "&簽核者=簽核者", Assert.IsType<PostbackTemplateAction>(buttons[1].Action).Data);
    }

    /// <summary>
    /// 決策資料達到 SDK 的 300 字上限仍須逐字保留；超出一字則在建構前拒絕。
    /// 使用合成簽核者名稱注入邊界，防止 SDK 靜默截斷後發布不完整身分資料。
    /// 模型由本案擁有，沒有外部資源或傳輸。
    /// </summary>
    [Fact]
    public void Create_PostbackCapacity_PreservesBoundaryAndRejectsOverflow()
    {
        var prefix = "訊息類別=課程簽核&動作=同意&EntityId=" + StorLessonId.ToString("D") + "&簽核者=";
        var signer = new string('x', 300 - prefix.Length);
        var input = new LessonSigningMessageInput(StorLessonId, SignerId, "課程", "學員", "狀態",
            "https://image.example/a.png", signer);
        var bubble = Assert.IsType<BubbleContainer>(LessonSigningMessageFactory.Create(input).Contents);
        var buttons = Assert.IsType<BoxComponent>(bubble.Footer).Contents.OfType<ButtonComponent>().ToArray();
        Assert.Equal(prefix + signer, Assert.IsType<PostbackTemplateAction>(buttons[0].Action).Data);
        Assert.Equal("訊息類別=課程簽核&動作=退回&EntityId=" + StorLessonId.ToString("D") + "&簽核者=" + signer,
            Assert.IsType<PostbackTemplateAction>(buttons[1].Action).Data);
        Assert.Throws<ArgumentException>(() => LessonSigningMessageFactory.Create(
            input with { SignerDisplayName = signer + "x" }));
    }

    /// <summary>空正式 ID 或非 HTTPS 圖片不得建立可能誤發的通知模型。</summary>
    [Fact]
    public void Create_InvalidIdentityOrImageUrl_RejectsBeforeComposition()
    {
        Assert.Throws<ArgumentException>(() => LessonSigningMessageFactory.Create(
            new LessonSigningMessageInput(Guid.Empty, SignerId, "課程", "學員", "狀態",
                "https://image.example/a.png", "簽核者")));
        Assert.Throws<ArgumentException>(() => LessonSigningMessageFactory.Create(
            new LessonSigningMessageInput(StorLessonId, SignerId, "課程", "學員", "狀態",
                "http://image.example/a.png", "簽核者")));
    }
}
