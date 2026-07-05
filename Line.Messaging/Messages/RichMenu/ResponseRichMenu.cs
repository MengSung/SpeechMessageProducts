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
