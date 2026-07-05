// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Flex/Style/BubbleStyles.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class BubbleStyles
// 主要成員：Header、Hero、Body、Footer
// 引用命名空間：System、System.Collections.Generic、System.Text
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    /// <summary>
    /// Use the following two objects to define the style of blocks in a bubble.
    /// </summary>
    public class BubbleStyles
    {
        /// <summary>
        /// Style of the header block
        /// <para>(Optional)</para>
        /// </summary>
        public BlockStyle Header { get; set; }

        /// <summary>
        /// Style of the hero block
        /// <para>(Optional)</para>
        /// </summary>
        public BlockStyle Hero { get; set; }

        /// <summary>
        /// Style of the body block
        /// <para>(Optional)</para>
        /// </summary>
        public BlockStyle Body { get; set; }

        /// <summary>
        /// Style of the footer block
        /// <para>(Optional)</para>
        /// </summary>
        public BlockStyle Footer { get; set; }
    }
}
