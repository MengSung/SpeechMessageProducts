/*
 * 檔案整體說明
 *
 * 目的與定位：建立 P5-08 預約簽核通知的純 Flex 模型，讓 Plugin、Workflow
 * 與離線黃金測試共用同一份訊息契約。此檔不查詢 Dataverse、不解密 Token、
 * 不建立 LINE client，也不保存跨執行的可變狀態。
 *
 * 主要流程：呼叫端先完成預約、簽核者與教會的授權查詢，再傳入正式 appointment
 * ID、簽核者 ID、類型、內容、申請人及簽核者顯示名稱；本檔驗證純值與 HTTPS
 * 圖片網址，建立一個含「同意／退回」Postback 的單 Bubble Flex。
 *
 * 使用邊界與維護：Postback 僅攜帶正式來源 ID，真正授權仍由 webhook handler
 * 在回呼時重新查證。輸入只活到同步建立模型結束，不使用 Session、cache、timer、
 * 背景工作或 credential；失敗時不回傳部分模型。測試只驗離線結構，不宣稱實機送達。
 */
using System;
using Line.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Appointments;

/// <summary>預約簽核訊息的脫離平台純值，沒有 Token 或連線狀態。</summary>
public sealed record AppointmentSigningMessageInput(
    Guid AppointmentId,
    Guid SignerId,
    string Subject,
    string Content,
    string ApplicantName,
    string SignerDisplayName,
    string ImageUrl);

/// <summary>建立與退役預約簽核流程相容的單 Bubble Flex。</summary>
public static class AppointmentSigningMessageFactory
{
    private const string AltText = "有一個簽核的動作等待您的決定....";
    private const string ActionPrefix = "訊息類別=簽核&動作=";
    private const string SignerSuffix = "&簽核者=";

    /// <summary>
    /// 先驗證所有純值，再建立獨立的 Flex 物件。任何一個來源欄位不完整時
    /// 都 fail-closed，避免上層將部分內容當成可發布通知。
    /// </summary>
    public static FlexMessage Create(AppointmentSigningMessageInput input)
    {
#if NETFRAMEWORK
        if (input is null) throw new ArgumentNullException(nameof(input));
#else
        ArgumentNullException.ThrowIfNull(input);
#endif
        Validate(input);
        var suffix = "&EntityId=" + input.AppointmentId.ToString("D") + SignerSuffix + input.SignerDisplayName;
        var approve = ActionPrefix + "同意" + suffix;
        var reject = ActionPrefix + "退回" + suffix;
        if (approve.Length > 300 || reject.Length > 300)
            throw new ArgumentException("Appointment signing action exceeds postback capacity.", nameof(input));

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
                    new TextComponent(input.Subject) { Weight = Weight.Bold, Size = ComponentSize.Md },
                    new BoxComponent(BoxLayout.Baseline)
                    {
                        Margin = Spacing.Md,
                        Contents = { Star(), Star(), Star(), Star(), Star(),
                            new TextComponent("5.0") { Size = ComponentSize.Sm, Margin = Spacing.Md, Flex = 0, Color = "#999999" } }
                    },
                    new BoxComponent(BoxLayout.Baseline)
                    {
                        Spacing = Spacing.Sm,
                        Contents = { new TextComponent("申請人: " + input.ApplicantName + "\r\n" + input.Content)
                            { Wrap = true, Size = ComponentSize.Sm, Color = "#666666", Flex = 5 } }
                    }
                }
            },
            Footer = new BoxComponent(BoxLayout.Vertical)
            {
                Spacing = Spacing.Sm,
                Flex = 0,
                Contents =
                {
                    new ButtonComponent(new PostbackTemplateAction("同意", approve, "您簽核同意"))
                        { Style = ButtonStyle.Link, Height = ButtonHeight.Sm },
                    new ButtonComponent(new PostbackTemplateAction("退回", reject, "您簽核退回"))
                        { Style = ButtonStyle.Link, Height = ButtonHeight.Sm },
                    new SpacerComponent { Size = ComponentSize.Sm }
                }
            },
            Styles = new BubbleStyles
            {
                Body = new BlockStyle { BackgroundColor = "#C0C8C8", Separator = true, SeparatorColor = "#9400D3" },
                Footer = new BlockStyle { BackgroundColor = "#FFFFF0" }
            }
        };
        return new FlexMessage(AltText) { Contents = bubble };
    }

    /// <summary>每次建立新的星號元件，避免 Flex 可變物件跨操作共用。</summary>
    private static IconComponent Star() => new()
    {
        Url = "https://scdn.line-apps.com/n/channel_devcenter/img/fx/review_gold_star_28.png",
        Size = ComponentSize.Sm
    };

    /// <summary>固定純值上限並限制圖片為 HTTPS，拒絕空值與不完整來源。</summary>
    private static void Validate(AppointmentSigningMessageInput input)
    {
        if (input.AppointmentId == Guid.Empty || input.SignerId == Guid.Empty)
            throw new ArgumentException("Appointment signing identities are required.");
        ValidateText(input.Subject, nameof(input.Subject));
        ValidateText(input.Content, nameof(input.Content));
        ValidateText(input.ApplicantName, nameof(input.ApplicantName));
        ValidateText(input.SignerDisplayName, nameof(input.SignerDisplayName));
        if (!Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            throw new ArgumentException("Appointment signing image URL must use HTTPS.", nameof(input));
    }

    /// <summary>文字僅在本次組合期間存活，避免無界訊息佔用模型與記錄資源。</summary>
    private static void ValidateText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000)
            throw new ArgumentException("Appointment signing text is invalid.", parameterName);
    }
}
