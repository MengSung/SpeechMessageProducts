using System;

namespace SpeechMessage.Localization
{
    /// <summary>
    /// 明確區分 UTC 瞬間與教會牆上時間的跨產品契約；每次傳入伺服器已授權的教會時區。
    /// 不讀伺服器 local 或全域文化，不把語言選擇當成時區、教會或授權依據。
    /// </summary>
    public interface IChurchClock
    {
        /// <summary>取得 offset 為零的 UTC 瞬間；來源故障原樣向 host 傳播。</summary>
        DateTimeOffset UtcNow { get; }

        /// <summary>UTC Kind 瞬間轉成明確教會的 Unspecified local；未知時區或超日期範圍拒絕。</summary>
        DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId);

        /// <summary>使用同一次 UTC 讀值求教會當地午夜日期，回傳 Unspecified，而非伺服器 Today。</summary>
        DateTime TodayInChurch(string ianaTimeZoneId);

        /// <summary>將教會 local 轉為 Utc Kind；DST invalid／ambiguous 必須拒絕，不猜標準時間。</summary>
        DateTime ToUtc(LocalDateTimeOfChurch local);
    }

    /// <summary>
    /// 無教會共享狀態的同步時鐘，唯一依賴為借入的 IClock；每次的時區與結果只在操作堆疊存在。
    /// 不建立連線、timer、背景 Task、快取或訂閱；來源由外部 owner 擁有，故本型別不 Dispose 來源。
    /// 可供不同已授權教會並行使用；不得以此型別取代 host 的授權與設定驗證。
    /// </summary>
    public sealed class ChurchClock : IChurchClock
    {
        /// <summary>僅持有無租戶狀態的時間來源，最長至本時鐘釋放；不保存任何轉換輸入。</summary>
        private readonly IClock clock;

        /// <summary>使用無狀態 UTC 系統來源；net10 host 需要可控時間時應明確注入 TimeProviderClock。</summary>
        public ChurchClock() : this(new SystemClock()) { }

        /// <summary>借用明確來源，不取得其資源所有權；來源須由 host 保證可並行且不含 request／教會狀態。</summary>
        /// <param name="clock">host／測試持有的純 UTC 來源。</param>
        public ChurchClock(IClock clock) => this.clock = clock ?? throw new ArgumentNullException(nameof(clock));

        /// <summary>每次直接讀來源並正規化 offset；不吞例外、不保存失敗狀態，讓 host 共用通知原 Exception。</summary>
        public DateTimeOffset UtcNow => clock.UtcNow.ToUniversalTime();

        /// <summary>
        /// 明確 UTC 瞬間轉當地牆上時間；以 ticks 檢查溢位，避免 BCL ConvertTime 靜默夾到 Min／Max。
        /// UTC→local 即使落在秋季重複時段仍有唯一瞬間，故只有反向 ToUtc 需要拒絕模糊輸入。
        /// </summary>
        /// <param name="utc">Kind 必須為 Utc，不能猜測 Unspecified／Local。</param>
        /// <param name="ianaTimeZoneId">伺服器已授權教會的 IANA。</param>
        /// <returns>Kind 為 Unspecified 的當地時間。</returns>
        public DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId)
        {
            if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC time must have Utc kind.", nameof(utc));
            var zone = TimeZoneLookup.Resolve(ianaTimeZoneId);
            return Shift(utc, zone.GetUtcOffset(utc), DateTimeKind.Unspecified);
        }

        /// <summary>只讀一次 UTC，再按該教會時區取 Date；不改程序文化或跨呼叫記住上一間教會。</summary>
        /// <param name="ianaTimeZoneId">已授權教會的 IANA。</param>
        /// <returns>該教會當天午夜的 Unspecified 日期。</returns>
        public DateTime TodayInChurch(string ianaTimeZoneId) => ToChurchTime(UtcNow.UtcDateTime, ianaTimeZoneId).Date;

        /// <summary>
        /// 嚴格拒絕 DST 無效／模糊 local，然後才讀唯一 offset；不讓標準時間預設值隱藏歧義。
        /// 候選完全驗證後才回傳 UTC，不保存 local 物件，故 caller 故障或重試不會污染其他教會。
        /// </summary>
        /// <param name="local">不可變教會牆上時間，來源教會設定已由 host 授權。</param>
        /// <returns>Kind 為 Utc 的唯一瞬間。</returns>
        public DateTime ToUtc(LocalDateTimeOfChurch local)
        {
#if NETFRAMEWORK
            if (local is null) throw new ArgumentNullException(nameof(local));
#else
            ArgumentNullException.ThrowIfNull(local);
#endif
            var zone = TimeZoneLookup.Resolve(local.IanaTimeZoneId);
            if (zone.IsInvalidTime(local.Value) || zone.IsAmbiguousTime(local.Value))
                throw new ArgumentException("Church local time is invalid or ambiguous.", nameof(local));
            return Shift(local.Value, -zone.GetUtcOffset(local.Value), DateTimeKind.Utc);
        }

        /// <summary>只做有界 ticks 加法，超 DateTime 範圍拒絕；offset 至多一天，不會造成 Int64 算術溢位。</summary>
        private static DateTime Shift(DateTime value, TimeSpan offset, DateTimeKind kind)
        {
            var ticks = value.Ticks + offset.Ticks;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(nameof(value), "Time conversion exceeds the supported date range.");
            return new DateTime(ticks, kind);
        }
    }
}
