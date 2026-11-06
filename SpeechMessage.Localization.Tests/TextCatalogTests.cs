using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>
/// 保護五層解析、完整教會範圍、具名格式化與完整快照發布。資源及覆寫均為合成資料，
/// 透過真實 SystemTextTable／TextCatalog 執行；替身僅提供已建立的快照，不模擬解析結果。
/// 每次操作沒有外部 I/O、連線或背景工作，並行案例必須等待全部工作結束。
/// </summary>
public sealed class TextCatalogTests
{
    /// <summary>逐層移除較高優先資料，答案必須來自指定層；預展開繁中不得搶先蓋掉英文父語系。</summary>
    /// <param name="layer">保留的最高優先層。</param>
    /// <param name="expected">獨立手訂的答案。</param>
    [Theory(DisplayName = "完整字典依五層順序選出第一個有效原文")]
    [InlineData(1, "church-exact")]
    [InlineData(2, "church-wildcard")]
    [InlineData(3, "system-exact")]
    [InlineData(4, "system-parent")]
    [InlineData(5, "neutral")]
    public void Resolve_FiveLayers_UsesFirstOriginalValue(int layer, string expected)
    {
        var context = Context("en-GB");
        var rows = new List<ChurchTextOverride>();
        if (layer == 1) rows.Add(Row(context, "en-GB", "church-exact"));
        if (layer <= 2) rows.Add(Row(context, "*", "church-wildcard"));
        var table = new SystemTextTable(["zh-TW", "en", "en-GB"],
            [["Test.Value", "neutral", layer <= 4 ? "system-parent" : null, layer <= 3 ? "system-exact" : null]]);
        var catalog = new TextCatalog(table, new FixedProvider(new ChurchTextOverrideSnapshot(context, rows)));
        Assert.Equal(expected, catalog.GetText(context, new TextKey("Test.Value")));
    }

    /// <summary>zh-HK 的明確別名是 zh-TW；不可讓 zh 父語系在別名之前覆蓋核定繁中。</summary>
    [Fact(DisplayName = "香港文字語系依核定別名回繁中")]
    public void Resolve_ZhHkAlias_UsesTraditionalChinese()
    {
        var context = Context("zh-HK");
        var table = new SystemTextTable(["zh-TW", "zh"], [["Test.Value", "traditional", "parent"]]);
        Assert.Equal("traditional", Catalog(context, table).GetText(context, new TextKey("Test.Value")));
    }

    /// <summary>系統舊完整表仍回中性文字，而原始查詢對缺少翻譯回 false，對真實翻譯回 true。</summary>
    [Fact(DisplayName = "原始翻譯存在性與舊完整後備分開")]
    public void OriginalTable_MissingTranslation_DoesNotPretendNeutralIsOriginal()
    {
        var table = new SystemTextTable(["zh-TW", "en"], [["Test.Value", "neutral", null]]);
        Assert.Equal("neutral", table.GetText("en", new TextKey("Test.Value")));
        Assert.False(table.TryGetOriginalText("en", new TextKey("Test.Value"), out _));
        Assert.True(table.TryGetOriginalText("zh-TW", new TextKey("Test.Value"), out var original));
        Assert.Equal("neutral", original);
        Assert.False(table.TryGetOriginalText("fr-FR", new TextKey("Test.Value"), out _));
    }

    /// <summary>所有來源缺鍵時必須明確失敗；不得回 key、空字串或偽造成功。</summary>
    [Fact(DisplayName = "完整解析全部缺鍵明確失敗")]
    public void Resolve_AllLayersMissing_Throws()
    {
        var context = Context("en");
        Assert.Throws<KeyNotFoundException>(() => Catalog(context, new SystemTextTable())
            .GetText(context, new TextKey("Test.Missing")));
    }

