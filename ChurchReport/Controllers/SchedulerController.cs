using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

using ChurchReport.Models;

namespace ChurchReport.Controllers
{
    public class SchedulerController : Controller
    {
        // GET: /<controller>/
        public ActionResult Scheduler()
        {
            AppointmentsList aAppointmentsList = new AppointmentsList();
            return View(aAppointmentsList);
        }
    }
}

