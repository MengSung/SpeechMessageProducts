/*
 * 檔案整體說明
 *
 * 目的與定位：承載奉獻明細列的奉獻者、奉獻日、付款日、金額、方式、類別與期數，供奉獻查閱及畫面組裝；它描述已映射的資料，不負責銀行回呼驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 DedicationFee。
 * 資料／選項欄位包含 DedicationFee、FullName、MobilePhone、DedicationDate、PayDate、Amount、PayWay、Category、Others、PaidPeriod，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Dedication/DonationGivingHistoryTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class DedicationFee
    {
        public DedicationFee()
        { }

        public string FullName { get; set; }                // 姓名
        public string MobilePhone { get; set; }             // 行動電話
        public DateTime DedicationDate { get; set; }        // 奉獻日期
        public DateTime PayDate { get; set; }               // 繳費日期
        public int Amount { get; set; }                     // 繳費金額
        public String PayWay { get; set; }                  // 付款方式
        public String Category { get; set; }                // 奉獻類別
        public String Others { get; set; }                  // 其他奉獻
        public String PaidPeriod { get; set; }              // 定期定額期數
    }
}
