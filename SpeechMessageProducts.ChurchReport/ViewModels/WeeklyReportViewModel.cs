/*
 * 檔案整體說明
 *
 * 目的與定位：組合週報資料、分析內容與顯示旗標，讓週報Razor取得完整畫面模型；本檔不負責下載或保存，應由控制器完成對應週次及小組範圍的組裝。
 *
 * 主要功能與流程：
 * 本檔宣告 WeeklyReportViewModel。
 * 資料／選項欄位包含 WeeklyReportViewModel、WeeklyReportData、WeeklyReportAnalysis、DisplayFlag，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs、
 * SpeechMessageProducts.ChurchReport/Models/WeeklyReportData.cs、
 * SpeechMessageProducts.ChurchReport/Views/Home/SmallGroupReportView.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Home/WeeklyReport.cshtml 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

using ChurchReport.Models;

namespace ChurchReport.ViewModels
{
    public class WeeklyReportViewModel
    {
        public String WeeklyReportData { get; set; }
        public String WeeklyReportAnalysis { get; set; }
        public bool DisplayFlag { get; set; }

        //static public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();
    }
}
