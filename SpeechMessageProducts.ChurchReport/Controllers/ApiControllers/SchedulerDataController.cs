/*
 * 檔案整體說明
 *
 * 目的與定位：提供行事曆元件的查詢、新增、更新及刪除HTTP端點，將元件傳入值映射到約會資料Context；請求資料是編輯候選，應沿目前登入及排程範圍處理。
 *
 * 主要功能與流程：
 * 本檔宣告 SchedulerDataController。
 * 主要操作入口包含 Get、Post、Put、Delete。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.Models;
using DevExtreme.AspNet.Mvc;
using DevExtreme.AspNet.Data;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ChurchReport.Controllers
{
    //[Route("apic/[controller]")]
    public class SchedulerDataController : Controller
    {
        InMemoryAppointmentsDataContext _data;

        public SchedulerDataController()
        {
        }
        public SchedulerDataController(IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache)
        {
            _data = new InMemoryAppointmentsDataContext(httpContextAccessor, memoryCache);
        }

        [HttpGet]
        public object Get(DataSourceLoadOptions loadOptions)
        {
            return DataSourceLoader.Load(_data.Appointments, loadOptions);
        }

        [HttpPost]
        public IActionResult Post(string values)
        {
            var newAppointment = new Appointment();
            JsonConvert.PopulateObject(values, newAppointment);

            //if (!TryValidateModel(newAppointment))
            //    return BadRequest(ModelState.GetFullErrorMessage());

            _data.Appointments.Add(newAppointment);
            //_data.SaveChanges();

            return Ok();
        }

        [HttpPut]
        public IActionResult Put(String key, string values)
        {
            var appointment = _data.Appointments.First(a => a.AppointmentId == key);
            JsonConvert.PopulateObject(values, appointment);

            //if (!TryValidateModel(appointment))
            //    return BadRequest(ModelState.GetFullErrorMessage());

            _data.SaveChanges(key);

            return Ok();
        }


        [HttpDelete]
        public void Delete(String key)
        {
            var appointment = _data.Appointments.First(a => a.AppointmentId == key);
            _data.Appointments.Remove(appointment);
            _data.SaveChanges(key);
        }
    }
}
