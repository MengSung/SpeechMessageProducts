#if !NETFRAMEWORK
using System;
using System.Globalization;

namespace SpeechMessage.Localization
{
    /// <summary>
    /// net10 host 的啟動檢查入口；現有 ChurchReport 已呼叫，階段 2 Hosting 必須沿用，不暗改 runtime 設定。
    /// 使用 Microsoft 官方 SortVersion 判定，不能只看環境變數，因 runtimeconfig 與 app-local ICU 亦會決定實際模式。
    /// 無靜態可變狀態、檔案、timer 或外部資源；驗證失敗由啟動邊界通知並拒絕啟動。
    /// </summary>
    public static class GlobalizationModeGuard
    {
        /// <summary>
        /// 真實 ICU 的 SortId 前四 bytes 應為非零且等於 FullVersion；NLS／Invariant 均拒絕。
        /// 不依賴私有 runtime 欄位或僅能讀取一次的自訂旗標，部署改模式後必須重啟 host 再驗。
        /// </summary>
        public static void ValidateIcuMode()
        {
            var version = CultureInfo.InvariantCulture.CompareInfo.Version;
            var bytes = version.SortId.ToByteArray();
            var identifier = bytes[3] << 24 | bytes[2] << 16 | bytes[1] << 8 | bytes[0];
            if (identifier == 0 || identifier != version.FullVersion)
                throw new InvalidOperationException("ICU globalization mode is required.");
        }
    }
}
#endif
