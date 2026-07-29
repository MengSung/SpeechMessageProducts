// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Execution/DynamicsExecutionMode.cs
// 目的：定義產品要用哪一種 Dynamics 連線邊界。
//
// 保母教學：
// 1. 我們最終不要產品直接拿 legacy CRM SDK client interfaces。
// 2. 產品只在「Gateway」或「Embedded」兩種模式中選一個。
// 3. Gateway = 呼叫共用 Web Service（預設正式環境）。
// 4. Embedded = 產品程序內嵌同一套受控操作（方便 Visual Studio 本機除錯）。
// 5. 兩種模式對外操作契約必須一致，不能各寫各的 CRM API。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Execution;

/// <summary>
/// 產品執行 Dynamics 操作時可選的模式。
/// </summary>
public enum DynamicsExecutionMode
{
    /// <summary>
    /// 透過集中式 Gateway Web Service 執行。正式環境預設值。
    /// </summary>
    Gateway = 0,

    /// <summary>
    /// 在產品程序內嵌同一套受控操作主機。適合 VS 本機開發或明確允許的隔離部署。
    /// </summary>
    Embedded = 1
}
