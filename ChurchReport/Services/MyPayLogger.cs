using System;
using Microsoft.Extensions.Logging;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Services
{
    public class MyPayLogger
    {
        private readonly ILogger<MyPayLogger> _logger;

        public MyPayLogger(ILogger<MyPayLogger> logger)
        {
            _logger = logger;
        }

        public void LogPaymentCallbackResult(PaymentCallbackResult result)
        {
            try
            {
                _logger.LogInformation(
                    "[MyPay回傳] Core callback result: OrderId={OrderId}, TransactionId={TransactionId}, Status={Status}, Error={ErrorKind}, Amount={Amount}",
                    result.ProductOrderId,
                    result.ProviderTransactionId,
                    result.Status,
                    result.Error.Kind,
                    result.Amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MyPay回傳] 記錄 core callback result 時發生錯誤");
            }
        }
    }
}
