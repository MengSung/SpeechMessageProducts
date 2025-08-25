using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Tools
{
    /// <summary>
    /// 串接服務請求欄位
    /// </summary>
    public class ServiceRequest
    {
        public string service_name { get; set; }
        public string cmd { get; set; }
    }
}