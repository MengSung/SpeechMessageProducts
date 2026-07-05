// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBuffer.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：struct SecureBufferInternal、class SecureBuffer
// 主要成員：Type、Buffer、Length
// 引用命名空間：System、System.Runtime.InteropServices
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace NSspi.Buffers
{
    /// <summary>
    /// Represents a native SecureBuffer structure, which is used for communicating
    /// buffers to the native APIs.
    /// </summary>
    [StructLayout( LayoutKind.Sequential )]
    internal struct SecureBufferInternal
    {
        /// <summary>
        /// When provided to the native API, the total number of bytes available in the buffer.
        /// On return from the native API, the number of bytes that were filled or used by the
        /// native API.
        /// </summary>
        public int Count;

        /// <summary>
        /// The type or purpose of the buffer.
        /// </summary>
        public BufferType Type;

        /// <summary>
        /// An pointer to a pinned byte[] buffer.
        /// </summary>
        public IntPtr Buffer;
    }

    /// <summary>
    /// Stores buffers to provide tokens and data to the native SSPI APIs.
    /// </summary>
    /// <remarks>The buffer is translated into a SecureBufferInternal for the actual call.
    /// To keep the call setup code simple, and to centralize the buffer pinning code,
    /// this class stores and returns buffers as regular byte arrays. The buffer
    /// pinning support code in SecureBufferAdapter handles conversion to SecureBufferInternal
    /// for pass to the managed api, as well as pinning relevant chunks of memory.
    ///
    /// Furthermore, the native API may not use the entire buffer, and so a mechanism
    /// is needed to communicate the usage of the buffer separate from the length
    /// of the buffer.</remarks>
    internal class SecureBuffer
    {
        /// <summary>
        /// Initializes a new instance of the SecureBuffer class.
        /// </summary>
        /// <param name="buffer">The buffer to wrap.</param>
        /// <param name="type">The type or purpose of the buffer, for purposes of
        /// invoking the native API.</param>
        public SecureBuffer( byte[] buffer, BufferType type )
        {
            this.Buffer = buffer;
            this.Type = type;
            this.Length = this.Buffer?.Length ?? 0;
        }

        /// <summary>
        /// The type or purposes of the API, for invoking the native API.
        /// </summary>
        public BufferType Type { get; set; }

        /// <summary>
        /// The buffer to provide to the native API.
        /// </summary>
        public byte[] Buffer { get; set; }

        /// <summary>
        /// The number of elements that were actually filled or used by the native API,
        /// which may be less than the total length of the buffer.
        /// </summary>
        public int Length { get; internal set; }
    }
}