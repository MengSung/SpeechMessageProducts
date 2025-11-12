using System;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay CRM 資料更新服務
    /// 負責更新 Dynamics 365 CRM 中的收費單資訊
    /// </summary>
    public class MyPayCrmService
    {
        private readonly ILogger<MyPayCrmService> _logger;
        private readonly MyPayStatusHelper _statusHelper;
        private const int PAYMENT_STATUS_PAID = 100000001;
        private const int PAYMENT_METHOD_CREDIT_CARD = 100000001;

        public MyPayCrmService(ILogger<MyPayCrmService> logger, MyPayStatusHelper statusHelper)
        {
            _logger = logger;
            _statusHelper = statusHelper;
        }

        /// <summary>
        /// ========================================
        /// 更新 CRM 收費單（使用 MyPayReturnModel）
        /// ========================================
        /// 
        /// 【更新內容】
        /// 
        /// ? 成功交易更新項目：
        /// - new_pay_status: 設為「已繳費」（100000001）
        /// - new_fee_really_paid: 實付金額
        /// - new_difference_fee_paid: 差額（設為 0）
        /// - new_pay_date: 付款日期時間
        /// - new_pay_way: 付款方式（信用卡 = 100000001）
        /// 
        /// ? 成功與失敗都更新的項目：
        /// - new_description: 附加交易明細
        /// </summary>
        public void UpdateFeeEntityWithMyPayReturn(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            MyPayReturnModel model,
            bool isSuccess)
        {
            try
            {
                // 步驟 1：解析付款時間
                DateTime paymentTime = _statusHelper.ParseFinishTime(model.finishtime);

                // 步驟 2：如果交易成功，更新付款狀態相關欄位
                if (isSuccess)
                {
                    var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                    toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);
                }

                // 步驟 3：準備描述欄位資料
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
                var paymentMethodName = _statusHelper.GetPaymentMethodName(model.pfn);
                var statusMessage = _statusHelper.GetPaymentStatusMessage(model.prc);

                // 步驟 4：建立新的描述內容
                var newDescription = originalDescription + Environment.NewLine +
                    $"[金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    "====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號(order_id): {model.order_id}" + Environment.NewLine +
                    $"交易流水號(uid): {model.uid}" + Environment.NewLine +
                    $"交易驗證碼(key): {model.key}" + Environment.NewLine +
                    $"交易狀態碼(prc): {model.prc} ({statusMessage})" + Environment.NewLine +
                    "====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {paymentTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {model.cost}" + Environment.NewLine +
                    $"實際金額: {model.actual_cost ?? model.cost}" + Environment.NewLine +
                    $"交易幣別: {model.currency ?? "TWD"}" + Environment.NewLine +
                    "====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {paymentMethodName}" + Environment.NewLine +
                    $"卡號: {model.cardno}" + Environment.NewLine +
                    $"授權碼: {model.acode}" + Environment.NewLine +
                    $"卡別: {model.card_type}" + Environment.NewLine +
                    $"發卡行: {model.issuing_bank}" + Environment.NewLine +
                    $"發卡行代碼: {model.issuing_bank_uid}" + Environment.NewLine;

                // 步驟 5：附加選填資訊
                if (!string.IsNullOrEmpty(model.installment))
                    newDescription += $"分期資訊: {model.installment}" + Environment.NewLine;

                if (!string.IsNullOrEmpty(model.redeem))
                    newDescription += $"紅利資訊: {model.redeem}" + Environment.NewLine;

                if (!string.IsNullOrEmpty(model.supplier_name))
                {
                    newDescription += "====== 服務商資訊 ======" + Environment.NewLine +
                                      $"服務商: {model.supplier_name}" + Environment.NewLine +
                                      $"服務商代碼: {model.supplier_code}" + Environment.NewLine;
                }

                if (!string.IsNullOrEmpty(model.payment_name) ||
                    !string.IsNullOrEmpty(model.nois) ||
                    !string.IsNullOrEmpty(model.group_id))
                {
                    newDescription += "====== 定期定額資訊 ======" + Environment.NewLine +
                                      $"扣款名稱: {model.payment_name}" + Environment.NewLine +
                                      $"期數: {model.nois}" + Environment.NewLine +
                                      $"群組編號: {model.group_id}" + Environment.NewLine;
                }

                if (!string.IsNullOrEmpty(model.bank_id) ||
                    !string.IsNullOrEmpty(model.expired_date))
                {
                    newDescription += "====== 虛擬帳號資訊 ======" + Environment.NewLine +
                                      $"銀行代碼: {model.bank_id}" + Environment.NewLine +
                                      $"有效期限: {model.expired_date}" + Environment.NewLine;
                }

                if (!string.IsNullOrEmpty(model.echo_0) ||
                    !string.IsNullOrEmpty(model.echo_1) ||
                    !string.IsNullOrEmpty(model.echo_2) ||
                    !string.IsNullOrEmpty(model.echo_3) ||
                    !string.IsNullOrEmpty(model.echo_4))
                {
                    newDescription += "====== 自訂參數 ======" + Environment.NewLine +
                                      $"echo_0: {model.echo_0}" + Environment.NewLine +
                                      $"echo_1: {model.echo_1}" + Environment.NewLine +
                                      $"echo_2: {model.echo_2}" + Environment.NewLine +
                                      $"echo_3: {model.echo_3}" + Environment.NewLine +
                                      $"echo_4: {model.echo_4}" + Environment.NewLine;
                }

                newDescription += "====== 舊版相容欄位 ======" + Environment.NewLine +
                                  $"state: {model.state}" + Environment.NewLine +
                                  $"msg: {model.msg}" + Environment.NewLine +
                                  $"transaction_id: {model.transaction_id}" + Environment.NewLine +
                                  $"store_uid: {model.store_uid}" + Environment.NewLine +
                                  $"hash: {model.hash}" + Environment.NewLine;

                // 步驟 6：更新描述欄位
                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);

                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        /// <summary>
        /// ========================================
        /// 更新 CRM 收費單（使用個別參數，舊版相容）
        /// ========================================
        /// </summary>
        public void UpdateFeeEntityForSuccessWithMyPay(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            string orderId,
            string uid,
            string key,
            string cost,
            string actualCost,
            string prc,
            string pfn,
            DateTime paymentTime,
            string cardno,
            string acode)
        {
            try
            {
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

                DateTime transTime = _statusHelper.ParseFinishTime(paymentTime.ToString("yyyyMMddHHmmss"));
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;

                var newDescription = originalDescription + Environment.NewLine +
                    $"[金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    "====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號(order_id): {orderId}" + Environment.NewLine +
                    $"交易流水號(uid): {uid}" + Environment.NewLine +
                    $"交易驗證碼(key): {key}" + Environment.NewLine +
                    $"交易狀態碼(prc): {prc} ({_statusHelper.GetPaymentStatusMessage(prc)})" + Environment.NewLine +
                    "====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {transTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {cost}" + Environment.NewLine +
                    $"實際金額: {actualCost ?? cost}" + Environment.NewLine +
                    $"交易幣別: TWD" + Environment.NewLine +
                    "====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {pfn}" + Environment.NewLine +
                    $"卡號: {cardno}" + Environment.NewLine +
                    $"授權碼: {acode}" + Environment.NewLine;

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {orderId}");
                throw;
            }
        }
    }
}
