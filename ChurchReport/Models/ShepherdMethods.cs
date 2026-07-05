// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/ShepherdMethods.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ShepherdMethodData
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、System.Collections.Generic、System.Linq
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================


using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Models
{
    public partial class ShepherdMethodData
    {
        public static List<ShepherdMethod> ShepherdMethodList = new List<ShepherdMethod> {
            new ShepherdMethod {
                ID = 1,
                Name = "打電話"
            },
            new ShepherdMethod {
                ID = 2,
                Name = "一起吃飯"
            },
            new ShepherdMethod {
                ID = 3,
                Name = "陪讀聖經"
            },
            new ShepherdMethod {
                ID = 4,
                Name = "Line聯絡"
            },
            new ShepherdMethod {
                ID = 5,
                Name = "親自拜訪"
            },
        };
    }
}
