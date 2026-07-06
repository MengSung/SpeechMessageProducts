// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/ProfilingSwitch.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 ProfilingSwitch 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class ProfilingSwitch
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// 剖析總開關。僅 Debug 編譯存在；再加 runtime 旗標（預設關）雙重保險，
    /// 即使正式機誤以 Debug 部署，未開設定就不剖析。由 Startup 從 Profiling:Enabled 設定。
    /// </summary>
    public static class ProfilingSwitch
    {
        public static volatile bool Enabled = false;
    }
}
#endif
