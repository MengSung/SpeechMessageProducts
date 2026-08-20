// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs
// 所屬區塊：ChurchReport Session 隔離診斷與效能量測保護層。
// 檔案責任：提供一個只承載程序級診斷旗標的 Debug-only 開關，讓 Session GUID、
//           BoundUserId、X-Forwarded-For、User-Agent 與快取 key 組成細節等逐步除錯輸出，
//           在不刪除原始診斷程式碼的前提下預設完全停用。
// 安全與隔離不變量：此型別只保存一個 volatile Boolean；不得加入 request、Session、
//           使用者、租戶、credential、token、traceId 或任何可變請求資料。開關切換只改變
//           是否允許輸出，不得改變 Session key、指紋、dirty flag 或任何商業邏輯結果。
// 效能不變量：預設 false 時，呼叫端只需一次 volatile read，且不得建立 writer、同步磁碟
//           I/O、背景工作或無界集合；因此量測工具不會把逐步診斷成本混入 request phase。
// 編譯防線：#if DEBUG 使正式 Release 不包含此診斷旗標；Release 組態由編譯器移除呼叫端
//           的輸出實作，外部設定不得繞過這個第二層停用保護。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
#if DEBUG
namespace ChurchReport.Diagnostics
{
    /// <summary>
    /// 控制 Session 隔離除錯訊息是否可進入 System.Diagnostics.Debug 輸出管線。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 這是程序級、無使用者資料的設定旗標，不是 Session 或 request 狀態容器。它只允許
    /// 維護人員在 Debug 除錯時重新取得既有的逐步診斷，不得被用來保存目前登入者、租戶、
    /// correlation id、連線、租約或其他跨請求可變資料。
    /// </para>
    /// <para>
    /// 預設值刻意為 <see langword="false"/>。既有診斷行可能包含 Session GUID、BoundUserId、
    /// 原始轉發 IP、User-Agent、指紋輸入與快取 key；若預設開啟，會造成敏感資料留存以及
    /// 每行同步 Debug listener I/O。呼叫端先檢查此旗標，再由共用 helper 寫入，確保關閉時
    /// 不會改變任何資料流程，只移除診斷副作用。
    /// </para>
    /// <para>
    /// <see langword="volatile"/> 保證不同 request 執行緒可觀察到最新程序設定，而不需要
    /// 以鎖包住每一筆輸出。此欄位不得承載 request、使用者或租戶狀態；程序停止時不需要
    /// 釋放任何資源，因為它不擁有 listener、stream、timer、task 或 cancellation registration。
    /// </para>
    /// </remarks>
    public static class SessionDiagnosticsSwitch
    {
        /// <summary>
        /// 取得或設定是否允許逐步 Session 診斷輸出；預設停用以避免量測污染與敏感資料留存。
        /// </summary>
        /// <remarks>
        /// Startup 只可依受信任的 Debug <c>DiagnosticTraceOptions.Enabled</c> 同步此旗標；
        /// 不得從 request、Session、query string、form、cookie、租戶欄位或使用者輸入指派。
        /// Release 不會編譯此欄位，因此即使外部部署設定誤設 enabled，也沒有 runtime 路徑
        /// 可以重新建立這些逐步輸出。
        /// </remarks>
        public static volatile bool Enabled = false;
    }
}
#endif
