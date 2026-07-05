// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/Credentials/Credential.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class Credential
// 主要成員：Dispose、CheckLifecycle、PackageInfo、SecurityPackage、PrincipleName、Expiry、Handle
// 引用命名空間：System、System.Runtime.CompilerServices、System.Runtime.InteropServices
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NSspi.Credentials
{
    /// <summary>
    /// Provides access to the pre-existing credentials of a security principle.
    /// </summary>
    public class Credential : IDisposable
    {
        /// <summary>
        /// The name of the security package that controls the credential.
        /// </summary>
        private readonly string securityPackage;

        /// <summary>
        /// Whether the Credential has been disposed.
        /// </summary>
        private bool disposed;

        /// <summary>
        /// A safe handle to the credential's handle.
        /// </summary>
        private SafeCredentialHandle safeCredHandle;

        /// <summary>
        /// The UTC time the credentials expire.
        /// </summary>
        private DateTime expiry;

        /// <summary>
        /// Initializes a new instance of the Credential class.
        /// </summary>
        /// <param name="package">The security package to acquire the credential from.</param>
        public Credential( string package )
        {
            this.securityPackage = package;

            this.disposed = false;
            this.expiry = DateTime.MinValue;
            this.PackageInfo = PackageSupport.GetPackageCapabilities( this.SecurityPackage );
        }

        /// <summary>
        /// Gets metadata for the security package associated with the credential.
        /// </summary>
        public SecPkgInfo PackageInfo { get; private set; }

        /// <summary>
        /// Gets the name of the security package that owns the credential.
        /// </summary>
        public string SecurityPackage
        {
            get
            {
                CheckLifecycle();

                return this.securityPackage;
            }
        }

        /// <summary>
        /// Returns the User Principle Name of the credential. Depending on the underlying security
        /// package used by the credential, this may not be the same as the Down-Level Logon Name
        /// for the user.
        /// </summary>
        public string PrincipleName
        {
            get
            {
                QueryNameAttribCarrier carrier;
                SecurityStatus status;
                string name = null;
                bool gotRef = false;

                CheckLifecycle();

                status = SecurityStatus.InternalError;
                carrier = new QueryNameAttribCarrier();

                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    this.safeCredHandle.DangerousAddRef( ref gotRef );
                }
                catch( Exception )
                {
                    if( gotRef == true )
                    {
                        this.safeCredHandle.DangerousRelease();
                        gotRef = false;
                    }
                    throw;
                }
                finally
                {
                    if( gotRef )
                    {
                        status = CredentialNativeMethods.QueryCredentialsAttribute_Name(
                            ref this.safeCredHandle.rawHandle,
                            CredentialQueryAttrib.Names,
                            ref carrier
                        );

                        this.safeCredHandle.DangerousRelease();

                        if( status == SecurityStatus.OK && carrier.Name != IntPtr.Zero )
                        {
                            try
                            {
                                name = Marshal.PtrToStringUni( carrier.Name );
                            }
                            finally
                            {
                                NativeMethods.FreeContextBuffer( carrier.Name );
                            }
                        }
                    }
                }

                if( status.IsError() )
                {
                    throw new SSPIException( "Failed to query credential name", status );
                }

                return name;
            }
        }

        /// <summary>
        /// Gets the UTC time the credentials expire.
        /// </summary>
        public DateTime Expiry
        {
            get
            {
                CheckLifecycle();

                return this.expiry;
            }

            protected set
            {
                CheckLifecycle();

                this.expiry = value;
            }
        }

        /// <summary>
        /// Gets a handle to the credential.
        /// </summary>
        public SafeCredentialHandle Handle
        {
            get
            {
                CheckLifecycle();

                return this.safeCredHandle;
            }

            protected set
            {
                CheckLifecycle();

                this.safeCredHandle = value;
            }
        }

        /// <summary>
        /// Releases all resources associated with the credential.
        /// </summary>
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }

        /// <summary>
        /// Releases all resources associted with the credential.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose( bool disposing )
        {
            if( this.disposed == false )
            {
                if( disposing )
                {
                    this.safeCredHandle.Dispose();
                }

                this.disposed = true;
            }
        }

        private void CheckLifecycle()
        {
            if( this.disposed )
            {
                throw new ObjectDisposedException( "Credential" );
            }
        }
    }
}