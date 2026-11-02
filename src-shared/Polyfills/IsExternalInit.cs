namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// 提供 net48 缺少的唯初始化成員中繼資料標記，讓共用原始碼能使用 init 與 record。
    /// 此定義僅由嚴格模式的 net48 專案連結編譯，不覆蓋 net10 的框架型別。
    /// 標記沒有執行期狀態、配置或資源生命週期，不保存任何使用者或教會資料。
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
