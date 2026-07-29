// ============================================================================
// 檔案：SpeechMessage.Dynamics.SmokeTests/LiveSmokePlaceholderTests.cs
// 目的：為 CE 8.2 / 9.1 真實煙測預留專案入口。
//
// 保母教學：
// - 沒有設定環境變數時，這個測試只驗證「預設關閉 live smoke」。
// - 之後接上真實環境時，再把 live call 補進來並改成真正斷言。
// ============================================================================

namespace SpeechMessage.Dynamics.SmokeTests;

public sealed class LiveSmokePlaceholderTests
{
    [Fact]
    public void Live_smoke_is_disabled_by_default()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_ENABLED"),
            "1",
            StringComparison.Ordinal);

        // 預設必須關閉，避免 CI 在沒有 CE 環境時亂打外部系統。
        Assert.False(enabled);
    }
}
