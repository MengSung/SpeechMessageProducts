/*
 * 檔案整體說明
 *
 * 目的與定位：保留舊MultiGroupController的API控制器宣告與相容型別位置；實際多小組畫面及提交流程位於SmallGroupController各partial，不可因檔名而假設此處已有完整指派實作。
 *
 * 主要功能與流程：
 * 本檔宣告 MultiGroupController。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using ChurchReport.Models;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace ChurchReport.Controllers
{
    //[Route("apicontroller/[controller]")]
    public class MultiGroupController : Controller
    {
        public MultiGroupController()
        { }

    }
}
