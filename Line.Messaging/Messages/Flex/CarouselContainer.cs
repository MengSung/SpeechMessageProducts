/*
 * 檔案整體說明
 *
 * 目的與定位：本檔將多個bubble組成可橫向切換的Flex輪播容器。
 *
 * 主要功能與流程：
 * 本檔宣告 CarouselContainer。
 * 資料／選項欄位包含 Type、Contents，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Flex/Extensions/CarouselContainerFlexMessges.cs、
 * Line.Messaging/Messages/FlexMessage.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    public class CarouselContainer : IFlexContainer
    {
        public FlexContainerType Type => FlexContainerType.Carousel;

        /// <summary>
        /// Array of bubble containers. Max: 10 bubbles
        /// <para>(Required)</para>
        /// </summary>
        public IList<BubbleContainer> Contents { get; set; } = new List<BubbleContainer>();

        public CarouselContainer()
        {
        }
    }
}