    /// <summary>相同參數可重複使用；插入值中的大括號不可被再次解析，避免參數注入及遞迴替換。</summary>
    [Fact(DisplayName = "具名參數單次替換且不重新解析參數值")]
    public void Format_NamedParameters_Replaced()
    {
        var context = Context("en");
        var table = Table("{name}/{count}/{name}");
        Assert.Equal("{count}/12/{count}", Catalog(context, table).GetText(context,
            new TextKey("Test.Value"), new TextArg("count", 12), new TextArg("name", "{count}")));
    }

    /// <summary>缺少、多出及重複參數皆為呼叫契約錯誤；重複名稱不能靠字典覆寫隱藏。</summary>
    [Fact(DisplayName = "具名參數集合必須完整且唯一")]
    public void Format_MissingExtraOrDuplicateParameter_Throws()
    {
        var context = Context("en");
        var catalog = Catalog(context, Table("{name}"));
        var key = new TextKey("Test.Value");
        Assert.Throws<FormatException>(() => catalog.GetText(context, key));
        Assert.Throws<FormatException>(() => catalog.GetText(context, key, new TextArg("name", "ok"), new TextArg("extra", 1)));
        Assert.Throws<FormatException>(() => catalog.GetText(context, key, new TextArg("name", "one"), new TextArg("name", "two")));
        Assert.Throws<FormatException>(() => catalog.GetText(context, key, default(TextArg)));
    }

    /// <summary>當參數集合已確定缺少必要名稱，不得先執行其他值的任意格式化程式；注入會拋例外值，決定性結果仍須是集合 FormatException。</summary>
    [Fact(DisplayName = "參數集合完整驗證後才執行參數值格式化")]
    public void Format_MissingParameter_DoesNotExecuteOtherValues()
    {
        var context = Context("en");
        Assert.Throws<FormatException>(() => Catalog(context, Table("{first}/{second}"))
            .GetText(context, new TextKey("Test.Value"), new TextArg("first", new ExplodingValue())));
    }

    /// <summary>日期及數字只使用 context 格式文化；明確與同一 BCL 格式契約比較，且目前執行緒文化未被修改。</summary>
    /// <param name="formatCulture">獨立於文字語系的格式文化。</param>
    [Theory(DisplayName = "日期與數值按明確格式文化輸出")]
    [InlineData("en-US")]
    [InlineData("zh-TW")]
    [InlineData("de-DE")]
    public void Format_DateParameter_UsesFormatCulture(string formatCulture)
    {
        var context = Context("en", formatCulture);
        var date = new DateTime(2027, 3, 28, 13, 2, 3, DateTimeKind.Utc);
        var cultureBefore = CultureInfo.CurrentCulture;
        var culture = CultureInfo.GetCultureInfo(formatCulture);
        var actual = Catalog(context, Table("{date}|{amount}|{empty}")).GetText(context, new TextKey("Test.Value"),
            new TextArg("date", date), new TextArg("amount", 1234.5m), new TextArg("empty", null));
        Assert.Equal(date.ToString(null, culture) + "|" + 1234.5m.ToString(null, culture) + "|", actual);
        Assert.Same(cultureBefore, CultureInfo.CurrentCulture);
    }

    /// <summary>變更語言只建立新的不可變 context；產品、組織及教會不能跟著改動，偽語系固定使用 en-US 格式。</summary>
    [Fact(DisplayName = "語言變更保留完整邊界且偽語系格式固定")]
    public void Resolve_LanguageNeverChangesChurch()
    {
        var original = Context("zh-TW");
        var changed = original.WithCultures("qps-ploc", "zh-TW");
        Assert.Equal(original.ChurchId, changed.ChurchId);
        Assert.Equal(original.Organization, changed.Organization);
        Assert.Equal(original.Product, changed.Product);
        Assert.Equal("en-US", changed.FormatCulture);
        Assert.Equal("zh-TW", original.TextCulture);
    }

