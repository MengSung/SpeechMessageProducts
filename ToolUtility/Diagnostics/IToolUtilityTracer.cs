// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Diagnostics/IToolUtilityTracer.cs
// 所屬區塊：ToolUtility 診斷層，負責追蹤輸出的資源擁有權與寫入契約。
// 檔案責任：定義追蹤寫入契約，使追蹤資源的擁有權可以與資料存取物件的生命週期分離。
// 主要型別：interface IToolUtilityTracer
// 主要成員：Write
// 引用命名空間：System.Diagnostics
// 閱讀路徑：先看 Write 的參數說明，特別是為何由呼叫端提供 StackFrame。
// 維護重點：此介面的實作必須是程序級單一實例；追蹤資源不可隨請求建立或釋放。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig。
// ============================================================================
using System.Diagnostics;

namespace ToolUtilityNameSpace.Diagnostics
{
    /// <summary>
    /// 追蹤輸出的寫入契約。
    /// </summary>
    /// <remarks>
    /// 存在理由：追蹤所需的檔案串流與 <see cref="TraceListener"/> 是「程序級」資源
    /// （<see cref="Trace.Listeners"/> 為行程內的靜態集合），而資料存取物件應為
    /// 「request 範圍」。兩者混在同一型別上，會使該型別無法安全地改變生命週期 ——
    /// 每建立一個實例就會再向全域集合加入一個 listener，造成無界成長與重複輸出。
    /// 本介面把追蹤職責獨立出來，其實作必須註冊為 Singleton。
    ///
    /// 資源最大生命週期：實作所持有的串流與 listener 存活至應用程式關閉，
    /// 由 DI 容器於程序結束時釋放。
    ///
    /// 隔離保證：本介面不接收也不保存任何使用者、Session 或請求層級的狀態；
    /// 傳入的訊息由呼叫端自行決定，實作僅負責輸出。
    /// </remarks>
    public interface IToolUtilityTracer
    {
        /// <summary>
        /// 依層級輸出一筆追蹤紀錄。
        /// </summary>
        /// <param name="totalLevel">呼叫端設定的總層級。</param>
        /// <param name="qualifiedLevel">達到此層級才輸出。</param>
        /// <param name="message">要輸出的訊息內容。</param>
        /// <param name="callerFrame">
        /// 呼叫端的堆疊框架。
        /// 由呼叫端提供而非在實作內自行擷取，是為了讓輸出的 StackTrace 仍指向
        /// 「原始呼叫者」；若在實作內擷取，框架深度會因為多了一層委派而位移，
        /// 導致既有日誌的內容改變。
        /// </param>
        void Write(int totalLevel, int qualifiedLevel, string message, StackFrame callerFrame);
    }
}
