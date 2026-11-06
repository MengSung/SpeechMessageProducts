/*
 * 檔案整體說明
 *
 * 目的與定位：net10 host 的啟動檢查入口；現有 ChurchReport 已呼叫，階段 2 Hosting 必須沿用，不暗改 runtime 設定。 使用 Microsoft 官方 SortVersion 判定，
 * 不能只看環境變數，因 runtimeconfig 與 app-local ICU 亦會決定實際模式。 無靜態可變狀態、檔案、timer 或外部資源；驗證失敗由啟動邊界通知並拒絕啟動。
 *
 * 主要功能與流程：
 * 本檔宣告 GlobalizationModeGuard。
 * - 真實 ICU 的 SortId 前四 bytes 應為非零且等於 FullVersion；NLS／Invariant 均拒絕。 不依賴私有 runtime 欄位或僅能讀取一次的自訂旗標，部署改模式後必須重啟 host 
 * 再驗。
 * 主要操作入口包含 ValidateIcuMode。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Contracts/ChurchClockStartupTests.cs、
 * SpeechMessage.Localization.Tests/ChurchClockTests.cs、SpeechMessageProducts.ChurchReport/Program.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
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
