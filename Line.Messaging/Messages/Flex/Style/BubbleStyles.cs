/*
 * 檔案整體說明
 *
 * 目的與定位：本檔組合bubble各區塊的樣式設定，讓版面內容與視覺設定分離。
 *
 * 主要功能與流程：
 * 本檔宣告 BubbleStyles。
 * 資料／選項欄位包含 BubbleStyles、Header、Hero、Body、Footer，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Flex/BubbleContainer.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs 追查目前工作樹中的合作／使用位置。
 */
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
