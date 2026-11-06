/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示webhook簽章不符的專用例外，讓入口可以在處理事件前辨識驗證失敗。
 *
 * 主要功能與流程：
 * 本檔宣告 InvalidSignatureException。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging.Tests/WebhookRawSignatureTests.cs、Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging
{
    /// <summary>
    /// Capture Invalid Signature Exception.
    /// </summary>
    public class InvalidSignatureException : Exception
    {
        public InvalidSignatureException()
        {
        }

        public InvalidSignatureException(string message) : base(message)
        {
        }

        public InvalidSignatureException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
