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
