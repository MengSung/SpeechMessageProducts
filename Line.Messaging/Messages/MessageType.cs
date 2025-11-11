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
