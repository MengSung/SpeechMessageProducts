/*
 * 檔案整體說明
 *
 * 目的與定位：承載幸福小組單一BEST對象的出席、決志、介紹人、關係及備註編輯狀態，並區分記錄ID、父週報ID與異動標記，供週報畫面及上傳映射使用。
 *
 * 主要功能與流程：
 * 本檔宣告 BestRecord。
 * 資料／選項欄位包含 BestRecord、BestModifiedFlag、BestRecordEntityId、BestRecordId、BestRecordParentId、FullName、MobilePhone、
 * Present、Decision、Note、BestLeader、BestIntroducer、BestRelationship、ModifiedFlag，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/HappyGroupDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/HappyGroupWeeklyReport.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 幸福小組出席紀錄單
    public class BestRecord
    {
        public bool BestModifiedFlag { get; set; }

        public string BestRecordEntityId { set; get; } // 幸福小組出席紀錄單在 CRM 系統中的 EntityId
        public string BestRecordId { set; get; } // 幸福小組出席紀錄單在回報陣列中的Id
        public string BestRecordParentId { set; get; } // 幸福小組出席紀錄單的上一層周報的Id
        public string FullName { get; set; }
        public string MobilePhone { get; set; }
        public bool Present { get; set; }
        public bool Decision { get; set; }
        public string Note { get; set; }
        public string BestLeader { get; set; } // 屬靈認領者
        public string BestIntroducer { get; set; } // 介紹人
        public string BestRelationship { get; set; } // 與介紹人關係

        public bool ModifiedFlag { set; get; } = false;         // 是否有被修改過

    }
}