    /// <summary>串接真實 resolver、完整字典與生成資源，涵蓋預設繁中、英文未開放回退及開發偽語系；不是只檢查 resx 存在。</summary>
    /// <param name="preference">成員偏好。</param>
    /// <param name="allowPseudo">是否允許開發偽語系。</param>
    /// <param name="expected">真實 sender 範本完成格式化後的獨立答案。</param>
    [Theory(DisplayName = "真實語言決定與生成資源流程一致")]
    [InlineData("zh-TW", false, "Alpha小幫手")]
    [InlineData("en", false, "Alpha小幫手")]
    [InlineData("qps-ploc", true, "［Alpha小幫手ǂǂ］")]
    public void FullFlow_ResolvedCulture_FormatsActualGeneratedResource(string preference, bool allowPseudo, string expected)
    {
        var options = new LocalizationOptions(allowPseudoLocale: allowPseudo);
        options.Validate(false);
        var resolved = new MemberCultureResolver(options).Resolve(new MemberLanguagePreference(preference),
            new ChurchLocaleSettings("zh-TW", ["zh-TW", "en", "qps-ploc"]));
        var context = Context(resolved.TextCulture, resolved.FormatCulture);
        Assert.Equal(expected, Catalog(context, new SystemTextTable())
            .GetText(context, new TextKey("Line.Sender.Template"), new TextArg("churchName", "Alpha")));
    }

    /// <summary>空教會、無組織／產品及未知格式文化都必須在任何資料查詢前拒絕；context 本身不授予存取權。</summary>
    [Fact(DisplayName = "無效文字範圍拒絕建立")]
    public void Context_InvalidBoundary_Rejects()
    {
        Assert.Throws<ArgumentException>(() => new TextContext(Guid.Empty, "en", "en-US"));
        Assert.Throws<ArgumentException>(() => new TextContext(Guid.NewGuid(), "en", "en-US", ""));
        Assert.Throws<ArgumentException>(() => new TextContext(Guid.NewGuid(), "en", "en-US", "org", ""));
        Assert.Throws<CultureNotFoundException>(() => Context("en", "!invalid!"));
    }

    /// <summary>故意回傳其他教會／組織／產品的快照；即使 key 與資料看似有效，也必須拒絕發布給呼叫者。</summary>
    [Fact(DisplayName = "快照完整邊界不符直接拒絕")]
    public void Catalog_ForeignSnapshot_FailsClosed()
    {
        var context = Context("en");
        foreach (var other in new[]
        {
            new TextContext(Guid.NewGuid(), "en", "en-US", context.Organization, context.Product),
            new TextContext(context.ChurchId, "en", "en-US", "other-org", context.Product),
            new TextContext(context.ChurchId, "en", "en-US", context.Organization, "other-product")
        })
        {
            var snapshot = new ChurchTextOverrideSnapshot(other, [Row(other, "*", "foreign")]);
            Assert.Throws<InvalidOperationException>(() => new TextCatalog(Table("neutral"), new FixedProvider(snapshot))
                .GetText(context, new TextKey("Test.Value")));
        }
    }

    /// <summary>同 ID、同 key/culture、空 ID 及不同教會列都拒絕整個候選，不按相同文字去重或保留部分。</summary>
    [Fact(DisplayName = "覆寫候選驗證正式識別與教會邊界")]
    public void Snapshot_DuplicateOrForeignIdentity_RejectsCandidate()
    {
        var context = Context("en");
        var row = Row(context, "en", "same");
        Assert.Throws<ArgumentException>(() => new ChurchTextOverrideSnapshot(context, [row, row]));
        Assert.Throws<ArgumentException>(() => new ChurchTextOverrideSnapshot(context, [row, Row(context, "EN", "same")]));
        Assert.Throws<ArgumentException>(() => new ChurchTextOverride(Guid.Empty, context.ChurchId, new TextKey("Test.Value"), "*", "text"));
        Assert.Throws<ArgumentException>(() => new ChurchTextOverrideSnapshot(context, [Row(Context("en"), "en", "foreign")]));
    }

