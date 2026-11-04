using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using Xunit;

namespace SpeechMessage.Localization.Tests;

/// <summary>
/// 以真實生成資料保護雙目標取字、後備、鍵格式與不可變快照；以假陣列注入衝突及呼叫者修改。
/// 全部測試離線，不依賴主機文化或時間；並行測試的執行緒、閘門及故障槽由單一案例擁有並確定排空。
/// </summary>
public sealed class SystemTextCatalogTests
{
    /// <summary>
    /// 保護 Plugin 取得的 Localization 專案輸出為 net462，而非舊 net48 或僅適用主機的 net10。
    /// 讀取實際載入組件上的 TargetFrameworkAttribute，避免建置設定與引用資產不一致時誤報通過。
    /// </summary>
    [Fact(DisplayName = "Localization 專案參考選取 Framework 4.6.2 輸出")]
    public void Assembly_TargetFramework_IsNet462()
    {
        var attribute = typeof(SystemTextTable).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>().Single();

        Assert.Equal(".NETFramework,Version=v4.6.2", attribute.FrameworkName);
    }

    /// <summary>六個固定資源都須由真實生成表返回原文，包含 P0-10 診斷鍵，不可以替身字典略過產生器。</summary>
    /// <param name="key">resx 的穩定鍵。</param>
    /// <param name="expected">核定的原始繁中文字串。</param>
    [Theory(DisplayName = "繁中取字使用真實系統資源")]
    [InlineData("Common.Diagnostics.Ping", "連線測試成功")]
    [InlineData("Common.Diagnostics.Culture", "目前語系：{culture}")]
    [InlineData("Line.Sender.Template", "{churchName}小幫手")]
    [InlineData("Common.Diagnostics.InvalidConfiguration", "診斷外掛設定格式錯誤。")]
    [InlineData("Common.Diagnostics.PushTest", "P0 推播測試 {utc}")]
    [InlineData("Common.Diagnostics.Retryable", "診斷推播暫時失敗；RetryKey={retryKey}")]
    public void Get_ZhTw_ReturnsChineseText(string key, string expected) => Assert.Equal(expected, new SystemTextTable().GetText("zh-TW", new TextKey(key)));

    /// <summary>逐項採附錄 E.3 核定算式，參數保持原樣；P0-10 新鍵包含標點、空白及拉丁字元的完整 Rune 計數。</summary>
    /// <param name="key">真實資源鍵。</param>
    /// <param name="expected">完整偽語系答案。</param>
    [Theory(DisplayName = "偽語系取字符合核定對照表")]
    [InlineData("Common.Diagnostics.Ping", "［連線測試成功ǂǂǂ］")]
    [InlineData("Common.Diagnostics.Culture", "［目前語系：{culture}ǂǂ］")]
    [InlineData("Line.Sender.Template", "［{churchName}小幫手ǂǂ］")]
    [InlineData("Common.Diagnostics.InvalidConfiguration", "［診斷外掛設定格式錯誤。ǂǂǂǂǂ］")]
    [InlineData("Common.Diagnostics.PushTest", "［P0 推播測試 {utc}ǂǂǂǂ］")]
    [InlineData("Common.Diagnostics.Retryable", "［診斷推播暫時失敗；RetryKey={retryKey}ǂǂǂǂǂǂǂǂ］")]
    public void Get_PseudoLocale_ReturnsWrappedText(string key, string expected) => Assert.Equal(expected, new SystemTextTable().GetText("qps-ploc", new TextKey(key)));

    /// <summary>未知、空字串或空值都回同一繁中快照，不按外部語系字串建立永久快取。</summary>
    /// <param name="culture">不在系統固定清單的語系。</param>
    [Theory(DisplayName = "未知語系確定回繁中")]
    [InlineData("fr-FR")]
    [InlineData("")]
    [InlineData(null)]
    public void Get_UnknownCulture_FallsBackToZhTw(string? culture) => Assert.Equal("連線測試成功", new SystemTextTable().GetText(culture, new TextKey("Common.Diagnostics.Ping")));

    /// <summary>英文空殼沒有翻譯的每個鍵在建立時就補齊，查詢不可回 null 或重複配置後備。</summary>
    /// <param name="key">需要中性後備的真實鍵。</param>
    /// <param name="expected">繁中中性值。</param>
    [Theory(DisplayName = "英文空殼缺字回繁中")]
    [InlineData("Common.Diagnostics.Ping", "連線測試成功")]
    [InlineData("Common.Diagnostics.Culture", "目前語系：{culture}")]
    [InlineData("Line.Sender.Template", "{churchName}小幫手")]
    [InlineData("Common.Diagnostics.InvalidConfiguration", "診斷外掛設定格式錯誤。")]
    [InlineData("Common.Diagnostics.PushTest", "P0 推播測試 {utc}")]
    [InlineData("Common.Diagnostics.Retryable", "診斷推播暫時失敗；RetryKey={retryKey}")]
    public void Get_EnShellMissingKey_FallsBackToZhTw(string key, string expected) => Assert.Equal(expected, new SystemTextTable().GetText("en", new TextKey(key)));

