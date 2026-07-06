// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/ViewModels/FeeChangeHistoryViewModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class FeeChangeHistoryViewModel、class FeeChangeRecord
// 主要成員：RecordChange、GetChanges、ClearAll、GetPendingCount、HasPendingChanges、StorLessonsId、LastModifiedTime
// 引用命名空間：System、System.Collections.Generic、ChurchReport.Models
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using ChurchReport.Models;

namespace ChurchReport.ViewModels
{
    /// <summary>
    /// 課程繳費點名修改歷程 ViewModel
    /// 用於暫存使用者的修改，在按下「上傳」按鈕後才批次提交到資料庫
    /// </summary>
    public class FeeChangeHistoryViewModel
    {
        /// <summary>
        /// 待處理的修改記錄清單
        /// Key: StorLessonsId (上課紀錄單ID)
        /// Value: 該筆記錄的所有欄位修改
        /// </summary>
        public Dictionary<string, FeeChangeRecord> PendingChanges { get; set; }

        /// <summary>
        /// 建構函式
        /// </summary>
        public FeeChangeHistoryViewModel()
        {
            PendingChanges = new Dictionary<string, FeeChangeRecord>();
        }

        /// <summary>
        /// 記錄單一欄位的修改
        /// </summary>
        /// <param name="storLessonsId">上課紀錄單ID</param>
        /// <param name="fieldName">欄位名稱</param>
        /// <param name="newValue">新值</param>
        public void RecordChange(string storLessonsId, string fieldName, string newValue)
        {
            if (!PendingChanges.ContainsKey(storLessonsId))
            {
                PendingChanges[storLessonsId] = new FeeChangeRecord
                {
                    StorLessonsId = storLessonsId,
                    ModifiedFields = new Dictionary<string, string>()
                };
            }

            PendingChanges[storLessonsId].ModifiedFields[fieldName] = newValue;
            PendingChanges[storLessonsId].LastModifiedTime = DateTime.Now;
        }

        /// <summary>
        /// 取得指定記錄的所有修改
        /// </summary>
        /// <param name="storLessonsId">上課紀錄單ID</param>
        /// <returns>修改記錄，若無則返回 null</returns>
        public FeeChangeRecord GetChanges(string storLessonsId)
        {
            return PendingChanges.ContainsKey(storLessonsId) ? PendingChanges[storLessonsId] : null;
        }

        /// <summary>
        /// 清除所有待處理的修改
        /// </summary>
        public void ClearAll()
        {
            PendingChanges.Clear();
        }

        /// <summary>
        /// 取得待處理修改的總數
        /// </summary>
        public int GetPendingCount()
        {
            return PendingChanges.Count;
        }

        /// <summary>
        /// 檢查是否有待處理的修改
        /// </summary>
        public bool HasPendingChanges()
        {
            return PendingChanges.Count > 0;
        }
    }

    /// <summary>
    /// 單筆繳費記錄的修改歷程
    /// </summary>
    public class FeeChangeRecord
    {
        /// <summary>
        /// 上課紀錄單ID (主鍵)
        /// </summary>
        public string StorLessonsId { get; set; }

        /// <summary>
        /// 已修改的欄位
        /// Key: 欄位名稱 (例如: "Lesson1", "Amount", "PayDate")
        /// Value: 新值 (字串格式)
        /// </summary>
        public Dictionary<string, string> ModifiedFields { get; set; }

        /// <summary>
        /// 最後修改時間
        /// </summary>
        public DateTime LastModifiedTime { get; set; }

        /// <summary>
        /// 建構函式
        /// </summary>
        public FeeChangeRecord()
        {
            ModifiedFields = new Dictionary<string, string>();
            LastModifiedTime = DateTime.Now;
        }
    }
}
