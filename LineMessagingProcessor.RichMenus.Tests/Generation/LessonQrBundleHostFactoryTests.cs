/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-07 課程 QR 待辦 Host factory 的 payload 重驗證、每筆
 * DI scope 所有權及取消清理契約。
 *
 * 主要功能與流程：以合成 OutboundWorkItem、設定提供者、發布服務與 scoped
 * lifecycle probe 建立真實 LessonQrBundleHostFactory；測試只確認 handler 能
 * 接收合法工作、錯誤類型／payload 在建立 scope 前拒絕，以及 lease Dispose
 * 讓 scoped owner 回到基線。測試不發布圖片、不連 D365、LINE 或檔案系統。
 *
 * 使用邊界與維護：測試替身不保存 credential、Session 或跨案例可變資料；每案
 * 自行擁有 ServiceProvider，finally／await using 釋放唯一 scope。這些測試保護
 * Host 組合契約，不宣稱正式教會設定或實機 URL 回寫已完成。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.RichMenus.QrImages;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Outbound;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>保護課程 QR Host factory 的 scope 與封閉 payload 契約。</summary>
public sealed class LessonQrBundleHostFactoryTests
{
    private static readonly Guid ChurchId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid LessonId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>合法工作建立真正 handler lease，釋放後 scoped owner 回到零。</summary>
    [Fact]
    public async Task OpenAsync_ValidPayload_DisposesScopedOwner()
    {
        var counts = new Counts();
        await using var services = new ServiceCollection()
            .AddSingleton(counts)
            .AddScoped<ILessonQrBundleOptionsProvider, OptionsProvider>()
            .AddScoped<ILessonQrBundlePublicationService, PublicationService>()
            .AddScoped<ScopedProbe>()
            .BuildServiceProvider();
        var factory = new LessonQrBundleHostFactory(services.GetRequiredService<IServiceScopeFactory>());
        var item = CreateItem();

        await using (var lease = await factory.OpenAsync(item, CancellationToken.None))
        {
            Assert.NotNull(lease.Handler);
        }

        Assert.Equal(1, counts.Created);
        Assert.Equal(1, counts.Disposed);
        Assert.Equal(0, counts.Active);
    }

    /// <summary>不支援的類型及錯誤 payload 不得建立 scoped owner。</summary>
    [Fact]
    public async Task OpenAsync_InvalidInput_FailsClosedBeforeScope()
    {
        var counts = new Counts();
        await using var services = new ServiceCollection()
            .AddSingleton(counts)
            .AddScoped<ILessonQrBundleOptionsProvider, OptionsProvider>()
            .AddScoped<ILessonQrBundlePublicationService, PublicationService>()
            .AddScoped<ScopedProbe>()
            .BuildServiceProvider();
        var factory = new LessonQrBundleHostFactory(services.GetRequiredService<IServiceScopeFactory>());

        await Assert.ThrowsAsync<ArgumentException>(() => factory.OpenAsync(
            CreateItem("{\"schema\":1}"), CancellationToken.None).AsTask());
        Assert.Equal(0, counts.Created);

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenAsync(
            CreateItem(type: OutboundWorkType.RichMenuGenerate), CancellationToken.None).AsTask());
        Assert.Equal(0, counts.Created);
    }

    private static OutboundWorkItem CreateItem(string? payload = null,
        OutboundWorkType type = OutboundWorkType.QrImageGenerate)
    {
        payload ??= "{\"contentVersion\":1,\"imageCount\":31,\"includePoll\":0," +
            "\"lessonId\":\"11111111-2222-3333-4444-555555555555\",\"schema\":1}";
        return new OutboundWorkItem(Guid.NewGuid(), "1", ChurchId, null, type, payload,
            "synthetic-key", OutboundWorkStatus.Processing, 0, null,
            new DateTime(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc));
    }

    private sealed class OptionsProvider : ILessonQrBundleOptionsProvider
    {
        public LessonQrBundleOptions GetOptions(Guid churchId) =>
            churchId == ChurchId
                ? new("https://front.example/qr?QrCodeId=", "https://poll.example/qr?QrCodeId=")
                : throw new InvalidOperationException("Unexpected church.");
    }

    private sealed class PublicationService : ILessonQrBundlePublicationService
    {
        public PublicationService(ScopedProbe probe) { _ = probe; }

        public Task PublishAndCommitAsync(Guid churchId, Guid lessonId, int contentVersion,
            IReadOnlyList<LessonQrRenderedImage> images, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The factory test must not publish.");
    }

    private sealed class ScopedProbe : IDisposable
    {
        public ScopedProbe(Counts counts) { counts.Opened(); _counts = counts; }
        private readonly Counts _counts;
        public void Dispose() => _counts.Closed();
    }

    private sealed class Counts
    {
        public int Created;
        public int Disposed;
        public int Active;
        public void Opened() { Created++; Active++; }
        public void Closed() { Disposed++; Active--; }
    }
}
