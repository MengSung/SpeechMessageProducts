// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Flex/BubbleContainer.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class BubbleContainer
// 主要成員：Direction、Header、Hero、Body、Footer、Styles
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
    public class BubbleContainer : IFlexContainer
    {
        public FlexContainerType Type => FlexContainerType.Bubble;

        /// <summary>
        /// Text directionality and the order of components in horizontal boxes in the container. <para>
        /// Specify one of the following values:
        /// / ltr: Left to right
        /// / rtl: Right to left
        /// , The default value is ltr.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public ComponentDirection Direction { get; set; }

        /// <summary>
        /// Header block. Specify a box component.
        /// <para>(Optional)</para>
        /// </summary>
        public BoxComponent Header { get; set; }

        /// <summary>
        /// Hero block. Specify an image component.
        /// <para>(Optional)</para>
        /// </summary>
        public ImageComponent Hero { get; set; }

        /// <summary>
        /// Body block. Specify a box component.
        /// <para>(Optional)</para>
        /// </summary>
        public BoxComponent Body { get; set; }

        /// <summary>
        /// Footer block. Specify a box component.
        /// <para>(Optional)</para>
        /// </summary>
        public BoxComponent Footer { get; set; }

        /// <summary>
        /// Style of each block. Specify a bubble style object. For more information, see Objects for the block style.
        /// <para>(Optional)</para>
        /// </summary>
        public BubbleStyles Styles { get; set; }
    }
}
