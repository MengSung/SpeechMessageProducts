using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Embedded.DependencyInjection;

/// <summary>
/// 提供保留中的 Embedded 註冊入口。Embedded 目前沒有獲准在產品行程內建立 Dynamics transport，
/// 因此本入口只負責於任何行程、連線、背景工作或秘密解析資源建立前明確拒絕啟動。
/// </summary>
public static class EmbeddedServiceCollectionExtensions
{
    /// <summary>
    /// 驗證呼叫端確實選擇 Embedded 後立即 fail closed，並引導部署改用獨立行程的 Local Gateway。
    /// 本方法不修改 <paramref name="services"/>，不解析 <paramref name="additionalSecrets"/>，也不建立
    /// HTTP client、token cache、worker、timer、subscription 或其他需要清理的資源；因此失敗路徑沒有
    /// 隱藏的生命週期 owner，亦不可能把產品、使用者或憑證狀態保留在 process-level DI container。
    /// </summary>
    /// <param name="services">產品行程擁有的 DI collection；拒絕完成後內容保持不變。</param>
    /// <param name="productOptions">部署所綁定的 Dynamics host mode。</param>
    /// <param name="additionalSecrets">
    /// 相容舊呼叫端而保留的參數。Embedded deferred 期間禁止讀取或複製其中的秘密值。
    /// </param>
    /// <returns>此版本永遠不會成功返回。</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> 或 <paramref name="productOptions"/> 為 <see langword="null"/>。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 呼叫端沒有選擇 Embedded，或 Embedded 尚未通過隔離、容量與生命週期核准。
    /// </exception>
    public static IServiceCollection AddSpeechMessageDynamicsEmbedded(
        this IServiceCollection services,
        ProductDynamicsOptions productOptions,
        IReadOnlyDictionary<string, string>? additionalSecrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(productOptions);

        if (productOptions.ExecutionMode != DynamicsExecutionMode.Embedded)
        {
            throw new InvalidOperationException(
                "AddSpeechMessageDynamicsEmbedded requires ExecutionMode=Embedded.");
        }

        // 此處刻意不檢查或列舉 additionalSecrets。即使呼叫端仍傳入舊設定，拒絕路徑也不能讓秘密值
        // 進入例外、記錄、static cache 或 DI descriptor；Local Gateway 才是目前唯一核准的本機路徑。
        _ = additionalSecrets;
        throw new InvalidOperationException(
            "Embedded Dynamics hosting is deferred and remains fail closed. Use a separately running Local Gateway.");
    }
}
