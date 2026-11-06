/*
 * 檔案整體說明
 *
 * 目的與定位：表示舊奉獻頁的單項金額、類別與可選類別清單，提供畫面繫結相容形狀；正式多品項及金流建立流程由對應表單與工作流程處理。
 *
 * 主要功能與流程：
 * 本檔宣告 DedicationModel。
 * 資料／選項欄位包含 DedicationModel、Amount、Category、DedicationCategoryList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Views/Home/DedicationView.cshtml 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class DedicationModel
    {
        public DedicationModel()
        { }

        public int Amount { get; set; }
        public String Category { get; set; }

        // ✅ 新增：動態奉獻類別清單（從 Dynamics 365 OptionSet 取得）
        public List<String> DedicationCategoryList { get; set; } = new List<String>();
    }
}
