/*
 * 檔案整體說明
 *
 * 目的與定位：以 net48／net10.0 相同合成案例保護 P5-05 統計與通知純規則，
 * 不建立 Dynamics 或 LINE 主機，也不宣稱此處離線結果代表實機驗收。
 *
 * 主要功能與流程：依 inventory 的 reading 證據驗證分類排除、缺整數 -9999、
 * 零分母不寫四欄、穩定事件旗標，以及正式出席列重複時整批拒絕。通知以明確
 * UTC、IANA 時區及文化核對逐字文字、四角色順序與重試識別的分教會隔離。
 * 故障注入涵蓋空識別、超預算、重複列、整數溢位及不明日期 Kind。
 * P6-02 另以既有合成 scenarios 的分類值保護共用分類入口與固定旗標原文；資源 reader
 * 由該案例擁有並同步 Dispose，舊黃金期望與其他統計／通知斷言不變。
 *
 * 使用邊界與維護：測試只擁有區域陣列、純值與同步 ChurchClock；不讀真實 token、
 * LINE ID、秘密、快照資料列或網路。呼叫後修改輸入集合的斷言保護不可變發布；
 * 沒有 Task、timer、連線或待排空資源。外殼仍須負責正式授權、教會歸屬與持久化
 * 冪等，測試中的穩定 RetryKey 不等於已完成實際推播去重。
 */
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>使用可重現的純值輸入驗證統計及通知；各測試不共享可變狀態或外部資源。</summary>
public sealed class WeeklyReportRuleTests
{
    /// <summary>
    /// 明確注入缺分類與三個舊排除值，必須不計；其餘值取自階段 5 嵌入合成情境而非新規則的輸出。
    /// 讀取只屬本案例且 reader 確定關閉，不連 D365，也不將分類語法當作會友授權。
    /// </summary>
    [Fact]
    public void IsCountedClassification_ExcludedAndMissingValues_NotCounted()
    {
        foreach (var classification in new int?[] { null, 100000004, 100000000, 100000007 })
            Assert.False(WeeklyReportStatisticsRule.IsCountedClassification(classification));
        var assembly = typeof(WeeklyReportRuleTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith(".Golden.Legacy.weeklyreports.scenarios.json", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var included = JObject.Parse(reader.ReadToEnd()).SelectTokens("$..customertypecode.option")
            .Select(value => value.Value<int>()).Distinct()
            .Where(value => value is not (100000004 or 100000000 or 100000007)).ToArray();
        Assert.NotEmpty(included);
        foreach (var classification in included)
            Assert.True(WeeklyReportStatisticsRule.IsCountedClassification(classification));
    }

    /// <summary>
    /// 固定旗標是 D365 寫入契約，不隨 UI 語系或重新命名改變；兩個測試宿主逐字核對舊原文，
    /// 呼叫既有固定 zh-TW accessor，不讀設定、不建立操作狀態或外部資源。
    /// </summary>
    [Fact]
    public void CalculationPrefix_IsLegacyFlagText() =>
        Assert.Equal("計算出席率", WeeklyReportStatisticsRule.CalculationPrefix());

    /// <summary>新旗標相較 PreImage 改變才計算；分類缺值回傳補值意圖，排除列不計分母。</summary>
    [Fact]
    public void WeeklyReport_Statistics_FromPreImageDelta()
    {
        Assert.Equal("計算出席率", WeeklyReportStatisticsRule.CalculationPrefix());
        var rows = new[]
        {
            Row(1, 11, 1, 1, 0), Row(2, 12, 1, 0, 1),
            Row(3, 13, 100000004, 99, 99), Row(4, 14, null, 99, 99),
            new WeeklyReportAttendanceRow(Id(5), Id(15), false, 1, 99, 99),
        };
        var result = Statistics("old", "計算出席率-1", Id(90), rows);
        Assert.Equal(2, result.EligibleCount);
        Assert.Equal(1, result.SundayCount);
        Assert.Equal(1, result.GroupCount);
        Assert.Equal(0.5d, result.SundayRate);
        Assert.Equal(0.5d, result.GroupRate);
        Assert.Equal(Id(90).ToString("D"), result.NextSavedFlag);
        Assert.Equal(new[] { Id(14) }, result.ContactIdsNeedingClassification);
        var unchanged = Statistics("計算出席率-1", "計算出席率-1", Id(90), rows);
        Assert.Null(unchanged.NextSavedFlag);
        Assert.Null(unchanged.SundayCount);
        Assert.Empty(unchanged.ContactIdsNeedingClassification);
        Assert.Null(Statistics(null, null, Id(90), rows).NextSavedFlag);
        Assert.Null(Statistics(null, "prefix計算出席率", Id(90), rows).NextSavedFlag);
    }

    /// <summary>三個舊排除分類與未啟用列都不計；零分母只更新事件旗標，四統計欄位保持未寫。</summary>
    [Fact]
    public void WeeklyReport_Statistics_ZeroDenominator_DoesNotWriteFourFields()
    {
        var result = Statistics(null, "計算出席率", Id(90), new[]
        {
            Row(1, 11, 100000004, 1, 1), Row(2, 12, 100000000, 1, 1),
            Row(3, 13, 100000007, 1, 1), Row(4, 14, null, 1, 1),
        });
        Assert.Equal(0, result.EligibleCount);
        Assert.Null(result.SundayCount);
        Assert.Null(result.GroupCount);
        Assert.Null(result.SundayRate);
        Assert.Null(result.GroupRate);
        Assert.Equal(Id(90).ToString("D"), result.NextSavedFlag);
        Assert.Equal(new[] { Id(14) }, result.ContactIdsNeedingClassification);
    }

    /// <summary>缺整數保留讀碼確認的 -9999，數值不夾限、不乘百或捨入，比例仍以有效列數為分母。</summary>
    [Fact]
    public void WeeklyReport_Statistics_MissingInteger_PreservesLegacySentinel()
    {
        var result = Statistics(null, "計算出席率", Id(90),
            new[] { Row(1, 11, 1, null, 2), Row(2, 12, 1, -2, null) });
        Assert.Equal(-10001, result.SundayCount);
        Assert.Equal(-9997, result.GroupCount);
        Assert.Equal(-5000.5d, result.SundayRate);
        Assert.Equal(-4998.5d, result.GroupRate);
    }

    /// <summary>重複 record 即使被分類排除也拒絕；同 contact 的不同正式 record 仍完整計入。</summary>
    [Fact]
    public void WeeklyReport_Statistics_RecordIdentityConflict_FailsClosed()
    {
        Assert.Throws<ArgumentException>(() => Statistics(null, "計算出席率", Id(90),
            new[] { Row(1, 11, 100000000, 1, 1), Row(1, 12, 1, 0, 0) }));
        var result = Statistics(null, "計算出席率", Id(90),
            new[] { Row(1, 11, 1, 1, 0), Row(2, 11, 1, 0, 1) });
        Assert.Equal(2, result.EligibleCount);
        Assert.Equal(0.5d, result.SundayRate);
    }

    /// <summary>待補分類只列一次正式 contact，輸出拒絕可變存取且呼叫後改陣列不污染既有結果。</summary>
    [Fact]
    public void WeeklyReport_Statistics_ResultIsDetachedAndReadOnly()
    {
        var rows = new[] { Row(1, 11, null, 0, 0), Row(2, 11, null, 0, 0) };
        var result = Statistics(null, "計算出席率", Id(90), new IndexOnlyList<WeeklyReportAttendanceRow>(rows));
        rows[0] = Row(3, 13, 1, 99, 99);
        Assert.Equal(new[] { Id(11) }, result.ContactIdsNeedingClassification);
        var list = Assert.IsAssignableFrom<System.Collections.Generic.IList<Guid>>(result.ContactIdsNeedingClassification);
        Assert.Throws<NotSupportedException>(() => list[0] = Id(99));
    }

    /// <summary>注入空識別、超預算及加總溢位；拒絕而不發布部分統計或任意截斷。</summary>
    [Fact]
    public void WeeklyReport_Statistics_InvalidIdentityBudgetAndOverflow_Rejected()
    {
        Assert.Throws<ArgumentException>(() => Row(0, 11, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => Row(1, 0, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => Statistics(null, "計算出席率", Guid.Empty,
            Array.Empty<WeeklyReportAttendanceRow>()));
        Assert.Throws<ArgumentException>(() => Statistics(null, "計算出席率", Id(90),
            Enumerable.Range(1, 10001).Select(n => Row(n, 11, 1, 0, 0)).ToArray()));
        Assert.Throws<OverflowException>(() => Statistics(null, "計算出席率", Id(90),
            new[] { Row(1, 11, 1, int.MaxValue, 0), Row(2, 12, 1, 1, 0) }));
        var maximum = Statistics(null, "計算出席率", Id(90),
            Enumerable.Range(1, 10000).Select(n => Row(n, 11, 1, 1, 0)).ToArray());
        Assert.Equal(10000, maximum.SundayCount);
        Assert.Equal(1d, maximum.SundayRate);
    }

    /// <summary>四角色同人僅一個意圖且保留首次順序，文字精確保留「回報」後空格與 CRLF。</summary>
    [Fact]
    public void WeeklyReport_Notification_PushesOncePerRecipient()
    {
        Assert.Equal(NotificationTemplate, WeeklyReportNotificationRule.Template("zh-TW"));
        Assert.Equal(NotificationTemplate, WeeklyReportNotificationRule.Template("en"));
        var result = Notification(null, "已收到", new[] { Id(11), Id(12), Id(11), Guid.Empty });
        Assert.Equal("區牧 回應您主日日期為 2026/11/15 的聚會回報 \r\n已收到", result.Message);
        Assert.Equal(new[] { Id(11), Id(12) }, result.Recipients.Select(r => r.ContactId));
        Assert.NotEqual(result.Recipients[0].RetryKey, result.Recipients[1].RetryKey);
        var repeated = Notification(null, "已收到", new[] { Id(11), Id(12), Id(11), Guid.Empty });
        Assert.Equal(result.Recipients[0].RetryKey, repeated.Recipients[0].RetryKey);
    }

    /// <summary>相同回應不通知；null 與空字串不同，清空回應仍保留舊文字與換行。</summary>
    [Fact]
    public void WeeklyReport_Notification_ReplyComparisonPreservesNullAndEmpty()
    {
        var unchanged = Notification("已收到", "已收到", new[] { Id(11), Id(12), Id(13), Id(14) });
        Assert.Null(unchanged.Message);
        Assert.Empty(unchanged.Recipients);
        var cleared = Notification("已收到", null, new[] { Id(11), Guid.Empty, Guid.Empty, Guid.Empty });
        Assert.Equal("區牧 回應您主日日期為 2026/11/15 的聚會回報 \r\n", cleared.Message);
        Assert.Single(Notification(null, "", new[] { Id(11), Guid.Empty, Guid.Empty, Guid.Empty }).Recipients);
    }

    /// <summary>同一 UTC 在台北為週日、洛杉磯仍週六；使用同一明確文化核對日期跨日且不讀程序文化。</summary>
    [Fact]
    public void WeeklyReport_WeekBoundary_UsesChurchTimeZone()
    {
        var utc = new DateTime(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc);
        var roles = new[] { Id(11), Guid.Empty, Guid.Empty, Guid.Empty };
        var taipei = WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90), null, "OK", "區牧",
            utc, "Asia/Taipei", CultureInfo.GetCultureInfo("zh-TW"), roles, NotificationTemplate);
        var losAngeles = WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90), null, "OK", "區牧",
            utc, "America/Los_Angeles", CultureInfo.GetCultureInfo("zh-TW"), roles, NotificationTemplate);
        Assert.Equal("區牧 回應您主日日期為 2026/11/15 的聚會回報 \r\nOK", taipei.Message);
        Assert.Equal("區牧 回應您主日日期為 2026/11/14 的聚會回報 \r\nOK", losAngeles.Message);
        Assert.Equal(taipei.Recipients[0].RetryKey, losAngeles.Recipients[0].RetryKey);
        var english = WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90), null, "{pastor}", "區牧",
            utc, "Asia/Taipei", CultureInfo.GetCultureInfo("en-US"), new IndexOnlyList<Guid>(roles), NotificationTemplate);
        Assert.Equal("區牧 回應您主日日期為 11/15/2026 的聚會回報 \r\n{pastor}", english.Message);
    }

    /// <summary>四個正式識別各自改變都必須隔離重試鍵；重算相同事件不依文字、日期或程序狀態變動。</summary>
    [Fact]
    public void WeeklyReport_Notification_RetryKey_IsStableForSameEvent()
    {
        var key = WeeklyReportNotificationRule.RetryKey(Id(80), Id(81), Id(90), Id(11));
        Assert.Equal(new Guid("a9aeac19-6f0e-3652-a047-ccc10f7e754a"), key);
        Assert.Equal(key, WeeklyReportNotificationRule.RetryKey(Id(80), Id(81), Id(90), Id(11)));
        Assert.NotEqual(key, WeeklyReportNotificationRule.RetryKey(Id(82), Id(81), Id(90), Id(11)));
        Assert.NotEqual(key, WeeklyReportNotificationRule.RetryKey(Id(80), Id(82), Id(90), Id(11)));
        Assert.NotEqual(key, WeeklyReportNotificationRule.RetryKey(Id(80), Id(81), Id(91), Id(11)));
        Assert.NotEqual(key, WeeklyReportNotificationRule.RetryKey(Id(80), Id(81), Id(90), Id(12)));
    }

    /// <summary>通知結果不引用 caller 的角色陣列，空角色不建立意圖；四角色以外的輸入不能靜默裁切。</summary>
    [Fact]
    public void WeeklyReport_Notification_ResultIsDetachedAndBounded()
    {
        var roles = new[] { Id(11), Guid.Empty, Guid.Empty, Guid.Empty };
        var result = Notification(null, "OK", roles);
        roles[0] = Id(12);
        Assert.Equal(Id(11), Assert.Single(result.Recipients).ContactId);
        Assert.Empty(Notification(null, "OK", new Guid[4]).Recipients);
        Assert.Throws<ArgumentException>(() => Notification(null, "OK", new Guid[5]));
        Assert.Throws<ArgumentException>(() => Notification(null, new string('x', 2001), new Guid[4]));
    }

    /// <summary>UTC Kind、精確 IANA 與明確文化皆為必要輸入，不允許日期或時區猜測後發送。</summary>
    [Fact]
    public void WeeklyReport_Notification_InvalidDateZoneOrCulture_Rejected()
    {
        var utc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90),
            null, "OK", "區牧", DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), "Asia/Taipei",
            CultureInfo.InvariantCulture, new Guid[4], NotificationTemplate));
        Assert.Throws<ArgumentException>(() => WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90),
            null, "OK", "區牧", utc, "Unknown/Zone", CultureInfo.InvariantCulture, new Guid[4], NotificationTemplate));
        Assert.Throws<ArgumentNullException>(() => WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90),
            null, "OK", "區牧", utc, "Asia/Taipei", null!, new Guid[4], NotificationTemplate));
        Assert.Throws<ArgumentException>(() => WeeklyReportNotificationRule.RetryKey(Guid.Empty, Id(81), Id(90), Id(11)));
        Assert.Throws<FormatException>(() => WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90),
            null, "OK", "區牧", utc, "Asia/Taipei", CultureInfo.InvariantCulture, new Guid[4],
            "{pastor} {date} {reply} {reply}"));
        Assert.Throws<FormatException>(() => WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90),
            null, "OK", "區牧", utc, "Asia/Taipei", CultureInfo.InvariantCulture, new Guid[4],
            "{pastor} {date} {reply} {unknown}"));
    }

    /// <summary>測試專用舊格式原文，產品端必須由外殼資源選取，不從此測試取得文字。</summary>
    private const string NotificationTemplate = "{pastor} 回應您主日日期為 {date} 的聚會回報 \r\n{reply}";

    /// <summary>測試注入固定 zh-TW 舊旗標前綴，避免產品規則自行持有中文字面值。</summary>
    private static WeeklyReportStatisticsResult Statistics(string? prior, string? current, Guid eventId,
        System.Collections.Generic.IReadOnlyList<WeeklyReportAttendanceRow> rows) =>
        WeeklyReportStatisticsRule.Evaluate(prior, current, eventId, rows, WeeklyReportStatisticsRule.CalculationPrefix());

    /// <summary>建立固定合成日期與文化的通知呼叫，所有識別只由本測試擁有，不使用真實 LINE ID。</summary>
    private static WeeklyReportNotificationPlan Notification(string? prior, string? current, Guid[] roles) =>
        WeeklyReportNotificationRule.Evaluate(Id(80), Id(81), Id(90), prior, current, "區牧",
            new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            "Asia/Taipei", CultureInfo.GetCultureInfo("zh-TW"), roles, WeeklyReportNotificationRule.Template("zh-TW"));

    /// <summary>建立指定分類及整數空值的正式出席列；零識別刻意留給負向案例。</summary>
    private static WeeklyReportAttendanceRow Row(int record, int contact, int? classification, int? sunday, int? group) =>
        new WeeklyReportAttendanceRow(Id(record), Id(contact), true, classification, sunday, group);

    /// <summary>從固定整數建立合成 GUID；零映射 Empty，以便測試正式身分拒絕，不讀取隨機來源。</summary>
    private static Guid Id(int value) => new Guid(value, 0, 0, new byte[8]);

    /// <summary>以索引可讀、列舉必定故障的合成集合，保護有界規則不信任 caller 的 enumerator。</summary>
    /// <typeparam name="T">本測試自行擁有的純值或不可變列型別。</typeparam>
    private sealed class IndexOnlyList<T> : System.Collections.Generic.IReadOnlyList<T>
    {
        /// <summary>測試區域的純值陣列；沒有背景使用者或外部資源。</summary>
        private readonly T[] values;
        /// <summary>借用本測試陣列以模擬呼叫後 mutation；規則結果不得保存此集合。</summary>
        public IndexOnlyList(T[] values) => this.values = values;
        /// <summary>回報真實有界列數，供規則先驗預算。</summary>
        public int Count => values.Length;
        /// <summary>允許已驗證次數的索引讀取，不執行 I/O 或延後工作。</summary>
        public T this[int index] => values[index];
        /// <summary>刻意拒絕列舉，讓改回 foreach 而忽略 Count 邊界的回歸立即失敗。</summary>
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
        /// <summary>非泛型列舉採相同故障注入，不建立需要 Dispose 的真實 enumerator。</summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
