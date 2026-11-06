/*
 * 檔案整體說明
 *
 * 目的與定位：為前端帶領者lookup組裝可選帶領者清單，配合目前產品的名單／登入資料載入；選項ID只能作已授權流程的輸入，不能因取得選單而擴大改派範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 SpiritLeaderLookupController。
 * 主要操作入口包含 Get、SetupSpiritLeaderList。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

using ChurchReport.Models;
using System.Collections.Generic;

using ChurchReport.WebServiceConnector;

namespace ChurchReport.Controllers.ApiControllers
{
    [Route("api/[controller]/[action]")]
    public class SpiritLeaderLookupController : Controller
    {
        [HttpGet]
        public object Get(String id, DataSourceLoadOptions loadOptions)
        {
            SpiritLeaderList aSpiritLeaderList = SetupSpiritLeaderList(id);

            return DataSourceLoader.Load(aSpiritLeaderList.SpiritLeaders, loadOptions);
        }


        public SpiritLeaderList SetupSpiritLeaderList(String ListEntityId)
        {
            SpiritLeaderList aSpiritLeaderList = new SpiritLeaderList();

            aSpiritLeaderList.SpiritLeaders = new List<SpiritLeader>();

            if (ListEntityId != null)
            {
                DownloadHappyGroup aDownloadHappyGroup = new DownloadHappyGroup();

                String ShepherdLeader = aDownloadHappyGroup.GetSpiritLeaderListString(ListEntityId);

                if (ShepherdLeader != null && ShepherdLeader != "")
                {
                    String[] SpiritLeaderArray = ShepherdLeader.Split(',');

                    for (int i = 0; i < SpiritLeaderArray.Length; i++)
                    {
                        if (SpiritLeaderArray[i] != "")
                        {
                            aSpiritLeaderList.SpiritLeaders.Add
                            (
                                new SpiritLeader
                                {
                                    ID = i + 1,
                                    Name = SpiritLeaderArray[i]
                                }
                            );
                        }
                    }
                }
            }

            return aSpiritLeaderList;
        }

    }

}
