/*
 * 檔案整體說明
 * 目的與定位：擴充 GoodShepherdWebhookRegistration 的 QR 組合根，以新設定啟用
 * schema2 meeting／personal 發布，同時維持 QrImageGenerate 唯一 consumer。
 * 主要流程：驗證兩個 Enabled 開關；新開關可獨立啟用，或在既有 Lessons worker
 * 啟用且提供 QrCodes:Qr 設定時沿用。兩者皆開只加 scoped schema2 adapter，
 * 由原有 Lesson factory 分派；只有新開關時才建立同一既有 factory 與 QR store。
 * 資源與隔離：singleton 只持有 ScopeFactory；每次認領與圖片工作各自擁有 scope，
 * 同步 SDK 在返回後才 Dispose；沒有第二個輪詢器、reload 訂閱或跨工作狀態。
 * 使用邊界與維護：缺設定一律 fail closed，原 schema1 的設定／行為不變。
 * 離線測試核對兩開關單 worker 及 scoped SDK，不代表啟動正式連線或實機驗收。
 */
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Hosting.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;

/// <summary>好牧人產品組合根的 schema2 scoped 註冊；沿用唯一 QR worker，不修改其他產品 consumer。</summary>
public static partial class GoodShepherdWebhookRegistration
{
    /// <summary>回 true 代表已建立獨立啟用的唯一 QR worker，原 Lesson 註冊應返回。</summary>
    private static bool RegisterMeetingPersonalQr(IServiceCollection services, IConfiguration configuration)
    {
        var lessons = ReadQrFlag(configuration.GetSection("Lessons:Qr:Enabled"));
        var section = configuration.GetSection("QrCodes:Qr");
        var enabled = ReadQrFlag(section.GetSection("Enabled")) ??
            (lessons == true && section.Exists());
        if (enabled != true) return false;
        services.AddMeetingPersonalQrPublication(configuration);
        services.AddScoped<IMeetingPersonalQrPublicationStore, MeetingPersonalQrD365Store>();
        if (lessons == true) return false;
        if (services.Any(d => d.ServiceType == typeof(LessonQrBundleHostFactory)))
            throw new InvalidOperationException("QR worker requires a single owner.");
        services.AddSingleton<LessonQrBundleHostFactory>();
        services.AddSingleton<IHostedService>(provider => new OutboundTaskWorker(
            new LessonQrWorkStore(provider.GetRequiredService<IServiceScopeFactory>()),
            provider.GetRequiredService<LessonQrBundleHostFactory>()));
        return true;
    }

    /// <summary>只允許純量布林或缺省，不能把錯誤設定視為 false 而靜默停用。</summary>
    private static bool? ReadQrFlag(IConfigurationSection section)
    {
        if (section.GetChildren().Any() ||
            (section.Value is not null && !bool.TryParse(section.Value, out _)))
            throw new InvalidOperationException("Invalid QR enabled configuration.");
        return section.Value is null ? null : bool.Parse(section.Value);
    }
}
