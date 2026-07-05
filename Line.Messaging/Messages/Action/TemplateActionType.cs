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
