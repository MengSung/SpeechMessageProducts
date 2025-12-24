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
