/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義Flex盒狀配置與子元件清單，支援巢狀盒、間距、動作及背景。
 *
 * 主要功能與流程：
 * 本檔宣告 BoxComponent。
 * - 選用的 Box 漸層背景；未指定時保持舊版 Flex JSON，不影響子元件的內容或順序。
 * 資料／選項欄位包含 Background、Type、Layout、Contents、Flex、Spacing、Margin、Action，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * Line.Messaging/Messages/Flex/BubbleContainer.cs、
 * Line.Messaging/Messages/Flex/Extensions/BoxComponentEntensions.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    /// <summary>
    /// This is a component that defines the layout of child components. You can also include a box in a box.
    /// </summary>
    public class BoxComponent : IFlexComponent
    {
        /// <summary>選用的 Box 漸層背景；未指定時保持舊版 Flex JSON，不影響子元件的內容或順序。</summary>
        public Background Background { get; set; }

        /// <summary>
        /// Box
        /// </summary>
        public FlexComponentType Type => FlexComponentType.Box;

        /// <summary>
        /// The placement style of components in this box. Specify one of the following values
        /// </summary>
        /// <param name="layout">
        /// The placement style of components in this box. Specify one of the following values
        /// </param>
        public BoxComponent(BoxLayout layout)
        {
            Layout = layout;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public BoxComponent() { }

        /// <summary>
        /// The placement style of components in this box. Specify one of the following values
        /// <para>(Required)</para>
        /// </summary>
        public BoxLayout Layout { get; set; }

        /// <summary>
        /// Components in this box. Here are the types of components available:<para>
        /// - When the layout property is horizontal or vertical: Box, button, filler, image, separator, and text components</para><para>
        /// - When the layout property is baseline: filler, icon, and text components</para>
        /// <para>(Required)</para>
        /// </summary>
        public IList<IFlexComponent> Contents { get; set; } = new List<IFlexComponent>();

        /// <summary>
        /// The ratio of the width or height of this box within the parent box.
        /// The default value for the horizontal parent box is 1, and the default value for the vertical parent box is 0.
        /// <para>(Optional)</para>
        /// </summary>
        public int? Flex { get; set; }

        /// <summary>
        /// Minimum space between components in this box.
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing. <para>
        /// The default value is none.
        /// To override this setting for a specific component, set the margin property of that component.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Spacing? Spacing { get; set; }

        /// <summary>
        /// Minimum space between this box and the previous component in the parent box.
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing. <para>
        /// The default value is the value of the spacing property of the parent box.
        /// If this box is the first component in the parent box, the margin property will be ignored.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Spacing? Margin { get; set; }

        /// <summary>
        /// Action performed when this box is tapped.
        /// Specify an action object. This property is supported on the following versions of LINE.<para>
        /// / LINE for iOS and Android: 8.11.0 and later
        /// / LINE for Windows and macOS: 5.9.0 and later
        /// </para>
        /// </summary>
        public ITemplateAction Action { get; set; }
    }
}
