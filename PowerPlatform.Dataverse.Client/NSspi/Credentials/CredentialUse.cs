// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/Credentials/CredentialUse.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：enum CredentialUse
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace NSspi.Credentials
{
    /// <summary>
    /// Indicates the manner in which a credential will be used for SSPI authentication.
    /// </summary>
    public enum CredentialUse : uint
    {
        /// <summary>
        /// The credentials will be used for establishing a security context with an inbound request, eg,
        /// the credentials will be used by a server building a security context with a client.
        /// </summary>
        Inbound = 1,

        /// <summary>
        /// The credentials will be used for establishing a security context as an outbound request,
        /// eg, the credentials will be used by a client to build a security context with a server.
        /// </summary>
        Outbound = 2,

        /// <summary>
        /// The credentials may be used to to either build a client's security context or a server's
        /// security context.
        /// </summary>
        Both = 3,
    }
}