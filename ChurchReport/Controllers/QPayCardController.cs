using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ChurchReport.Tools;

namespace ChurchReport.Controllers
{
    [Route("api/[controller]")]
    public class QPayCardController : Controller
    {
        [HttpPost]
        [Route("QPayReturnUrl")]
        public ActionResult QPayReturnUrl(string ShopNo, string PayToken)
        {
            using (QPayCardWebhook aQPayCardWebhook = new QPayCardWebhook())
            {
                return aQPayCardWebhook.QPayReturnUrl(ShopNo, PayToken);
            }
            //QPayCardWebhook aQPayCardWebhook = new QPayCardWebhook();
            //return aQPayCardWebhook.QPayReturnUrl(ShopNo, PayToken);
        }
    }
}
