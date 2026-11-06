/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示事件來源使用者與預期使用者不符的專用例外，避免錯把其他人的事件接入目前處理範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 UserIdMismatchException。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 webhook 的目的使用者識別與預期不符，供入口在處理事件前拒絕錯誤接收範圍。
    /// 此例外分類與 HMAC 簽章失敗不同；訊息不得包含 token、原始事件本文或其他使用者的資料。
    /// </summary>
    public class UserIdMismatchException : Exception
    {
        public UserIdMismatchException()
        {
        }

        public UserIdMismatchException(string message) : base(message)
        {
        }

        public UserIdMismatchException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
