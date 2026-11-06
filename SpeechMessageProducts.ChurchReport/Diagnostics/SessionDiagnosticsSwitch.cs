/*
 * 檔案整體說明
 *
 * 目的與定位：控制 Session 隔離除錯訊息是否可進入 System.Diagnostics.Debug 輸出管線。
 *
 * 主要功能與流程：
 * 本檔宣告 SessionDiagnosticsSwitch。
 * - 取得或設定是否允許逐步 Session 診斷輸出；預設停用以避免量測污染與敏感資料留存。
 * 資料／選項欄位包含 SessionDiagnosticsSwitch、Enabled，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs、
 * SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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
        /// Startup 只可依受信任的 Debug <c>DiagnosticTraceOptions.SessionVerbose</c> 同步此旗標；
        /// 不得從 request、Session、query string、form、cookie、租戶欄位或使用者輸入指派。
        /// Release 不會編譯此欄位，因此即使外部部署設定誤設 enabled，也沒有 runtime 路徑
        /// 可以重新建立這些逐步輸出。
        /// </remarks>
        public static volatile bool Enabled = false;
    }
}
#endif
