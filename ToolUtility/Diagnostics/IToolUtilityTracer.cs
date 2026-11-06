/*
 * 檔案整體說明
 *
 * 目的與定位：追蹤輸出的寫入契約。
 *
 * 主要功能與流程：
 * 本檔宣告 IToolUtilityTracer。
 * 資料／選項欄位包含 IToolUtilityTracer，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Models/ListManagerIntegratePublicationTests.cs、
 * ToolUtility.Dataverse.Tests/RunCServiceGraphTests.cs、
 * ToolUtility.Dataverse.Tests/ScopedToolUtilityConsumerTests.cs、
 * ToolUtility.Dataverse.Tests/ToolUtilityClassScopedLifetimeTests.cs、
 * ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
