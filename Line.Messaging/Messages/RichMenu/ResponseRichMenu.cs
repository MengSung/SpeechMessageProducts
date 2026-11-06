/*
 * 檔案整體說明
 *
 * 目的與定位：LINE 回傳的 RichMenu response 物件。 https://developers.line.me/en/docs/messaging-api/reference/#rich-menu-response-object
 * 在 RichMenu 的版面資料外，額外保存 LINE 建立或查詢後回傳的 provider id。
 *
 * 主要功能與流程：
 * 本檔宣告 ResponseRichMenu。
 * - LINE provider 端的 RichMenu ID。 link、unlink、alias、default 與 delete 操作都必須使用這個 provider identifier。
 * - 從 provider richMenuId 與本機 RichMenu 定義建立 response 物件。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs、
 * Line.Messaging/Messages/RichMenu/RichMenu.cs 追查目前工作樹中的合作／使用位置。
 */
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
