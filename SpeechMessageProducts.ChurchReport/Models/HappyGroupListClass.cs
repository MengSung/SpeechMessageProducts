/*
 * 檔案整體說明
 *
 * 目的與定位：包裝登入者可見的幸福小組週報根清單，供控制器及畫面取得巢狀週報資料；登入工作識別只供已授權流程定位，不能作任意小組存取的權限證明。
 *
 * 主要功能與流程：
 * 本檔宣告 HappyGroupListClass。
 * 資料／選項欄位包含 HappyGroupListClass、LoginUserId、HappyGroupWeeklyReportListClass，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/HappyGroupDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class HappyGroupListClass
    {
        // 幸福小組名單登入的使用者 Entity Id
        public string LoginUserId { set; get; }

        // 一個人會開多個幸福小組
        public List<HappyGroupWeeklyReportListClass> HappyGroupWeeklyReportListClass { set; get; } // 幸福小組出席紀錄單清單
    }
}
