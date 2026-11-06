/*
 * 檔案整體說明
 *
 * 目的與定位：LINE template action 的序列化型別。 RichMenu 使用此 enum 解析 action area 的 action type；新增 LINE action 時必須同步更新 
 * ActionArea.ParseTemplateAction(dynamic)，否則 provider 回傳的 RichMenu area 會無法還原成正確 action 物件。
 *
 * 主要功能與流程：
 * 本檔宣告 TemplateActionType。
 * - LINE RichMenu switch action，對應官方 JSON 字串 richmenuswitch 。 此值需要搭配 RichMenu alias 使用，讓切換 action 不直接綁死 provider 
 * richMenuId。
 * 資料／選項欄位包含 TemplateActionType，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Action/CameraRollTemplateAction.cs、
 * Line.Messaging/Messages/Action/CameraTemplateAction.cs、
 * Line.Messaging/Messages/Action/ClipboardTemplateAction.cs、
 * Line.Messaging/Messages/Action/DateTimePickerTemplateAction.cs、
 * Line.Messaging/Messages/Action/LocationTemplateAction.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Runtime.Serialization;

namespace Line.Messaging
{
    /// <summary>
    /// LINE template action 的序列化型別。
    /// RichMenu 使用此 enum 解析 action area 的 action type；新增 LINE action 時必須同步更新
    /// <see cref="ActionArea.ParseTemplateAction(dynamic)"/>，否則 provider 回傳的 RichMenu area 會無法還原成正確 action 物件。
    /// </summary>
    public enum TemplateActionType
    {
        [EnumMember(Value = "postback")]
        Postback,
        [EnumMember(Value = "message")]
        Message,
        [EnumMember(Value = "uri")]
        Uri,
        [EnumMember(Value = "datetimepicker")]
        Datetimepicker,
        [EnumMember(Value = "camera")]
        Camera,
        [EnumMember(Value = "cameraRoll")]
        CameraRoll,
        [EnumMember(Value = "location")]
        Location,
        /// <summary>
        /// LINE RichMenu switch action，對應官方 JSON 字串 <c>richmenuswitch</c>。
        /// 此值需要搭配 RichMenu alias 使用，讓切換 action 不直接綁死 provider richMenuId。
        /// </summary>
        [EnumMember(Value = "richmenuswitch")]
        RichMenuSwitch,
        [EnumMember(Value = "clipboard")]
        Clipboard
    }
}
