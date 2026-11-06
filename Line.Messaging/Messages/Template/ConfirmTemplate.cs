/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述文字搭配兩個動作按鈕的確認模板。
 *
 * 主要功能與流程：
 * 本檔宣告 ConfirmTemplate。
 * 資料／選項欄位包含 Type、Text、Actions，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Template message with two action buttons.
    /// </summary>
    public class ConfirmTemplate : ITemplate
    {
        public TemplateType Type { get; } = TemplateType.Confirm;

        /// <summary>
        /// Message text
        /// Max: 240 characters
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Action when tapped
        /// Set 2 actions for the 2 buttons
        /// </summary>
        public IList<ITemplateAction> Actions { get; }


        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="text">
        /// Message text
        /// Max: 240 characters
        /// </param>
        /// <param name="actions">
        /// Action when tapped
        /// Set 2 actions for the 2 buttons
        /// </param>
        public ConfirmTemplate(string text, IList<ITemplateAction> actions = null)
        {
            Text = text.Substring(0, Math.Min(text.Length, 240));
            Actions = actions ?? new List<ITemplateAction>();
        }
    }
}
