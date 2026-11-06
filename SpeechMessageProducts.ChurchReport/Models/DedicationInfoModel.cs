/*
 * 檔案整體說明
 *
 * 目的與定位：承載人工登錄奉獻的日期、銀行、序號、金額及類別選單，作為舊畫面與奉獻服務之間的輸入候選；顯示選項不得取代服務端對類別及權限的驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 DedicationInfoModel。
 * 資料／選項欄位包含 DedicationInfoModel、DedicationDate、BankName、SerialNumber、Amount、Category、Others、
 * DedicationCategoryList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Views/Home/DedicationInofView.cshtml 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class DedicationInfoModel
    {
        public DedicationInfoModel()
        { }

        public DateTime DedicationDate { get; set; }    //奉獻日期
        public String BankName { get; set; }            //銀行名稱
        public String SerialNumber { get; set; }        //奉獻號碼
        public int Amount { get; set; }                 //奉獻金額
        public String Category { get; set; }            //奉獻類別
        public String Others { get; set; }              //其他奉獻

        // ✅ 新增：動態奉獻類別清單（從 Dynamics 365 OptionSet 取得）
        public List<String> DedicationCategoryList { get; set; } = new List<String>();
    }
}
