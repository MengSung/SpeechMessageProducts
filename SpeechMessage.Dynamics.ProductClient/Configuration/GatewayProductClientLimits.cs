namespace SpeechMessage.Dynamics.ProductClient.Configuration;

/// <summary>
/// ProductClient 啟動驗證與執行期防線共用的不可變上限。
/// 數值集中定義可避免驗證器接受某個設定，但執行期又使用不同邊界。
/// </summary>
internal static class GatewayProductClientLimits
{
    public const int MinimumResponseBytes = 1024;
    public const int MaximumResponseBytes = 8_388_608;
}
