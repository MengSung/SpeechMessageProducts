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
    public void Registration_attaches_one_owner_and_releases_it_with_the_container()
    {
        var firstDirectory = Path.Combine(Path.GetTempPath(), "speechmessage-hosting-owner-a-" + Guid.NewGuid().ToString("N"));
        var secondDirectory = Path.Combine(Path.GetTempPath(), "speechmessage-hosting-owner-b-" + Guid.NewGuid().ToString("N"));

        using (var first = new ServiceCollection()
            .AddSpeechMessageHosting(firstDirectory, new ExceptionOutputOptions(false, false))
            .BuildServiceProvider())
        {
            var adapter = first.GetRequiredService<ExceptionReportingAdapter>();
            Assert.Same(first.GetRequiredService<ExceptionDiagnostics>(), adapter.Diagnostics);
            Assert.True(ExceptionReporting.IsActive);
        }

        Assert.False(ExceptionReporting.IsActive);

        using (var second = new ServiceCollection()
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
}
