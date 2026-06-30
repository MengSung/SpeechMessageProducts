using System;
using System.Threading.Tasks;
using ChurchReport.Payments;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.AspNetCore;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 舊 QPay 回傳 URL 的相容 Controller。
    /// 實際付款回傳流程已移到 <see cref="PaymentReturnController"/>；此類別只保留舊路由與舊 action 名稱，
    /// 避免既有 provider callback 設定或外部連結在改名期間中斷。
    /// </summary>
    [Obsolete("Use PaymentReturnController. QPayCardController is retained only for legacy callback routes.")]
    [Route("api/[controller]")]
    public class QPayCardController : PaymentReturnController
    {
        public QPayCardController(
            IPaymentGateway paymentGateway,
            PaymentHttpRequestMapper paymentHttpRequestMapper,
            ChurchReportPaymentProfileResolver paymentProfileResolver,
            IQPayReturnWorkflow qPayReturnWorkflow)
            : base(
                paymentGateway,
                paymentHttpRequestMapper,
                paymentProfileResolver,
                qPayReturnWorkflow)
        {
        }

        /// <summary>
        /// 舊永豐 QPay callback action 名稱。
        /// 這裡只轉呼叫中性的 <see cref="PaymentReturnController"/> 核心流程，不保留任何業務判斷。
        /// </summary>
        [HttpPost]
        [HttpGet]
        [Route("QPayReturnUrl")]
        public Task<IActionResult> QPayReturnUrl(string ShopNo, string PayToken)
        {
            return ReturnCore(ShopNo, PayToken, "QPayCardController.QPayReturnUrl");
        }
    }
}
