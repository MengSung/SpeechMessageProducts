// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/ViewModels/PersonalInfomationViewModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class PersonalInfomationViewModel
// 主要成員：ID、ContactId、FirstName、LastName、FullName、CustomerTypeCode、Gender、Phone、HomePhone、OfficePhone
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

namespace ChurchReport.ViewModels
{
    public class PersonalInfomationViewModel
    {
        public PersonalInfomationViewModel()
        { }

        public int ID { get; set; }

        /// <summary>
        /// Contact ID（用於圖片上傳等功能）
        /// </summary>
        public Guid ContactId { get; set; }

        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String FullName { get; set; }
        public String CustomerTypeCode { get; set; }//委身類型
        public String Gender { get; set; }
        public String Phone { get; set; } //行動電話
        public String HomePhone { get; set; } // 住家電話
        public String OfficePhone { get; set; } //公司電話
        public String Facebook { get; set; } //Facebook帳號
        public String Instagram { get; set; } //Instagram帳號
        public String Email { get; set; } //電子郵件
        public String Address { get; set; } // 地址
        public String LastSixDigit { get; set; } // 銀行帳戶後六碼
        //public bool NtbtOrNot { get; set; } // 是否上傳國稅局
        public String NtbtOrNot { get; set; } // 是否上傳國稅局
        public String PersonalId { get; set; } // 身份證字號
        public String Position { get; set; }
        public List<String> GroupArray { get; set; }
        public String GroupName { get; set; }
        public String MerrageState { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime HireDate { get; set; } // 進入教會日期
        public String Notes { get; set; }
        public int ReadBibleNumber { get; set; }
        public String Status { get; set; } // 新人信仰狀態

        public String Introducer { get; set; } // 邀請人
        public String IntroducerPhone { get; set; } // 邀請人電話
        public String IntroducerRelation { get; set; } // 邀請人關係
        public String IntroducerGroup { get; set; } // 邀請人小組

        public String Industry { get; set; } // 職業及專長
        public String EquipmentStatus { get; set; } // 裝備狀態
        public String SpiritualIdentity { get; set; } // 受洗狀態
        public String BaptizedSituation { get; set; } // 洗禮狀態(長老教會專用)

        public String PresentRecordId { get; set; } // 建立新人時也會新增建立的靈修出席紀錄單的ID

        public object FormData { get; set; }
    }
}
