/*
 * 檔案整體說明
 *
 * 目的與定位：本檔提供鏈式組裝Flex box子元件的擴充方法。
 *
 * 主要功能與流程：
 * 本檔宣告 BoxComponentExtensions。
 * 主要操作入口包含 AddContents。
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
    public static class BoxComponentExtensions
    {
        /// <summary>
        /// Add a flex component to the Box component.
        /// </summary>
        /// <param name="self">BoxComponent</param>
        /// <param name="component">Flex Component</param>
        /// <returns>BoxComponent</returns>
        public static BoxComponent AddContents(this BoxComponent self, IFlexComponent component)
        {
            self.Contents.Add(component);
            return self;
        }


    }
}
