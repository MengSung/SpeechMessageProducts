using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>
/// 以固定 UTC 向量保護兩產品的教會時間、DST 拒絕與生日純日期契約。
/// 不讀伺服器當地時間、不連線；故障、千次 A/B 及弱參照斷言保護隔離與生命週期。
/// </summary>
public sealed class ChurchClockTests
{
    /// <summary>產品尚無正式時鐘時必須失敗，不能以測試宿主自己的時間型別取代產品契約。</summary>
    [Fact(DisplayName = "兩個產品輸出都必須包含正式教會時鐘契約")]
    public void ClockContract_BothProductTargets_ContainsChurchClock()
    {
        var assembly = typeof(SystemTextTable).Assembly;
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.IClock", false));
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.IChurchClock", false));
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.ChurchClock", false));
        Assert.NotNull(assembly.GetType("SpeechMessage.Localization.LocalDateTimeOfChurch", false));
    }

    /// <summary>台北跨 UTC 日期界線必須為 +8，回傳 Unspecified 以免後續誤作伺服器 local。</summary>
    [Fact(DisplayName = "台北固定加八且今日採教會日期")]
    public void Taipei_NoDst_Plus8()
    {
        var utc = Utc(2027, 3, 27, 20, 15);
        var clock = new ChurchClock(new FixedClock(new DateTimeOffset(utc)));
        var local = clock.ToChurchTime(utc, "Asia/Taipei");
        Assert.Equal(new DateTime(2027, 3, 28, 4, 15, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
        Assert.Equal(new DateTime(2027, 3, 28), clock.TodayInChurch("Asia/Taipei"));
        Assert.Equal(utc, clock.ToUtc(new LocalDateTimeOfChurch(local, "Asia/Taipei")));
    }

    /// <summary>倫敦春季跳過一小時；兩個相鄰 UTC 分鐘必須落在 00:59 及 02:00。</summary>
    [Fact(DisplayName = "倫敦2027春季DST邊界")]
    public void London_DstStart_2027_03_28()
    {
        var clock = Create();
        Assert.Equal(new DateTime(2027, 3, 28, 0, 59, 0), clock.ToChurchTime(Utc(2027, 3, 28, 0, 59), "Europe/London"));
        Assert.Equal(new DateTime(2027, 3, 28, 2, 0, 0), clock.ToChurchTime(Utc(2027, 3, 28, 1, 0), "Europe/London"));
    }

    /// <summary>倫敦秋季重複一點；UTC 轉換有唯一答案，不可拒絕兩個不同瞬間。</summary>
    [Fact(DisplayName = "倫敦2027秋季DST邊界")]
    public void London_DstEnd_2027_10_31()
    {
        var clock = Create();
        Assert.Equal(new DateTime(2027, 10, 31, 1, 59, 0), clock.ToChurchTime(Utc(2027, 10, 31, 0, 59), "Europe/London"));
        Assert.Equal(new DateTime(2027, 10, 31, 1, 0, 0), clock.ToChurchTime(Utc(2027, 10, 31, 1, 0), "Europe/London"));
    }

    /// <summary>洛杉磯春季由 01:59 跳至 03:00，不能借用台北或伺服器 offset。</summary>
    [Fact(DisplayName = "洛杉磯2027春季DST邊界")]
    public void LosAngeles_DstStart_2027_03_14()
    {
        var clock = Create();
        Assert.Equal(new DateTime(2027, 3, 14, 1, 59, 0), clock.ToChurchTime(Utc(2027, 3, 14, 9, 59), "America/Los_Angeles"));
        Assert.Equal(new DateTime(2027, 3, 14, 3, 0, 0), clock.ToChurchTime(Utc(2027, 3, 14, 10, 0), "America/Los_Angeles"));
    }

    /// <summary>八個正式支援時區在兩個產品目標以相同固定答案驗證；一月雪梨為夏令時間。</summary>
    /// <param name="zone">正式 IANA 時區。</param>
    /// <param name="offsetHours">2027-01-15 的獨立預期 UTC offset。</param>
    [Theory(DisplayName = "Framework對照涵蓋全部八個教會時區")]
    [InlineData("Asia/Taipei", 8)]
    [InlineData("Asia/Hong_Kong", 8)]
    [InlineData("Asia/Singapore", 8)]
    [InlineData("Asia/Kuala_Lumpur", 8)]
    [InlineData("Europe/London", 0)]
    [InlineData("America/Los_Angeles", -8)]
    [InlineData("America/New_York", -5)]
    [InlineData("Australia/Sydney", 11)]
    public void NetFrameworkMapping_CoversAllSupportedChurchTimeZones(string zone, int offsetHours)
    {
        var utc = Utc(2027, 1, 15, 12, 0);
        var local = Create().ToChurchTime(utc, zone);
        Assert.Equal(DateTime.SpecifyKind(utc.AddHours(offsetHours), DateTimeKind.Unspecified), local);
        Assert.Equal(utc, Create().ToUtc(new LocalDateTimeOfChurch(local, zone)));
    }

    /// <summary>
    /// 以釘選原始 XML 作獨立 oracle，逐一核對 445 個生成 ID；不能只驗八個向量而漏掉其他生成列。
    /// DTD 宣告只略過、不解析；reader／stream 在本案立即釋放，固定公共資料不含任何教會或秘密。
    /// </summary>
    [Fact(DisplayName = "全部445個CLDR映射與原XML一致")]
    public void GeneratedMappings_AllPinnedCldrIdentities_MatchXml()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "windowsZones.xml"));
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var source = XDocument.Load(reader);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // 每案只配置一次分隔字元，不在 505 個來源列內反覆配置，也不引入可變 static 陣列。
        char[] separators = [' '];
        foreach (var row in source.Root!.Element("windowsZones")!.Element("mapTimezones")!.Elements("mapZone"))
        foreach (var iana in row.Attribute("type")!.Value.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(IanaWindowsZones.TryGetWindowsId(iana, out var windows));
            Assert.Equal(row.Attribute("other")!.Value, windows);
            ids.Add(iana);
        }
        Assert.Equal(445, ids.Count);
        Assert.False(IanaWindowsZones.TryGetWindowsId("Unknown/Church", out var missing));
        Assert.Equal(string.Empty, missing);
    }

    /// <summary>不存在或重複的 local 時間均 fail closed，不讓 BCL 預設標準時間替使用者猜測。</summary>
    /// <param name="zone">故障時區。</param>
    /// <param name="month">DST 異常月份。</param>
    /// <param name="day">異常日期。</param>
    /// <param name="hour">無效或模糊的小時。</param>
    [Theory(DisplayName = "DST無效與模糊local均拒絕")]
    [InlineData("Europe/London", 3, 28, 1)]
    [InlineData("Europe/London", 10, 31, 1)]
    [InlineData("America/Los_Angeles", 3, 14, 2)]
    [InlineData("America/Los_Angeles", 11, 7, 1)]
    [InlineData("America/New_York", 3, 14, 2)]
    [InlineData("Australia/Sydney", 4, 4, 2)]
    [InlineData("Australia/Sydney", 10, 3, 2)]
    public void ToUtc_InvalidOrAmbiguousLocal_Throws(string zone, int month, int day, int hour)
    {
        var local = new LocalDateTimeOfChurch(new DateTime(2027, month, day, hour, 30, 0), zone);
        Assert.Throws<ArgumentException>(() => Create().ToUtc(local));
    }

    /// <summary>UTC 入口拒絕 Unspecified／Local；local 值物件拒絕 Utc／Local，不推論來源。</summary>
    [Fact(DisplayName = "UTC與教會local的Kind不可混用")]
    public void DateTimeKinds_WrongBoundary_Throws()
    {
        var value = new DateTime(2027, 1, 1);
        Assert.Throws<ArgumentException>(() => Create().ToChurchTime(value, "Asia/Taipei"));
        Assert.Throws<ArgumentException>(() => Create().ToChurchTime(DateTime.SpecifyKind(value, DateTimeKind.Local), "Asia/Taipei"));
        Assert.Throws<ArgumentException>(() => new LocalDateTimeOfChurch(DateTime.SpecifyKind(value, DateTimeKind.Utc), "Asia/Taipei"));
        Assert.Throws<ArgumentException>(() => new LocalDateTimeOfChurch(DateTime.SpecifyKind(value, DateTimeKind.Local), "Asia/Taipei"));
        Assert.Throws<ArgumentNullException>(() => Create().ToUtc(null!));
        Assert.Throws<ArgumentNullException>(() => new ChurchClock(null!));
    }

    /// <summary>未知 IANA、Windows ID、大小寫猜測與空輸入都不得回伺服器預設時區。</summary>
    /// <param name="zone">惡意、錯誤或非 IANA 輸入。</param>
    [Theory(DisplayName = "未知IANA與Windows輸入拒絕")]
    [InlineData("Unknown/Church")]
    [InlineData("Taipei Standard Time")]
    [InlineData("asia/taipei")]
    [InlineData(" Asia/Taipei")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownIana_Throws(string? zone)
    {
        Assert.ThrowsAny<ArgumentException>(() => Create().ToChurchTime(Utc(2027, 1, 1, 0, 0), zone!));
        Assert.ThrowsAny<ArgumentException>(() => new LocalDateTimeOfChurch(new DateTime(2027, 1, 1), zone!));
    }

    /// <summary>極端日期不能被 BCL 靜默夾到 Min／Max 而改變業務瞬間；溢位必須拒絕。</summary>
    [Fact(DisplayName = "時間轉換溢位拒絕而非截斷")]
    public void Conversion_OutsideDateRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().ToChurchTime(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), "Asia/Taipei"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().ToUtc(new LocalDateTimeOfChurch(DateTime.MinValue, "Asia/Taipei")));
    }

    /// <summary>生日保持年月日且永遠沒有 offset；與同日跨時區瞬間對照，防止把 DateOnly 當 UTC 轉換。</summary>
    [Fact(DisplayName = "生日純日期永不因時區移日")]
    public void Birthday_DateOnly_NeverShiftsDay()
    {
        var birthday = new ChurchDate(2000, 2, 29);
        Assert.Equal(2000, birthday.Year);
        Assert.Equal(2, birthday.Month);
        Assert.Equal(29, birthday.Day);
        Assert.Equal(new DateTime(2000, 2, 29), birthday.ToDateTime());
        Assert.Equal(DateTimeKind.Unspecified, birthday.ToDateTime().Kind);
        Assert.Equal(birthday, ChurchDate.FromDateOnly(new DateTime(2000, 2, 29)));
        Assert.Equal(new ChurchDate(2000, 2, 29).GetHashCode(), birthday.GetHashCode());
        Assert.NotEqual(birthday, new ChurchDate(2000, 3, 1));
        Assert.Throws<ArgumentException>(() => ChurchDate.FromDateOnly(Utc(2000, 2, 29, 0, 0)));
        Assert.Throws<ArgumentException>(() => ChurchDate.FromDateOnly(new DateTime(2000, 2, 29, 1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChurchDate(2001, 2, 29));
        Assert.Equal(new DateTime(2000, 2, 28, 16, 0, 0), Create().ToChurchTime(Utc(2000, 2, 29, 0, 0), "America/Los_Angeles"));
        Assert.Equal(new DateTime(2000, 2, 29), birthday.ToDateTime());
    }

    /// <summary>時間來源 offset 不可污染 UtcNow；故障須保留同一 Exception，下次正常讀值可恢復。</summary>
    [Fact(DisplayName = "來源故障保真且下次可重試")]
    public void Clock_SourceFailure_PreservesExceptionAndRetries()
    {
        var fault = new InvalidOperationException("synthetic clock fault");
        var source = new FaultClock(fault);
        var clock = new ChurchClock(source);
        Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => _ = clock.UtcNow));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), clock.UtcNow);
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
        Assert.Equal(DateTimeKind.Utc, new SystemClock().UtcNow.UtcDateTime.Kind);
    }

    /// <summary>同一時鐘千次 A/B 並行不可互相改 zone；回傳值不能保存呼叫端 local 容器。</summary>
    [Fact(DisplayName = "千次跨教會時區並行與輸入回收")]
    public async Task Clock_ABZonesParallel_NoCrossTalkOrRetention()
    {
        var clock = Create();
        var utc = Utc(2027, 3, 14, 9, 30);
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
        {
            Assert.Equal(new DateTime(2027, 3, 14, 17, 30, 0), clock.ToChurchTime(utc, "Asia/Taipei"));
            Assert.Equal(new DateTime(2027, 3, 14, 1, 30, 0), clock.ToChurchTime(utc, "America/Los_Angeles"));
        })));
        var references = new List<WeakReference>();
        for (var index = 0; index < 100; index++) references.Add(ConvertTemporaryLocal(clock));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(clock);
    }

