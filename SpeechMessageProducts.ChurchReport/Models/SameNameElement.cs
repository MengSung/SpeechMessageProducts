/*
 * 檔案整體說明
 *
 * 目的與定位：呈現可能同名的奉獻者候選，並保留各自的正式ID、奉獻編號及聯絡／教會資訊供使用者辨識；同名候選必須維持不同記錄，不能按姓名去重。
 *
 * 主要功能與流程：
 * 本檔宣告 SameNameElement。
 * 資料／選項欄位包含 SameNameElement、SameNameElementId、DedicationNumber、NationId、FullName、Mobile、SmallGroupName、ChurchName，
 * 名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationKeyInDedicationService.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace ChurchReport.Models
{
    public class SameNameElement
    {
        public String SameNameElementId { get; set; }

        public String DedicationNumber { get; set; }
        public String NationId { get; set; }
        public String FullName { get; set; }
        public string Mobile { get; set; }
        public string SmallGroupName { get; set; }
        public String ChurchName { get; set; }                          //所屬教會
    }
}
