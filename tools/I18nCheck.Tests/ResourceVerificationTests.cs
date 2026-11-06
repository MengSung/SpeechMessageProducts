using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace I18nCheck.Tests;

/// <summary>
/// 以實際 verify 入口保護 P1-09 每項正反例；生成檔先以正式生成器建立，不以假綠替身跳過。
/// 每案的方案、limits、resx 及 writer 均由本案獨佔並立即釋放；不讀取真實產品設定或連網。
/// </summary>
public sealed class ResourceVerificationTests
{
    /// <summary>同組跨文化相同 key 是翻譯，不是重複；具名參數換順序／重複但集合相同應通過。</summary>
    [Fact(DisplayName = "翻譯鍵與參數集合相同通過")]
    public void Verify_SameGroupTranslationsAndParameterSets_Passes()
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.Text", "{name} {count}"));
        files.Write("Text", "en", ("A.Text", "{count} {name} {name}"));
        files.Generate();
        Assert.Equal(0, files.Verify(out var output));
        Assert.Contains("en 完成度 100.00%", output, StringComparison.Ordinal);
    }

    /// <summary>未知翻譯 key 或改名參數都由既有 ResourceCatalog 拒絕；不能被 stale output 遮蔽成成功。</summary>
    [Theory(DisplayName = "翻譯未知鍵或參數不一致拒絕")]
    [InlineData("A.Other", "{name}")]
    [InlineData("A.Text", "{other}")]
    public void Verify_TranslationKeyOrParametersMismatch_Rejects(string key, string text)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.Text", "{name}"));
        files.Generate();
        files.Write("Text", "en", (key, text));
        Assert.Equal(2, files.Verify(out _));
    }

    /// <summary>兩組中性資源同 key 即使文字相同也衝突；不同 key 同顯示文字合法，不可按內容去重。</summary>
    [Theory(DisplayName = "跨檔穩定鍵衝突拒絕且同文字不同鍵保留")]
    [InlineData("A.First", 1)]
    [InlineData("A.Second", 0)]
    public void Verify_AcrossResourceGroups_ValidatesStableKeys(string secondKey, int expected)
    {
        using var files = new VerificationFixture();
        files.Write("First", "", ("A.First", "same display"));
        files.Write("Second", "", (secondKey, "same display"));
        files.Generate();
        Assert.Equal(expected, files.Verify(out var output));
        if (expected != 0) Assert.Contains("I18N601", output, StringComparison.Ordinal);
    }

    /// <summary>保留正式 E4 字面十四字樣本；原後綴的 pseudo=21 必須失敗，縮固定後綴而不縮名稱才可通過。</summary>
    [Theory(DisplayName = "最長教會名與偽語系共同驗證sender上限")]
    [InlineData("{churchName}小幫手", 1)]
    [InlineData("{churchName}助手", 0)]
    public void Verify_SenderPseudoWithFullChurchName_Enforces20(string template, int expected)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("Line.Sender.Template", template));
        files.Generate();
        Assert.Equal(14, VerificationFixture.ChurchName.Length);
        Assert.Equal(expected, files.Verify(out var output));
        if (expected != 0)
        {
            Assert.Contains("I18N602", output, StringComparison.Ordinal);
            Assert.Contains("qps-ploc", output, StringComparison.Ordinal);
            Assert.DoesNotContain(VerificationFixture.ChurchName, output, StringComparison.Ordinal);
        }
    }

    /// <summary>每一原始語系都檢查 sender20/chatbar14；不是只檢中性或只靠 pseudo 失敗。</summary>
    [Theory(DisplayName = "各語系sender與聊天列原始長度皆守門")]
    [InlineData("Line.Sender.Template", "", 20, 1)]
    [InlineData("Line.Sender.Template", "en", 21, 1)]
    [InlineData("Line.Sender.Template", "en", 20, 0)]
    [InlineData("RichMenu.ChatBar.Main", "", 8, 0)]
    [InlineData("RichMenu.ChatBar.Main", "", 9, 1)]
    [InlineData("RichMenu.ChatBar.Main", "en", 14, 0)]
    [InlineData("RichMenu.ChatBar.Main", "en", 15, 1)]
    public void Verify_LimitedTemplates_AllCultures(string key, string culture, int length, int expected)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", (key, culture.Length == 0 ? new string('A', length) : "short"));
        if (culture.Length != 0) files.Write("Text", culture, (key, new string('A', length)));
        files.Generate();
        Assert.Equal(expected, files.Verify(out _));
    }

    /// <summary>emoji 以 UTF16 與 Sender 相同單位計數；不能把 surrogate 當一個單位而放寬平台限制。</summary>
    [Fact(DisplayName = "sender長度與SDK相同採UTF16保守計數")]
    public void Verify_SenderSurrogates_CountsUtf16()
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("Line.Sender.Template", "ok"));
        files.Write("Text", "en", ("Line.Sender.Template", string.Concat(Enumerable.Repeat("😀", 11))));
        files.Generate();
        Assert.Equal(1, files.Verify(out _));
    }

    /// <summary>不能為未知參數猜短值；配置超限或缺少必備檔必須拒絕，而不是跳過長度守門。</summary>
    [Theory(DisplayName = "受限模板未知參數及缺配置拒絕")]
    [InlineData("parameter")]
    [InlineData("missing")]
    [InlineData("relaxed")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    public void Verify_InvalidLengthConfiguration_Rejects(string fault)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("Line.Sender.Template", fault == "parameter" ? "{unknown}" : "short"));
        files.Generate();
        var path = Path.Combine(files.Root, "tools/I18nCheck/limits.json");
        if (fault == "missing") File.Delete(path);
        if (fault == "relaxed") File.WriteAllText(path, """{"longestChurchName":"full","limits":{"Line.Sender.Template":21,"RichMenu.ChatBar.*":14}}""");
        if (fault == "duplicate") File.WriteAllText(path, """{"longestChurchName":"first","longestChurchName":"second","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":14}}""");
        if (fault == "blank") File.WriteAllText(path, """{"longestChurchName":" ","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":14}}""");
        Assert.Equal(2, files.Verify(out _));
    }

    /// <summary>同意圖的重述合法；全形、Unicode空白、大小寫及跨語系異意圖衝突都不得靠先後順序隱藏。</summary>
    [Theory(DisplayName = "關鍵字正規化同意圖合法異意圖拒絕")]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    public void Verify_KeywordAliases_DetectsSameAndCrossCultureConflict(bool conflict, bool crossCulture, int expected)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("Keyword.First", "  ＳＨＯＷ　　ＩＤ  |show id"), ("Keyword.Second", conflict && !crossCulture ? "Show Id" : "different"));
        files.Write("Text", "en", ("Keyword.First", "SHOW ID"), ("Keyword.Second", conflict && crossCulture ? "show id" : "other"));
        files.Generate();
        Assert.Equal(expected, files.Verify(out var output));
        if (expected != 0)
        {
            Assert.Contains("I18N603", output, StringComparison.Ordinal);
            Assert.DoesNotContain("show id", output, StringComparison.Ordinal);
        }
    }

    /// <summary>空別名是配置錯誤，不可當永遠命中的路由或悄悄忽略。</summary>
    [Fact(DisplayName = "空關鍵字別名拒絕")]
    public void Verify_EmptyKeywordAlias_Rejects()
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("Keyword.First", "valid| "));
        files.Generate();
        Assert.Equal(2, files.Verify(out _));
    }

    /// <summary>英文缺譯、部分及全譯都輸出精確資訊而通過；其他語系的翻譯不能冒充英文進度。</summary>
    [Theory(DisplayName = "英文完成度僅報告而不阻斷")]
    [InlineData(0, "0.00%")]
    [InlineData(1, "50.00%")]
    [InlineData(2, "100.00%")]
    public void Verify_EnglishCompletion_IsInformational(int translated, string expected)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.First", "one"), ("A.Second", "two"));
        files.Write("Text", "en", translated switch
        {
            0 => [],
            1 => [("A.First", "first")],
            _ => [("A.First", "first"), ("A.Second", "second")]
        });
        files.Write("Text", "de", ("A.First", "eins"), ("A.Second", "zwei"));
        files.Generate();
        Assert.Equal(0, files.Verify(out var output));
        Assert.Contains("en 完成度 " + expected, output, StringComparison.Ordinal);
    }

    /// <summary>失敗不得寫任何檔；修正來源並重生後下次可恢復，所有檔案把手在回傳時可獨占開啟。</summary>
    [Fact(DisplayName = "verify唯讀失敗可重試且釋放全部把手")]
    public void Verify_FailureThenRetry_IsReadOnlyAndReleasesFiles()
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("RichMenu.ChatBar.Main", new string('X', 15)));
        files.Generate();
        var before = Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        Assert.Equal(1, files.Verify(out _));
        Assert.Equal(before.Count, Directory.GetFiles(files.Root, "*", SearchOption.AllDirectories).Length);
        foreach (var pair in before)
        {
            Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            using var exclusive = File.Open(pair.Key, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(exclusive.CanWrite);
        }
        files.Write("Text", "", ("RichMenu.ChatBar.Main", "ok"));
        files.Generate();
        Assert.Equal(0, files.Verify(out _));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>兩個 repo 交錯多次 verify，錯誤狀態與完成度不能串入另一個；所有 Task 結束後立即清理。</summary>
    [Fact(DisplayName = "並行repo驗證互不洩漏且完成後排空")]
    public async Task Verify_ParallelRepositories_NoStateLeak()
    {
        using var first = new VerificationFixture();
        using var second = new VerificationFixture();
        first.Write("Text", "", ("RichMenu.ChatBar.Main", "ok"));
        second.Write("Text", "", ("RichMenu.ChatBar.Main", new string('X', 15)));
        first.Generate();
        second.Generate();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() =>
        {
            bool valid = index % 2 == 0;
            Assert.Equal(valid ? 0 : 1, (valid ? first : second).Verify(out _));
        })));
    }

    /// <summary>格式錯誤的配置只能得到固定拒絕，不能因JSON型別錯誤逃出CLI或輸出內容。</summary>
    [Theory(DisplayName = "limits所有型別與重複屬性錯誤封閉拒絕")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"longestChurchName":1,"limits":{}}""")]
    [InlineData("""{"longestChurchName":"ok","limits":[]}""")]
    [InlineData("""{"longestChurchName":"ok","limits":{"Line.Sender.Template":"20","RichMenu.ChatBar.*":14}}""")]
    [InlineData("""{"longestChurchName":"ok","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":"14"}}""")]
    [InlineData("""{"longestChurchName":"ok","limits":{"Line.Sender.Template":20,"Line.Sender.Template":20,"RichMenu.ChatBar.*":14}}""")]
    [InlineData("""{"longestChurchName":"ok","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":15}}""")]
    [InlineData("""{"longestChurchName":"\uD800","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":14}}""")]
    [InlineData("""{"\uD800":"ok","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":14}}""")]
    public void Verify_MalformedLimits_FailsClosed(string json)
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.Text", "sample"));
        files.Generate();
        File.WriteAllText(Path.Combine(files.Root, "tools/I18nCheck/limits.json"), json);
        Assert.Equal(2, files.Verify(out _));
    }

    /// <summary>最長名稱與替換數皆有界；只量長不建展開字串，插入值的大括號不得被重新解析。</summary>
    [Fact(DisplayName = "最長名稱與參數展開有界且單次替換")]
    public void Length_MeasuresWithoutExpansion_RejectsUnboundedInputs()
    {
        Assert.Equal(18, Placeholders.LengthWithChurchName("{churchName}{churchName}", "{x}123456"));
        Assert.Throws<InvalidDataException>(() => Placeholders.LengthWithChurchName(string.Concat(Enumerable.Repeat("{churchName}", 257)), "full"));
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.Text", "sample"));
        files.Generate();
        File.WriteAllText(Path.Combine(files.Root, "tools/I18nCheck/limits.json"), "{\"longestChurchName\":\"" + new string('X', 1025) + "\",\"limits\":{\"Line.Sender.Template\":20,\"RichMenu.ChatBar.*\":14}}");
        Assert.Equal(2, files.Verify(out _));
    }

    /// <summary>過大的資源文字與鍵數在發布前拒絕，不讓大量候選或pseudo展開造成無界配置。</summary>
    [Theory(DisplayName = "單組資源鍵與文字預算拒絕")]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceCatalog_OverBudget_Rejects(bool keys)
    {
        using var files = new ResourceFixture();
        files.Write("", keys ? Enumerable.Range(0, 20_001).Select(index => ("A.Key" + index.ToString(CultureInfo.InvariantCulture), "value")).ToArray() : [("A.Text", new string('X', 32_769))]);
        Assert.Contains("I18N110", Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath)).Message, StringComparison.Ordinal);
    }

    /// <summary>全部resx的檔數與合計大小先於XML配置檢查；稀疏合成檔避免真實大資料或外部內容。</summary>
    [Theory(DisplayName = "方案資源檔數與合計大小皆有界")]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceSet_RepositoryBudget_Rejects(bool fileCount)
    {
        using var files = new RepositoryFixture();
        files.AddProject("App", false);
        int count = fileCount ? 4097 : 9;
        for (int index = 0; index < count; index++)
        {
            string path = Path.Combine(files.Root, "App", "Text" + index.ToString(CultureInfo.InvariantCulture) + ".resx");
            using var stream = File.Create(path);
            if (!fileCount) stream.SetLength(8 * 1024 * 1024);
        }
        Assert.Contains("I18N605", Assert.Throws<InvalidDataException>(() => ResourceSet.LoadRepository(files.Root)).Message, StringComparison.Ordinal);
    }

    /// <summary>同一目錄由兩個專案走訪不可重複發布同一實體資源；不以Distinct隱藏組裝衝突。</summary>
    [Fact(DisplayName = "方案重述同一實體資源拒絕")]
    public void ResourceSet_RepeatedFileIdentity_Rejects()
    {
        using var files = new RepositoryFixture();
        files.AddProject("App", false);
        files.Write("App/Text.resx", "<root/>");
        files.Write("App/Second.csproj", "<Project/>");
        File.AppendAllText(Path.Combine(files.Root, "SpeechMessageProducts.sln"),
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Second\", \"App/Second.csproj\", \"{00000000-0000-0000-0000-000000000009}\"\r\nEndProject\r\n");
        Assert.Contains("I18N604", Assert.Throws<InvalidDataException>(() => ResourceSet.LoadRepository(files.Root)).Message, StringComparison.Ordinal);
    }

    /// <summary>千次完整候選驗證共享的只有不可變資源；不同repo錯誤／文化狀態不串流，全部Task排空。</summary>
    [Fact(DisplayName = "千次資源快照並行驗證排空且不串repo")]
    public async Task Verify_ThousandSnapshotCalls_NoLeak()
    {
        using var first = new VerificationFixture();
        using var second = new VerificationFixture();
        first.Write("Text", "", ("RichMenu.ChatBar.Main", "ok"));
        second.Write("Text", "", ("RichMenu.ChatBar.Main", new string('X', 15)));
        var a = ResourceSet.LoadRepository(first.Root);
        var b = ResourceSet.LoadRepository(second.Root);
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
        {
            bool valid = index % 2 == 0;
            using var output = new StringWriter();
            Assert.Equal(valid ? 0 : 1, ResourceVerifier.Verify(valid ? first.Root : second.Root, valid ? a : b, output));
        })));
    }

    /// <summary>長生命靜態驗證入口不保留資源快照；百次臨時候選在GC後全部回基線。</summary>
    [Fact(DisplayName = "驗證器不保留操作區域快照")]
    public void Verify_TemporaryCatalogs_AreCollectible()
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.Text", "sample"));
        var references = Enumerable.Range(0, 100).Select(_ => VerifyTemporaryCatalog(files.Root)).ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>孤兒翻譯沒有zh-TW來源，不能偽裝成新的中性資源而繞過key一致性。</summary>
    [Fact(DisplayName = "孤兒語系檔不得冒充中性來源")]
    public void ResourceSet_OrphanTranslation_Rejects()
    {
        using var files = new RepositoryFixture();
        files.AddProject("App", false);
        files.Write("App/Text.en.resx", "<root><data name='A.OnlyEnglish'><value>only</value></data></root>");
        Assert.Throws<InvalidDataException>(() => ResourceSet.LoadRepository(files.Root));
    }

    /// <summary>保留語系不因檔名大小寫逃過檢查；qps只能由既有產生器建立，不能自行提供。</summary>
    [Theory(DisplayName = "保留語系大小寫變體拒絕")]
    [InlineData("zh-tw")]
    [InlineData("QPS-PLOC")]
    public void ResourceCatalog_ReservedCultureCase_Rejects(string culture)
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "sample"));
        files.Write(culture, ("A.Text", "other"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
    }

    /// <summary>顯式generate也不能繞過組級64MiB預算；在XML內容配置前，合計過大即固定拒絕。</summary>
    [Fact(DisplayName = "單組資源大小預檢不能由顯式生成繞過")]
    public void ResourceCatalog_GroupBudget_PrecedesParsing()
    {
        using var files = new ResourceFixture();
        string directory = Path.GetDirectoryName(files.BasePath)!;
        for (int index = 0; index < 9; index++)
        {
            using var stream = File.Create(Path.Combine(directory, index == 0 ? "Sample.resx" : "Sample.en-" + index.ToString(CultureInfo.InvariantCulture) + ".resx"));
            stream.SetLength(8 * 1024 * 1024);
        }
        Assert.Contains("I18N616", Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath)).Message, StringComparison.Ordinal);
    }

    /// <summary>以獨立stack frame排除測試JIT暫留；只回弱參照，全部reader與writer在回傳前關閉。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference VerifyTemporaryCatalog(string root)
    {
        var candidate = ResourceSet.LoadRepository(root);
        using var output = new StringWriter();
        Assert.Equal(0, ResourceVerifier.Verify(root, candidate, output));
        return new WeakReference(candidate[0].Catalog);
    }
}

/// <summary>單案 owner 封裝既有隔離 repo；全部來源合成，Dispose 後由原 owner 確定清理。</summary>
internal sealed class VerificationFixture : IDisposable
{
    /// <summary>正式 E4 字面樣本完整保留，實際十四字，不依文字中的「十二」猜長度。</summary>
    internal const string ChurchName = "這是一個長度剛好十二字的教會";
    /// <summary>唯一擁有者，沒有共享或背景存取；最長生命週期為單一案例。</summary>
    private readonly RepositoryFixture repository = new();
    /// <summary>本案建立的中性資源組名稱，僅供重生且最多本案明列的少數項目。</summary>
    private readonly HashSet<string> stems = new(StringComparer.Ordinal);
    /// <summary>公開本案根供故障注入，不接受外部目錄作清理目標。</summary>
    internal string Root => repository.Root;

    /// <summary>建立空 App 與正式形狀的limits及CJK基準，不讀主工作樹或任何User Secrets。</summary>
    internal VerificationFixture()
    {
        repository.AddProject("App", false);
        repository.Write("tools/I18nCheck/limits.json", """{"longestChurchName":"這是一個長度剛好十二字的教會","limits":{"Line.Sender.Template":20,"RichMenu.ChatBar.*":14}}""");
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(Root, ["scan", "--write-baseline", "tools/I18nCheck/baselines/cjk-literals.json"], output));
    }

    /// <summary>以XML API建立純字串假資源；明確保留重複或錯誤內容供拒絕測試使用。</summary>
    internal void Write(string stem, string culture, params (string Key, string Text)[] rows)
    {
        if (culture.Length == 0) stems.Add(stem);
        var xml = new XElement("root", rows.Select(row => new XElement("data", new XAttribute("name", row.Key), new XElement("value", row.Text))));
        repository.Write("App/" + stem + (culture.Length == 0 ? "" : "." + culture) + ".resx", xml.ToString());
    }

    /// <summary>透過正式CLI生成真實資料，拒絕以手造.g.cs跳過生成一致性；所有writer本次立即Dispose。</summary>
    internal void Generate()
    {
        foreach (var stem in stems)
        {
            using var output = new StringWriter();
            Assert.Equal(0, CommandRunner.Run(Root, ["generate", "--resx", "App/" + stem + ".resx", "--namespace", "Sample.Resources", "--class", stem + "Data", "--out", "App/" + stem + ".g.cs"], output));
        }
    }

    /// <summary>真正執行verify且返回單次輸出，命令不得保存writer或來源至下次呼叫。</summary>
    internal int Verify(out string text)
    {
        using var output = new StringWriter();
        int result = CommandRunner.Run(Root, ["verify"], output);
        text = output.ToString();
        return result;
    }

    /// <summary>本案所有Task均由測試先await；之後清理自己建立的檔案，不碰主工作樹。</summary>
    public void Dispose() => repository.Dispose();
}
