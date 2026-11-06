using System;

namespace SpeechMessage.Localization
{
    /// <summary>
    /// 尚未解析 offset 的教會牆上時間，強制 Unspecified 與明確 IANA；建構不是教會授權。
    /// caller 必須先由伺服器授權並取得該教會時區；DST 的模糊／無效時段由 ToUtc 再拒絕。
    /// 僅有不可變純值，不保存教會實體、Session、HttpContext 或連線，生命週期由單次操作決定。
    /// </summary>
    public sealed class LocalDateTimeOfChurch
    {
        /// <summary>建立牆上時間，不接受 UTC／伺服器 Local，也不自動修正未知、大小寫或前後空白的 ID。</summary>
        /// <param name="value">Kind 必須是 Unspecified，不是生日純日期的替代型別。</param>
        /// <param name="ianaTimeZoneId">伺服器已授權教會的受支援 IANA 時區。</param>
        public LocalDateTimeOfChurch(DateTime value, string ianaTimeZoneId)
        {
            if (value.Kind != DateTimeKind.Unspecified) throw new ArgumentException("Church local time must be Unspecified.", nameof(value));
            TimeZoneLookup.ValidateId(ianaTimeZoneId);
            Value = value;
            IanaTimeZoneId = ianaTimeZoneId;
        }

        /// <summary>未帶 offset 的不可變牆上時間；不能直接呼叫 ToUniversalTime 依伺服器時區推論。</summary>
        public DateTime Value { get; }

        /// <summary>該值所屬的明確 IANA ID；不含 tenant 或 credential，也不構成授權證據。</summary>
        public string IanaTimeZoneId { get; }
    }

    /// <summary>集中時區邊界；只有固定 CLDR 公共映射，禁止以 caller ID 建立無界快取或回落伺服器 local。</summary>
    internal static class TimeZoneLookup
    {
        /// <summary>在呼叫 OS 之前以有界固定映射拒絕未知與 Windows ID；不在錯誤本文輸出 caller 值。</summary>
        /// <param name="iana">來源於已授權教會設定的 ID；建構值物件不代表已授權。</param>
        /// <returns>對應 Windows ID，供 Framework 使用。</returns>
        internal static string ValidateId(string iana)
        {
            if (string.IsNullOrEmpty(iana) || iana.Length > 128 || !IanaWindowsZones.TryGetWindowsId(iana, out var windows))
                throw new ArgumentException("A supported IANA time zone is required.", nameof(iana));
            return windows;
        }

        /// <summary>Framework 使用內建 Windows 對照；net10 直接查 IANA；OS 缺規則原樣失敗，不猜預設時區。</summary>
        /// <param name="iana">先經固定公共映射驗證的 IANA。</param>
        /// <returns>由 BCL 管理、無教會或請求狀態的唯讀時區規則。</returns>
        internal static TimeZoneInfo Resolve(string iana)
        {
            var windows = ValidateId(iana);
#if NETFRAMEWORK
            return TimeZoneInfo.FindSystemTimeZoneById(windows);
#else
            return TimeZoneInfo.FindSystemTimeZoneById(iana);
#endif
        }
    }
}
