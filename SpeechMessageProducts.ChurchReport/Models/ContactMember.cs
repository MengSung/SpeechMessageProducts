// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/ContactMember.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ContactMember
// 主要成員：FullName、ContactId、SmallGroupName、SmallGroupId、Status、RaceLeaderSmallGroup、ChurchSmallGroup、MobilePhone、HomePhone、Address
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
