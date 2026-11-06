/*
 * 檔案整體說明
 *
 * 目的與定位：聚合多小組的週報記錄，作為多組回報與統計畫面的共同資料形狀；每筆資料仍須保留原小組／週報身分，不能依顯示名稱合併。
 *
 * 主要功能與流程：
 * 本檔宣告 MultiGroupList。
 * 資料／選項欄位包含 MultiGroupList、m_WeeklyReportRecordListData，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs、
 * SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class MultiGroupList
    {
        // 新增新人時，選擇進入哪一個小組的清單 + 小家長或一人帶多個小組時，提供選擇點選進入觀看的Grid
        public List<WeeklyReportRecord> m_WeeklyReportRecordListData { get; set; }
    }
}
