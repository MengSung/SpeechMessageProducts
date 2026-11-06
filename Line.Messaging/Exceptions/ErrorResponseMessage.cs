/*
 * 檔案整體說明
 *
 * 目的與定位：本檔LINE平台錯誤回應的訊息與欄位明細模型，供協定解析使用；遠端本文不可信，不能直接回傳使用者或整份寫入日誌。
 *
 * 主要功能與流程：
 * 本檔宣告 ErrorResponseMessage、ErrorDetails。
 * 主要操作入口包含 ToString。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging/Exceptions/LineResponseException.cs、Line.Messaging/HttpResponseMessageExtensions.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Error returned from LINE Platform
    /// https://developers.line.me/en/docs/messaging-api/reference/#error-responses
    /// </summary>
    public class ErrorResponseMessage
    {
        /// <summary>
        /// Summary of the error
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Details of the error
        /// </summary>
        public IList<ErrorDetails> Details { get; set; }

        public ErrorResponseMessage()
        {}

        public override string ToString()
        {
            return (Details == null) ? Message : $"{Message},[{string.Join(",", Details)}]";
        }

        /// <summary>
        /// Details of the error
        /// </summary>
        public class ErrorDetails
        {
            /// <summary>
            /// Details of the error
            /// </summary>
            public string Message { get; set; }

            /// <summary>
            /// Location of where the error occurred
            /// </summary>
            public string Property { get; set; }

            public ErrorDetails()
            {
            }

            public override string ToString()
            {
                return $"{{{Message}, {Property}}}";
            }
        }
    }
}
