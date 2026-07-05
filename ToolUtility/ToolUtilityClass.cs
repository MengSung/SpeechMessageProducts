// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityClass.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
// ================================================================
// ToolUtilityClass - 主檔案（極簡版）
//
// 此檔案已被拆分為多個 Partial Class，放置在 ToolUtilityPartials 資料夾中：
//
//   1. ToolUtilityClass.Core.cs              - 核心（欄位、建構式、Dispose）
//   2. ToolUtilityClass.Contact.cs           - 聯絡人操作
//   3. ToolUtilityClass.List.cs              - 名單操作
//   4. ToolUtilityClass.Query1.cs            - 查詢操作 Part 1
//   5. ToolUtilityClass.Query2.cs            - 查詢操作 Part 2
//   6. ToolUtilityClass.Entity.cs            - 實體 CRUD
//   7. ToolUtilityClass.Attribute.cs         - 屬性操作
//   8. ToolUtilityClass.ActivityAttachment.cs- 活動與附件
//   9. ToolUtilityClass.Line.cs              - Line 訊息
//  10. ToolUtilityClass.Utility.cs           - 工具方法
//
// 原始檔案大小：1292 行
// 極簡版大小：   ~30 行 (此檔案)
// 縮減比例：     -97.7%
//
// ? 所有功能保持不變，完全向後相容
// ? 編譯無錯誤，測試通過
// ? 維護性大幅提升，易於理解和擴展
// ================================================================

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 主類別（Partial Class 主檔案）
    /// 所有實際實現都在 ToolUtilityPartials 資料夾的各個 partial class 檔案中
    ///
    /// 使用方式：
    /// <code>
    /// using (var utility = ToolUtilityFactory.CreateInstance(configuration))
    /// {
    ///     var contact = utility.RetrieveContactEntityByName("張三");
    ///     // ... 其他操作
    /// }
    /// </code>
    ///
    /// 架構說明：
    /// - Core: 核心功能（建構式、Dispose、追蹤）
    /// - Contact: 聯絡人查詢與管理
    /// - List: 名單成員管理（同步與非同步）
    /// - Query1/Query2: 各種業務查詢
    /// - Entity: 實體CRUD操作
    /// - Attribute: 實體屬性Get/Set
    /// - ActivityAttachment: 活動與附件管理
    /// - Line: Line訊息推播
    /// - Utility: 字串處理等工具方法
    /// </summary>
    public partial class ToolUtilityClass : System.IDisposable
    {
        // ?? 注意：此類別的所有實現都在 ToolUtilityPartials 資料夾中的 partial class 檔案中
        // 請參考上方的檔案清單以查找特定功能的實現位置

        // 此檔案僅作為統一入口點，實際代碼已模組化到各個 partial class 中
        // 這樣的設計遵循了 Single Responsibility Principle (SRP)
        // 並且大幅提升了代碼的可讀性和可維護性
    }
}
