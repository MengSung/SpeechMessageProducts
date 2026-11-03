using System.Threading;
using System.Threading.Tasks;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 雙目標最小單人推播能力；不含群播、廣播、業務授權或管理者告警。
/// 呼叫端擁有輸入與結果的操作生命週期，以及最終失敗的回報責任。
/// </summary>
public interface ILinePushClient
{
    /// <summary>送出一次固定端點請求；正常 caller 取消直接拋出，傳輸失敗保留原始例外。</summary>
    /// <param name="credential">受信任伺服器邊界選定的憑證，不保存在共用標頭。</param>
    /// <param name="request">已驗證並完成快照的單人請求。</param>
    /// <param name="ct">呼叫端取消訊號，不得混同內部十秒逾時。</param>
    /// <returns>已釋放 HTTP 資源的操作結果。</returns>
    Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct);
}