    /// <summary>合法格式但未宣告的鍵必須拋 KeyNotFoundException，不吞掉程式錯誤或回傳鍵本身。</summary>
    [Fact(DisplayName = "缺少系統鍵直接失敗")]
    public void Get_UnknownKey_ThrowsKeyNotFound()
    {
        var catalog = new SystemTextTable();
        Assert.Throws<KeyNotFoundException>(() => catalog.GetText("zh-TW", new TextKey("Unknown.System.Key")));
    }

    /// <summary>注入沒有分層、錯誤段首、空段、尾端換行及非允許字元，必須拒絕整個鍵。</summary>
    /// <param name="value">刻意不合法的鍵。</param>
    [Theory(DisplayName = "文字鍵完整驗證分層格式")]
    [InlineData("")]
    [InlineData("Single")]
    [InlineData("lower.Key")]
    [InlineData("A.lower")]
    [InlineData("A..Key")]
    [InlineData(".A.Key")]
    [InlineData("A.Key.")]
    [InlineData("A.Key\n")]
    [InlineData("A.文字")]
    [InlineData("A.Key Value")]
    public void TextKey_InvalidFormat_Throws(string value) => Assert.Throws<ArgumentException>(() => new TextKey(value));

    /// <summary>三個語系表都完整保留六個資源鍵；不能轉回可變 Dictionary，IDictionary 若存在也必須拒絕寫入。</summary>
    [Fact(DisplayName = "呼叫者不能修改任何已發布語系表")]
    public void Table_IsImmutable_CallerCannotMutate()
    {
        var catalog = new SystemTextTable();
        foreach (var culture in new[] { "zh-TW", "en", "qps-ploc" })
        {
            var table = catalog.GetTable(culture);
            Assert.False(table is Dictionary<string, string>);
            if (table is IDictionary<string, string> mutable)
                Assert.Throws<NotSupportedException>(() => mutable["Common.Diagnostics.Ping"] = "caller-change");
            Assert.Equal(6, table.Count);
        }
        Assert.Equal("連線測試成功", catalog.GetText("zh-TW", new TextKey("Common.Diagnostics.Ping")));
    }

