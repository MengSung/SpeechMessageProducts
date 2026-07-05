// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/RichMenu/RichMenu.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenu
// 主要成員：ToResponseRichMenu、Size、Selected、Name、ChatBarText、Areas
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// RichMenu 建立用物件。
    /// https://developers.line.me/en/docs/messaging-api/reference/#rich-menu-object
    /// 此 model 用於建立 RichMenu 定義；LINE 建立成功後才會額外指派 provider richMenuId。
    /// </summary>
    public class RichMenu
    {
        /// <summary>
        /// <see cref="Name"/> 的 backing field，讓 setter 能集中套用 LINE 的長度限制。
        /// </summary>
        private string _name;

        /// <summary>
        /// <see cref="ChatBarText"/> 的 backing field，讓 setter 能集中套用 LINE 的長度限制。
        /// </summary>
        private string _chatBarText;

        /// <summary>
        /// RichMenu 在聊天室顯示時的寬高尺寸。
        /// LINE 只接受 2500x1686 或 2500x843；此尺寸必須與實際上傳的 PNG 圖片一致。
        /// </summary>
        public ImagemapSize Size { get; set; }

        /// <summary>
        /// 是否預設展開 RichMenu。
        /// true 代表 RichMenu 顯示時 chat bar 預設展開；false 則維持收合。
        /// </summary>
        public bool Selected { set; get; }

        /// <summary>
        /// RichMenu 名稱，不會顯示給使用者，主要供管理與佈建比對使用；LINE 最長允許 300 個字元。
        /// provisioning 程式可在此欄位嵌入 fingerprint，用來偵測可重用的 provider menu。
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                _name = value?.Substring(0, Math.Min(value.Length, 300));
            }
        }

        /// <summary>
        /// 顯示在 chat bar 的文字，LINE 最長允許 14 個字元。
        /// RichMenu 收合時，LINE client 會顯示這段文字。
        /// </summary>
        public string ChatBarText
        {
            get => _chatBarText;
            set
            {
                _chatBarText = value?.Substring(0, Math.Min(value.Length, 14));
            }
        }

        /// <summary>
        /// 定義可點擊區域座標與大小的 area 集合，LINE 最多允許 20 個 area。
        /// 座標必須落在 <see cref="Size"/> 內，並與上傳的 PNG 圖稿位置對齊。
        /// </summary>
        public IList<ActionArea> Areas { set; get; }

        /// <summary>
        /// 將本機 RichMenu 定義轉成 <see cref="ResponseRichMenu"/>。
        /// 主要供測試與 adapter 使用，讓本機定義可以模擬 LINE provider-style response。
        /// </summary>
        /// <param name="richMenuId">
        /// LINE provider 端的 RichMenu ID。
        /// </param>
        /// <returns>包含 provider richMenuId 的 response 物件。</returns>
        public ResponseRichMenu ToResponseRichMenu(string richMenuId = "")
        {
            return new ResponseRichMenu(richMenuId, this);
        }
    }
}
