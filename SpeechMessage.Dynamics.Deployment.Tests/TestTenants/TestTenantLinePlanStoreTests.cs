/*
檔案整體說明
目的與定位：保護 F12 測試租戶 LINE 清理計畫的跨 CLI 續跑、完整性、精確 scope、原子發布與本機資源生命週期；只覆蓋 TestTenantLinePlanStore，不啟動外部服務。
主要功能與流程：以測試 owner 獨占的暫存 artifacts 及 Windows CurrentUser DPAPI 真實落檔，搭配純合成 LINE client／預算，重現 alias 已刪但 menu 尚存、竄改、跨 scope、畸形 JSON、競爭發布、取消與本機 I/O 失敗。決定性斷言包含既存計畫零 factory 呼叫、provider ID 保留、檔案不覆寫、拒絕後證據仍在、沒有本次暫存檔及可移動／刪除的已排空目錄。
依賴與輸出：資料只有合成組織、產品、Channel、alias 與 menu ID；不包含真實 userId、token、HTTP、CRM、SQL、通知或實機操作。DPAPI 案例只在 Windows 執行，另有非 Windows 明確拒絕的入口斷言；純 JSON 格式案例可跨平台執行。
使用邊界與維護：每案以 using 關閉檔案 owner，finally 釋放非同步閘門並等所有操作完成；owner 清理自己建立且不需提權的 junction，再刪除唯一暫存目錄。禁止遞迴經由連結清除外部目標。原始證據／執行日誌交由呼叫端保存在 repo 外，測試不修改 manifest。
失敗處理與驗證：故障注入包含 factory 例外／取消、目標被目錄占用及重複／未知欄位；之後成功重試及目錄改名證明 handle 已釋放。這些本機合成驗證不代表 LINE、Dataverse 或外部 Channel 實機驗收。
*/
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using SpeechMessage.Scheduling;
using Xunit;
using Plan = SpeechMessage.Dynamics.Deployment.TestTenants.TestTenantLineCleanup.Plan;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>以有界本機檔案及合成 LINE 狀態驗證儲存計畫先於遠端副作用，錯誤與取消均能排空所有 owner。</summary>
public sealed class TestTenantLinePlanStoreTests
{
    private const string Organization = "goodshepherdback";
    private const string Product = "GoodShepherd2";
    private const string Channel = "T01";
    private const string Code = "c201";
    private const string ValidJson = """{"version":1,"organization":"goodshepherdback","product":"GoodShepherd2","channelKey":"T01","code":"c201","aliases":[{"alias":"c201-main","menu":"menu-original"}]}""";