    /// <summary>不同 key 可顯示相同文字；來源陣列被替換後既有快照結果仍完整不變。</summary>
    [Fact(DisplayName = "覆寫快照複製來源並保留不同正式列")]
    public void Snapshot_SourceMutation_DoesNotChangePublishedValues()
    {
        var context = Context("en");
        ChurchTextOverride[] rows =
        [
            Row(context, "en", "same"),
            new ChurchTextOverride(Guid.NewGuid(), context.ChurchId, new TextKey("Test.Other"), "en", "same")
        ];
        var snapshot = new ChurchTextOverrideSnapshot(context, rows);
        rows[0] = Row(context, "en", "mutated");
        Assert.True(snapshot.TryGetText("en", new TextKey("Test.Value"), out var first));
        Assert.True(snapshot.TryGetText("en", new TextKey("Test.Other"), out var second));
        Assert.Equal("same", first);
        Assert.Equal("same", second);
    }

    /// <summary>同一 catalog 同時解析 A/B 各千次，provider 按完整 scope 選快照，所有工作 await 排空且不改共享文化。</summary>
    [Fact(DisplayName = "不同教會千次並行取字不串資料")]
    public async Task Catalog_ConcurrentDifferentChurches_NoCrossTalk()
    {
        var first = Context("en");
        var second = Context("en");
        var provider = new PairProvider(new ChurchTextOverrideSnapshot(first, [Row(first, "*", "church-A")]),
            new ChurchTextOverrideSnapshot(second, [Row(second, "*", "church-B")]));
        var catalog = new TextCatalog(Table("neutral"), provider);
        var jobs = Enumerable.Range(0, 1000).Select(_ => Task.Run(() =>
        {
            Assert.Equal("church-A", catalog.GetText(first, new TextKey("Test.Value")));
            Assert.Equal("church-B", catalog.GetText(second, new TextKey("Test.Value")));
        })).ToArray();
        await Task.WhenAll(jobs);
        Assert.All(jobs, job => Assert.True(job.IsCompleted));
    }

