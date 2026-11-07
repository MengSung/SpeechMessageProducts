/*
檔案整體說明

目的與定位：驗證 Hosting registration／adapter 對既有 ToolUtility diagnostics owner 的
唯一綁定、DI 解析與確定性解除。測試不建立第二套 writer、queue 或 LINE client，也不
保存 request、Session、tenant、credential 或 logger state。

主要功能與流程：
本檔使用 Microsoft DI 建立短生命容器，解析 Hosting registration，確認同一 owner
可被既有 ExceptionReporting 使用；Dispose 容器後確認 registration 解除，下一個
獨立容器可以重新綁定。四種輸出開關的檔案／LINE 行為仍由既有 diagnostics 特性測試
及 Hosting focused tests 共同覆蓋。

使用邊界與維護：
每個案例使用唯一暫存目錄與合成 ExceptionOutputOptions。Dispose 順序由 DI 擁有；
案例不手動釋放容器已接管的 owner，避免 double Dispose。測試只證明離線生命週期，
不宣稱實際 LINE 送達。
*/
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using SpeechMessage.Hosting;
using Xunit;

namespace SpeechMessage.Hosting.Tests;

/// <summary>保護唯一 diagnostics owner 的 DI 綁定與解除，避免跨容器或跨測試洩漏。</summary>
public sealed class HostingRegistrationTests
{
    /// <summary>DI 解析會建立 adapter 並綁定唯一 owner；容器釋放後可由下一容器重新綁定。</summary>
    [Fact]
    public async Task Registration_attaches_one_owner_and_releases_it_with_the_container()
    {
        var firstDirectory = Path.Combine(Path.GetTempPath(), "speechmessage-hosting-owner-a-" + Guid.NewGuid().ToString("N"));
        var secondDirectory = Path.Combine(Path.GetTempPath(), "speechmessage-hosting-owner-b-" + Guid.NewGuid().ToString("N"));

        await using (var first = new ServiceCollection()
            .AddSpeechMessageHosting(firstDirectory, new ExceptionOutputOptions(false, false))
            .BuildServiceProvider())
        {
            var adapter = first.GetRequiredService<ExceptionReportingAdapter>();
            Assert.Same(first.GetRequiredService<ExceptionDiagnostics>(), adapter.Diagnostics);
            Assert.True(ExceptionReporting.IsActive);
        }

        Assert.False(ExceptionReporting.IsActive);

        await using (var second = new ServiceCollection()
            .AddSpeechMessageHosting(secondDirectory, new ExceptionOutputOptions(false, false))
            .BuildServiceProvider())
        {
            _ = second.GetRequiredService<ExceptionReportingAdapter>();
            Assert.True(ExceptionReporting.IsActive);
        }

        Assert.False(ExceptionReporting.IsActive);
        Assert.False(Directory.Exists(firstDirectory));
        Assert.False(Directory.Exists(secondDirectory));
    }

    /// <summary>
    /// 搬移不能只新增另一層 wrapper：三個共用例外型別必須真正由 Hosting 組件定義。
    /// 以執行時組件身分斷言唯一實作位置，不以檔名或 namespace 冒充完成抽取。
    /// </summary>
    [Fact]
    public void Exception_types_are_defined_by_Hosting_not_ToolUtility()
    {
        foreach (var type in new[] { typeof(ExceptionDiagnostics), typeof(ExceptionOutputOptions), typeof(ExceptionReporting) })
            Assert.Equal("SpeechMessage.Hosting", type.Assembly.GetName().Name);
    }

