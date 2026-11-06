/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述LIFF視窗的網址及尺寸，供LIFF管理請求組裝。
 *
 * 主要功能與流程：
 * 本檔宣告 View。
 * 資料／選項欄位包含 View、Type、Url，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Liff/LiffApp.cs、Line.Messaging/Liff/LiffClient.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging.Liff
{
    /// <summary>
    /// View object which contains the URL and view size of the LIFF app.
    /// </summary>
    public class View
    {
        /// <summary>
        /// Size of the LIFF app view. Specify one of the following values
        /// </summary>
        public ViewType Type { get; }

        /// <summary>
        /// URL of the LIFF app. Must start with HTTPS.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">
        /// Size of the LIFF app view. Specify one of the following values
        /// </param>
        /// <param name="url">
        /// URL of the LIFF app. Must start with HTTPS.
        /// </param>
        public View(ViewType type, string url)
        {
            Type = type;
            Url = url;
        }
    }
}
