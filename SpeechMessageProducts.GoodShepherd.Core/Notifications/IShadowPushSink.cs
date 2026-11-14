/*
 * 檔案整體說明
 *
 * 目的與定位：定義 MemberNotificationService 在 Shadow 模式的最小記錄邊界。實作只
 * 收到聯絡人正式 ID、範本代號、Retry Key、訊息數與內容雜湊，不能要求解密 token
 * 或取得推播 client。
 *
 * 主要功能與流程：服務完成收件者／教會共同檢查並組合訊息後，同步呼叫 Record；實作
 * 由單次 Plugin Execute 或測試案例擁有，應在該操作完成後排空並釋放自己的記錄。
 * 介面本身不建立 queue、timer、背景工作或共享可變狀態。
 *
 * 使用邊界與維護：記錄資料不含訊息本文、token、cookie 或未驗證個資；跨教會與跨
 * 會友隔離由呼叫端傳入已驗證的正式 ID 維持。若記錄器失敗，例外原樣交給上層處理，
 * 不得為了 Shadow 而偷偷改走 Live 解密或網路推播。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.Notifications;

/// <summary>Shadow 通知的最小同步記錄器，不具備解密或傳輸能力。</summary>
public interface IShadowPushSink
{
    /// <summary>記錄一次已完成組合的通知摘要，不接收 token 或完整訊息本文。</summary>
    /// <param name="contactId">已由來源查出的正式 Contact ID。</param>
    /// <param name="templateKey">呼叫端擁有的固定範本代號。</param>
    /// <param name="retryKey">該業務事件的穩定重試鍵。</param>
    /// <param name="messageCount">本次組合的訊息數量。</param>
    /// <param name="contentSha256">組合內容的 SHA-256 十六進位摘要。</param>
    void Record(Guid contactId, string templateKey, Guid retryKey, int messageCount, string contentSha256);
}
