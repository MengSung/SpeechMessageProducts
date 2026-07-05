// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/TimeStamp.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：struct TimeStamp
// 主要成員：ToDateTime
// 引用命名空間：System、System.Runtime.InteropServices
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace NSspi
{
    /// <summary>
    /// Represents a Windows API Timestamp structure, which stores time in units of 100 nanosecond
    /// ticks, counting from January 1st, year 1601 at 00:00 UTC. Time is stored as a 64-bit value.
    /// </summary>
    [StructLayout( LayoutKind.Sequential )]
    public struct TimeStamp
    {
        /// <summary>
        /// Returns the calendar date and time corresponding a zero timestamp.
        /// </summary>
        public static readonly DateTime Epoch = new DateTime( 1601, 1, 1, 0, 0, 0, DateTimeKind.Utc );

        /// <summary>
        /// Stores the time value. Infinite times are often represented as values near, but not exactly
        /// at the maximum signed 64-bit 2's complement value.
        /// </summary>
        private long time;

        /// <summary>
        /// Converts the TimeStamp to an equivalant DateTime object. If the TimeStamp represents
        /// a value larger than DateTime.MaxValue, then DateTime.MaxValue is returned.
        /// </summary>
        /// <returns></returns>
        public DateTime ToDateTime()
        {
            ulong test = (ulong)this.time + (ulong)( Epoch.Ticks );

            // Sometimes the value returned is massive, eg, 0x7fffff154e84ffff, which is a value
            // somewhere in the year 30848. This would overflow DateTime, since it peaks at 31-Dec-9999.
            // It turns out that this value corresponds to a TimeStamp's maximum value, reduced by my local timezone
            // http://stackoverflow.com/questions/24478056/
            if( test > (ulong)DateTime.MaxValue.Ticks )
            {
                return DateTime.MaxValue;
            }
            else
            {
                return DateTime.FromFileTimeUtc( this.time );
            }
        }
    }
}