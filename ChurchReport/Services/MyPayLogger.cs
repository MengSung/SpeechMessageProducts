using System;
using Microsoft.Extensions.Logging;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay 日誌記錄服務
    /// 負責記錄金流回傳資料
    /// </summary>
    public class MyPayLogger
    {
        private readonly ILogger<MyPayLogger> _logger;

        public MyPayLogger(ILogger<MyPayLogger> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// ========================================
        /// 記錄完整的金流回傳資料
        /// ========================================
        /// 
        /// 【記錄內容分類】
        /// 1. 核心欄位：uid, key, prc, order_id
        /// 2. 交易資訊：finishtime, cost, actual_cost
        /// 3. 付款資訊：pfn, cardno, acode
        /// 4. 消費者資訊：user_id
        /// 5. 自訂參數：echo_0~2
        /// 6. 舊版欄位：state, msg, transaction_id
        /// </summary>
        public void LogFullReturnData(MyPayReturnModel model)
        {
            try
            {
                var logData = $"[MyPay完整回傳資料]\n" +
                             $"核心欄位: uid={model.uid}, key={model.key}, prc={model.prc}, order_id={model.order_id}\n" +
                             $"交易資訊: finishtime={model.finishtime}, cost={model.cost}, actual_cost={model.actual_cost}\n" +
                             $"付款資訊: pfn={model.pfn}, cardno={model.cardno}, acode={model.acode}\n" +
                             $"消費者: user_id={model.user_id}\n" +
                             $"自訂參數: echo_0={model.echo_0}, echo_1={model.echo_1}, echo_2={model.echo_2}\n" +
                             $"舊版欄位: state={model.state}, msg={model.msg}, transaction_id={model.transaction_id}";

                _logger.LogInformation(logData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MyPay回傳] 記錄回傳資料時發生錯誤");
            }
        }
    }
}
