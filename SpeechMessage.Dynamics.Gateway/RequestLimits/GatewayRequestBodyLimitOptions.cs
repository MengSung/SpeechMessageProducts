using Microsoft.Extensions.Configuration;

namespace SpeechMessage.Dynamics.Gateway.RequestLimits;

/// <summary>
/// 定義 Gateway operation request body 的 deployment-owned 硬限制。
/// 同一份設定同時驅動 Kestrel、IIS 與 application reader，避免 hosting topology 之間出現不同 wire-byte 上限；
/// 設定只包含 bounded 整數，不持有 request、principal、body、buffer、token、credential、stream、timer 或背景工作，
/// 可由 Options singleton 在所有並行 request 間唯讀共享，Host disposal 時沒有額外 cleanup owner。
/// 驗證在 listener 接受流量前執行；任何零值、負值或超過 hard ceiling 的設定都 fail closed，不能靜默 clamp。
/// </summary>
public sealed class GatewayRequestBodyLimitOptions
{
    /// <summary>取得 configuration section 名稱；產品 request 或 HTTP header 不得改寫此 deployment authority。</summary>
    public const string SectionName = "DynamicsGateway:RequestLimits";

    /// <summary>
    /// 取得允許部署設定的絕對 request body 上限（一 MiB）。這是 allocation 與攻擊面 hard ceiling，
    /// 即使 Kestrel/IIS 支援更大值也不能由部署繞過；變更此常數需要獨立記憶體與壓力審查。
    /// </summary>
    public const int AbsoluteMaximumRequestBodyBytes = 1_048_576;

    /// <summary>取得允許的絕對 JSON depth 上限；限制 parser stack/metadata allocation，防止深層巢狀輸入造成過度 CPU/記憶體消耗。</summary>
    public const int AbsoluteMaximumJsonDepth = 64;

    /// <summary>
    /// 取得或設定 operation body 的最大 wire bytes；reader 只會租用此值加一個偵測 byte，預設 64 KiB。
    /// </summary>
    public int MaxRequestBodyBytes { get; set; } = 65_536;

    /// <summary>取得或設定 JSON parser 最大深度；exact depth 由 System.Text.Json 驗證，預設 32。</summary>
    public int JsonMaxDepth { get; set; } = 32;

    /// <summary>
    /// 從指定 configuration 建立新 snapshot 並立即執行相同 hard validation。
    /// 方法不保留 <see cref="IConfiguration"/> reference、不訂閱 reload，也不修改 provider；回傳 instance 由 caller/DI 唯一擁有。
    /// Kestrel 啟動 callback 使用此方法，確保 server transport limit 與 application options 採同一規則。
    /// </summary>
    /// <param name="configuration">Host 已合併的 deployment configuration。</param>
    /// <returns>已驗證、只含 bounded 整數的 options snapshot。</returns>
    public static GatewayRequestBodyLimitOptions BindAndValidate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new GatewayRequestBodyLimitOptions();
        configuration.GetSection(SectionName).Bind(options);
        options.Validate();
        return options;
    }

    /// <summary>
    /// 驗證 byte limit 與 JSON depth 都是正數且未超過絕對上限；失敗直接拋出啟動錯誤，不配置 buffer、不建立 listener 或背景工作。
    /// 此方法為純 CPU 常數時間檢查，可由 Options validation、Kestrel 與測試安全重複呼叫。
    /// </summary>
    /// <exception cref="InvalidOperationException">任一值超出 deployment contract 時拋出。</exception>
    public void Validate()
    {
        if (MaxRequestBodyBytes is <= 0 or > AbsoluteMaximumRequestBodyBytes)
        {
            throw new InvalidOperationException(
                $"Gateway request body maximum must be between 1 and {AbsoluteMaximumRequestBodyBytes} bytes.");
        }

        if (JsonMaxDepth is <= 0 or > AbsoluteMaximumJsonDepth)
        {
            throw new InvalidOperationException(
                $"Gateway request JSON maximum depth must be between 1 and {AbsoluteMaximumJsonDepth}.");
        }
    }
}
