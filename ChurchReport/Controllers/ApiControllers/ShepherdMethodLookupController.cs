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
    public class ShepherdMethodLookupController : Controller
    {

        [HttpGet]
        public object Get(DataSourceLoadOptions loadOptions)
        {
            return DataSourceLoader.Load(ShepherdMethodData.ShepherdMethodList, loadOptions);
        }

        public ActionResult GetType(DataSourceLoadOptions loadOptions)
        {
            return Content(JsonConvert.SerializeObject(DataSourceLoader.Load(ShepherdMethodData.ShepherdMethodList, loadOptions)), "application/json");
            //return DataSourceLoader.Load(ShepherdMethodData.ShepherdMethodList, loadOptions);
        }
    }
}