/*
 * 檔案整體說明
 *
 * 目的與定位：表示教會組織樹的區牧節點，保存CRM正式ID、區牧名稱、登入工作識別、異動標記及下層區長清單；上傳端必須依ID處理，顯示姓名不能代替節點身分。
 *
 * 主要功能與流程：
 * 本檔宣告 AreaLeader。
 * 資料／選項欄位包含 AreaLeader、AreaLeaderName、LoginUserId、AreaLeaderEntityId、DirtyFlag、RaceLeaderList，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Models/ChurchRoot.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Views/Home/ChurchRoot.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class AreaLeader
    {
        public string AreaLeaderName { set; get; }              // 區牧名稱

        public string LoginUserId { set; get; }                 // 登入的使用者 Entity Id

        public string AreaLeaderEntityId { set; get; }          // 區牧的 Entity Id

        public bool DirtyFlag { set; get; } = true;

        public List<RaceLeader> RaceLeaderList { set; get; }    // 區長清單
    }
}
