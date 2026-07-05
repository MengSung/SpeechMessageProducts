// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/ByteWriter.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class ByteWriter
// 主要成員：WriteInt16_BE、WriteInt32_BE、ReadInt16_BE、ReadInt32_BE
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace NSspi
{
    /// <summary>
    /// Reads and writes value types to byte arrays with explicit endianness.
    /// </summary>
    public static class ByteWriter
    {
        // Big endian: Most significant byte at lowest address in memory.

        /// <summary>
        /// Writes a 2-byte signed integer to the buffer in big-endian format.
        /// </summary>
        /// <param name="value">The value to write to the buffer.</param>
        /// <param name="buffer">The buffer to write to.</param>
        /// <param name="position">The index of the first byte to write to.</param>
        public static void WriteInt16_BE( Int16 value, byte[] buffer, int position )
        {
            buffer[position + 0] = (byte)( value >> 8 );
            buffer[position + 1] = (byte)( value );
        }

        /// <summary>
        /// Writes a 4-byte signed integer to the buffer in big-endian format.
        /// </summary>
        /// <param name="value">The value to write to the buffer.</param>
        /// <param name="buffer">The buffer to write to.</param>
        /// <param name="position">The index of the first byte to write to.</param>
        public static void WriteInt32_BE( Int32 value, byte[] buffer, int position )
        {
            buffer[position + 0] = (byte)( value >> 24 );
            buffer[position + 1] = (byte)( value >> 16 );
            buffer[position + 2] = (byte)( value >> 8 );
            buffer[position + 3] = (byte)( value );
        }

        /// <summary>
        /// Reads a 2-byte signed integer that is stored in the buffer in big-endian format.
        /// The returned value is in the native endianness.
        /// </summary>
        /// <param name="buffer">The buffer to read.</param>
        /// <param name="position">The index of the first byte to read.</param>
        /// <returns></returns>
        public static Int16 ReadInt16_BE( byte[] buffer, int position )
        {
            Int16 value;

            value = (Int16)( buffer[position + 0] << 8 );
            value += (Int16)( buffer[position + 1] );

            return value;
        }

        /// <summary>
        /// Reads a 4-byte signed integer that is stored in the buffer in big-endian format.
        /// The returned value is in the native endianness.
        /// </summary>
        /// <param name="buffer">The buffer to read.</param>
        /// <param name="position">The index of the first byte to read.</param>
        /// <returns></returns>
        public static Int32 ReadInt32_BE( byte[] buffer, int position )
        {
            Int32 value;

            value = (Int32)( buffer[position + 0] << 24 );
            value |= (Int32)( buffer[position + 1] << 16 );
            value |= (Int32)( buffer[position + 2] << 8 );
            value |= (Int32)( buffer[position + 3] );

            return value;
        }
    }
}