#if !NETFRAMEWORK
    /// <summary>net10 adapter 必須讀借入 TimeProvider，不建立 timer、不處置外部 owner。</summary>
    [Fact(DisplayName = "net10借用TimeProvider而非讀伺服器時間")]
    public void TimeProviderAdapter_ReadsInjectedUtc()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2027, 5, 1, 20, 0, 0, TimeSpan.Zero));
        var clock = new ChurchClock(new TimeProviderClock(time));
        Assert.Equal(time.GetUtcNow(), clock.UtcNow);
        Assert.Equal(new DateTime(2027, 5, 2), clock.TodayInChurch("Asia/Taipei"));
        Assert.Throws<ArgumentNullException>(() => new TimeProviderClock(null!));
    }

    /// <summary>
    /// 此案例由三個獨立 dotnet 程序執行；P106_EXPECTED_ICU 只是測試的外部預期，不是產品判定來源。
    /// 正常 ICU 須通過，NLS／Invariant 須拋出；避免在同一程序改環境卻誤以為 runtime 已切換。
    /// </summary>
    [Fact(DisplayName = "ICU啟動檢查依真實runtime模式拒絕")]
    public void GlobalizationGuard_RuntimeMode_FailsClosed()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("P106_EXPECTED_ICU"), "false", StringComparison.Ordinal))
            Assert.Throws<InvalidOperationException>(GlobalizationModeGuard.ValidateIcuMode);
        else
            GlobalizationModeGuard.ValidateIcuMode();
    }

    /// <summary>固定瞬間的測試 owner；只覆寫讀值，不使用 TimeProvider 的 timer 功能。</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        /// <summary>唯一固定瞬間，無 request／教會資料。</summary>
        private readonly DateTimeOffset value;
        /// <summary>由測試擁有輸入純值，不持有可釋放資源。</summary>
        internal FixedTimeProvider(DateTimeOffset value) => this.value = value;
        /// <summary>每次原樣回傳固定 UTC，讓 adapter 測試可辨識是否誤讀系統時間。</summary>
        public override DateTimeOffset GetUtcNow() => value;
    }
