/*
 * 檔案整體說明
 * 目的與定位：本檔由 SpeechMessageProducts.GoodShepherd.Hosting 擁有，負責 P5-09
 * 圖片發布的組態快照與 scoped 服務註冊，與 Lesson 設定隔離，
 * 不註冊 worker 或覆蓋同類型儲存選項。由 ASP.NET Core 組合根決定唯一 consumer。
 * 主要流程：只讀 QrCodes:Qr 的 StorageDirectory、PublicBaseUrl 與 Churches，
 * GUID 鍵逐筆驗證 MeetingPrefix／PersonalPrefix，全部成功才加入 DI descriptors。
 * 使用邊界與維護：最多二百五十六教會，前綴各最多二千零四十八字；不訂閱 reload，
 * 不持有 SDK、Session 或 token。快照由 Host 存活期擁有，publisher 每筆 scope
 * 擁有；stream 與檔案 lease 由 publisher 唯一清理，未設定教會不得 fallback。
 * 驗證方式：離線真組合根驗證雙開關單 worker、作用域與缺設定拒絕，不連正式資料庫。
 */
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpeechMessageProducts.GoodShepherd.Hosting.QrImages;

/// <summary>只建立受信任純值快照與 scoped 發布器，SDK adapter 由 Host 負責。</summary>
public static class MeetingPersonalQrRegistration
{
    /// <summary>先完整驗證設定再註冊，不建立隱藏容器或第二個同 type consumer。</summary>
    public static IServiceCollection AddMeetingPersonalQrPublication(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("QrCodes:Qr");
        var storage = new MeetingPersonalQrStorageOptions(
            new LessonQrStorageOptions(section["StorageDirectory"]!, section["PublicBaseUrl"]!));
        var options = new ConfiguredOptions(section.GetSection("Churches"));
        if (services.Any(d => d.ServiceType == typeof(MeetingPersonalQrPublicationService) ||
            d.ServiceType == typeof(IMeetingPersonalQrOptionsProvider) ||
            d.ServiceType == typeof(MeetingPersonalQrStorageOptions)))
            throw new InvalidOperationException("Schema2 QR registration requires a single owner.");
        services.AddSingleton(storage);
        services.AddSingleton<IMeetingPersonalQrOptionsProvider>(options);
        services.AddScoped<MeetingPersonalQrPublicationService>();
        return services;
    }

    /// <summary>僅保存有界正式教會 ID 與不可變前綴；不保存原始組態樹或其他工作資料。</summary>
    private sealed class ConfiguredOptions : IMeetingPersonalQrOptionsProvider
    {
        private readonly Dictionary<Guid, MeetingPersonalQrOptions> _churches = new();
        private readonly HashSet<Guid> _meetingIds = new();
        /// <summary>拒絕缺欄、重複 GUID、空教會清單與超容量，不作名稱猜測。</summary>
        internal ConfiguredOptions(IConfigurationSection churches)
        {
            foreach (var church in churches.GetChildren())
            {
                if (_churches.Count == 256 || !Guid.TryParseExact(church.Key, "D", out var id) ||
                    id == Guid.Empty || church["MeetingPrefix"] is not { Length: > 0 and <= 2048 } meeting ||
                    church["PersonalPrefix"] is not { Length: > 0 and <= 2048 } personal ||
                    !ReadMeetingIds(church, out var meetingIds) ||
                    meetingIds.Any(meetingId => !_meetingIds.Add(meetingId)) ||
                    !_churches.TryAdd(id, new MeetingPersonalQrOptions(meeting, personal, meetingIds)))
                    throw new InvalidOperationException("Invalid schema2 QR church configuration.");
            }
            if (_churches.Count == 0) throw new InvalidOperationException("Schema2 QR churches are required.");
        }
        /// <summary>精確匹配正式 ID，沒有全域或其他教會後備設定。</summary>
        public MeetingPersonalQrOptions GetOptions(Guid churchId) =>
            _churches.TryGetValue(churchId, out var options) ? options :
            throw new InvalidOperationException("Schema2 QR church is not configured.");

        /// <summary>讀取最多一萬個正式 meeting GUID；重複、空值及額外格式一律拒絕。</summary>
        private static bool ReadMeetingIds(IConfigurationSection church, out IReadOnlyCollection<Guid> ids)
        {
            var children = church.GetSection("MeetingIds").GetChildren().ToArray();
            var result = new HashSet<Guid>();
            foreach (var child in children)
            {
                if (result.Count == 10000 || !Guid.TryParseExact(child.Value, "D", out var id) ||
                    id == Guid.Empty || !result.Add(id))
                {
                    ids = Array.Empty<Guid>();
                    return false;
                }
            }
            ids = result.ToArray();
            return true;
        }
    }
}
