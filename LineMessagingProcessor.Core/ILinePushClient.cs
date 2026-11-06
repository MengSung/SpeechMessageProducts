/*
 * 檔案整體說明
 *
 * 目的與定位：雙目標最小單人推播能力；不含群播、廣播、業務授權或管理者告警。 呼叫端擁有輸入與結果的操作生命週期，以及最終失敗的回報責任。
 *
 * 主要功能與流程：
 * 本檔宣告 ILinePushClient。
 * - 送出一次固定端點請求；收到回應前的 caller 取消直接拋出，收到回應後保留狀態碼分類結果。 非 caller 取消與其他已核定傳輸失敗回傳 Retryable，保留原始例外交最終失敗邊界處理。
 * 資料／選項欄位包含 ILinePushClient，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Core/LinePushClient.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Threading;
using System.Threading.Tasks;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 雙目標最小單人推播能力；不含群播、廣播、業務授權或管理者告警。
/// 呼叫端擁有輸入與結果的操作生命週期，以及最終失敗的回報責任。
/// </summary>
public interface ILinePushClient
{
    /// <summary>
    /// 送出一次固定端點請求；收到回應前的 caller 取消直接拋出，收到回應後保留狀態碼分類結果。
    /// 非 caller 取消與其他已核定傳輸失敗回傳 Retryable，保留原始例外交最終失敗邊界處理。
    /// </summary>
    /// <param name="credential">受信任伺服器邊界選定的憑證，不保存在共用標頭。</param>
    /// <param name="request">已驗證並完成快照的單人請求。</param>
    /// <param name="ct">只在取得回應前生效的呼叫端取消訊號，不得混同內部十秒或 HttpClient 自身逾時。</param>
    /// <returns>已釋放 HTTP 資源的操作結果。</returns>
    Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct);
}
