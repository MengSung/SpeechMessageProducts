// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Flex/Component/ButtonComponent.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ButtonComponent
// 主要成員：Action、Flex、Margin、Height、Style、Color、Gravity
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
    /// This component draws a button. When the user taps a button, a specified action is performed.
    /// </summary>
    public class ButtonComponent : IFlexComponent
    {
        public FlexComponentType Type => FlexComponentType.Button;

        /// <summary>
        /// Action performed when this button is tapped. Specify an action object.
        /// <para>(Required)</para>
        /// </summary>
        public ITemplateAction Action { get; set; }

        /// <summary>
        /// The ratio of the width or height of this component within the parent box.
        /// The default value for the horizontal parent box is 1, and the default value for the vertical parent box is 0. For more information, see Width and height of components.
        /// <para>(Optional)</para>
        /// </summary>
        public int? Flex { get; set; }

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
        /// Height of the button. You can specify sm or md. The default value is md.
        /// <para>(Optional)</para>
        /// </summary>
        public ButtonHeight? Height { get; set; }

        /// <summary>
        /// Style of the button.<para>
        /// Specify one of the following values:
        /// - link: HTML link style
        /// - primary: Style for dark color buttons
        /// - secondary: Style for light color buttons</para>
        /// The default value is link.
        /// <para>(Optional)</para>
        /// </summary>
        public ButtonStyle? Style { get; set; }

        /// <summary>
        /// Character color when the style property is link.
        /// Background color when the style property is primary or secondary. Use a hexadecimal color code.
        /// <para>(Optional)</para>
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// Vertical alignment style.<para>
        /// Specify one of the following values:
        /// - top: Top-aligned
        /// - bottom: Bottom-aligned
        /// - center: Center-aligned</para>
        /// The default value is top.
        /// If the layout property of the parent box is baseline, the gravity property will be ignored.
        /// <para>(Optional)</para>
        /// </summary>
        public Gravity? Gravity { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="action">
        /// Action performed when this button is tapped. Specify an action object.
        /// </param>
        public ButtonComponent(ITemplateAction action)
        {
            Action = action;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public ButtonComponent()
        {

        }
    }
}
