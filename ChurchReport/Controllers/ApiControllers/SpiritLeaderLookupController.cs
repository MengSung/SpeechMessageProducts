using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

using ChurchReport.Models;
using System.Collections.Generic;

namespace ChurchReport.Controllers.ApiControllers
{
    [Route("api/[controller]/[action]")]
    public class SpiritLeaderLookupController : Controller
    {
        [HttpGet]
        public object Get(DataSourceLoadOptions loadOptions, String ShepherdLeader)
        {
            SpiritLeaderList aSpiritLeaderList = SetupSpiritLeaderList(ShepherdLeader);

            return DataSourceLoader.Load(aSpiritLeaderList.SpiritLeaders, loadOptions);
        }


        public SpiritLeaderList SetupSpiritLeaderList(String ShepherdLeader)
        {
            SpiritLeaderList aSpiritLeaderList = new SpiritLeaderList();

            aSpiritLeaderList.SpiritLeaders = new List<SpiritLeader>();

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
            return aSpiritLeaderList;
        }

    }

}

