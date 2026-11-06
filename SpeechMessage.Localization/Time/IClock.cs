/*
 * 檔案整體說明
 *
 * 目的與定位：兩產品共用的最小 UTC 瞬間來源；不得含伺服器 local、教會、身分或請求狀態。 注入來源由 host／測試擁有，ChurchClock 僅借用，不建立 timer、背景工作或 Dispose 外部 
 * owner。
 *
 * 主要功能與流程：
 * 本檔宣告 IClock、SystemClock、TimeProviderClock。
 * - 取得 UTC 瞬間；消費端仍正規化 offset，不能將此值當生日純日期。
 * - 取得 UTC 當下；net10 統一經 TimeProvider.System，Framework 無此 API 才直接讀 UTC。
 * - host 擁有的固定時間來源；最多保留至轉接物件釋放，不延長任何 request 或教會資料。
 * - 借用明確來源，拒絕 null 而不偷偷改用系統時間，維持測試與主機 owner 的時間契約。
 * 資料／選項欄位包含 IClock、UtcNow，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization.Tests/ChurchClockTests.cs、SpeechMessage.Localization/Time/ChurchClock.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace SpeechMessage.Localization
{
    /// <summary>
    /// 兩產品共用的最小 UTC 瞬間來源；不得含伺服器 local、教會、身分或請求狀態。
    /// 注入來源由 host／測試擁有，ChurchClock 僅借用，不建立 timer、背景工作或 Dispose 外部 owner。
    /// </summary>
    public interface IClock
    {
        /// <summary>取得 UTC 瞬間；消費端仍正規化 offset，不能將此值當生日純日期。</summary>
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>無狀態系統 UTC 來源；只讀當次瞬間，沒有快取、訂閱或可釋放資源，可安全跨教會共用。</summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>取得 UTC 當下；net10 統一經 TimeProvider.System，Framework 無此 API 才直接讀 UTC。</summary>
        public DateTimeOffset UtcNow
        {
            get
            {
#if NETFRAMEWORK
                return DateTimeOffset.UtcNow;
#else
                return TimeProvider.System.GetUtcNow();
#endif
            }
        }
    }

#if !NETFRAMEWORK
    /// <summary>
    /// net10 的 TimeProvider 薄轉接；只借用 GetUtcNow，不呼叫 CreateTimer、不擁有來源生命週期。
    /// host 應提供無請求／租戶狀態的來源；轉接與 ChurchClock 不保存任何呼叫結果。
    /// </summary>
    public sealed class TimeProviderClock : IClock
    {
        /// <summary>host 擁有的固定時間來源；最多保留至轉接物件釋放，不延長任何 request 或教會資料。</summary>
        private readonly TimeProvider timeProvider;

        /// <summary>借用明確來源，拒絕 null 而不偷偷改用系統時間，維持測試與主機 owner 的時間契約。</summary>
        /// <param name="timeProvider">由 host／測試擁有、無身分狀態的時間來源。</param>
        public TimeProviderClock(TimeProvider timeProvider)
        {
            this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <summary>直接讀取來源並正規化 UTC，不快取；來源真正故障原樣交由 host 共用告警邊界處理。</summary>
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow().ToUniversalTime();
    }
#endif
}
