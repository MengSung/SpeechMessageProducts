/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-07 課程簽核 Flex 訊息的離線契約，保護舊流程的替代文字、
 * 課程／學員／設備文字、圖片連結及同意／退回兩個決策動作。
 *
 * 主要功能與流程：測試以合成正式 ID 與固定輸入建立真正的 Line.Messaging
 * Flex 模型，再直接檢查 bubble 結構與 MessageTemplateAction 內容；不連 D365、
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
    public void Create_ApproveAndReject_MatchLegacyDecisionContract()
    {
        var message = LessonSigningMessageFactory.Create(new LessonSigningMessageInput(
            StorLessonId, SignerId, "課程名稱", "學員姓名", "已完成", "https://image.example/course.png",
            "簽核者"));

        Assert.Equal("有一個課程簽核的動作等待您的決定....", message.AltText);
        var bubble = Assert.IsType<BubbleContainer>(message.Contents);
        var body = Assert.IsType<BoxComponent>(bubble.Body);
        var bodyText = Assert.Single(body.Contents.OfType<TextComponent>()).Text;
        Assert.Equal("課程名稱: 課程名稱\r\n學員姓名: 學員姓名\r\n裝備狀態: 已完成", bodyText);

        var buttons = Assert.IsType<BoxComponent>(bubble.Footer).Contents
            .OfType<ButtonComponent>().ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.Equal("訊息類別=課程簽核&動作=同意&EntityId=" + StorLessonId.ToString("D") +
            "&簽核者=簽核者", Assert.IsType<MessageTemplateAction>(buttons[0].Action).Text);
        Assert.Equal("訊息類別=課程簽核&動作=退回&EntityId=" + StorLessonId.ToString("D") +
            "&簽核者=簽核者", Assert.IsType<MessageTemplateAction>(buttons[1].Action).Text);
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
