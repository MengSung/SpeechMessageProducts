/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述Flex區塊背景、分隔線及其顏色等樣式，供bubble各區塊一致設定。
 *
 * 主要功能與流程：
 * 本檔宣告 BlockStyle。
 * 資料／選項欄位包含 BlockStyle、BackgroundColor、Separator、SeparatorColor，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs、
 * Line.Messaging/Messages/Flex/Style/Background.cs、Line.Messaging/Messages/Flex/Style/BubbleStyles.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    /// <summary>
    /// Block Style
    /// </summary>
    public class BlockStyle
    {
        /// <summary>
        /// Background color of the block. Use a hexadecimal color code.
        /// <para>(Optional)</para>
        /// </summary>
        public string BackgroundColor { get; set; }

        /// <summary>
        /// true to place a separator above the block.<para>
        /// true will be ignored for the first block in a container because you cannot place a separator above the first block.
        /// The default value is false. </para>
        /// <para>(Optional)</para>
        /// </summary>

        public bool Separator { get; set; }

        /// <summary>
        /// Color of the separator. Use a hexadecimal color code.
        /// <para>(Optional)</para>
        /// </summary>
        public string SeparatorColor { get; set; }
    }
}
