/*
 * 檔案整體說明
 *
 * 目的與定位：本檔處理LINE列舉值與字串JSON的互轉，保留協定名稱及未知值的既有處理方式。
 *
 * 主要功能與流程：
 * 本檔宣告 CustomStringEnumConverter。
 * 主要操作入口包含 CanConvert、ReadJson、WriteJson。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging/Messages/Flex/ParameterTypes/ComponentSize.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Line.Messaging
{


    public class CustomStringEnumConverter<TEnum> : StringEnumConverter where TEnum : struct, Enum
    {
        private readonly IDictionary<TEnum, string> enumStrPairs;


        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ComponentSize);
        }

        public CustomStringEnumConverter(IDictionary<TEnum, string> enumStrPairs)
        {
            this.enumStrPairs = enumStrPairs ?? new Dictionary<TEnum, string>();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var value = (string)reader.Value;

                if (enumStrPairs.Any(kvp => value == kvp.Value))
                {
                    return enumStrPairs.First(kvp => value == kvp.Value).Key;
                }
            }

            return base.ReadJson(reader, objectType, existingValue, serializer);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {

            if (enumStrPairs.TryGetValue((TEnum)value, out string name))
            {
                writer.WriteValue(name);
            }
            else
            {
                base.WriteJson(writer, value, serializer);
            }

        }
    }
}
