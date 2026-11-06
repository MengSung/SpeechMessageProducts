/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義Flex父盒內的分隔線顏色及邊距。
 *
 * 主要功能與流程：
 * 本檔宣告 SeparatorComponent。
 * 資料／選項欄位包含 Type、Margin、Color，名稱、型別與序列化規約須與消費端保持一致。
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
    /// This component draws a separator between components in the parent box.
    /// </summary>
    public class SeparatorComponent : IFlexComponent
    {
        public FlexComponentType Type => FlexComponentType.Separator;

        /// <summary>
        /// Minimum space between this component and the previous component in the parent box.<para>
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing.
        /// The default value is the value of the spacing property of the parent box.
        /// If this component is the first component in the parent box, the margin property will be ignored.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Spacing? Margin { get; set; }

        /// <summary>
        /// Color of the separator. Use a hexadecimal color code.
        /// <para>(Optional)</para>
        /// </summary>
        public string Color { get; set; }
    }
}
