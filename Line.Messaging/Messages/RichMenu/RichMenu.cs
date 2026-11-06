/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu 建立用物件。 https://developers.line.me/en/docs/messaging-api/reference/#rich-menu-object 此 model 用於建立 
 * RichMenu 定義；LINE 建立成功後才會額外指派 provider richMenuId。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenu。
 * - Name 的 backing field，讓 setter 能集中套用 LINE 的長度限制。
 * - ChatBarText 的 backing field，讓 setter 能集中套用 LINE 的長度限制。
 * - RichMenu 在聊天室顯示時的寬高尺寸。 LINE 只接受 2500x1686 或 2500x843；此尺寸必須與實際上傳的 PNG 圖片一致。
 * - 是否預設展開 RichMenu。 true 代表 RichMenu 顯示時 chat bar 預設展開；false 則維持收合。
 * 主要操作入口包含 ToResponseRichMenu。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineClientLifecycleTests.cs、Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs、
 * Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/ILineMessagingClient.cs、
 * Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
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
