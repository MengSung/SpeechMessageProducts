/*
 * 檔案整體說明
 *
 * 目的與定位：LINE 訊息類型列舉 LINE message type enumeration
 *
 * 主要功能與流程：
 * 本檔宣告 MessageType。
 * - 文字訊息 - 最多 2000 字元 Text message - Maximum 2000 characters
 * - 文字訊息 v2 - 支援 substitution mention / emoji 的官方 textV2 訊息 Text message v2 - Official textV2 message with
 * substitution support
 * - 圖片訊息 - 支援 JPEG/PNG 格式 Image message - Supports JPEG/PNG format
 * - 影片訊息 - 支援 MP4 格式，最長 1 分鐘 Video message - Supports MP4 format, maximum 1 minute
 * 資料／選項欄位包含 MessageType，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/AudioMessage.cs、Line.Messaging/Messages/CouponMessage.cs、
 * Line.Messaging/Messages/FlexMessage.cs、Line.Messaging/Messages/ISendMessage.cs、
 * Line.Messaging/Messages/ImageMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// LINE 訊息類型列舉
    /// LINE message type enumeration
    /// </summary>
    /// <remarks>
    /// 定義 LINE Messaging API 支援的所有訊息類型。
    /// 每種類型對應不同的訊息格式和呈現方式。
    /// <para>
    /// Defines all message types supported by LINE Messaging API.
    /// Each type corresponds to different message format and presentation.
    /// </para>
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#message-objects
    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#message-objects
    /// </para>
    /// </remarks>
    public enum MessageType
    {
        /// <summary>
        /// 文字訊息 - 最多 2000 字元
        /// Text message - Maximum 2000 characters
        /// </summary>
        Text,

        /// <summary>
        /// 文字訊息 v2 - 支援 substitution mention / emoji 的官方 textV2 訊息
        /// Text message v2 - Official textV2 message with substitution support
        /// </summary>
        TextV2,

        /// <summary>
        /// 圖片訊息 - 支援 JPEG/PNG 格式
        /// Image message - Supports JPEG/PNG format
        /// </summary>
        Image,

        /// <summary>
        /// 影片訊息 - 支援 MP4 格式，最長 1 分鐘
        /// Video message - Supports MP4 format, maximum 1 minute
        /// </summary>
        Video,

        /// <summary>
        /// 音訊訊息 - 支援 M4A 格式，最長 1 分鐘
        /// Audio message - Supports M4A format, maximum 1 minute
        /// </summary>
        Audio,

        /// <summary>
        /// 位置訊息 - 包含標題、地址和經緯度
        /// Location message - Includes title, address and coordinates
        /// </summary>
        Location,

        /// <summary>
        /// 貼圖訊息 - 使用 LINE 官方貼圖
        /// Sticker message - Uses official LINE stickers
        /// </summary>
        Sticker,

        /// <summary>
        /// 優惠券訊息 - 透過 couponId 發送 LINE 官方帳號優惠券
        /// Coupon message - Sends an official account coupon by couponId
        /// </summary>
        Coupon,

        /// <summary>
        /// 圖片地圖訊息 - 可在圖片上設定多個可點擊區域
        /// Imagemap message - Allows multiple clickable areas on an image
        /// </summary>
        Imagemap,

        /// <summary>
        /// 模板訊息 - 包含按鈕、確認、輪播等互動式模板
        /// Template message - Includes buttons, confirm, carousel and other interactive templates
        /// </summary>
        Template,

        /// <summary>
        /// 檔案訊息 - 支援各種檔案類型（最大 200MB）
        /// File message - Supports various file types (maximum 200MB)
        /// </summary>
        File,

        /// <summary>
        /// Flex 訊息 - 高度自訂的版面配置訊息
        /// Flex message - Highly customizable layout message
        /// </summary>
        Flex,
    }
}