    /// <summary>
    /// 一百個實際執行緒各讀一萬次，語系交錯且每個結果都必須一致；全部 finally 釋放閘門並 join。
    /// 故障槽至多每個執行緒一筆，不使用無界佇列；排空後活躍計數必須為零。
    /// net10 在預熱後另外量測十萬次命中零配置，net48 沒有該 GC API，不偽造配置數據。
    /// </summary>
    [Fact(DisplayName = "百執行緒讀取一致並排空且 net10 零配置")]
    public void Table_ConcurrentReads_NoAllocationGrowth()
    {
        var catalog = new SystemTextTable();
        var key = new TextKey("Common.Diagnostics.Ping");
        const int workerCount = 100;
        var failures = new Exception?[workerCount];
        var threads = new List<Thread>(workerCount);
        var active = 0;
        using (var ready = new CountdownEvent(workerCount))
        using (var start = new ManualResetEventSlim(false))
        {
            var readyInTime = false;
            var allJoined = true;
            try
            {
                for (var worker = 0; worker < workerCount; worker++)
                {
                    var index = worker;
                    var thread = new Thread(() =>
                    {
                        Interlocked.Increment(ref active);
                        try
                        {
                            ready.Signal();
                            start.Wait();
                            var culture = index % 2 == 0 ? "zh-TW" : "qps-ploc";
                            var expected = index % 2 == 0 ? "連線測試成功" : "［連線測試成功ǂǂǂ］";
                            for (var read = 0; read < 10_000; read++)
                                if (!string.Equals(expected, catalog.GetText(culture, key), StringComparison.Ordinal)) throw new InvalidOperationException("Concurrent text mismatch.");
                        }
                        catch (Exception exception) { failures[index] = exception; }
                        finally { Interlocked.Decrement(ref active); }
                    }) { IsBackground = true };
                    thread.Start();
                    threads.Add(thread);
                }
                readyInTime = ready.Wait(TimeSpan.FromSeconds(30));
            }
            finally
            {
                start.Set();
                foreach (var thread in threads) allJoined &= thread.Join(TimeSpan.FromSeconds(10));
            }
            Assert.True(readyInTime);
            Assert.True(allJoined);
            Assert.Equal(0, active);
            Assert.All(failures, failure => Assert.Null(failure));
        }
#if NET10_0_OR_GREATER
        var expectedValue = catalog.GetText("zh-TW", key);
        for (var warm = 0; warm < 10_000; warm++) _ = catalog.GetText("zh-TW", key);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var read = 0; read < 100_000; read++)
            if (!ReferenceEquals(expectedValue, catalog.GetText("zh-TW", key))) throw new InvalidOperationException("Text reference changed.");
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
#endif
    }

    /// <summary>兩個不同穩定鍵可使用相同顯示值，不能按文字去重或覆寫其中一筆。</summary>
    [Fact(DisplayName = "相同顯示文字的不同鍵完整保留")]
    public void Table_SameTextDifferentKeys_PreservesBoth()
    {
        var catalog = new SystemTextTable(["zh-TW", "en"], [["A.First", "same", null], ["A.Second", "same", null]]);
        Assert.Equal(2, catalog.GetTable("zh-TW").Count);
        Assert.Equal("same", catalog.GetText("zh-TW", new TextKey("A.First")));
        Assert.Equal("same", catalog.GetText("en", new TextKey("A.Second")));
    }

    /// <summary>注入重複穩定鍵時整個建構失敗，不由字典索引覆寫或靜默丟掉一列。</summary>
    [Fact(DisplayName = "重複資源鍵拒絕發布")]
    public void Table_DuplicateKey_RejectsCandidate() => Assert.Throws<InvalidDataException>(() => new SystemTextTable(["zh-TW", "en"], [["A.First", "one", null], ["A.First", "two", null]]));

    /// <summary>建構完成後修改來源語系／列／值陣列，已發布表仍必須保持原結果及完整後備。</summary>
    [Fact(DisplayName = "來源陣列修改不影響已發布表")]
    public void Table_SourceArraysMutated_DoesNotChangeSnapshot()
    {
        string[] cultures = ["zh-TW", "en"];
        string?[][] rows = [["A.First", "original", null]];
        var catalog = new SystemTextTable(cultures, rows);
        cultures[0] = "changed";
        rows[0][1] = "changed";
        rows[0] = ["B.Other", "changed", "changed"];
        Assert.Equal("original", catalog.GetText("zh-TW", new TextKey("A.First")));
        Assert.Equal("original", catalog.GetText("en", new TextKey("A.First")));
    }

    /// <summary>語系名稱大小寫等價，重複欄位、缺中性值或列長度錯誤均必須在發布前失敗。</summary>
    [Fact(DisplayName = "錯誤語系與不完整列拒絕發布")]
    public void Table_InvalidMetadata_FailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => new SystemTextTable(["zh-TW", "ZH-tw"], [["A.First", "one", "two"]]));
        Assert.Throws<InvalidDataException>(() => new SystemTextTable(["en"], [["A.First", "one"]]));
        Assert.Throws<InvalidDataException>(() => new SystemTextTable(["zh-TW", "en"], [["A.First", "one"]]));
        Assert.Throws<InvalidDataException>(() => new SystemTextTable(["zh-TW", "en"], [["A.First", null, "two"]]));
    }

    /// <summary>固定清單包含三種明確狀態，不能從清單介面修改，亦不自動開放英文或偽語系。</summary>
    [Fact(DisplayName = "支援語系中繼資料固定且唯讀")]
    public void SupportedCultures_Metadata_IsImmutable()
    {
        var all = SupportedCultures.All;
        Assert.Equal(3, all.Count);
        Assert.Equal(CultureReleaseStatus.Released, all.Single(culture => culture.Name == "zh-TW").Status);
        Assert.Equal(CultureReleaseStatus.Shell, all.Single(culture => culture.Name == "en").Status);
        Assert.Equal(CultureReleaseStatus.Pseudo, all.Single(culture => culture.Name == "qps-ploc").Status);
        if (all is IList<SupportedCulture> mutable) Assert.Throws<NotSupportedException>(() => mutable[0] = all[1]);
        Assert.Same(all, SupportedCultures.All);
    }

    /// <summary>相同鍵雜湊與相等運算必須一致；不同鍵及不同型別不能因顯示值相同而相等。</summary>
    [Fact(DisplayName = "文字鍵相等與雜湊遵守 Ordinal 契約")]
    public void TextKey_EqualityAndHash_AreConsistent()
    {
        var first = new TextKey("A.First");
        var same = new TextKey("A.First");
        var other = new TextKey("A.Second");
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.True(first != other);
        Assert.False(first == other);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.True(first.Equals((object)same));
        Assert.False(first.Equals("A.First"));
        Assert.Equal("A.First", first.Value);
        Assert.Equal("A.First", first.ToString());
    }

    /// <summary>預設 struct 不可能繞過鍵驗證命中合法資源；空參數建構則明確拒絕。</summary>
    [Fact(DisplayName = "預設文字鍵與空參數不會偽裝成合法鍵")]
    public void TextKey_DefaultAndNull_AreHandledExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => new TextKey(null!));
        Assert.Equal(string.Empty, default(TextKey).Value);
        Assert.Equal(default, default(TextKey));
        Assert.Throws<KeyNotFoundException>(() => new SystemTextTable().GetText("zh-TW", default));
    }

    /// <summary>未知語系與大小寫變化只重用既有六鍵快照，不能每次新增表或建立暫存翻譯；固定次數後容量不變。</summary>
    [Fact(DisplayName = "未知語系重用既有後備快照")]
    public void Table_UnknownCultures_ReuseFixedFallback()
    {
        var catalog = new SystemTextTable();
        var original = catalog.GetTable("zh-TW");
        for (var index = 0; index < 1000; index++) Assert.Same(original, catalog.GetTable("unrecognized-" + index));
        Assert.Same(original, catalog.GetTable("ZH-tw"));
        Assert.Equal(6, original.Count);
    }
}
