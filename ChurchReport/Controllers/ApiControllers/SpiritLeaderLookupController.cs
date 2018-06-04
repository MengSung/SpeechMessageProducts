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

