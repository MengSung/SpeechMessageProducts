/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 會友語言變更外殼；contact.new_preferredlanguage 的更新只處理
 * 會友宮格 alias 換綁意圖。註冊限定 PostOperation 非同步、以篩選欄位觸發，
 * Shadow 只記錄安全 alias，Live 支援離線契約但本階段不得以 Live 啟用。
 *
 * 主要功能與流程：先以正式 Contact ID 讀取會友、教會、頻道、語系及加密封包，
 * 再從 accountnumber 取得伺服器教會代碼；MemberRichMenuService 共用收件者與
 * 頻道隔離、語系解析、alias 命名及 Shadow／Live 邊界。未綁定不讀頻道，
 * 教會或頻道錯配不解密，非法代碼以固定業務例外拒絕。
 *
 * 使用邊界與維護：Plugin 外殼只保存無狀態 client／resolver；SDK、來源 adapter、
 * token key 與 shadow 回呼均限於單次 Execute。Shadow 不解密、不連線、不真寫；
 * Live 的解密、HTTP、取消及 response 清理由既有 Core owner 負責。未知例外原樣
 * 交給共用外殼，沒有跨執行快取、timer、queue 或靜態可變資料。
 */
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;
namespace SpeechMessage.Dynamics.Plugins.Localization;
/// <summary>以共用 Core 服務執行會友語言變更的單次 alias 換綁意圖。</summary>
public sealed class MemberLanguageChangedPlugin : PluginShellBase
{
    private readonly MemberRichMenuService menuService;
    /// <summary>平台建構入口；client 使用 net462 無狀態共享 HTTP owner。</summary>
    public MemberLanguageChangedPlugin(string unsecureConfig, string secureConfig)
        : this(unsecureConfig, secureConfig,
            new MemberRichMenuService(new LinePushClient(), new MemberCultureResolver(new LocalizationOptions()))) { }
    /// <summary>離線注入 Core 服務，外殼不保存會友或教會資料。</summary>
    internal MemberLanguageChangedPlugin(string unsecureConfig, string secureConfig, MemberRichMenuService menuService)
        : base(unsecureConfig, secureConfig, new DepthPolicy(1)) =>
        this.menuService = menuService ?? throw new ArgumentNullException(nameof(menuService));
    /// <summary>只接受 contact Update 及必要篩選欄位，完成所有同步 I/O 後返回。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.MessageName != "Update" || input.PrimaryEntityName != "contact") return;
        if (input.Stage != 40 || input.Mode != 1 || input.PrimaryEntityId == Guid.Empty) throw Invalid();
        var target = input.Target;
        if (target is null || target.LogicalName != "contact" || target.Id != input.PrimaryEntityId ||
            !target.Contains("new_preferredlanguage")) return;
        var queries = new LineRecipientQueries(services.Organization);
        var church = queries.ReadMember(input.PrimaryEntityId, CancellationToken.None).ChurchId;
        var account = services.Organization.Retrieve("account", church, new ColumnSet("accountnumber"));
        if (account is null || account.Id != church || account.LogicalName != "account" ||
            account.GetAttributeValue<string>("accountnumber") is not { Length: > 0 } churchCode)
            throw Invalid();
        var context = new TextContext(church, "zh-TW", "zh-TW",
            input.OrganizationId.ToString("D"), "GoodShepherd");
        var key = Configuration.Mode == PluginExecutionMode.Live ? DecodeKey(Configuration.SecureConfig) : null;
        try
        {
            var result = menuService.LinkAsync(queries, context, input.PrimaryEntityId, churchCode,
                "main", "home", key, services.IsShadow ? (contact, alias) => TraceAlias(services, contact, alias) : null,
                CancellationToken.None).GetAwaiter().GetResult();
            if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
                throw Invalid();
        }
        finally { if (key is not null) Array.Clear(key, 0, key.Length); }
    }
    private static void TraceAlias(PluginServices services, Guid contactId, string alias) =>
        services.ShadowPush!.Record(contactId, "RichMenuAlias", contactId, 0,
            HashAlias(alias));

    private static string HashAlias(string alias)
    {
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(alias));
        var text = new StringBuilder(64);
        foreach (var value in bytes) text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }
    private static byte[] DecodeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid();
        try { return Convert.FromBase64String(value); } catch (FormatException) { throw Invalid(); }
    }
    private static InvalidPluginExecutionException Invalid() =>
        new(new SystemTextTable().GetText("zh-TW", new TextKey("Common.Plugin.InvalidConfiguration")));
}
