// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ResponseRichMenu
// 主要成員：CreateFrom、RichMenuId
// 引用命名空間：System.Collections.Generic、System.Linq
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Collections.Generic;
using System.Linq;

namespace Line.Messaging
{
    /// <summary>
    /// LINE 回傳的 RichMenu response 物件。
    /// https://developers.line.me/en/docs/messaging-api/reference/#rich-menu-response-object
    /// 在 <see cref="RichMenu"/> 的版面資料外，額外保存 LINE 建立或查詢後回傳的 provider id。
    /// </summary>
    public class ResponseRichMenu : RichMenu
    {
        /// <summary>
        /// LINE provider 端的 RichMenu ID。
        /// link、unlink、alias、default 與 delete 操作都必須使用這個 provider identifier。
        /// </summary>
        public string RichMenuId { get; set; }

        /// <summary>
        /// 從 provider richMenuId 與本機 RichMenu 定義建立 response 物件。
        /// </summary>
        /// <param name="richMenuId">
        /// LINE provider 端的 RichMenu ID。
        /// </param>
        /// <param name="source">
        /// 本機 RichMenu 版面物件。
        /// </param>
        public ResponseRichMenu(string richMenuId, RichMenu source)
        {
            RichMenuId = richMenuId;
            Size = source.Size;
            Selected = source.Selected;
            Name = source.Name;
            ChatBarText = source.ChatBarText;
            Areas = source.Areas;
        }

        internal static ResponseRichMenu CreateFrom(dynamic dynamicObject)
        {

            // LINE 會以巢狀 JSON 回傳 action areas。
            // 將解析集中在這裡，避免呼叫端重複 dynamic access，或不小心與 provider 欄位名稱脫節。
            var areas = new List<ActionArea>();
            foreach (var area in dynamicObject?.areas ?? Enumerable.Empty<dynamic>())
            {
                areas.Add(ActionArea.CreateFrom(area));
            }

            var menu = new RichMenu()
            {
                Name = (string)dynamicObject?.name,
                Size = new ImagemapSize((int)(dynamicObject?.size?.width ?? 0), (int)(dynamicObject?.size?.height ?? 0)),
                Selected = (bool)(dynamicObject?.selected ?? false),
                ChatBarText = (string)dynamicObject?.chatBarText,
                Areas = areas
            };
            return new ResponseRichMenu((string)dynamicObject?.richMenuId, menu);
        }
    }
}
