/*
 * 檔案整體說明
 *
 * 目的與定位：提供小組回報的組合資料容器，連接小組、成員與週報等既有模型；本檔定義資料形狀，實際讀取、驗證、快照發布及上傳由對應管理服務負責。
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupReportData。
 * 資料／選項欄位包含 SmallGroupReportData、SmallGroupReport，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public static class SmallGroupReportData
    {
        public static String SmallGroupReport = "";
    }
}
