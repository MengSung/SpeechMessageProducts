/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示Flex元件尺寸及其JSON字串轉換。
 *
 * 主要功能與流程：
 * 本檔宣告 ComponentSize、ComponentSizeEnumConverter。
 * 資料／選項欄位包含 ComponentSize，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Json/CustomStringEnumConverter.cs、Line.Messaging/Messages/Flex/Component/IconComponent.cs、
 * Line.Messaging/Messages/Flex/Component/ImageComponent.cs、
 * Line.Messaging/Messages/Flex/Component/SpacerComponent.cs、
 * Line.Messaging/Messages/Flex/Component/TextComponent.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    [JsonConverter(typeof(ComponentSizeEnumConverter))]
    public enum ComponentSize
    {
        Xxs,
        Xs,
        Sm,
        Md,
        Lg,
        Xl,
        Xxl,
        _3xl,
        _4xl,
        _5xl,
        Full
    }

    internal class ComponentSizeEnumConverter : CustomStringEnumConverter<ComponentSize>
    {
        public ComponentSizeEnumConverter() : base(new Dictionary<ComponentSize, string>
        {
            [ComponentSize._3xl] = "3xl",
            [ComponentSize._4xl] = "4xl",
            [ComponentSize._5xl] = "5xl",
        })
        {
            CamelCaseText = true;
        }
    }

}
