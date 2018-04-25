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
    public class AssignSmallGroupController : Controller
    {
        public AssignSmallGroupController()
        { }

        //[HttpGet]
        //public object Get(DataSourceLoadOptions loadOptions)
        //{
        //    //return DataSourceLoader.Load(AssignSmallGroupList.AssignSmallGroupListData, loadOptions);
        //}
        //[HttpGet]
        //public ActionResult GetType(DataSourceLoadOptions loadOptions)
        //{
        //    //return Content(JsonConvert.SerializeObject(DataSourceLoader.Load(AssignSmallGroupList.AssignSmallGroupListData, loadOptions)), "application/json");
        //}
    }
}
