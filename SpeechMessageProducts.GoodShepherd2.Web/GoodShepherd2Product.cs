/*
 * 檔案整體說明
 *
 * 目的與定位：提供好牧人 2.0 Web 組合根使用的固定產品識別，讓 ToolUtility 與後續
 * 共用元件在完整隔離鍵中使用 GoodShepherd2，避免沿用既有 GoodShepherd 產品名稱。
 *
 * 主要功能與流程：只輸出編譯期常數 Name，由受信任的 Program 註冊服務時傳入
 * AddToolUtility；產品名稱不從 request、路由、Session 或可變部署設定解析。
 *
 * 使用邊界與維護：本檔是純常數角色，不執行授權、組織驗證、連線或資料查詢，
 * 不擁有快取、timer、背景工作或其他可釋放資源；真正端點守門由 StartupGuard 負責，
 * manager 與 pool 由主機容器擁有。修改時以 StartupTests 的真實 DI 隔離鍵及新舊產品
 * pool owner 比對驗證，不能只比較常數，也不能把離線結果宣稱為實機租借證據。
 */
namespace SpeechMessageProducts.GoodShepherd2.Web;

/// <summary>
/// 定義好牧人 2.0 主機的不可變產品邊界，僅供伺服器組合根註冊共用服務；
/// 此識別是連線分割鍵的一部分，不構成使用者、租戶或組織的授權證明。
/// </summary>
public static class GoodShepherd2Product
{
    /// <summary>
    /// 好牧人 2.0 專用的固定產品識別；組合根傳給 ToolUtility 後，完整連線鍵必須
    /// 與舊 GoodShepherd 產品不同，不能由 request 或組態覆寫而跨產品重用 client。
    /// </summary>
    public const string Name = "GoodShepherd2";
}
