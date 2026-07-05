// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBufferType.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：enum BufferType
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace NSspi.Buffers
{
    /// <summary>
    /// Describes the type and purpose of a secure buffer passed to the native API.
    /// </summary>
    internal enum BufferType : int
    {
        /// <summary>
        /// The buffer is empty.
        /// </summary>
        Empty = 0x00,

        /// <summary>
        /// The buffer contains message data. Message data can be plaintext or cipher text data.
        /// </summary>
        Data = 0x01,

        /// <summary>
        /// The buffer contains opaque authentication token data.
        /// </summary>
        Token = 0x02,

        /// <summary>
        /// The buffer contains parameters specific to the security package.
        /// </summary>
        Parameters = 0x03,

        /// <summary>
        /// The buffer placeholder indicating that some data is missing.
        /// </summary>
        Missing = 0x04,

        /// <summary>
        /// The buffer passed to an API call contained more data than was necessary for completing the action,
        /// such as the case when a streaming-mode connection that does not preserve message bounders, such as TCP
        /// is used as the transport. The extra data is returned back to the caller in a buffer of this type.
        /// </summary>
        Extra = 0x05,

        /// <summary>
        /// The buffer contains a security data trailer, such as a message signature or marker, or framing data.
        /// </summary>
        Trailer = 0x06,

        /// <summary>
        /// The buffer contains a security data header, such as a message signature, marker, or framing data.
        /// </summary>
        Header = 0x07,

        Padding = 0x09,
        Stream = 0x0A,
        ChannelBindings = 0x0E,
        TargetHost = 0x10,
        ReadOnlyFlag = unchecked((int)0x80000000),
        ReadOnlyWithChecksum = 0x10000000
    }
}