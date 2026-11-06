/*
 * 檔案整體說明
 *
 * 目的與定位：本檔將指定值以字串表示寫入JSON，集中處理只支援輸出時的轉換限制。
 *
 * 主要功能與流程：
 * 本檔宣告 ToStringJsonConverter。
 * 主要操作入口包含 CanConvert、WriteJson、ReadJson。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging/Messages/Flex/ParameterTypes/AspectRatio.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{

    public class ToStringJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return true;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

    }
}
