/*
檔案整體說明
目的與定位：F11四項就緒彙總的離線契約測試，以受控probe故障確認失敗只發布固定名稱，不保留例外原文或連線字串。
主要功能與流程：反射確認新中立契約存在產生可編譯RED；GREEN後交四個真正被依序呼叫的delegate，以一項故障、正常取消及停用回傳驗證原錯誤身分、失敗集合及呼叫次數。結果不代表任何外部系統可用。
使用邊界與維護：每案唯一擁有diagnostics及取消來源；probe僅短命委派，不建立背景task或遺棄未完成工作。彙總只等待合作取消，資源清理由probe確定完成；測試不能把Task完成替代實際SDK／SQL釋放證據。
*/
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Observability;

/// <summary>不依賴產品、SDK或SQL的固定四項安全回應測試。</summary>
public sealed class ReadinessChecksTests
{
    /// <summary>注入每個實際probe失敗位置，成功項不可出現在失敗集合；原錯誤canary永不傳到回應。</summary>
    [Theory]
    [InlineData(0, "d365")]
    [InlineData(1, "sql")]
    [InlineData(2, "queue")]
    [InlineData(3, "channels")]
    public async Task Health_Response_ContainsNoSecrets(int failed, string expected)
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var probes = Enumerable.Range(0, 4).Select(index => (Func<CancellationToken, Task<bool>>)(token =>
            index == failed ? throw new IOException("synthetic-password-connection-string-canary") : Task.FromResult(true))).ToArray();
        var type = typeof(StartupGuards).Assembly.GetType("SpeechMessage.Hosting.Observability.ReadinessChecks");
        Assert.NotNull(type);
        var owner = Activator.CreateInstance(type, probes[0], probes[1], probes[2], probes[3], diagnostics);
        var result = await Assert.IsAssignableFrom<Task<IReadOnlyList<string>>>(type.GetMethod("CheckAsync")!.Invoke(owner, new object[] { CancellationToken.None }));
        Assert.Equal(new[] { expected }, result);
        Assert.DoesNotContain("canary", string.Join(',', result), StringComparison.Ordinal);
    }

    /// <summary>預期的停用／容量拒絕仍回503項目但不造假例外；預先取消不啟動任何probe。</summary>
    [Fact]
    public async Task Health_ExpectedNotReady_AndCallerCancellation_AreSeparate()
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var calls = 0;
        Func<CancellationToken, Task<bool>> no = _ => { calls++; return Task.FromResult(false); };
        var type = typeof(StartupGuards).Assembly.GetType("SpeechMessage.Hosting.Observability.ReadinessChecks");
        Assert.NotNull(type);
        var owner = Activator.CreateInstance(type, no, no, no, no, diagnostics);
        var method = type.GetMethod("CheckAsync")!;
        var result = await Assert.IsAssignableFrom<Task<IReadOnlyList<string>>>(method.Invoke(owner, new object[] { CancellationToken.None }));
        Assert.Equal<string>(["d365", "sql", "queue", "channels"], result);
        calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Assert.IsAssignableFrom<Task<IReadOnlyList<string>>>(method.Invoke(owner, new object[] { new CancellationToken(true) })));
        Assert.Equal(0, calls);
    }

    /// <summary>故障注入不合作probe：期限觸發後仍回true也不能發布成功；等待probe確定退出，四項仍執行且安全回d365。</summary>
    [Fact]
    public async Task Health_LateTrueAfterDeadline_IsNotReady()
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var exited = false;
        var calls = 0;
        async Task<bool> Late(CancellationToken token)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            exited = true;
            return true;
        }
        Task<bool> Success(CancellationToken _) { calls++; return Task.FromResult(true); }
        var owner = new SpeechMessage.Hosting.Observability.ReadinessChecks(Late, Success, Success, Success, diagnostics);
        var failed = await owner.CheckAsync(CancellationToken.None);
        Assert.True(exited);
        Assert.Equal(3, calls);
        Assert.Collection(failed, name => Assert.Equal("d365", name));
    }

    /// <summary>新固定四probe契約未建立時明確失敗，不靠無測試匹配當RED或吞掉編譯錯誤。</summary>
    [Fact]
    public void Readiness_NeutralFourProbeContract_Exists()
    {
        var type = typeof(StartupGuards).Assembly.GetType("SpeechMessage.Hosting.Observability.ReadinessChecks");
        Assert.NotNull(type);
        Assert.NotNull(type.GetMethod("CheckAsync"));
    }
}
