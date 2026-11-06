/*
 * 檔案整體說明
 *
 * 目的與定位：承接個人基本資料及照片相關畫面欄位，供目前登入聯絡人的查詢／編輯流程繫結；Contact ID須由授權流程核對，模型值本身不能授予照片或資料寫入權限。
 *
 * 主要功能與流程：
 * 本檔宣告 PersonalInfomationViewModel。
 * 資料／選項欄位包含 PersonalInfomationViewModel、ID、ContactId、FirstName、LastName、FullName、CustomerTypeCode、Gender、Phone、
 * HomePhone、OfficePhone、Facebook、Instagram、Email、Address、LastSixDigit，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs、
 * SpeechMessageProducts.ChurchReport/Models/PersonalInfomationModel.cs、
 * SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationView.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationViewWithImage.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Personal/PersonalInfomationView_fix.cshtml 追查目前工作樹中的合作／使用位置。
 */
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
