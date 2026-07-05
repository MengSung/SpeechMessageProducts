// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/PackageSupport.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class PackageSupport
// 主要成員：GetPackageCapabilities、EnumeratePackages
// 引用命名空間：System、System.Runtime.CompilerServices、System.Runtime.InteropServices
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NSspi
{
    /// <summary>
    /// Queries information about security packages.
    /// </summary>
    public static class PackageSupport
    {
        /// <summary>
        /// Returns the properties of the named package.
        /// </summary>
        /// <param name="packageName">The name of the package.</param>
        /// <returns></returns>
        public static SecPkgInfo GetPackageCapabilities( string packageName )
        {
            SecPkgInfo info;
            SecurityStatus status = SecurityStatus.InternalError;

            IntPtr rawInfoPtr;

            rawInfoPtr = new IntPtr();
            info = new SecPkgInfo();

            RuntimeHelpers.PrepareConstrainedRegions();
            try
            { }
            finally
            {
                status = NativeMethods.QuerySecurityPackageInfo( packageName, ref rawInfoPtr );

                if( rawInfoPtr != IntPtr.Zero )
                {
                    try
                    {
                        if( status == SecurityStatus.OK )
                        {
                            // This performs allocations as it makes room for the strings contained in the SecPkgInfo class.
                            Marshal.PtrToStructure( rawInfoPtr, info );
                        }
                    }
                    finally
                    {
                        NativeMethods.FreeContextBuffer( rawInfoPtr );
                    }
                }
            }

            if( status != SecurityStatus.OK )
            {
                throw new SSPIException( "Failed to query security package provider details", status );
            }

            return info;
        }

        /// <summary>
        /// Returns a list of all known security package providers and their properties.
        /// </summary>
        /// <returns></returns>
        public static SecPkgInfo[] EnumeratePackages()
        {
            SecurityStatus status = SecurityStatus.InternalError;
            SecPkgInfo[] packages = null;
            IntPtr pkgArrayPtr;
            IntPtr pkgPtr;
            int numPackages = 0;
            int pkgSize = Marshal.SizeOf( typeof( SecPkgInfo ) );

            pkgArrayPtr = new IntPtr();

            RuntimeHelpers.PrepareConstrainedRegions();
            try { }
            finally
            {
                status = NativeMethods.EnumerateSecurityPackages( ref numPackages, ref pkgArrayPtr );

                if( pkgArrayPtr != IntPtr.Zero )
                {
                    try
                    {
                        if( status == SecurityStatus.OK )
                        {
                            // Bwooop Bwooop Alocation Alert
                            // 1) We allocate the array
                            // 2) We allocate the individual elements in the array (they're class objects).
                            // 3) We allocate the strings in the individual elements in the array when we
                            //    call Marshal.PtrToStructure()

                            packages = new SecPkgInfo[numPackages];

                            for( int i = 0; i < numPackages; i++ )
                            {
                                packages[i] = new SecPkgInfo();
                            }

                            for( int i = 0; i < numPackages; i++ )
                            {
                                pkgPtr = IntPtr.Add( pkgArrayPtr, i * pkgSize );

                                Marshal.PtrToStructure( pkgPtr, packages[i] );
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.FreeContextBuffer( pkgArrayPtr );
                    }
                }
            }

            if( status != SecurityStatus.OK )
            {
                throw new SSPIException( "Failed to enumerate security package providers", status );
            }

            return packages;
        }
    }
}