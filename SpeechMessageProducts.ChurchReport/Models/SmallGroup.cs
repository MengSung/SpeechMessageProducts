// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/SmallGroup.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class SmallGroup
// 主要成員：SmallGroupName、SmallGroupId、SmallGroupLeaderName、SmallGroupLeaderEntityId、RaceLeaderName、RaceLeaderEntityId、SmallGroupModifiedFlag、ContactMemberModifiedFlag、ChangeRaceLeader、ChangeAreaLeader
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 小組名單
    public class SmallGroup
    {
        public string SmallGroupName { set; get; }              // 小組名稱
        public string SmallGroupId { set; get; }                // 小組在 CRM 系統中的 EntityId
        public string SmallGroupLeaderName { set; get; }        // 小組長名稱
        public string SmallGroupLeaderEntityId { set; get; }    // 小組長 EntityId
        public string RaceLeaderName { set; get; }              // 區長名稱
        public string RaceLeaderEntityId { set; get; }          // 區長 EntityId

        public bool SmallGroupModifiedFlag { get; set; }
        public bool ContactMemberModifiedFlag { get; set; }

        public string ChangeRaceLeader { set; get; }          // 換區長
        public string ChangeAreaLeader { set; get; }          // 換區牧

        //public List<String> RaceLeaderArray { get; set; } = new List<string>();     //換區長要用到的區長清單
        //public List<String> AreaLeaderArray { get; set; } = new List<string>();     //換區牧要用到的區牧清單


        public DateTime MeetingDate { get; set; }                   // 小組聚會日期
        public string Location { set; get; }                        // 小組聚會地點

        public bool ModifiedFlag { set; get; } = false;             // 設定小組是否有被修改過
        public List<ContactMember> ContactMemberList { set; get; }  // 小組成員名單
    }
}
