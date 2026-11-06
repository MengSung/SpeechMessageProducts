/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述快速回覆中開啟位置選擇畫面的動作。
 *
 * 主要功能與流程：
 * 本檔宣告 LocationTemplateAction。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 LineMessagingProcessor.Workflows/LineQuickReplyFactory.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging
{
    /// <summary>
    /// This action can be configured only with quick reply buttons. When a button associated with this action is tapped, the location screen in the LINE app is opened.
    /// https://developers.line.me/en/reference/messaging-api/#location-action
    /// </summary>
    public class LocationTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.Location;

        /// <summary>
        /// Label for the action
        /// Max: 20 characters
        /// </summary>
        public string Label { get; }

        public LocationTemplateAction(string label)
        {
            Label = label.Substring(0, Math.Min(label.Length, 20));
        }

        internal static LocationTemplateAction CreateFrom(dynamic dynamicObject)
        {
            return new LocationTemplateAction((string)dynamicObject?.label);
        }
    }
}