    /// <summary>提供者首次拋出真實例外，catalog 必須保留同一例外向上傳遞；下次完整成功，不能保存失敗或局部狀態。</summary>
    [Fact(DisplayName = "來源故障保留原例外且後續可重試")]
    public void Catalog_ProviderFault_NextCallRetriesWithoutRetainedFailure()
    {
        var context = Context("en");
        var failure = new InvalidOperationException("synthetic-provider-fault");
        var provider = new FailOnceProvider(failure, new ChurchTextOverrideSnapshot(context, [Row(context, "*", "recovered")]));
        var catalog = new TextCatalog(Table("neutral"), provider);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => catalog.GetText(context, new TextKey("Test.Value"))));
        Assert.Equal("recovered", catalog.GetText(context, new TextKey("Test.Value")));
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>對持續存活的 catalog 執行百次暫時 context 與參數格式化；確定沒有背景工作後強制回收，全部弱參照須已釋放。</summary>
    [Fact(DisplayName = "長生命 catalog 不保留呼叫 context 與參數值")]
    public void Catalog_CompletedCalls_DoNotRetainContextOrArguments()
    {
        var context = Context("en");
        var catalog = Catalog(context, Table("{value}"));
        var references = new List<WeakReference>();
        for (var index = 0; index < 100; index++) references.AddRange(ExerciseTemporaryValues(catalog, context));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(catalog);
    }

    /// <summary>阻止 JIT 將區域暫存延伸到回收案例，確保測到 catalog 的真實保留而非測試堆疊。</summary>
    /// <param name="catalog">在回收後仍存活的真實實例。</param>
    /// <param name="original">提供者擁有的固定 scope，臨時 context 另建。</param>
    /// <returns>只保留弱參照，不持有測試值。</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ExerciseTemporaryValues(TextCatalog catalog, TextContext original)
    {
        var temporary = original.WithCultures("en", "en-US");
        var payload = new object();
        Assert.NotEmpty(catalog.GetText(temporary, new TextKey("Test.Value"), new TextArg("value", payload)));
        return [new WeakReference(temporary), new WeakReference(payload)];
    }

    /// <summary>以固定 scope 欄位和隨機合成教會隔離各案例，不保存靜態教會資料或憑證。</summary>
    /// <param name="culture">文字語系。</param>
    /// <param name="formatCulture">格式文化。</param>
    /// <returns>僅由本測試擁有的不可變範圍。</returns>
    private static TextContext Context(string culture, string formatCulture = "en-US") => new(Guid.NewGuid(), culture, formatCulture, "test-org", "test-product");

    /// <summary>建立單一正式識別覆寫，所有值均為合成資料。</summary>
    /// <param name="context">所屬範圍。</param>
    /// <param name="culture">文字語系或星號。</param>
    /// <param name="text">不含個資的測試值。</param>
    /// <returns>不可變的一列。</returns>
    private static ChurchTextOverride Row(TextContext context, string culture, string text) => new(Guid.NewGuid(), context.ChurchId, new TextKey("Test.Value"), culture, text);

    /// <summary>建立具有單一合成鍵的真實系統表，不使用假的解析器。</summary>
    /// <param name="text">中性原文。</param>
    /// <returns>完成驗證的系統表。</returns>
    private static SystemTextTable Table(string text) => new(["zh-TW", "en"], [["Test.Value", text, null]]);

    /// <summary>為明確範圍建立空覆寫 catalog，空快照仍必須帶原完整邊界。</summary>
    /// <param name="context">授權後的測試範圍。</param>
    /// <param name="table">真實系統表。</param>
    /// <returns>沒有外部資源的真實 catalog。</returns>
    private static TextCatalog Catalog(TextContext context, SystemTextTable table) => new(table, new FixedProvider(new ChurchTextOverrideSnapshot(context, [])));

    /// <summary>固定快照提供者僅借用不可變快照，不擁有 task、連線或 request；可刻意回外部 scope 供拒絕測試。</summary>
    /// <param name="snapshot">本案例獨佔的完整快照。</param>
    private sealed class FixedProvider(IChurchTextOverrideSnapshot snapshot) : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>原樣交回快照，使真實 catalog 負責檢查 scope；不模擬授權成功。</summary>
        /// <param name="context">本次範圍。</param>
        /// <returns>固定不可變快照。</returns>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) => snapshot;
    }

    /// <summary>只持有兩個測試自有快照，按完整 scope 分派；案例結束後沒有靜態根或待排空工作。</summary>
    /// <param name="first">A 教會快照。</param>
    /// <param name="second">B 教會快照。</param>
    private sealed class PairProvider(IChurchTextOverrideSnapshot first, IChurchTextOverrideSnapshot second) : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>由 context 的完整邊界選擇快照，語言不參與權限範圍。</summary>
        /// <param name="context">已授權的合成範圍。</param>
        /// <returns>唯一匹配的不可變快照。</returns>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) => first.Context.HasSameScope(context) ? first : second;
    }

    /// <summary>只在單一同步案例內注入一次故障，沒有排程或共用狀態；原始例外引用由案例擁有。</summary>
    /// <param name="failure">要原樣傳遞的故障。</param>
    /// <param name="snapshot">故障後可重試的完整快照。</param>
    private sealed class FailOnceProvider(Exception failure, IChurchTextOverrideSnapshot snapshot) : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>同步呼叫次數，斷言真實 catalog 沒有永久失敗快取。</summary>
        internal int Calls { get; private set; }
        /// <summary>第一次拋原例外，第二次回完整快照。</summary>
        /// <param name="context">本次 scope，不保留。</param>
        /// <returns>完整快照。</returns>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context)
        {
            Calls++;
            if (Calls == 1) throw failure;
            return snapshot;
        }
    }

    /// <summary>若錯誤參數集合仍執行格式化就立即失敗，沒有外部資源或狀態。</summary>
    private sealed class ExplodingValue : IFormattable
    {
        /// <summary>刻意拋出可辨識故障，測試必須在到達這裡之前拒絕缺參數集合。</summary>
        /// <param name="format">未使用的格式。</param>
        /// <param name="formatProvider">未使用的文化。</param>
        /// <returns>永不正常回傳。</returns>
        public string ToString(string? format, IFormatProvider? formatProvider) => throw new InvalidOperationException("Unexpected formatting before validation.");
    }
}
