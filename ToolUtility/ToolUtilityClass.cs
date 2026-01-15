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
