/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-07 課程簽核通知的共用 Flex 訊息組合器，讓課程 Plugin、
 * Workflow 及後續預約簽核沿用同一份決策文字與版面資料契約。它只組合已由
 * 呼叫端授權及查證的純值，不查詢 D365、不解密 Token、不建立 LINE client。
 *
 * 主要功能與流程：輸入正式課程紀錄 ID、簽核者 ID、課程／學員／設備文字及
 * HTTPS 圖片 URL，先驗證固定長度與 URI，再建立一個 bubble，包含舊流程的
 * alt text、圖片、三行摘要及「同意／退回」MessageTemplateAction。動作文字
 * 保留正式來源 ID與顯示簽核者，供上層以穩定事件鍵及伺服器端再次授權。
 *
 * 使用邊界與維護：本檔不保存 Session、使用者狀態、credential、快取、timer
 * 或背景 Task；回傳模型由本次通知組合呼叫端擁有。輸入文字只保留在同步呼叫
 * 期間，錯誤不回顯資料。呼叫端仍須使用 MemberNotificationService 完成收件者、
 * 教會、語系、Token 及傳輸檢查；本檔的 URI／ID 驗證不是授權。
 *
 * 驗證方式：Core 雙目標測試直接檢查 Flex 結構、正文、兩個決策 action 及
 * 非法 ID／URI 的 fail-closed 行為；測試不宣稱 D365 或 LINE 實機送達。
 */
using System;
using Line.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>課程簽核訊息的已脫離SDK純值輸入，不含Token或收件者credential。</summary>
public sealed record LessonSigningMessageInput(
    Guid StorLessonId,
    Guid SignerId,
    string CourseName,
    string MemberName,
    string EquipmentStatus,
    string ImageUrl,
    string SignerDisplayName);

/// <summary>建立與舊課程簽核相容的單 bubble Flex 訊息。</summary>
public static class LessonSigningMessageFactory
{
    private const string AltText = "有一個課程簽核的動作等待您的決定....";

    /// <summary>
    /// 先驗證純值，再建立不含傳輸狀態的 Flex 模型。任何資料不完整時不回傳
    /// 部分模型，避免上層把不完整通知送出。
    /// </summary>
    public static FlexMessage Create(LessonSigningMessageInput input)
    {
#if NETFRAMEWORK
        if (input is null) throw new ArgumentNullException(nameof(input));
#else
        ArgumentNullException.ThrowIfNull(input);
#endif
        Validate(input);

        var actionPrefix = "訊息類別=課程簽核&動作=";
        var entity = "&EntityId=" + input.StorLessonId.ToString("D") +
            "&簽核者=" + input.SignerDisplayName;
        var bubble = new BubbleContainer
        {
            Hero = new ImageComponent(input.ImageUrl)
            {
                Size = ComponentSize.Full,
                AspectRatio = AspectRatio._20_13,
                AspectMode = AspectMode.Cover,
                Action = new UriTemplateAction("說明網頁/填寫表單", input.ImageUrl)
            },
            Body = new BoxComponent(BoxLayout.Vertical)
            {
                Contents =
                {
                    new TextComponent("課程名稱: " + input.CourseName + "\r\n" +
                        "學員姓名: " + input.MemberName + "\r\n" +
                        "裝備狀態: " + input.EquipmentStatus)
                    {
                        Wrap = true,
                        Size = ComponentSize.Sm,
                        Color = "#666666"
                    }
                }
            },
            Footer = new BoxComponent(BoxLayout.Vertical)
            {
                Contents =
                {
                    new ButtonComponent(new MessageTemplateAction("同意",
                        actionPrefix + "同意" + entity)),
                    new ButtonComponent(new MessageTemplateAction("退回",
                        actionPrefix + "退回" + entity))
                }
            }
        };

        return new FlexMessage(AltText) { Contents = bubble };
    }

    /// <summary>固定上限及 HTTPS 邊界，拒絕空值與會造成跨資料列誤用的輸入。</summary>
    private static void Validate(LessonSigningMessageInput input)
    {
        if (input.StorLessonId == Guid.Empty || input.SignerId == Guid.Empty)
            throw new ArgumentException("Signing identities are required.");
        ValidateText(input.CourseName, nameof(input.CourseName));
        ValidateText(input.MemberName, nameof(input.MemberName));
        ValidateText(input.EquipmentStatus, nameof(input.EquipmentStatus));
        ValidateText(input.SignerDisplayName, nameof(input.SignerDisplayName));
        if (!Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            throw new ArgumentException("Signing image URL must use HTTPS.", nameof(input));
    }

    /// <summary>文字只在本次組合使用，固定上限避免模型及日誌被無界輸入佔滿。</summary>
    private static void ValidateText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000)
            throw new ArgumentException("Signing text is invalid.", parameterName);
    }
}
