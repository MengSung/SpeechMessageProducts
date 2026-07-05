// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/MessageType.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：enum MessageType
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