#endif

    /// <summary>建立固定 UTC 時鐘；不以 DateTime.Now 或程序文化決定答案。</summary>
    private static ChurchClock Create() => new(new FixedClock(new DateTimeOffset(Utc(2027, 1, 15, 12, 0))));

    /// <summary>產生明確 UTC Kind 的固定向量，避免 Parse 受程序文化或 offset 影響。</summary>
    private static DateTime Utc(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>獨立堆疊保證 local 輸入不被 JIT 暫存；clock 存活時此物件仍必須可回收。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ConvertTemporaryLocal(ChurchClock clock)
    {
        var local = new LocalDateTimeOfChurch(new DateTime(2027, 1, 15, 20, 0, 0), "Asia/Taipei");
        Assert.Equal(Utc(2027, 1, 15, 12, 0), clock.ToUtc(local));
        return new WeakReference(local);
    }

    /// <summary>只保存固定瞬間的純測試來源；不擁有 timer、Task、連線或其他 disposable。</summary>
    private sealed class FixedClock : IClock
    {
        /// <summary>不可變固定時間，生命週期由該次測試控制。</summary>
        private readonly DateTimeOffset value;
        /// <summary>複製純值，沒有跨測試共享狀態。</summary>
        internal FixedClock(DateTimeOffset value) => this.value = value;
        /// <summary>每次取得相同值；產品應自行正規化 UTC offset。</summary>
        public DateTimeOffset UtcNow => value;
    }

    /// <summary>一次注入來源故障；只由單一同步測試擁有，沒有背景執行或全域狀態。</summary>
    private sealed class FaultClock : IClock
    {
        /// <summary>故障實例供決定性 Same 斷言，第一次讀取後立即清除。</summary>
        private Exception? fault;
        /// <summary>借用合成例外，不記錄其訊息或發通知。</summary>
        internal FaultClock(Exception fault) => this.fault = fault;
        /// <summary>先拋原例外，後續回非零 offset 純值，驗證恢復與 UTC 正規化。</summary>
        public DateTimeOffset UtcNow
        {
            get
            {
                if (fault is { } failure) { fault = null; throw failure; }
                return new DateTimeOffset(2027, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
            }
        }
    }
}
