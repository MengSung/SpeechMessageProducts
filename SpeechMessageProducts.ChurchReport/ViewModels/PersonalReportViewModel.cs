/*
 * 檔案整體說明
 *
 * 目的與定位：承載單一會友的主日／小組出席、靈修、晨禱、關懷及代禱事項等個人回報輸入；回報者與週次由服務端確認，不因提交ID而可替其他會友回報。
 *
 * 主要功能與流程：
 * 本檔宣告 PersonalReportViewModel。
 * 資料／選項欄位包含 PersonalReportViewModel、ID、SundayPrayers、GroupName、FullName、SundayPresent、SmallGroupPresent、
 * SpiritualWork、MorningPray、GeneralCare、PrayItem，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs、
 * SpeechMessageProducts.ChurchReport/Views/Personal/PersonalReport.cshtml 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.ViewModels
{
    public class PersonalReportViewModel
    {
        public PersonalReportViewModel()
        { }

        public int ID { get; set; }
        public DateTime SundayPrayers { get; set; } // 小組日期

        public String GroupName { get; set; }
        public String FullName { get; set; }
        public bool SundayPresent { get; set; }
        public bool SmallGroupPresent { get; set; }
        public int SpiritualWork { get; set; }
        public int MorningPray { get; set; }
        public int GeneralCare { get; set; }
        public String PrayItem { get; set; }
    }
}
