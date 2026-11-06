/*
 * 檔案整體說明
 *
 * 目的與定位：本檔限制LIFF視窗的合法尺寸代號，維持與LINE JSON字串規格一致。
 *
 * 主要功能與流程：
 * 本檔宣告 ViewType。
 * 資料／選項欄位包含 ViewType，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LiffAndStreamLifecycleTests.cs、Line.Messaging/Liff/LiffClient.cs、
 * Line.Messaging/Liff/View.cs、SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLogin.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging.Liff
{
    /// <summary>
    /// Size of the LIFF app view. Specify one of the following values
    /// </summary>
    public enum ViewType
    {
        /// <summary>
        /// 50% of the screen height of the device. This size can be specified only for the chat screen.
        /// </summary>
        Compact,

        /// <summary>
        /// 80% of the screen height of the device. This size can be specified only for the chat screen.
        /// </summary>
        Tall,

        /// <summary>
        /// 100% of the screen height of the device. This size can be specified for any screens in the LINE app.
        /// </summary>
        Full
    }
}
