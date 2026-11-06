/*
 * 檔案整體說明
 *
 * 目的與定位：本檔列出Flex盒內水平、垂直或基線配置方向。
 *
 * 主要功能與流程：
 * 本檔宣告 BoxLayout。
 * 資料／選項欄位包含 BoxLayout，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * Line.Messaging/Messages/Flex/Component/BoxComponent.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    /// <summary>
    /// The placement style of components in this box. Specify one of the following values
    /// </summary>
    public enum BoxLayout
    {
        /// <summary>
        /// Components are placed horizontally. The direction property of the bubble container specifies the order.
        /// </summary>
        Horizontal,
        /// <summary>
        /// Components are placed vertically from top to bottom.
        /// </summary>
        Vertical,
        /// <summary>
        /// Components are placed in the same way as horizontal is specified except the baselines of the components are aligned.
        /// </summary>
        Baseline,
    }
}
