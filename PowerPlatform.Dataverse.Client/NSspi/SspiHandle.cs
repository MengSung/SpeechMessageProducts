// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/NSspi/SspiHandle.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：struct RawSspiHandle、class SafeSspiHandle
// 主要成員：IsZero、SetInvalid、ReleaseHandle、IsInvalid
// 引用命名空間：System、System.Runtime.ConstrainedExecution、System.Runtime.InteropServices
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

namespace NSspi
{
    /// <summary>
    /// Represents the raw structure for any handle created for the SSPI API, for example, credential
    /// handles, context handles, and security package handles. Any SSPI handle is always the size
    /// of two native pointers.
    /// </summary>
    /// <remarks>
    /// The documentation for SSPI handles can be found here:
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa380495(v=vs.85).aspx
    ///
    /// This class is not reference safe - if used directly, or referenced directly, it may be leaked,
    /// or subject to finalizer races, or any of the hundred of things SafeHandles were designed to fix.
    /// Do not directly use this class - use only though SafeHandle wrapper objects. Any reference needed
    /// to this handle for performing work (InitializeSecurityContext, eg) should be performed a CER
    /// that employs handle reference counting across the native API invocation.
    /// </remarks>
    [StructLayout( LayoutKind.Sequential, Pack = 1 )]
    internal struct RawSspiHandle
    {
        private IntPtr lowPart;
        private IntPtr highPart;

        /// <summary>
        /// Returns whether or not the handle is set to the default, empty value.
        /// </summary>
        /// <returns></returns>
        public bool IsZero()
        {
            return this.lowPart == IntPtr.Zero && this.highPart == IntPtr.Zero;
        }

        /// <summary>
        /// Sets the handle to an invalid value.
        /// </summary>
        /// <remarks>
        /// This method is executed in a CER during handle release.
        /// </remarks>
        [ReliabilityContract( Consistency.WillNotCorruptState, Cer.Success )]
        public void SetInvalid()
        {
            this.lowPart = IntPtr.Zero;
            this.highPart = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Safely encapsulates a raw handle used in the SSPI api.
    /// </summary>
    public abstract class SafeSspiHandle : SafeHandle
    {
        internal RawSspiHandle rawHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="SafeSspiHandle"/> class.
        /// </summary>
        protected SafeSspiHandle()
            : base( IntPtr.Zero, true )
        {
            this.rawHandle = new RawSspiHandle();
        }

        /// <summary>
        /// Gets whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid
        {
            get { return IsClosed || this.rawHandle.IsZero(); }
        }

        /// <summary>
        /// Marks the handle as no longer being in use.
        /// </summary>
        /// <returns></returns>
        [ReliabilityContract( Consistency.WillNotCorruptState, Cer.Success )]
        protected override bool ReleaseHandle()
        {
            this.rawHandle.SetInvalid();
            return true;
        }
    }
}