/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義Flex按鈕的動作、位置、尺寸、外觀與顏色。
 *
 * 主要功能與流程：
 * 本檔宣告 ButtonComponent。
 * 資料／選項欄位包含 Type、Action、Flex、Margin、Height、Style、Color、Gravity，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 */
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
