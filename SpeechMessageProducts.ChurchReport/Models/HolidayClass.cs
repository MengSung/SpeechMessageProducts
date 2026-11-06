/*
 * 檔案整體說明
 *
 * 目的與定位：描述政府假日開放資料JSON的封裝、欄位定義及逐日紀錄，保存日期、是否放假、假別與說明；資料模型本身不發出HTTP請求，也不判定教會時區。
 *
 * 主要功能與流程：
 * 本檔宣告 HolidayOpenData、Result、Field、Record。
 * 資料／選項欄位包含 HolidayOpenData、success、result、Result、resource_id、limit、total、fields、records、Field、type、id、Record、
 * date、name、isHoliday，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs、
 * SpeechMessageProducts.ChurchReport/Models/FeeList.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{

    public class HolidayOpenData
    {
        public bool success { get; set; }
        public Result result { get; set; }
    }

    public class Result
    {
        public string resource_id { get; set; }
        public int limit { get; set; }
        public int total { get; set; }
        public Field[] fields { get; set; }
        public Record[] records { get; set; }
    }

    public class Field
    {
        public string type { get; set; }
        public string id { get; set; }
    }

    public class Record
    {
        public string date { get; set; }
        public string name { get; set; }
        public string isHoliday { get; set; }
        public string holidayCategory { get; set; }
        public string description { get; set; }
    }
}
