/*
 * 檔案整體說明
 *
 * 目的與定位：Theme 設定快照，提供 View 層統一取得主題資訊。
 *
 * 主要功能與流程：
 * 本檔宣告 ThemeSettings。
 * 資料／選項欄位包含 ThemeSettings、CurrentTheme、CurrentThemeCssClass，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Filters/ThemeViewDataFilter.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
namespace ChurchReport.Services.Theme
{
    /// <summary>
    /// Theme 設定快照，提供 View 層統一取得主題資訊。
    /// </summary>
    public sealed class ThemeSettings
    {
        public ThemeSettings(string currentTheme, string currentThemeCssClass)
        {
            CurrentTheme = currentTheme;
            CurrentThemeCssClass = currentThemeCssClass;
        }

        /// <summary>
        /// 目前啟用的主題名稱。
        /// </summary>
        public string CurrentTheme { get; }

        /// <summary>
        /// 目前啟用主題對應的 CSS class。
        /// </summary>
        public string CurrentThemeCssClass { get; }
    }
}
