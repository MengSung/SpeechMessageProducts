using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
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
    /// <summary>註冊設定的不可變模式；沙箱可重用外掛，但不得寫入此次 Execute 的狀態。</summary>
    private readonly bool pushMode;
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
    /// <param name="secureConfig">此步驟專屬測試 channel token；不得追蹤或進入共用狀態。</param>
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
            if (config.Properties().Any(property => property.Name is not ("mode" or "culture" or "lineUserId"))) throw InvalidConfiguration();
            var mode = ReadString(config, "mode");
            culture = ReadString(config, "culture");
            if (mode is not ("trace" or "push") || culture is not ("zh-TW" or "qps-ploc")) throw InvalidConfiguration();
            pushMode = string.Equals(mode, "push", StringComparison.Ordinal);
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
            tracing.Trace("FrameworkFileVersion={0}", typeof(object).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
            tracing.Trace("EnvironmentVersion={0}", Environment.Version.ToString());
            var key = new TextKey("Common.Diagnostics.Ping");
            tracing.Trace("zh-TW={0}", Texts.GetText("zh-TW", key));
            tracing.Trace("qps-ploc={0}", Texts.GetText("qps-ploc", key));
            tracing.Trace("Entity={0}; Message={1}", SafeIdentifier(context.PrimaryEntityName), SafeIdentifier(context.MessageName));
            if (!pushMode) return;
            var retryKey = context.PrimaryEntityId;
            var text = Texts.GetText(culture, new TextKey("Common.Diagnostics.PushTest"))
                .Replace("{utc}", utcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            var request = new LinePushRequest(userId!, new[] { text }, retryKey);
            // 沙箱同步 IPlugin 邊界不能捕獲其 SynchronizationContext；工作在線程池內啟動，
            // 同步等待十秒有界的 Core 完成才返回，沒有未等待工作，所有 HTTP／取消資源由 Core using 清理。
            var result = Task.Run(() => client.PushAsync(credential!, request, CancellationToken.None)).GetAwaiter().GetResult();
            var requestId = Guid.TryParse(result.RequestId, out var parsed) ? parsed.ToString("D", CultureInfo.InvariantCulture) : "unavailable";
            tracing.Trace("Outcome={0}; Status={1}; RequestId={2}; ExceptionType={3}", result.Outcome,
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
