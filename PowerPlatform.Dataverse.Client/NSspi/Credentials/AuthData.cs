// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/Credentials/AuthData.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：struct NativeAuthData、enum NativeAuthDataFlag
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、System.Runtime.InteropServices
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace NSspi.Credentials
{
    /// <summary>
    /// Provides authentication data in native method calls.
    /// </summary>
    /// <remarks>
    /// Implements the 'SEC_WINNT_AUTH_IDENTITY' structure. See:
    ///
    /// https://msdn.microsoft.com/en-us/library/windows/desktop/aa380131(v=vs.85).aspx
    /// </remarks>
    [StructLayout( LayoutKind.Sequential )]
    internal struct NativeAuthData
    {
        public NativeAuthData( string domain, string username, string password, NativeAuthDataFlag flag )
        {
            this.Domain = domain;
            this.DomainLength = domain.Length;

            this.User = username;
            this.UserLength = username.Length;

            this.Password = password;
            this.PasswordLength = password.Length;

            this.Flags = flag;
        }

        [MarshalAs( UnmanagedType.LPWStr )]
        public string User;

        public int UserLength;

        [MarshalAs( UnmanagedType.LPWStr )]
        public string Domain;

        public int DomainLength;

        [MarshalAs( UnmanagedType.LPWStr )]
        public string Password;

        public int PasswordLength;

        public NativeAuthDataFlag Flags;
    }

    internal enum NativeAuthDataFlag : int
    {
        Ansi = 1,

        Unicode = 2
    }
}