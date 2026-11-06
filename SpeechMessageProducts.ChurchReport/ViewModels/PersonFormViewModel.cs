/*
 * 檔案整體說明
 *
 * 目的與定位：描述新人及個人基本資料編輯表單，保存聯絡人ID、姓名、聯絡方式、選項、小組候選與異動輸入；它是提交候選，服務端須重驗欄位及可操作ID。
 *
 * 主要功能與流程：
 * 本檔宣告 PersonFormViewModel。
 * 資料／選項欄位包含 PersonFormViewModel、ID、FirstName、LastName、CustomerTypeCode、Gender、Phone、HomePhone、Position、GroupArray、
 * GroupName、MerrageState、BirthDate、HireDate、Notes、Address，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs、
 * SpeechMessageProducts.ChurchReport/Models/NewPersonModel.cs、
 * SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs、
 * SpeechMessageProducts.ChurchReport/Views/NewPerson/NewPerson.cshtml 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.ViewModels
{
    public class PersonFormViewModel
    {
        public PersonFormViewModel()
        { }

        public int ID { get; set; }

        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String CustomerTypeCode { get; set; }//委身類型
        public String Gender { get; set; }
        public String Phone { get; set; }
        public String HomePhone { get; set; }
        public String Position { get; set; }
        public List<String> GroupArray { get; set; }
        public String GroupName { get; set; }
        public String MerrageState { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime HireDate { get; set; } // 進入教會日期
        public String Notes { get; set; }
        public String Address { get; set; }
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