    /// <summary>以真實落檔及合成 LINE 刪除中斷，證明新一輪在 alias 消失後仍恢復同一 menu ID，且不再次查詢 factory。</summary>
    [Fact]
    public async Task RoundTripPreservesMenuIdsAfterAliasDisappears()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        var (client, state) = TestTenantLineCleanupTests.SyntheticLineClient.Create(("c201-main", "menu-original"));
        using var clientOwner = state;
        using var budget = new SyntheticBudget();
        var cleanup = new TestTenantLineCleanup(client, budget, Product, Channel);
        var first = await Load(files, () => cleanup.CreatePlanAsync(Code, default));
        state.BeforeCall = operation => operation == "menu-delete:menu-original"
            ? Task.FromException(new IOException("Synthetic interrupted menu delete.")) : Task.CompletedTask;
        await Assert.ThrowsAsync<IOException>(() => cleanup.CleanupAsync(first, [], default));
        Assert.Empty(state.Aliases);
        Assert.Contains("menu-original", state.Menus);
        var calls = 0;
        var resumed = await Load(files, () =>
        {
            calls++;
            return cleanup.CreatePlanAsync(Code, default);
        });
        Assert.Equal(0, calls);
        Assert.Equal(first.Aliases, resumed.Aliases);
        Assert.Equal("menu-original", Assert.Single(resumed.Aliases).Value);
        state.BeforeCall = null;
        await cleanup.CleanupAsync(resumed, [], default);
        Assert.Empty(state.Menus);
        Assert.Equal(0, state.ActiveOperations);
        TestTenantLinePlanStore.Delete(files.Root, Organization, Product, Channel, Code);
        Assert.False(File.Exists(files.PlanPath));
        files.AssertNoTemporaryFiles();
    }

    /// <summary>既有加密計畫必須直接讀回；注入會拋例外的 factory，並逐位元組斷言檔案沒有重寫。</summary>
    [Fact]
    public async Task ExistingPlanNeverInvokesCreateOrOverwrites()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        await Load(files, () => Task.FromResult(NewPlan()));
        var before = File.ReadAllBytes(files.PlanPath);
        try
        {
            var loaded = await Load(files, () => throw new InvalidOperationException("Factory must not run."));
            Assert.Equal("menu-original", Assert.Single(loaded.Aliases).Value);
            AssertFileBytes(before, files.PlanPath);
        }
        finally { CryptographicOperations.ZeroMemory(before); }
        files.AssertNoTemporaryFiles();
    }

    /// <summary>單一密文字節遭變更後 Load 與 Delete 都須拒絕，factory 不執行且損毀證據不被刪除。</summary>
    [Fact]
    public async Task TamperedCiphertextIsRejectedAndPreserved()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        await Load(files, () => Task.FromResult(NewPlan()));
        var bytes = File.ReadAllBytes(files.PlanPath);
        try
        {
            bytes[^1] ^= 1;
            File.WriteAllBytes(files.PlanPath, bytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => Load(files,
                () => throw new InvalidOperationException("Factory must not run.")));
            Assert.Throws<InvalidDataException>(() => TestTenantLinePlanStore.Delete(files.Root, Organization, Product, Channel, Code));
            AssertFileBytes(bytes, files.PlanPath);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>個別改變組織、產品、Channel 或代碼；同一份密文不能跨 scope 載入或刪除，合法原檔始終保留。</summary>
    [Theory]
    [InlineData("other-test-org", Product, Channel, Code)]
    [InlineData(Organization, "GoodButler", Channel, Code)]
    [InlineData(Organization, Product, "T02", Code)]
    [InlineData(Organization, Product, Channel, "c202")]
    public async Task WrongScopeIsRejected(string organization, string product, string channel, string code)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        await Load(files, () => Task.FromResult(NewPlan()));
        var alternatePath = Path.Combine(files.DirectoryPath, code + ".line-cleanup.bin");
        if (code != Code) File.Copy(files.PlanPath, alternatePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => TestTenantLinePlanStore.LoadOrCreateAsync(
            files.Root, organization, product, channel, code,
            () => throw new InvalidOperationException("Factory must not run."), default));
        Assert.Throws<InvalidDataException>(() => TestTenantLinePlanStore.Delete(
            files.Root, organization, product, channel, code));
        Assert.True(File.Exists(files.PlanPath));
        Assert.True(File.Exists(alternatePath));
    }

    /// <summary>格式故障使用合法基準只改一項，必須拒絕重複／未知／缺漏／型別／版本／深度及尾端文件。</summary>
    [Theory]
    [InlineData("duplicate-root")]
    [InlineData("unknown-root")]
    [InlineData("duplicate-row")]
    [InlineData("unknown-row")]
    [InlineData("duplicate-alias")]
    [InlineData("missing-menu")]
    [InlineData("missing-scope")]
    [InlineData("wrong-version")]
    [InlineData("wrong-type")]
    [InlineData("wrong-prefix")]
    [InlineData("deep")]
    [InlineData("trailing")]
    [InlineData("comment")]
    [InlineData("trailing-comma")]
    [InlineData("invalid-utf8")]
    [InlineData("too-many")]
    public void StrictJsonRejectsMalformedDocument(string fault)
    {
        var text = fault switch
        {
            "duplicate-root" => ValidJson.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal),
            "unknown-root" => ValidJson.Replace("\"version\":1", "\"version\":1,\"userId\":\"forbidden\"", StringComparison.Ordinal),
            "duplicate-row" => ValidJson.Replace("\"menu\":", "\"alias\":\"c201-main\",\"menu\":", StringComparison.Ordinal),
            "unknown-row" => ValidJson.Replace("\"menu\":", "\"token\":\"forbidden\",\"menu\":", StringComparison.Ordinal),
            "duplicate-alias" => ValidJson.Replace("}]}", "},{\"alias\":\"c201-main\",\"menu\":\"menu-other\"}]}", StringComparison.Ordinal),
            "missing-menu" => ValidJson.Replace(",\"menu\":\"menu-original\"", "", StringComparison.Ordinal),
            "missing-scope" => ValidJson.Replace("\"organization\":\"goodshepherdback\",", "", StringComparison.Ordinal),
            "wrong-version" => ValidJson.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal),
            "wrong-type" => ValidJson.Replace("\"menu-original\"", "123", StringComparison.Ordinal),
            "wrong-prefix" => ValidJson.Replace("c201-main", "c2010-main", StringComparison.Ordinal),
            "deep" => ValidJson.Replace("\"menu-original\"", "[[[[[]]]]]", StringComparison.Ordinal),
            "trailing" => ValidJson + "{}",
            "comment" => "/*unexpected*/" + ValidJson,
            "trailing-comma" => ValidJson.Replace("}]}", "}],}", StringComparison.Ordinal),
            "too-many" => ValidJson.Replace("{\"alias\":\"c201-main\",\"menu\":\"menu-original\"}",
                string.Join(",", Enumerable.Range(0, 1001).Select(i =>
                    "{\"alias\":\"c201-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\",\"menu\":\"menu-original\"}")), StringComparison.Ordinal),
            _ => ValidJson
        };
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            if (fault == "invalid-utf8") bytes[bytes.AsSpan().IndexOf("menu-original"u8)] = 0xff;
            Assert.Throws<InvalidDataException>(() => TestTenantLinePlanStore.ParsePlan(bytes, Organization, Product, Channel, Code));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>合法 JSON 仍須逐欄核對明列 scope，不得只相信 DPAPI 呼叫端；相同 menu 的不同 alias 則完整保留。</summary>
    [Fact]
    public void StrictJsonValidatesScopeAndPreservesDistinctAliases()
    {
        var bytes = Encoding.UTF8.GetBytes(ValidJson.Replace("}]}", "},{\"alias\":\"c201-second\",\"menu\":\"menu-original\"}]}", StringComparison.Ordinal));
        try
        {
            var plan = TestTenantLinePlanStore.ParsePlan(bytes, Organization, Product, Channel, Code);
            Assert.Equal(2, plan.Aliases.Count);
            Assert.All(plan.Aliases, row => Assert.Equal("menu-original", row.Value));
            Assert.Throws<InvalidDataException>(() => TestTenantLinePlanStore.ParsePlan(bytes, "other-org", Product, Channel, Code));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>超過密文讀取上限、空檔及剛好上限的無效密文均拒絕，且不以重新建立覆蓋既有證據。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(512 * 1024)]
    [InlineData(512 * 1024 + 1)]
    public async Task BoundedReadRejectsInvalidSizeOrCiphertext(int size)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        Directory.CreateDirectory(files.DirectoryPath);
        using (var file = File.Create(files.PlanPath)) file.SetLength(size);
        await Assert.ThrowsAsync<InvalidDataException>(() => Load(files,
            () => throw new InvalidOperationException("Factory must not run.")));
        Assert.Equal(size, new FileInfo(files.PlanPath).Length);
        files.AssertNoTemporaryFiles();
    }

    /// <summary>factory 失敗或回傳錯 scope 時沒有發布任何計畫；使用相同路徑重試可成功，證明 handle 全部關閉。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFactoryLeavesNoPlanOrTemporaryFiles(bool wrongScope)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        if (wrongScope)
            await Assert.ThrowsAsync<InvalidDataException>(() => Load(files, () => Task.FromResult(
                new Plan(Product, "T02", Code, []))));
        else
            await Assert.ThrowsAsync<IOException>(() => Load(files, () => Task.FromException<Plan>(new IOException("Synthetic factory failure."))));
        Assert.False(File.Exists(files.PlanPath));
        files.AssertNoTemporaryFiles();
        await Load(files, () => Task.FromResult(NewPlan()));
    }

    /// <summary>factory 回傳 null 不具可信計畫，必須拒絕且沒有檔案；下一次合法候選仍能正常發布。</summary>
    [Fact]
    public async Task NullFactoryResultIsRejectedWithoutPublication()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        await Assert.ThrowsAsync<InvalidDataException>(() => Load(files, () => Task.FromResult<Plan>(null!)));
        Assert.False(File.Exists(files.PlanPath));
        files.AssertNoTemporaryFiles();
        await Load(files, () => Task.FromResult(NewPlan()));
    }

    /// <summary>一千個最長 alias／menu 邊界仍可有界往返，所有 alias 身份完整保留，不按相同 menu 刪列。</summary>
    [Fact]
    public async Task MaximumAliasCountRoundTripsWithoutDroppingRows()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        var rows = Enumerable.Range(0, 1000).Select(index => new KeyValuePair<string, string>(
            "c201-" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + new string('a', 91),
            new string('m', 128))).ToArray();
        var first = await Load(files, () => Task.FromResult(new Plan(Product, Channel, Code, rows)));
        var next = await Load(files, () => throw new InvalidOperationException("Factory must not run."));
        Assert.Equal(1000, next.Aliases.Count);
        Assert.Equal(first.Aliases, next.Aliases);
        Assert.InRange(new FileInfo(files.PlanPath).Length, 1L, 512L * 1024);
        files.AssertNoTemporaryFiles();
    }

    /// <summary>factory 忽略取消時仍等它真完成，取消後不落檔；釋放閘門並等 pending 結束後才能清理目錄。</summary>
    [Fact]
    public async Task CancellationDrainsFactoryAndLeavesNoTemporaryFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Plan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Load(files, () => { entered.SetResult(); return release.Task; }, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            release.TrySetResult(NewPlan());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.False(File.Exists(files.PlanPath));
        files.AssertNoTemporaryFiles();
    }

    /// <summary>進入前已取消須零 factory 呼叫及零儲存副作用，不保留取消註冊或背景工作。</summary>
    [Fact]
    public async Task PreCanceledTokenDoesNotInvokeFactory()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Load(files,
            () => throw new InvalidOperationException("Factory must not run."), cancellation.Token));
        Assert.False(Directory.Exists(files.DirectoryPath));
    }

    /// <summary>factory 建立同名目錄阻擋發布，確定已建立的 temp 在 Move 失敗後被移除；移除障礙可重新執行。</summary>
    [Fact]
    public async Task PublicationFailureRemovesItsTemporaryFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        var failure = await Record.ExceptionAsync(() => Load(files, () =>
        {
            Directory.CreateDirectory(files.PlanPath);
            return Task.FromResult(NewPlan());
        }));
        Assert.True(failure is IOException or InvalidDataException);
        files.AssertNoTemporaryFiles();
        Directory.Delete(files.PlanPath);
        await Load(files, () => Task.FromResult(NewPlan()));
    }

    /// <summary>八個 factory 同時獲准產生不同候選，所有成功結果都須等於唯一已發布計畫，落敗者不得使用自己的未保存候選。</summary>
    [Fact]
    public async Task ConcurrentCreatorsReturnOnePersistedWinner()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        var entered = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, 8).Select(index => Load(files, async () =>
        {
            if (Interlocked.Increment(ref entered) == 8) release.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return NewPlan("menu-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        })).ToArray();
        Plan[] results;
        try { results = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { release.TrySetResult(); await Task.WhenAll(pending); }
        var persisted = await Load(files, () => throw new InvalidOperationException("Factory must not run."));
        Assert.All(results, plan => Assert.Equal(persisted.Aliases, plan.Aliases));
        Assert.Single(Directory.GetFiles(files.DirectoryPath, "*.bin"));
        files.AssertNoTemporaryFiles();
    }

    /// <summary>完整合法計畫刪除可重跑且只移除精確 bin，不改動同租戶 manifest 或其他檔案。</summary>
    [Fact]
    public async Task DeleteIsIdempotentAndPreservesManifest()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        TestTenantLinePlanStore.Delete(files.Root, Organization, Product, Channel, Code);
        Assert.False(Directory.Exists(files.DirectoryPath));
        await Load(files, () => Task.FromResult(NewPlan()));
        var manifest = Path.Combine(files.DirectoryPath, Code + ".json");
        File.WriteAllText(manifest, "synthetic-manifest");
        TestTenantLinePlanStore.Delete(files.Root, Organization, Product, Channel, Code);
        TestTenantLinePlanStore.Delete(files.Root, Organization, Product, Channel, Code);
        Assert.False(File.Exists(files.PlanPath));
        Assert.Equal("synthetic-manifest", File.ReadAllText(manifest));
        files.AssertNoTemporaryFiles();
    }

    /// <summary>只允許可信本機根及安全租戶代碼，路徑跳脫在執行 factory 前失敗。</summary>
    [Theory]
    [InlineData("../escape")]
    [InlineData("c201/escape")]
    [InlineData("c201\\escape")]
    [InlineData("c201:stream")]
    [InlineData("C201")]
    public async Task UnsafeCodeIsRejectedBeforeFactory(string code)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        await Assert.ThrowsAsync<InvalidDataException>(() => TestTenantLinePlanStore.LoadOrCreateAsync(
            files.Root, Organization, Product, Channel, code,
            () => throw new InvalidOperationException("Factory must not run."), default));
        Assert.False(Directory.Exists(files.DirectoryPath));
    }

    /// <summary>祖先、根、test-tenants 及精確檔名的 junction（含 dangling）全部拒絕，外部合成目標始終未受影響且不需要提權。</summary>
    [Theory]
    [InlineData("ancestor")]
    [InlineData("root")]
    [InlineData("directory")]
    [InlineData("file")]
    [InlineData("dangling")]
    public async Task ReparsePathsAreRejected(string location)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        var external = Path.Combine(files.BasePath, "external");
        Directory.CreateDirectory(external);
        var root = files.Root;
        string link;
        if (location == "ancestor")
        {
            link = Path.Combine(files.BasePath, "ancestor-link");
            CreateJunction(link, external);
            root = Path.Combine(link, "artifacts");
        }
        else if (location == "root")
        {
            link = root;
            CreateJunction(link, external);
        }
        else if (location == "directory")
        {
            Directory.CreateDirectory(root);
            link = files.DirectoryPath;
            CreateJunction(link, external);
        }
        else
        {
            Directory.CreateDirectory(files.DirectoryPath);
            link = files.PlanPath;
            CreateJunction(link, location == "dangling" ? Path.Combine(external, "missing") : external);
            if (location == "file") File.WriteAllText(Path.Combine(external, "outside.bin"), "external-evidence");
        }
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => TestTenantLinePlanStore.LoadOrCreateAsync(
                root, Organization, Product, Channel, Code,
                () => throw new InvalidOperationException("Factory must not run."), default));
            Assert.Throws<InvalidDataException>(() => TestTenantLinePlanStore.Delete(root, Organization, Product, Channel, Code));
            Assert.Empty(Directory.GetFiles(external, "*.tmp", SearchOption.AllDirectories));
            if (location == "file") Assert.Equal("external-evidence", File.ReadAllText(Path.Combine(external, "outside.bin")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    /// <summary>Windows 以外明確拒絕儲存及刪除，不可降級成明文；Windows 下由其他案例驗證真正 DPAPI。</summary>
    [Fact]
    public async Task NonWindowsIsExplicitlyRejected()
    {
        if (OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => TestTenantLinePlanStore.LoadOrCreateAsync(
            "/tmp/artifacts", Organization, Product, Channel, Code,
            () => Task.FromResult(NewPlan()), default));
        Assert.Throws<PlatformNotSupportedException>(() => TestTenantLinePlanStore.Delete(
            "/tmp/artifacts", Organization, Product, Channel, Code));
    }

    /// <summary>短迴圈反覆保存／讀回／刪除與失敗重試後，目錄可以立刻改名，證明沒有延後關閉的檔案或祖先 handle。</summary>
    [Fact]
    public async Task RepeatedOperationsDrainAllFileHandles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new FileOwner();
        for (var iteration = 0; iteration < 12; iteration++)
        {
            await Load(files, () => Task.FromResult(NewPlan()));
            await Load(files, () => throw new InvalidOperationException("Factory must not run."));
            TestTenantLinePlanStore.Delete(files.Root, Organization, Product, Channel, Code);
            files.AssertNoTemporaryFiles();
        }
        var moved = files.Root + "-moved";
        Directory.Move(files.Root, moved);
        Directory.Move(moved, files.Root);
        Assert.Empty(Directory.GetFiles(files.DirectoryPath));
    }

    /// <summary>所有測試以明列 scope 呼叫產品入口；委派由單一測試持有，沒有 static 回呼或跨測試狀態。</summary>
    private static Task<Plan> Load(FileOwner files, Func<Task<Plan>> create, CancellationToken ct = default) =>
        TestTenantLinePlanStore.LoadOrCreateAsync(files.Root, Organization, Product, Channel, Code, create, ct);

    /// <summary>建立只有一個合成 alias 的不可變計畫，menu 可變動以重現並行不同候選。</summary>
    private static Plan NewPlan(string menu = "menu-original") =>
        new(Product, Channel, Code, [new KeyValuePair<string, string>("c201-main", menu)]);

    /// <summary>逐位元組比較實際落檔後立即清除讀取副本，測試不額外保留密文或範圍 buffer。</summary>
    private static void AssertFileBytes(byte[] expected, string path)
    {
        var actual = File.ReadAllBytes(path);
        try { Assert.Equal(expected, actual); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }

    /// <summary>以不需 symlink 特權的 NTFS mount-point reparse 建立合成 junction；只觸及本案空目錄，原生 handle 與輸入 buffer 都由 using／finally 釋放。</summary>
    [SupportedOSPlatform("windows")]
    private static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(link);
        var substitute = @"\??\" + Path.GetFullPath(target);
        var print = Path.GetFullPath(target);
        var names = Encoding.Unicode.GetBytes(substitute + "\0" + print + "\0");
        var bytes = new byte[16 + names.Length];
        try
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0xa0000003);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), checked((ushort)(8 + names.Length)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), checked((ushort)(substitute.Length * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12, 2), checked((ushort)((substitute.Length + 1) * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14, 2), checked((ushort)(print.Length * 2)));
            names.CopyTo(bytes, 16);
            using var handle = OpenJunction(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid || !SetJunction(handle, 0x000900a4, bytes, checked((uint)bytes.Length),
                    IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException("Synthetic junction creation failed.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(names);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>只開啟本案剛建立的空目錄取得設定 junction 所需權限；SafeFileHandle 確保失敗也確定關閉。</summary>
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle OpenJunction(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    /// <summary>只送出一次 FSCTL_SET_REPARSE_POINT，不開程序、不更改本機權限或全域設定。</summary>
    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetJunction(SafeFileHandle handle, uint operation, [In] byte[] input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    /// <summary>純記憶體速率預算借給合成清理器；不建立 SQL、排程或背景工作，owner Dispose 後無資源保留。</summary>
    private sealed class SyntheticBudget : IChannelRateBudget, IDisposable
    {
        /// <summary>所有預算都以已完成 Task 回覆允許，不涉及真實供應商配額。</summary>
        public Task<bool> TryAcquireAsync(string product, string channelKey, string operation, CancellationToken ct) => Task.FromResult(true);
        /// <summary>只有測試 owner 結束此替身生命週期，沒有待排空的外部依賴。</summary>
        public void Dispose() { GC.SuppressFinalize(this); }
    }

    /// <summary>唯一暫存目錄 owner；只持有測試建立的本機路徑，using 結束同步移除自己所有檔案，失敗會讓測試失敗。</summary>
    private sealed class FileOwner : IDisposable
    {
        /// <summary>當次測試唯一且已存在的目錄，不與 repo 或其他案例共用。</summary>
        internal string BasePath { get; } = Path.Combine(Path.GetTempPath(), "line-plan-tests-" + Guid.NewGuid().ToString("N"));
        /// <summary>模擬 CLI 固定 artifacts 根，建立時不存在，方便斷言前置失敗零 I/O。</summary>
        internal string Root => Path.Combine(BasePath, "artifacts");
        /// <summary>產品只可使用的固定子目錄。</summary>
        internal string DirectoryPath => Path.Combine(Root, "test-tenants");
        /// <summary>精確代碼對應的計畫檔，不能指向 manifest。</summary>
        internal string PlanPath => Path.Combine(DirectoryPath, Code + ".line-cleanup.bin");
        /// <summary>先建立唯一測試容器，不建立 artifacts，產品負責有界逐層建立所需路徑。</summary>
        internal FileOwner() { Directory.CreateDirectory(BasePath); }
        /// <summary>任何成功、取消或故障結果均不得留下本次 .tmp；不掃描父目錄或其他測試。</summary>
        internal void AssertNoTemporaryFiles()
        {
            if (Directory.Exists(DirectoryPath)) Assert.Empty(Directory.GetFiles(DirectoryPath, "*.tmp"));
        }
        /// <summary>測試先自行解除其 junction，再僅遞迴刪除已知唯一容器；若產品遺留 handle，刪除失敗即暴露生命週期違約。</summary>
        public void Dispose() { Directory.Delete(BasePath, recursive: true); GC.SuppressFinalize(this); }
    }
}
