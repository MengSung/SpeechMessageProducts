/*
 * 檔案整體說明
 *
 * 目的與定位：將聯絡人基本資料、所屬小組、聯絡方式、生日、裝備與信仰狀態投影為名單編輯列，並保留ContactId及異動標記；服務端必須重新驗證提交ID的可存取範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactMember。
 * 資料／選項欄位包含 ContactMember、FullName、ContactId、SmallGroupName、SmallGroupId、Status、RaceLeaderSmallGroup、
 * ChurchSmallGroup、MobilePhone、HomePhone、Address、BirthDate、Industry、EquipmentStatus、SpiritualIdentity、
 * BaptizedSituation，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/SmallGroup.cs、
 * SpeechMessageProducts.ChurchReport/Views/Home/SmallGroupMemberList.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class ContactMember
    {
        // 名單管理要用到的成員
        public ContactMember()
        { }

        public string FullName { get; set; }
        public String ContactId { get; set; }
        public string SmallGroupName { get; set; }
        public string SmallGroupId { get; set; }
        public string Status { get; set; } // 委身類型
        public string RaceLeaderSmallGroup { get; set; } // 本區小組
        public string ChurchSmallGroup { get; set; } // 全教會小組

        #region 個人基本資料
        public string MobilePhone
        {
            get;
            set;
        }

        public string HomePhone
        {
            get;
            set;
        }

        public string Address
        {
            get;
            set;
        }

        public DateTime BirthDate
        {
            get;
            set;
        }
        public string Industry
        {
            get;
            set;
        }

        // 裝備狀態
        public string EquipmentStatus
        {
            get;
            set;
        }

        // 受洗狀態
        public string SpiritualIdentity
        {
            get;
            set;
        }

        // 洗禮狀態(長老教會專用)
        public string BaptizedSituation
        {
            get;
            set;
        }

        #endregion

        public bool ModifyFlag { get; set; }

    }
}
