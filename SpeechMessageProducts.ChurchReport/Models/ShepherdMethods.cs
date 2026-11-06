/*
 * 檔案整體說明
 *
 * 目的與定位：提供既有牧養方式選項資料來源，將固定或已載入的ShepherdMethod清單交給查詢端點；這是選單資料，不以選項本身決定目前使用者可操作的會友範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 ShepherdMethodData。
 * 資料／選項欄位包含 ShepherdMethodData、ShepherdMethodList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/ShepherdMethodLookupController.cs 
 * 追查目前工作樹中的合作／使用位置。
 */


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
