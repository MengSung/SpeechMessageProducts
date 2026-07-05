// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Flex/ParameterTypes/AspectRatio.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class AspectRatio
// 主要成員：ToString
// 引用命名空間：System、System.Collections.Generic、System.Text、Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Aspect ratio of the image.
    /// Specify in the {width}:{height} format. <para>
    /// Specify the value of the {width} property and the {height} property in the range from 1 to 100000. However,
    /// you cannot set the {height} property to a value that is more than three times the value of the {width} property. </para>
    /// The default value is 1:1.
    /// </summary>
    [JsonConverter(typeof(ToStringJsonConverter))]
    public class AspectRatio
    {
        /// <summary>1:1</summary>
        public static readonly AspectRatio _1_1 = new AspectRatio(1, 1);
        /// <summary>1.51:1</summary>
        public static readonly AspectRatio _151_1 = new AspectRatio(151, 100);
        /// <summary>1.91:1</summary>
        public static readonly AspectRatio _191_1 = new AspectRatio(191, 100);
        /// <summary>4:3</summary>
        public static readonly AspectRatio _4_3 = new AspectRatio(4, 3);
        /// <summary>16:9</summary>
        public static readonly AspectRatio _16_9 = new AspectRatio(16, 9);
        /// <summary>20:13</summary>
        public static readonly AspectRatio _20_13 = new AspectRatio(20, 13);
        /// <summary>2:1</summary>
        public static readonly AspectRatio _2_1 = new AspectRatio(2, 1);
        /// <summary>3:1</summary>
        public static readonly AspectRatio _3_1 = new AspectRatio(3, 1);
        /// <summary>3:4</summary>
        public static readonly AspectRatio _3_4 = new AspectRatio(3, 4);
        /// <summary>9:16</summary>
        public static readonly AspectRatio _9_16 = new AspectRatio(9, 16);
        /// <summary>1:2</summary>
        public static readonly AspectRatio _1_2 = new AspectRatio(1, 2);
        /// <summary>1:3</summary>
        public static readonly AspectRatio _1_3 = new AspectRatio(1, 3);

        private readonly int _width;
        private readonly int _height;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="width">Width of aspect ratio</param>
        /// <param name="height">Height of aspect ratio</param>
        public AspectRatio(int width, int height)
        {
            if (width < 1 || width > 100000) { throw new ArgumentException($"The {nameof(width)} property must be in range from 1 to 100000.", nameof(width)); }
            if (height < 1 || height > 100000) { throw new ArgumentException($"The {nameof(height)} property must be in range from 1 to 100000.", nameof(height)); }
            if(height > width * 3) { throw new ArgumentException($"Cannot set the {nameof(height)} property to a value that is more than three times the value of the {nameof(width)} property."); }

            _width = width;
            _height = height;
        }
        public override string ToString()
        {
            return _width + ":" + _height;
        }
    }


}