    /// <summary>
    /// 同一部署快照重複註冊須冪等，不能讓 IEnumerable 產生第二個 owner 或全域訂閱。
    /// 此案例只建立描述子，不解析資源，故不需建立／刪除實際診斷目錄。
    /// </summary>
    [Fact]
    public void Registration_repeated_identical_snapshot_is_idempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hosting-registration-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSpeechMessageHosting(directory, new ExceptionOutputOptions(false, false));
        services.AddSpeechMessageHosting(directory, new ExceptionOutputOptions(false, false));
        Assert.Single(services.Where(item => item.ServiceType == typeof(ExceptionDiagnostics)));
        Assert.Single(services.Where(item => item.ServiceType == typeof(ExceptionReportingAdapter)));
    }

    /// <summary>
    /// 第二次註冊若改變目的地即拒絕，不能以最後註冊覆蓋第一個產品的告警策略。
    /// 斷言失敗不修改既有描述子，未解析容器所以不取得任何檔案／事件資源。
    /// </summary>
    [Fact]
    public void Registration_conflicting_snapshot_fails_without_partial_publication()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hosting-registration-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSpeechMessageHosting(directory, new ExceptionOutputOptions(false, false));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddSpeechMessageHosting(directory, new ExceptionOutputOptions(true, false)));
        Assert.Equal(before, services.ToArray());
    }

    /// <summary>
    /// 未經共用入口加入的owner不可被下一次註冊悄悄蓋過；描述子衝突須在建立資源前拒絕。
    /// 外部owner由本案例擁有，容器不建立；finally的非同步釋放清除CTS與channel。
    /// </summary>
    [Fact]
    public async Task Registration_rejects_preexisting_owner_without_partial_publication()
    {
        await using var owner = new ExceptionDiagnostics(Path.GetTempPath(),
            outputOptions: new ExceptionOutputOptions(false, false));
        var services = new ServiceCollection();
        services.AddSingleton(owner);
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddSpeechMessageHosting(Path.GetTempPath(), new ExceptionOutputOptions(false, false)));
        Assert.Equal(before, services.ToArray());
    }

    /// <summary>
    /// 不同部署目錄是不同輸出邊界，不能因開關相同就把第二組誤視為重入；失敗不發布任何描述子。
    /// 本例不解析DI，故無檔案、mutex或consumer需要清理。
    /// </summary>
    [Fact]
    public void Registration_rejects_different_directory_without_partial_publication()
    {
        var root = Path.Combine(Path.GetTempPath(), "hosting-scope-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSpeechMessageHosting(Path.Combine(root, "a"), new ExceptionOutputOptions(false, false));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddSpeechMessageHosting(Path.Combine(root, "b"), new ExceptionOutputOptions(false, false)));
        Assert.Equal(before, services.ToArray());
    }

    /// <summary>
    /// 外部組合根提供的 owner 不可被 DI 接管；容器只解除自己建立的 adapter。
    /// 容器釋放後同一 owner 仍能重新綁定，最後由案例 await using 完成唯一釋放。
    /// </summary>
    [Fact]
    public async Task Registration_borrows_external_owner_and_releases_only_its_adapter()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hosting-borrowed-" + Guid.NewGuid().ToString("N"));
        await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new ExceptionOutputOptions(false, false));
        await using (var provider = new ServiceCollection().AddSpeechMessageHosting(diagnostics).BuildServiceProvider())
        {
            Assert.Same(diagnostics, provider.GetRequiredService<ExceptionReportingAdapter>().Diagnostics);
            Assert.True(ExceptionReporting.IsActive);
        }
        Assert.False(ExceptionReporting.IsActive);
        // 若容器錯誤釋放了外部 owner，此處 StartNotifications 必須拋 ObjectDisposedException。
        diagnostics.StartNotifications((_, _) => Task.CompletedTask);
        using var registration = ExceptionReporting.Attach(diagnostics);
        Assert.True(ExceptionReporting.IsActive);
    }

    /// <summary>
    /// 呼叫端不能把 A 的 adapter 與 B 的 owner 混合註冊，否則 DI 與 static 將分流。
    /// 兩個合成 owner 均停用輸出，由案例先解除 adapter，再依反向順序非同步釋放。
    /// </summary>
    [Fact]
    public async Task Registration_mismatched_external_adapter_is_rejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hosting-mismatch-" + Guid.NewGuid().ToString("N"));
        await using var first = new ExceptionDiagnostics(directory, outputOptions: new ExceptionOutputOptions(false, false));
        await using var second = new ExceptionDiagnostics(directory, outputOptions: new ExceptionOutputOptions(false, false));
        using var adapter = new ExceptionReportingAdapter(first);
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddSpeechMessageHosting(second, adapter));
        Assert.Empty(services);
        Assert.True(ExceptionReporting.IsActive);
    }
}
