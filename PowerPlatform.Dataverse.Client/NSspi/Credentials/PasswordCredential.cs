// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/Credentials/PasswordCredential.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class PasswordCredential
// 主要成員：Init
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Runtime.CompilerServices、System.Text
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace NSspi.Credentials
{
    /// <summary>
    /// Represents credentials acquired by providing a username, password, and domain.
    /// </summary>
    public class PasswordCredential : Credential
    {
        /// <summary>
        /// Initializes a new instance of the PasswordCredential class.
        /// </summary>
        /// <remarks>
        /// It is possible to acquire a valid handle to credentials that do not provide a valid
        /// username-password combination. The username and password are not validation until the
        /// authentication cycle begins.
        /// </remarks>
        /// <param name="domain">The domain to authenticate to.</param>
        /// <param name="username">The username of the user to authenticate as.</param>
        /// <param name="password">The user's password.</param>
        /// <param name="secPackage">The SSPI security package to create credentials for.</param>
        /// <param name="use">
        /// Specify inbound when acquiring credentials for a server; outbound for a client.
        /// </param>
        public PasswordCredential( string domain, string username, string password, string secPackage, CredentialUse use )
            : base( secPackage )
        {
            NativeAuthData authData = new NativeAuthData( domain, username, password, NativeAuthDataFlag.Unicode );

            Init( authData, secPackage, use );
        }

        private void Init( NativeAuthData authData, string secPackage, CredentialUse use )
        {
            string packageName;
            TimeStamp rawExpiry = new TimeStamp();
            SecurityStatus status = SecurityStatus.InternalError;

            // -- Package --
            // Copy off for the call, since this.SecurityPackage is a property.
            packageName = this.SecurityPackage;

            this.Handle = new SafeCredentialHandle();


            // The finally clause is the actual constrained region. The VM pre-allocates any stack space,
            // performs any allocations it needs to prepare methods for execution, and postpones any
            // instances of the 'uncatchable' exceptions (ThreadAbort, StackOverflow, OutOfMemory).
            RuntimeHelpers.PrepareConstrainedRegions();
            try { }
            finally
            {
                status = CredentialNativeMethods.AcquireCredentialsHandle_AuthData(
                   null,
                   packageName,
                   use,
                   IntPtr.Zero,
                   ref authData,
                   IntPtr.Zero,
                   IntPtr.Zero,
                   ref this.Handle.rawHandle,
                   ref rawExpiry
               );
            }

            if( status != SecurityStatus.OK )
            {
                throw new SSPIException( "Failed to call AcquireCredentialHandle", status );
            }

            this.Expiry = rawExpiry.ToDateTime();
        }
    }
}
