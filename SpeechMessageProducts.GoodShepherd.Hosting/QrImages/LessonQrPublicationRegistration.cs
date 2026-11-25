/*
 * 檔案整體說明
 * 目的與定位：本檔由 SpeechMessageProducts.GoodShepherd.Hosting 擁有，提供課程 QR
 * 發布的純組合服務，從受信任 Host 組態建立有界教會前綴快照，
 * 註冊真正檔案發布器與每筆 handler scope；SDK store 由 ASP.NET Core Host 註冊。
 * 主要流程：只讀 Lessons:Qr，啟用後要求專屬 StorageDirectory、PublicBaseUrl 與 Churches。
 * Churches 以正式教會 GUID 為鍵，每筆明確提供 RegularPrefix 與 PollPrefix；
 * 不依教會名稱、組織代碼或呼叫者傳入的網址分支。缺設定在容器發布前拒絕。
 * 使用邊界與維護：快照至多 256 教會，每前綴至多 2048 字元，沒有 reload 訂閱、
 * token、SDK、Session 或背景資源。不可變設定由 Host 持有到停止，publisher 由單筆
 * async DI scope 擁有，stream 與暫存目錄清理由 handler／publisher 各自負責。
 * 本檔不註冊通用 worker 介面，以免覆蓋付款 AllotQuery 的 store 與 factory；
 * 正式 Host 必須以 QR 專用 store 建立獨立 worker。離線測試驗證設定拒絕與作用域，
 * 並不代表正式目錄 ACL、公開網址映射或 D365 連線已驗收。
 */
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpeechMessageProducts.GoodShepherd.Hosting.QrImages;

/// <summary>建立不可變部署設定並註冊單筆 QR 發布服務，不解析任何 SDK。</summary>
public static class LessonQrPublicationRegistration
{
    /// <summary>啟用 Host 在 Build 前呼叫；所有設定先驗完才加入 service descriptors。</summary>
    public static IServiceCollection AddLessonQrPublication(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Lessons:Qr");
        var storage = new LessonQrStorageOptions(section["StorageDirectory"]!, section["PublicBaseUrl"]!);
        var options = new ConfiguredOptions(section.GetSection("Churches"));
        if (services.Any(d => d.ServiceType == typeof(LessonQrBundleHostFactory) ||
            d.ServiceType == typeof(ILessonQrBundlePublicationService)))
            throw new InvalidOperationException("Lesson QR registration requires a single owner.");
        services.AddSingleton(storage);
        services.AddSingleton<ILessonQrBundleOptionsProvider>(options);
        services.AddScoped<ILessonQrBundlePublicationService, LessonQrBundlePublicationService>();
        services.AddSingleton<LessonQrBundleHostFactory>();
        return services;
    }

    /// <summary>Host 啟動快照只保存正式 GUID 與純前綴；工作只能讀取已配置教會。</summary>
    private sealed class ConfiguredOptions : ILessonQrBundleOptionsProvider
    {
        private readonly Dictionary<Guid, LessonQrBundleOptions> _churches = new();

        /// <summary>逐筆驗證並限制教會數量，重複正式 ID 或錯誤設定全部拒絕。</summary>
        internal ConfiguredOptions(IConfigurationSection churches)
        {
            foreach (var section in churches.GetChildren())
            {
                if (_churches.Count == 256 || !Guid.TryParseExact(section.Key, "D", out var id) ||
                    id == Guid.Empty || section["RegularPrefix"] is not { Length: > 0 and <= 2048 } regular ||
                    section["PollPrefix"] is not { Length: > 0 and <= 2048 } poll ||
                    !_churches.TryAdd(id, new LessonQrBundleOptions(regular, poll)))
                    throw new InvalidOperationException("Invalid lesson QR church configuration.");
            }
            if (_churches.Count == 0)
                throw new InvalidOperationException("Lesson QR requires configured churches.");
        }

        /// <summary>以正式 ID 精確取得啟動快照，不提供其他教會或全域後備。</summary>
        public LessonQrBundleOptions GetOptions(Guid churchId) =>
            _churches.TryGetValue(churchId, out var options) ? options :
            throw new InvalidOperationException("Lesson QR church is not configured.");
    }
}
