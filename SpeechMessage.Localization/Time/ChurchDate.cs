using System;

namespace SpeechMessage.Localization
{
    /// <summary>
    /// 生日等純年月日值；不帶時區、offset 或 UTC 語意，兩產品共用以避免 net462 缺 DateOnly。
    /// 只能從明確年月日或 Unspecified 午夜建構，禁止把瞬間偷偷截成日期；不含個資、owner 或外部參照。
    /// </summary>
    public readonly struct ChurchDate : IEquatable<ChurchDate>
    {
        /// <summary>只保存 Unspecified 午夜純值；default 為有效的 0001-01-01，不含動態或共享狀態。</summary>
        private readonly DateTime value;

        /// <summary>依 Gregorian 年月日驗證有效性；不存在的閏日直接拒絕，不移到次月。</summary>
        /// <param name="year">1 到 9999。</param>
        /// <param name="month">1 到 12。</param>
        /// <param name="day">該月的有效日。</param>
        public ChurchDate(int year, int month, int day) => value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>純日期的年，不受文化或時區轉換影響。</summary>
        public int Year => value.Year;
        /// <summary>純日期的月，不含 offset。</summary>
        public int Month => value.Month;
        /// <summary>純日期的日，生日不能透過時鐘移日。</summary>
        public int Day => value.Day;

        /// <summary>由已確認 DateOnly 語意的 Unspecified 午夜建立；拒絕有時間或 UTC／Local 的值。</summary>
        /// <param name="date">資料來源已明確標示為純日期的值。</param>
        /// <returns>不帶時區的年月日，不呼叫任何轉換。</returns>
        public static ChurchDate FromDateOnly(DateTime date)
        {
            if (date.Kind != DateTimeKind.Unspecified || date.TimeOfDay != TimeSpan.Zero)
                throw new ArgumentException("A date-only value must be Unspecified midnight.", nameof(date));
            return new ChurchDate(date.Year, date.Month, date.Day);
        }

        /// <summary>回傳 Unspecified 午夜供舊 API／格式化使用；這不是可丟進時區轉換的 UTC 瞬間。</summary>
        public DateTime ToDateTime() => value;

        /// <summary>只比較年月日，不以顯示文字、時區或參照識別判斷。</summary>
        public bool Equals(ChurchDate other) => value == other.value;
        /// <summary>僅相同純日期型別可相等；不把 DateTime 瞬間當成日期。</summary>
        public override bool Equals(object? obj) => obj is ChurchDate other && Equals(other);
        /// <summary>純日期值的穩定雜湊，不保存外部狀態。</summary>
        public override int GetHashCode() => value.GetHashCode();
        /// <summary>比較兩個純年月日。</summary>
        public static bool operator ==(ChurchDate left, ChurchDate right) => left.Equals(right);
        /// <summary>判斷兩個純年月日不同。</summary>
        public static bool operator !=(ChurchDate left, ChurchDate right) => !left.Equals(right);
    }
}
