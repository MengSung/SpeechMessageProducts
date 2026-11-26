/*
 * 檔案整體說明
 * 目的與定位：F6 LIFF 驗證失敗的共用邊界，供 LiffAuthenticationFilter 區分預期
 * token 拒絕與需要交給共用 diagnostics 的服務故障，避免取消或業務拒絕誤發告警。
 * 主要功能與流程：所有實例使用固定訊息；IsServiceFailure 只承載故障分類，
 * InnerException 保留當次真正傳輸、逾時、解析或 HTTP 狀態證據，不能以字串取代。
 * 使用邊界與維護：不保存 token、姓名、供應商本文、request 或 response；原始例外
 * 仍可能含第三方細節，最終 HTTP 邊界只能交給會移除敏感訊息的共用診斷，不得序列化。
 * 本值不啟動通知、不擁有工作或資源；生命期只到請求完成與最終一次故障分類。
 * 呼叫端明確取消必須保留 OperationCanceledException，不應建立此型別。
 * 驗證方式為離線狀態、逾時與原始例外身分測試，另核對固定訊息不帶合成秘密。
 */
using System;

namespace SpeechMessage.Liff.AspNetCore;

/// <summary>
/// 使用固定安全訊息拒絕本次 LIFF 驗證；最終 HTTP 邊界依服務故障旗標決定是否上報。
/// 原始故障只供受控診斷取型別與程式位置，不能把整個例外回傳或直接 ToString 記錄。
/// </summary>
public sealed class LiffVerificationException : Exception
{
    /// <summary>建立預期 token 拒絕；不含原始本文，也不要求對業務拒絕發送故障通知。</summary>
    public LiffVerificationException() : this(false) { }

    /// <summary>
    /// 建立有明確分類的驗證失敗並保留原始例外物件；本建構子不進行日誌或 LINE 通知。
    /// 呼叫端須在同一請求最終邊界交給共用 diagnostics，不能將內部訊息直接公開。
    /// </summary>
    /// <param name="isServiceFailure">真正傳輸、內部逾時或供應商協定故障為 true；預期 token 拒絕為 false。</param>
    /// <param name="innerException">同次操作的真實故障；預期拒絕可省略，不得傳入 request 或身分資料。</param>
    public LiffVerificationException(bool isServiceFailure, Exception? innerException = null)
        : base("LIFF ID token verification failed.", innerException) => IsServiceFailure = isServiceFailure;

    /// <summary>
    /// 指示最終 HTTP filter 應將真實故障交給共用診斷；false 仍必須拒絕驗證，
    /// 只表示預期 token 拒絕而非服務事故，不能視為成功或允許降級登入。
    /// </summary>
    public bool IsServiceFailure { get; }
}
