/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義用來填滿Flex剩餘空間的不可見元件。
 *
 * 主要功能與流程：
 * 本檔宣告 FillerComponent。
 * 資料／選項欄位包含 Type，名稱、型別與序列化規約須與消費端保持一致。
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
    /// This is an invisible component to fill extra space between components.
    /// </summary>
    public class FillerComponent : IFlexComponent
    {

        public FlexComponentType Type => FlexComponentType.Filler;
    }
}
