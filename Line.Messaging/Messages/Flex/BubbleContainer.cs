/*
 * 檔案整體說明
 *
 * 目的與定位：本檔組合單一Flex bubble的header、hero、body、footer、樣式與閱讀方向。
 *
 * 主要功能與流程：
 * 本檔宣告 BubbleContainer。
 * 資料／選項欄位包含 Type、Direction、Header、Hero、Body、Footer、Styles，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * Line.Messaging/Messages/Flex/CarouselContainer.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerFlexMessages.cs 追查目前工作樹中的合作／使用位置。
 */
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
