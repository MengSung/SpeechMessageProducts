// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/SSPIException.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class SSPIException
// 主要成員：GetObjectData、ErrorCode、Message
// 引用命名空間：System、System.Runtime.Serialization
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.Serialization;

namespace NSspi
{
    /// <summary>
    /// The exception that is thrown when a problem occurs hwen using the SSPI system.
    /// </summary>
    [Serializable]
    public class SSPIException : Exception
    {
        private SecurityStatus errorCode;
        private string message;

        /// <summary>
        /// Initializes a new instance of the SSPIException class with the given message and status.
        /// </summary>
        /// <param name="message">A message explaining what part of the system failed.</param>
        /// <param name="errorCode">The error code observed during the failure.</param>
        public SSPIException( string message, SecurityStatus errorCode )
        {
            this.message = message;
            this.errorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the SSPIException class from serialization data.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected SSPIException( SerializationInfo info, StreamingContext context )
            : base( info, context )
        {
            this.message = info.GetString( "message" );
            this.errorCode = (SecurityStatus)info.GetUInt32( "errorCode" );
        }

        /// <summary>
        /// Serializes the exception.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        public override void GetObjectData( SerializationInfo info, StreamingContext context )
        {
            base.GetObjectData( info, context );

            info.AddValue( "message", this.message );
            info.AddValue( "errorCode", this.errorCode );
        }

        /// <summary>
        /// The error code that was observed during the SSPI call.
        /// </summary>
        public SecurityStatus ErrorCode
        {
            get
            {
                return this.errorCode;
            }
        }

        /// <summary>
        /// A human-readable message indicating the nature of the exception.
        /// </summary>
        public override string Message
        {
            get
            {
                return string.Format(
                    "{0}. Error Code = '0x{1:X}' - \"{2}\".",
                    this.message,
                    this.errorCode,
                    EnumMgr.ToText( this.errorCode )
                );
            }
        }
    }
}