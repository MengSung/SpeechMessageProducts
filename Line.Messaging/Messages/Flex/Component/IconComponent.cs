// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Flex/Component/IconComponent.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class IconComponent
// 主要成員：Url、Margin、Size、AspectRatio
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
    public class IconComponent : IFlexComponent
    {
        public FlexComponentType Type => FlexComponentType.Icon;

        /// <summary>
        /// Image URL<para>
        /// Protocol: HTTPS
        /// / Image format: JPEG or PNG
        /// / Maximum image size: 240×240 pixels
        /// / Maximum data size: 1 MB
        /// </para>
        /// <para>(Required)</para>
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Minimum space between this component and the previous component in the parent box.<para>
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing.</para><para>
        /// The default value is the value of the spacing property of the parent box.
        /// If this component is the first component in the parent box, the margin property will be ignored.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Spacing? Margin { get; set; }

        /// <summary>
        /// Maximum size of the icon width. <para>
        /// You can specify one of the following values: xxs, xs, sm, md, lg, xl, xxl, 3xl, 4xl, or 5xl.
        /// The size increases in the order of listing. The default value is md.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public ComponentSize? Size { get; set; }

        /// <summary>
        /// Aspect ratio of the image.
        /// Specify in the {width}:{height} format. <para>
        /// Specify the value of the {width} property and the {height} property in the range from 1 to 100000. However,
        /// you cannot set the {height} property to a value that is more than three times the value of the {width} property. </para>
        /// The default value is 1:1.
        /// <para>(Optional)</para>
        /// </summary>
        public AspectRatio AspectRatio { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="url">
        /// Image URL<para>
        /// Protocol: HTTPS
        /// / Image format: JPEG or PNG
        /// / Maximum image size: 240×240 pixels
        /// / Maximum data size: 1 MB
        /// </para>
        /// </param>
        public IconComponent(string url)
        {
            Url = url;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public IconComponent()
        {

        }
    }
}
