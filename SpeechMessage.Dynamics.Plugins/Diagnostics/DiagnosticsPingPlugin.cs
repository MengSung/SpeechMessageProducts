/*
 * 檔案整體說明
 *
 * 目的與定位：trace/push及P2-11 member完整通知鏈的沙箱診斷外殼；只保存不可變註冊設定，執行內容及傳輸資源不得跨執行保留。 Core 擁有每次 HTTP 訊息及十秒逾時；外掛只追蹤安全欄位並在邊界重新拋出真正失敗。
 *
 * 主要功能與流程：
 * 追蹤 Plugin 自身 FileVersion 供更新／回復比對；FrameworkFileVersion 另表示 mscorlib，
 * 兩者均只讀固定組件屬性，不讀設定或憑證，也不保存執行結果。
 * 本檔宣告 DiagnosticsPingPlugin。
 * - 只包含固定資源的唯讀表，不承載教會、使用者、token 或執行內容。
 * - 註冊設定的不可變模式；沙箱可重用外掛，但不得寫入此次 Execute 的狀態。
 * - 只屬此 secure 註冊步驟的不可變憑證；不進 static、追蹤或其他步驟。
 * - 只屬此測試步驟的固定收件者；執行時不由輸入內容覆寫，也不寫追蹤。
 * - HTTP 成功的診斷分類固定映射為 Accepted，供部署 smoke 判定受理；不代表手機送達，也不更名 Core enum。
 * 主要操作入口包含 Execute、ReadString、IsValidUserId、InvalidConfiguration、ReadUtcNow、SafeIdentifier。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.LineRecipients;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;

namespace SpeechMessage.Dynamics.Plugins.Diagnostics;

/// <summary>
/// 階段零的沙箱診斷外殼；只保存不可變註冊設定，執行內容及傳輸資源不得跨執行保留。
/// Core 擁有每次 HTTP 訊息及十秒逾時；外掛只追蹤安全欄位並在邊界重新拋出真正失敗。
/// </summary>
public sealed class DiagnosticsPingPlugin : IPlugin
{
    /// <summary>只包含固定資源的唯讀表，不承載教會、使用者、token 或執行內容。</summary>
    private static readonly SystemTextTable Texts = new();
    /// <summary>純文化政策常數，不含組織或使用者資料，不對外暴露可變陣列。</summary>
    private static readonly string[] DiagnosticCultures = { "zh-TW", "qps-ploc" };
    /// <summary>註冊設定的不可變模式；沙箱可重用外掛，但不得寫入此次 Execute 的狀態。</summary>
    private readonly bool pushMode;
    /// <summary>member只使用部署管理的來源教會，不能由Contact語系改換授權範圍。</summary>
    private readonly bool memberMode;
    /// <summary>已部署來源教會身分，沒有快取其資料或執行結果。</summary>
    private readonly Guid sourceChurchId;
    /// <summary>此註冊步驟的不可變secure key字串；每次Execute複製解碼並清零位元組。</summary>
    private readonly string? memberKey;
    /// <summary>診斷步驟明確opt-in偽語系，不由會友偏好自行開啟。</summary>
    private readonly bool allowPseudoLocale;
    /// <summary>此註冊步驟選定的文字語系，不改程序或執行緒文化。</summary>
    private readonly string culture;
    /// <summary>只屬此 secure 註冊步驟的不可變憑證；不進 static、追蹤或其他步驟。</summary>
    private readonly LineChannelCredential? credential;
    /// <summary>只屬此測試步驟的固定收件者；執行時不由輸入內容覆寫，也不寫追蹤。</summary>
    private readonly string? userId;
    /// <summary>無使用者狀態的 Core 入口；外掛不保存它回傳的結果或原始例外。</summary>
    private readonly ILinePushClient client;
    /// <summary>UTC 時鐘的無狀態委派；測試可固定時間，不持有 provider 或執行內容。</summary>
    private readonly Func<DateTimeOffset> utcNow;

    /// <summary>供沙箱以註冊設定建立診斷外掛，不接受由執行內容提供的憑證。</summary>
    /// <param name="unsecureConfig">只有模式、語系與測試收件者的 JSON。</param>
    /// <param name="secureConfig">push是測試channel token；member是僅tokenKey的JSON。不得追蹤或跨步驟共享。</param>
    public DiagnosticsPingPlugin(string unsecureConfig, string secureConfig)
        : this(unsecureConfig, secureConfig, new LinePushClient(), ReadUtcNow) { }

    /// <summary>供離線測試注入 Core 及時鐘；不建立新的通知或背景工作接點。</summary>
    /// <param name="unsecureConfig">已核定的診斷 JSON。</param>
    /// <param name="secureConfig">僅此外掛實例持有的合成憑證。</param>
    /// <param name="client">單次呼叫 Core 能力，其 HTTP 連線池由沙箱或測試 owner 擁有。</param>
    /// <param name="utcNow">不捕獲執行內容的 UTC 時鐘。</param>
    internal DiagnosticsPingPlugin(string unsecureConfig, string secureConfig, ILinePushClient client, Func<DateTimeOffset> utcNow)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        try
        {
            if (string.IsNullOrWhiteSpace(unsecureConfig) || unsecureConfig.Length > 4096) throw InvalidConfiguration();
            var config = JObject.Parse(unsecureConfig, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (config.Properties().Any(property => property.Name is not ("mode" or "culture" or "lineUserId" or "sourceChurchId" or "allowPseudoLocale"))) throw InvalidConfiguration();
            var mode = ReadString(config, "mode");
            culture = ReadString(config, "culture");
            if (mode is not ("trace" or "push" or "member") || culture is not ("zh-TW" or "qps-ploc")) throw InvalidConfiguration();
            pushMode = string.Equals(mode, "push", StringComparison.Ordinal);
            memberMode = string.Equals(mode, "member", StringComparison.Ordinal);
            if (memberMode)
            {
                if (!Guid.TryParseExact(ReadString(config, "sourceChurchId"), "D", out sourceChurchId) || sourceChurchId == Guid.Empty ||
                    client is not ILineMemberPushClient || config["lineUserId"] is not null) throw InvalidConfiguration();
                if (config["allowPseudoLocale"] is not null && config["allowPseudoLocale"]!.Type != JTokenType.Boolean) throw InvalidConfiguration();
                allowPseudoLocale = config["allowPseudoLocale"]?.Value<bool>() == true;
                if (string.IsNullOrEmpty(secureConfig) || secureConfig.Length > 256) throw InvalidConfiguration();
                var secrets = JObject.Parse(secureConfig, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (secrets.Properties().Count() != 1) throw InvalidConfiguration();
                memberKey = ReadString(secrets, "tokenKey");
                var key = DecodeMemberKey(memberKey);
                Array.Clear(key, 0, key.Length);
            }
            else if (config["sourceChurchId"] is not null || config["allowPseudoLocale"] is not null) throw InvalidConfiguration();
            if (pushMode)
            {
                userId = ReadString(config, "lineUserId");
                if (!IsValidUserId(userId)) throw InvalidConfiguration();
                credential = new LineChannelCredential(secureConfig);
            }
        }
        catch (JsonException) { throw InvalidConfiguration(); }
        catch (ArgumentException) { throw InvalidConfiguration(); }
    }

    /// <summary>深度守門後追蹤雙語文字，必要時推播並依分類處理例外；不得保留 provider。</summary>
    /// <param name="serviceProvider">這次執行唯一的服務來源，不存入欄位或 static。</param>
    public void Execute(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null) throw new ArgumentNullException(nameof(serviceProvider));
        var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        try
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            if (context.Depth > 1) return;
            tracing.Trace("P0-PING OK");
            tracing.Trace("Assembly={0}", typeof(DiagnosticsPingPlugin).Assembly.FullName);
            tracing.Trace("PluginFileVersion={0}", typeof(DiagnosticsPingPlugin).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
            tracing.Trace("FrameworkFileVersion={0}", typeof(object).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
            tracing.Trace("EnvironmentVersion={0}", Environment.Version.ToString());
            var key = new TextKey("Common.Diagnostics.Ping");
            // member不輸出訊息本文；既有trace/push的雙語診斷相容保留。
            if (!memberMode)
            {
                tracing.Trace("zh-TW={0}", Texts.GetText("zh-TW", key));
                tracing.Trace("qps-ploc={0}", Texts.GetText("qps-ploc", key));
            }
            tracing.Trace("Entity={0}; Message={1}", SafeIdentifier(context.PrimaryEntityName), SafeIdentifier(context.MessageName));
            if (memberMode)
            {
                ExecuteMember(serviceProvider, context, tracing);
                return;
            }
            if (!pushMode) return;
            var retryKey = context.PrimaryEntityId;
            var text = Texts.GetText(culture, new TextKey("Common.Diagnostics.PushTest"))
                .Replace("{utc}", utcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            var request = new LinePushRequest(userId!, new[] { text }, retryKey);
            // 沙箱同步 IPlugin 邊界不能捕獲其 SynchronizationContext；工作在線程池內啟動，
            // 同步等待十秒有界的 Core 完成才返回，沒有未等待工作，所有 HTTP／取消資源由 Core using 清理。
            var result = Task.Run(() => client.PushAsync(credential!, request, CancellationToken.None)).GetAwaiter().GetResult();
            var requestId = Guid.TryParse(result.RequestId, out var parsed) ? parsed.ToString("D", CultureInfo.InvariantCulture) : "unavailable";
            // smoke 消費的是外部診斷契約 Accepted，不是 Core 內部 enum 名稱；只轉譯成功分類，
            // AlreadyAccepted／Retryable／Permanent 保留原義，不能把 HTTP 409 或失敗誤報為首次受理。
            var diagnosticOutcome = result.Outcome == LinePushOutcome.Success ? "Accepted" : result.Outcome.ToString();
            tracing.Trace("Outcome={0}; Status={1}; RequestId={2}; ExceptionType={3}", diagnosticOutcome,
                result.StatusCode.HasValue ? ((int)result.StatusCode.Value).ToString(CultureInfo.InvariantCulture) : "no-response",
                requestId, result.TransportException?.GetType().FullName ?? "none");
            if (result.Outcome == LinePushOutcome.Retryable)
            {
                var message = Texts.GetText("zh-TW", new TextKey("Common.Diagnostics.Retryable"))
                    .Replace("{retryKey}", retryKey.ToString("D", CultureInfo.InvariantCulture));
                throw new InvalidPluginExecutionException(message, result.TransportException);
            }
        }
        catch (Exception exception)
        {
            // 只追蹤型別，不把原始訊息／堆疊／內文／收件者寫進 Plugin 追蹤；重新拋出保留實例。
            // 沙箱不寫 Exception.log 或發管理者 LINE；階段五彙總器才負責最終告警。
            tracing.Trace("ExceptionType={0}", exception.GetType().FullName);
            throw;
        }
    }

    /// <summary>
    /// 只以task正式ID取關聯Contact，借用執行UserId權限的SDK；key及所有資料只在此次Execute存活。
    /// 同步SDK由D365擁有且有沙箱執行期限，HTTP由Core十秒期限；所有Task完成才返回，不處置借用SDK。
    /// </summary>
    private void ExecuteMember(IServiceProvider provider, IPluginExecutionContext context, ITracingService tracing)
    {
        if (!string.Equals(context.PrimaryEntityName, "task", StringComparison.Ordinal) || context.PrimaryEntityId == Guid.Empty ||
            context.OrganizationId == Guid.Empty) throw InvalidConfiguration();
        var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
        var sdk = factory.CreateOrganizationService(context.UserId);
        var task = sdk.Retrieve("task", context.PrimaryEntityId, new ColumnSet("regardingobjectid"));
        if (task is null || task.Id != context.PrimaryEntityId || !string.Equals(task.LogicalName, "task", StringComparison.Ordinal) ||
            task.GetAttributeValue<EntityReference>("regardingobjectid") is not { LogicalName: "contact" } contact || contact.Id == Guid.Empty)
            throw InvalidConfiguration();
        var key = DecodeMemberKey(memberKey!);
        try
        {
            using var source = new LineRecipientQueries(sdk);
            var options = new LocalizationOptions(allowPseudoLocale ? DiagnosticCultures : null, allowPseudoLocale);
            var catalog = new TextCatalog(Texts, new EmptyOverrides());
            var notifier = new MemberNotificationService((ILineMemberPushClient)client, new MemberCultureResolver(options), catalog);
            var scope = new TextContext(sourceChurchId, "zh-TW", "zh-TW", context.OrganizationId.ToString("D", CultureInfo.InvariantCulture), "GoodShepherd");
            // SDK與範本準備在此同步邊界完成；Core所有HTTP await均ConfigureAwait(false)，
            // 直接等待有界傳輸排空，不把D365服務/provider送到自建執行緒或保存在背景工作。
            var result = notifier.NotifyDiagnosticAsync(source, scope, contact.Id, context.PrimaryEntityId, key,
                resolved => tracing.Trace("TemplateKey=Diagnostics; Culture={0}", resolved), CancellationToken.None).GetAwaiter().GetResult();
            var requestId = Guid.TryParse(result.RequestId, out var parsed) ? parsed.ToString("D", CultureInfo.InvariantCulture) : "unavailable";
            tracing.Trace("Outcome={0}; Status={1}; RequestId={2}; Reason={3}",
                result.Outcome == LinePushOutcome.Success ? "Accepted" : result.Outcome.ToString(),
                result.StatusCode.HasValue ? ((int)result.StatusCode.Value).ToString(CultureInfo.InvariantCulture) : "no-response", requestId, result.Reason ?? "none");
            if (result.Outcome == LinePushOutcome.Retryable)
                throw new InvalidPluginExecutionException(Texts.GetText("zh-TW", new TextKey("Common.Diagnostics.Retryable"))
                    .Replace("{retryKey}", context.PrimaryEntityId.ToString("D", CultureInfo.InvariantCulture)), result.TransportException);
        }
        finally { Array.Clear(key, 0, key.Length); }
    }

    /// <summary>secure key只接受canonical64byte Base64，錯誤訊息不回顯材料；本次呼叫擁有並必須清零結果。</summary>
    private static byte[] DecodeMemberKey(string value)
    {
        byte[] key;
        try { key = Convert.FromBase64String(value); }
        catch (FormatException) { throw InvalidConfiguration(); }
        if (key.Length == 64 && string.Equals(Convert.ToBase64String(key), value, StringComparison.Ordinal)) return key;
        Array.Clear(key, 0, key.Length);
        throw InvalidConfiguration();
    }

    /// <summary>診斷鏈只使用系統字典；沒有cache/SDK/教會覆寫，不保存任何context或資料。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>每次回同scope空快照，純資料在當次格式化後即可回收，不產生背景工作。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }

    /// <summary>從 JSON 取出明確字串型別，避免隱含轉型或回顯原始設定。</summary>
    /// <param name="config">只在建構期間存活、長度已限制的設定物件。</param>
    /// <param name="name">由程式擁有的固定欄位名。</param>
    /// <returns>不可變設定字串；缺漏或型別錯誤時固定拒絕。</returns>
    private static string ReadString(JObject config, string name) =>
        config[name]?.Type == JTokenType.String ? config[name]!.Value<string>()! : throw InvalidConfiguration();

    /// <summary>固定最多三十三個碼元的單人識別驗證，防止群播或任意輸入進傳輸。</summary>
    /// <param name="value">這個註冊步驟自己的收件者識別，不追蹤或快取。</param>
    /// <returns>只有單人 LINE 識別格式通過。</returns>
    private static bool IsValidUserId(string value) =>
        value.Length == 33 && value[0] == 'U' && value.Skip(1).All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>只發布固定繁中錯誤，不保留含 token／個資的 JSON 解析例外作 InnerException。</summary>
    /// <returns>不含原始設定資料的 SDK 例外。</returns>
    private static InvalidPluginExecutionException InvalidConfiguration() =>
        new(Texts.GetText("zh-TW", new TextKey("Common.Diagnostics.InvalidConfiguration")));

    /// <summary>正式執行使用 UTC；不讀取本機時區、不改動全域時鐘或文化。</summary>
    /// <returns>這次診斷操作使用的 UTC 時點。</returns>
    private static DateTimeOffset ReadUtcNow() => DateTimeOffset.UtcNow;

    /// <summary>事件結構名稱只允許有限 ASCII 識別字元；不把可疑外部字串或長文字寫進追蹤。</summary>
    /// <param name="value">SDK 提供的結構名稱，不是可回顯的自由文字。</param>
    /// <returns>有效結構名或固定缺省標記。</returns>
    private static string SafeIdentifier(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.All(character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
            ? value : "unavailable";
}
