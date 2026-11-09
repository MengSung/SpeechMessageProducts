/*
檔案整體說明
目的與定位：保護 P3-01 來源唯讀、CP950 無損、轉碼子程序排空與完整輸出發布的檔案／資源契約。
主要功能與流程：合成十份白名單檔經真正 P0-05 腳本轉碼，逐 byte 核對原檔不變、manifest 與行號；另注入非法編碼、過大檔、額外設定路徑、取消及轉碼失敗。CLI 測試驗證十三檔完整產出，發布錯誤保留上一版，並確認暫存目錄歸零。
使用邊界與維護：每個案例的 fixture 是唯一目錄 owner，所有 task 完成後 Dispose；使用的失敗腳本只在該根，沒有外部連線或真實秘密。原始轉碼 manifest 只留本案例 scratch，結束即清理；不是實機證據。斷言錯誤分類與檔案內容，不能把 MSBuild 或工具環境問題當功能紅燈。
*/
using System.Globalization;
using System.Text;
using System.Text.Json;
using LegacyGolden;
using Xunit;
using GoldenProgram = LegacyGolden.Program;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>檔案、程序與失敗清理測試；不讀外部舊來源或產品設定。</summary>
public sealed class LegacyGoldenIoTests
{
    /// <summary>用真正 P0-05 轉碼含繁中的 CP950 合成檔，原 byte 不變、行號一致且副本目錄清空。</summary>
    [Fact]
    public async Task SourceReader_RealConverter_PreservesBytesLinesAndManifest()
    {
        using var fixture = new LegacyGoldenFixture();
        var path = Path.Combine(fixture.Source, LegacySourceReader.MatcherPath);
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
        File.AppendAllText(path, "// 合成來源：維持原行號。\r\n", encoding);
        var before = LegacySourceReader.AllowedPaths.ToDictionary(relative => relative,
            relative => File.ReadAllBytes(Path.Combine(fixture.Source, relative)), StringComparer.Ordinal);
        var result = await LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, LegacyGoldenFixture.Converter, CancellationToken.None);
        Assert.Equal(10, result.Length);
        foreach (var source in result)
        {
            Assert.Equal(before[source.Path], File.ReadAllBytes(Path.Combine(fixture.Source, source.Path)));
            Assert.Equal(LegacySourceReader.Hash(before[source.Path]), source.OriginalSha256);
            Assert.Equal(LegacySourceReader.CountLines(encoding.GetString(before[source.Path])), source.LineCount);
        }
        Assert.Contains("合成來源", result.Single(source => source.Path == LegacySourceReader.MatcherPath).Text, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
        var manifestPath = Assert.Single(Directory.GetFiles(fixture.Scratch, "*-conversion.json"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(0, manifest.RootElement.GetProperty("failureCount").GetInt32());
        Assert.All(manifest.RootElement.GetProperty("files").EnumerateArray(), item =>
        {
            Assert.Equal("applied", item.GetProperty("status").GetString());
            Assert.True(item.GetProperty("stringLiteralsUnchanged").GetBoolean());
        });
    }

    /// <summary>未定義 CP950 序列以 exception fallback 拒絕，不可以替代字元補完後當作無損。</summary>
    [Fact]
    public void SourceReader_StrictCp950RejectsInvalidBytes_AndPreservesNewlines()
    {
        Assert.Throws<DecoderFallbackException>(() => LegacySourceReader.DecodeCp950([0x81]));
        Assert.Equal("一", LegacySourceReader.DecodeCp950([0xa4, 0x40]));
        Assert.Equal("a\r\nb\r\n\r\n", LegacySourceReader.Normalize("a\nb\n\n"));
        Assert.Equal(3, LegacySourceReader.CountLines("a\nb\n\n"));
        Assert.Equal("a\r\n", LegacySourceReader.Normalize("a"));
    }

    /// <summary>路徑白名單在讀取前即生效；不得接觸 appsettings 或透過相對路徑繞過根。</summary>
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("../private.cs")]
    [InlineData("LineMessagePlugIn/Processors/Keywords/MessageKeywordMatcherOptimized.cs")]
    public void SourceReader_RejectsUnallowlistedPaths(string path)
    {
        using var fixture = new LegacyGoldenFixture();
        Assert.Equal("Source is not allowlisted.", Assert.Throws<InvalidDataException>(() => LegacySourceReader.ReadAllowed(fixture.Source, path)).Message);
    }

    /// <summary>超過一 MiB 的白名單檔拒絕且不載入大內容，其他來源與本次暫存都不受污染。</summary>
    [Fact]
    public async Task SourceReader_RejectsSizeAndInvalidBytes_AndCleansCandidate()
    {
        using var fixture = new LegacyGoldenFixture();
        var path = Path.Combine(fixture.Source, LegacySourceReader.MatcherPath);
        File.WriteAllBytes(path, new byte[1024 * 1024 + 1]);
        Assert.Equal("Source exceeds one MiB.", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, LegacyGoldenFixture.Converter, CancellationToken.None))).Message);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
        File.WriteAllBytes(path, [0x81]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, LegacyGoldenFixture.Converter, CancellationToken.None));
        Assert.Equal(new byte[] { 0x81 }, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    /// <summary>轉碼拒絕與 caller 取消都結束程序並清副本；失敗 script 不得被視為成功轉碼。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceReader_ConverterFailureOrCancellation_DrainsBeforeCleanup(bool cancel)
    {
        using var fixture = new LegacyGoldenFixture();
        var converter = Path.Combine(fixture.Root, "fault.ps1");
        File.WriteAllText(converter, cancel ? "Start-Sleep -Seconds 120\r\n" : "exit 7\r\n", new UTF8Encoding(false));
        using var timeout = new CancellationTokenSource();
        if (cancel) timeout.CancelAfter(TimeSpan.FromSeconds(1));
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, converter, timeout.Token));
        else Assert.Equal("P0-05 conversion failed.", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, converter, timeout.Token))).Message);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
        Assert.Equal(10, LegacySourceReader.AllowedPaths.Count(relative => File.Exists(Path.Combine(fixture.Source, relative))));
    }

    /// <summary>故障 converter 只回成功但沒有 P0-05 manifest 時必須拒絕；ASCII 原檔碰巧已是 UTF-8 不能冒充完成轉碼。</summary>
    [Fact]
    public async Task SourceReader_RejectsSuccessfulProcessWithoutP005Manifest()
    {
        using var fixture = new LegacyGoldenFixture();
        var converter = Path.Combine(fixture.Root, "false-success.ps1");
        File.WriteAllText(converter, "exit 0\r\n", new UTF8Encoding(false));
        Assert.Equal("P0-05 manifest is missing.", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, converter, CancellationToken.None))).Message);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    /// <summary>偽造 dry-run、hash 或候選內容的 converter 即使回零也不得發布；每次精確斷言稽核拒絕與副本清空。</summary>
    [Theory]
    [InlineData("dry-run", "P0-05 manifest is incomplete.")]
    [InlineData("hash", "P0-05 manifest does not match the source bytes.")]
    [InlineData("content", "P0-05 manifest does not match the source bytes.")]
    public async Task SourceReader_RejectsTamperedManifestOrCandidate(string fault, string expected)
    {
        using var fixture = new LegacyGoldenFixture();
        var converter = Path.Combine(fixture.Root, "tamper.ps1");
        var script = """
            param([string[]]$Path, [string]$ManifestPath, [switch]$Apply)
            $records = @($Path | ForEach-Object {
                $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
                @{ path=[IO.Path]::GetFullPath($_); status='applied'; stringLiteralsUnchanged=$true; beforeSha256=$hash; afterSha256=$hash }
            })
            $audit = @{ files=$records; dryRun=$false; failureCount=0 }
            switch ('FAULT') {
                'dry-run' { $audit.dryRun=$true }
                'hash' { $records[0].afterSha256='00' }
                'content' { [IO.File]::AppendAllText($Path[0], 'changed') }
            }
            [IO.File]::WriteAllText($ManifestPath, ($audit | ConvertTo-Json -Depth 8))
            exit 0
            """;
        File.WriteAllText(converter, LegacySourceReader.Normalize(script.Replace("FAULT", fault, StringComparison.Ordinal)), new UTF8Encoding(false));
        Assert.Equal(expected, (await Assert.ThrowsAsync<InvalidDataException>(() =>
            LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, converter, CancellationToken.None))).Message);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
        Assert.Single(Directory.GetFiles(fixture.Scratch, "*-conversion.json"));
    }

    /// <summary>來源與暫存互相包含時在建立副本前拒絕；已取消呼叫也不得留下目錄。</summary>
    [Fact]
    public async Task SourceReader_RejectsOverlappingRoots_AndPreCancelledOperation()
    {
        using var fixture = new LegacyGoldenFixture();
        Assert.Equal("Source and scratch roots must be separate.", (await Assert.ThrowsAsync<InvalidDataException>(() =>
            LegacySourceReader.ReadAsync(fixture.Source, Path.Combine(fixture.Source, "temp"), LegacyGoldenFixture.Converter, CancellationToken.None))).Message);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LegacySourceReader.ReadAsync(fixture.Source, fixture.Scratch, LegacyGoldenFixture.Converter, cancellation.Token));
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    /// <summary>真 CLI 管線發布十三個檔案且可重跑逐 byte 一致，原資料不變，退出碼與安全摘要可由上層保存。</summary>
    [Fact]
    public async Task Cli_RealConversionAndGeneration_PublishesDeterministicCompleteSet()
    {
        using var fixture = new LegacyGoldenFixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var args = Arguments(fixture);
        Assert.Equal(0, await GoldenProgram.RunAsync(args, output, error, CancellationToken.None));
        var first = Directory.GetFiles(fixture.Output, "*.json", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(fixture.Output, path), File.ReadAllBytes, StringComparer.Ordinal);
        Assert.Equal(13, first.Count);
        Assert.Equal(0, await GoldenProgram.RunAsync(args, output, error, CancellationToken.None));
        foreach (var item in first) Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(fixture.Output, item.Key)));
        Assert.Empty(error.ToString());
        Assert.Contains("original matcher executed", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".legacy-golden-*"));
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    /// <summary>非法參數、根重疊與缺少 converter 都只回固定分類；來源路徑或例外細節不得被寫到 CLI 錯誤。</summary>
    [Fact]
    public async Task Cli_InvalidOperations_FailWithoutPublishingOrLeakingDetails()
    {
        using var fixture = new LegacyGoldenFixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(2, await GoldenProgram.RunAsync([], output, error, CancellationToken.None));
        var args = Arguments(fixture);
        args[7] = fixture.Source;
        Assert.Equal(1, await GoldenProgram.RunAsync(args, output, error, CancellationToken.None));
        args = Arguments(fixture); args[5] = Path.Combine(fixture.Root, "absent-private-marker.ps1");
        Assert.Equal(1, await GoldenProgram.RunAsync(args, output, error, CancellationToken.None));
        Assert.DoesNotContain("absent-private-marker", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Output));
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    /// <summary>任何工具不擁有的檔案或越界相對路徑都拒絕，前版 JSON 必須原樣保留。</summary>
    [Fact]
    public void Publisher_RejectsForeignFilesAndUnsafePaths_PreservesPreviousSnapshot()
    {
        using var fixture = new LegacyGoldenFixture();
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["Keywords/routing.json"] = "{}\r\n" };
        GoldenOutput.Publish(fixture.Output, files);
        var foreign = Path.Combine(fixture.Output, "other-task.txt");
        File.WriteAllText(foreign, "owned-by-other");
        Assert.Throws<InvalidDataException>(() => GoldenOutput.Publish(fixture.Output, files));
        Assert.Equal("owned-by-other", File.ReadAllText(foreign));
        Assert.Equal("{}\r\n", File.ReadAllText(Path.Combine(fixture.Output, "Keywords", "routing.json")));
        files.Add("Events/synthetic-../../escape.json", "{}");
        Assert.Equal("Output contains an unowned filename.", Assert.Throws<InvalidDataException>(() => GoldenOutput.Publish(fixture.Output, files)).Message);
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".legacy-golden-*"));
    }

    /// <summary>空的未知目錄也是別包資產，不能因沒有檔案就遞迴刪除；發布期間目的地被檔案占用時 staging 必須清理。</summary>
    [Fact]
    public void Publisher_RejectsForeignDirectory_AndCleansFailedPublish()
    {
        using var fixture = new LegacyGoldenFixture();
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["Keywords/routing.json"] = "{}\r\n" };
        Directory.CreateDirectory(Path.Combine(fixture.Output, "other-task"));
        Assert.Equal("Output contains a directory owned by another task.", Assert.Throws<InvalidDataException>(() => GoldenOutput.Publish(fixture.Output, files)).Message);
        Directory.Delete(Path.Combine(fixture.Output, "other-task")); Directory.Delete(fixture.Output);
        File.WriteAllText(fixture.Output, "blocking-file");
        Assert.Throws<IOException>(() => GoldenOutput.Publish(fixture.Output, files));
        Assert.Equal("blocking-file", File.ReadAllText(fixture.Output));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".legacy-golden-*"));
    }

    /// <summary>真正先搬移舊版，再於第二次 rename 注入失敗；rollback 若也失敗必須保留唯一舊快照，不能在 finally 刪掉救援資料。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publisher_RenameAndRollbackFailures_NeverDeletePreviousData(bool rollbackFailure)
    {
        using var fixture = new LegacyGoldenFixture();
        var original = new Dictionary<string, string>(StringComparer.Ordinal) { ["Keywords/routing.json"] = "{\"version\":1}\r\n" };
        var next = new Dictionary<string, string>(StringComparer.Ordinal) { ["Keywords/routing.json"] = "{\"version\":2}\r\n" };
        GoldenOutput.Publish(fixture.Output, original);
        var calls = 0;
        void Move(string from, string to)
        {
            calls++;
            if (calls == 2 || rollbackFailure && calls == 3) throw new IOException("Synthetic rename fault.");
            Directory.Move(from, to);
        }
        if (rollbackFailure)
        {
            var failure = Assert.Throws<AggregateException>(() => GoldenOutput.Publish(fixture.Output, next, Move));
            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.All(failure.InnerExceptions, exception => Assert.IsType<IOException>(exception));
        }
        else Assert.Throws<IOException>(() => GoldenOutput.Publish(fixture.Output, next, Move));
        var preserved = rollbackFailure ? Assert.Single(Directory.GetDirectories(fixture.Root, ".legacy-golden-backup-*")) : fixture.Output;
        Assert.Equal(original["Keywords/routing.json"], File.ReadAllText(Path.Combine(preserved, "Keywords", "routing.json")));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".legacy-golden-stage-*"));
    }

    /// <summary>purpose-built CLI 引數只指本 fixture 與正式 converter，從不讀主機的外部舊方案。</summary>
    private static string[] Arguments(LegacyGoldenFixture fixture) =>
        ["--source-root", fixture.Source, "--scratch-root", fixture.Scratch, "--converter", LegacyGoldenFixture.Converter, "--output", fixture.Output];
}
