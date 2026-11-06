/*
 * 檔案整體說明
 *
 * 目的與定位：本檔以HTTP狀態與受控錯誤資訊表示LINE API失敗，讓上游決定重試與最終告警。
 *
 * 主要功能與流程：
 * 本檔宣告 LineResponseException。
 * 主要操作入口包含 ToString。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging.Tests/LiffAndStreamLifecycleTests.cs、Line.Messaging.Tests/LineClientLifecycleTests.cs、
 * Line.Messaging/HttpResponseMessageExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Net;

namespace Line.Messaging
{
    /// <summary>
    /// Capture Error from LINE platform
    /// </summary>
    public class LineResponseException : Exception
    {
        /// <summary>
        /// HTTP Status Code
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Error returned from LINE Platform
        /// </summary>
        public ErrorResponseMessage ResponseMessage { get; set; }

        public LineResponseException()
        {}

        public LineResponseException(string message) : base(message)
        {}

        public LineResponseException(string message, Exception innerException) : base(message, innerException)
        {}

        public override string ToString()
        {
            return $"StatusCode={StatusCode}, {ResponseMessage}" + Environment.NewLine
                + base.ToString();
        }
    }
}
