using System.Runtime.Serialization;

namespace Line.Messaging
{
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
        [EnumMember(Value = "richmenuswitch")]
        RichMenuSwitch,
        [EnumMember(Value = "clipboard")]
        Clipboard
    }
}
