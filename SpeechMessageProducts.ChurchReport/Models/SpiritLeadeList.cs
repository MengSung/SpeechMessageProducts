/*
 * 檔案整體說明
 *
 * 目的與定位：包裝帶領者選單資料，供小組／幸福小組畫面選取現有帶領者；選項只提供ID與顯示文字，不代表伺服器已授權改派。
 *
 * 主要功能與流程：
 * 本檔宣告 SpiritLeaderList。
 * 資料／選項欄位包含 SpiritLeaderList、SpiritLeaders，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs、
 * SpeechMessageProducts.ChurchReport/Models/HappyGroupWeeklyReportListClass.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ChurchReport.Models
{
    public class SpiritLeaderList
    {
        public List<SpiritLeader> SpiritLeaders = new List<SpiritLeader>();
    }
}
