using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.Models;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ChurchReport.Controllers
{
    //[Route("api/[controller]")]
    public class TempController : Controller
    {
        public TempController()
        {
        }

        [HttpGet]
        public object Get()
        {
            return "";
        }
    }
}
