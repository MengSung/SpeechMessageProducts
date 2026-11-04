namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// 為 .NET Framework 目標提供條件式非空值契約，讓雙目標 API 可向編譯器描述 Try 模式的輸出狀態。
    /// 此屬性只描述中繼資料，不在執行期驗證輸入，也不得用來略過真正的資料檢查。
    /// 只在 net462 與保留的 net48 測試目標連結編譯，避免與 net10 內建型別重複；每個實例只有一個不可變布林值。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>
        /// 指定方法回傳哪個布林結果時，被標註的參數可以視為非空值。
        /// 這個宣告須與方法真正的分支行為一致，否則會讓呼叫端錯誤相信空值安全性。
        /// </summary>
        /// <param name="returnValue">保證參數非空值時的方法回傳結果。</param>
        public NotNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }

        /// <summary>
        /// 取得使被標註參數成立非空值契約的方法回傳結果。
        /// 建構後不可變，沒有跨請求或跨使用者的可變狀態。
        /// </summary>
        public bool ReturnValue { get; }
    }
}
