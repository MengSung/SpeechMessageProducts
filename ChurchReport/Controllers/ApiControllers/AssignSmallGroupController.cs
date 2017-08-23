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

namespace ChurchReport.Controllers
{
    public class AssignSmallGroupController : Controller
    {
        [HttpGet]
        public object Get(DataSourceLoadOptions loadOptions)
        {
            return DataSourceLoader.Load(AssignSmallGroupList.AssignSmallGroupListData, loadOptions);
        }
        [HttpGet]
        public ActionResult GetType(DataSourceLoadOptions loadOptions)
        {
            return Content(JsonConvert.SerializeObject(DataSourceLoader.Load(AssignSmallGroupList.AssignSmallGroupListData, loadOptions)), "application/json");
        }
    }
}
