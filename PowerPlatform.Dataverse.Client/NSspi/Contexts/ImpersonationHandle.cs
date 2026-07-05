// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/Contexts/ImpersonationHandle.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class ImpersonationHandle
// 主要成員：Dispose
// 引用命名空間：System、System.Security.Principal、System.Threading
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Security.Principal;
using System.Threading;

namespace NSspi.Contexts
{
    /// <summary>
    /// Represents impersonation performed on a server on behalf of a client.
    /// </summary>
    /// <remarks>
    /// The handle controls the lifetime of impersonation, and will revert the impersonation
    /// if it is disposed, or if it is finalized ie by being leaked and garbage collected.
    ///
    /// If the handle is accidentally leaked while operations are performed on behalf of the user,
    /// impersonation may be reverted at any arbitrary time, perhaps during those operations.
    /// This may lead to operations being performed in the security context of the server,
    /// potentially leading to security vulnerabilities.
    /// </remarks>
    public class ImpersonationHandle : IDisposable
    {
        private readonly ServerContext server;

        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the ImpersonationHandle. Does not perform impersonation.
        /// </summary>
        /// <param name="server">The server context that is performing impersonation.</param>
        internal ImpersonationHandle( ServerContext server )
        {
            this.server = server;
            this.disposed = false;
        }

        /// <summary>
        /// Finalizes the ImpersonationHandle by reverting the impersonation.
        /// </summary>
        ~ImpersonationHandle()
        {
            Dispose( false );
        }

        /// <summary>
        /// Reverts impersonation.
        /// </summary>
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }

        /// <summary>
        /// Reverts impersonation.
        /// </summary>
        /// <param name="disposing">True if being disposed, false if being finalized.</param>
        private void Dispose( bool disposing )
        {
            // This implements a variant of the typical dispose pattern. Always try to revert
            // impersonation, even if finalizing. Don't do anything if we're already reverted.

            if( this.disposed == false )
            {
                this.disposed = true;

                // Just in case the reference is being pulled out from under us, pull a stable copy
                // of the reference while we're null-checking.
                var serverCopy = this.server;

                if( serverCopy != null && serverCopy.Disposed == false )
                {
                    serverCopy.RevertImpersonate();
                }
            }
        }
    }
}