using Microsoft.AspNetCore.Mvc;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;
using Line.Messaging;
using ChurchReport.Tools;

namespace ChurchReport.文件
{
    /// <summary>
    /// 高鉅金流 PayPage 回傳處理控制器
    /// - 接收金流回傳(MyPayReturn)
    /// - 顯示成功結果(success)
    /// - 顯示失敗結果(failure)
    /// 整理為清晰區塊，補充說明註解。
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        #region 常數定義
        private const string LINE_CHANNEL_ACCESS_TOKEN = @"g1jtWWNkjbH3OCh1cKoRvPBUkCJIygNuvV/neHXR9I4J5GBgVE85inaIaTcT4AAZ1qCuqrqJXDawrUweyBqLcX97GGokXnTRQ6MxjXAutd5Yr2FkPsZnq6kMelc/C+mqNUHaVUKFAuvTD8JvXbNmpAdB04t89/1O/w1cDnyilFU="; // 用於 LINE 推播
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365"; // CRM連線名稱
        private const int PAYMENT_STATUS_PAID =100000001; // new_pay_status: 信用卡已繳費
        private const int PAYMENT_METHOD_CREDIT_CARD =100000001; // new_pay_way: 信用卡
        #endregion

        private readonly ILogger<MyPayController> _logger;

        public MyPayController(ILogger<MyPayController> logger)
        {
            _logger = logger;
        }

        #region API: MyPay 回傳
        /// <summary>
        /// 金流伺服器回呼端點。處理後需回傳字串8888代表已接收。
        /// 處理『交易完成回傳資訊』、『非即時交易回傳資訊』、『訂單確認回傳資訊`
        /// </summary>
        [HttpPost("MyPayNotify")]
        public async Task<IActionResult> PaymentNotify([FromForm] MyPayReturnModel returnModel)
        {
            _logger.LogInformation($"[MyPay回傳] 收到高鉅金流回傳，OrderID: {returnModel?.order_id}, UID: {returnModel?.uid}, PRC: {returnModel?.prc}");
            
            ToolUtilityClass utility = null;
            try
            {
                // 1. 基本檢查
                if (returnModel == null)
                {
                    _logger.LogWarning("[MyPay回傳] 回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                // 2. 記錄完整的回傳資訊
                LogFullReturnData(returnModel);

                // 3. 驗證必要欄位
                var validation = returnModel.ValidateAllFields();
                if (!validation.IsValid)
                {
                    _logger.LogWarning($"[MyPay回傳] 資料驗證失敗: {string.Join(", ", validation.Errors)}");
                    // 仍回傳8888避免金流平台重送
                    return Ok("8888");
                }

                // 4. 解析交易狀態
                bool isSuccess = IsSuccessfulPaymentStatus(returnModel.prc);
                _logger.LogInformation($"[MyPay回傳] 交易狀態判定: PRC={returnModel.prc}, IsSuccess={isSuccess}");

                // 5. 查詢收費單
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", returnModel.order_id);
                
                if (feeEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] 找不到對應收費單 - OrderId: {returnModel.order_id}");
                    return Ok("8888"); // 仍回傳成功避免重送
                }

                _logger.LogInformation($"[MyPay回傳] 找到收費單 - FeeId: {feeEntity.Id}");

                // 6. 判斷收費單類型（奉獻 vs 課程）
                FeeType feeType = DetermineFeeType(utility, feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單類型: {feeType}");

                // 7. 取得連絡人資訊
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                Entity contactEntity = null;
                string fullName = "會友";
                string lineId = null;

                if (contactId != Guid.Empty)
                {
                    contactEntity = utility.RetrieveEntity("contact", contactId);
                    if (contactEntity != null)
                    {
                        fullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                        lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                        _logger.LogInformation($"[MyPay回傳] 連絡人: {fullName}, LINE ID: {!string.IsNullOrEmpty(lineId)}");
                    }
                }

                // 8. 更新收費單
                UpdateFeeEntityWithMyPayReturn(utility, feeEntity, returnModel, isSuccess);
                utility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單已更新 - FeeId: {feeEntity.Id}");

                // 9. 發送LINE通知（僅成功時）
                if (isSuccess && !string.IsNullOrWhiteSpace(lineId))
                {
                    try
                    {
                        SendLineNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
                        _logger.LogInformation($"[MyPay回傳] LINE通知已發送 - OrderId: {returnModel.order_id}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {returnModel.order_id}");
                        // 不中斷主流程
                    }
                }

                // 10. 回傳8888確認接收
                _logger.LogInformation($"[MyPay回傳] 處理完成 - OrderId: {returnModel.order_id}");
                return Ok("8888");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 處理異常 - OrderId: {returnModel?.order_id}");
                // 發生異常仍回傳8888避免無限重送
                return Ok("8888");
            }
            finally
            {
                utility?.Dispose();
            }
        }
        #endregion

        #region 輔助方法

        /// <summary>
        /// 記錄完整的回傳資料（用於除錯）
        /// </summary>
        private void LogFullReturnData(MyPayReturnModel model)
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

        /// <summary>
        /// 更新收費單欄位（支援所有高鉅金流回傳參數）
        /// </summary>
        private void UpdateFeeEntityWithMyPayReturn(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            MyPayReturnModel model,
            bool isSuccess)
        {
            try
            {
                // 解析交易時間
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                if (isSuccess)
                {
                    // 成功：更新為已繳費狀態
                    var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                    toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);
                }

                // 組合完整的備註資訊
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
                var paymentMethodName = GetPaymentMethodName(model.pfn);
                var statusMessage = GetPaymentStatusMessage(model.prc);

                var newDescription = originalDescription + Environment.NewLine +
                    $"[高鉅金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    $"====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號(order_id): {model.order_id}" + Environment.NewLine +
                    $"交易流水號(uid): {model.uid}" + Environment.NewLine +
                    $"交易驗證碼(key): {model.key}" + Environment.NewLine +
                    $"交易狀態碼(prc): {model.prc} ({statusMessage})" + Environment.NewLine +
                    $"====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {paymentTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {model.cost}" + Environment.NewLine +
                    $"實際金額: {model.actual_cost ?? model.cost}" + Environment.NewLine +
                    $"交易幣別: {model.currency ?? "TWD"}" + Environment.NewLine +
                    $"====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {paymentMethodName}" + Environment.NewLine +
                    $"卡號: {model.cardno}" + Environment.NewLine +
                    $"授權碼: {model.acode}" + Environment.NewLine +
                    $"卡別: {model.card_type}" + Environment.NewLine +
                    $"發卡行: {model.issuing_bank}" + Environment.NewLine +
                    $"發卡行代碼: {model.issuing_bank_uid}" + Environment.NewLine;

                // 如果有分期資訊
                if (!string.IsNullOrEmpty(model.installment))
                {
                    newDescription += $"分期資訊: {model.installment}" + Environment.NewLine;
                }

                // 如果有紅利資訊
                if (!string.IsNullOrEmpty(model.redeem))
                {
                    newDescription += $"紅利資訊: {model.redeem}" + Environment.NewLine;
                }

                // 服務商資訊
                if (!string.IsNullOrEmpty(model.supplier_name))
                {
                    newDescription += $"====== 服務商資訊 ======" + Environment.NewLine +
                                    $"服務商: {model.supplier_name}" + Environment.NewLine +
                                    $"服務商代碼: {model.supplier_code}" + Environment.NewLine;
                }

                // 定期定額資訊
                if (!string.IsNullOrEmpty(model.payment_name))
                {
                    newDescription += $"====== 定期定額資訊 ======" + Environment.NewLine +
                                    $"扣款名稱: {model.payment_name}" + Environment.NewLine +
                                    $"期數: {model.nois}" + Environment.NewLine +
                                    $"群組編號: {model.group_id}" + Environment.NewLine;
                }

                // 虛擬帳號/超商代碼資訊
                if (!string.IsNullOrEmpty(model.bank_id))
                {
                    newDescription += $"====== 虛擬帳號資訊 ======" + Environment.NewLine +
                                    $"銀行代碼: {model.bank_id}" + Environment.NewLine +
                                    $"有效期限: {model.expired_date}" + Environment.NewLine;
                }

                // 自訂參數
                if (!string.IsNullOrEmpty(model.echo_0) || !string.IsNullOrEmpty(model.echo_1))
                {
                    newDescription += $"====== 自訂參數 ======" + Environment.NewLine +
                                    $"echo_0: {model.echo_0}" + Environment.NewLine +
                                    $"echo_1: {model.echo_1}" + Environment.NewLine +
                                    $"echo_2: {model.echo_2}" + Environment.NewLine +
                                    $"echo_3: {model.echo_3}" + Environment.NewLine +
                                    $"echo_4: {model.echo_4}" + Environment.NewLine;
                }

                // 舊版欄位（向下相容）
                newDescription += $"====== 舊版相容欄位 ======" + Environment.NewLine +
                                $"state: {model.state}" + Environment.NewLine +
                                $"msg: {model.msg}" + Environment.NewLine +
                                $"transaction_id: {model.transaction_id}" + Environment.NewLine +
                                $"store_uid: {model.store_uid}" + Environment.NewLine;

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
        /// 根據收費單類型發送不同的LINE通知
        /// </summary>
        private void SendLineNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            MyPayReturnModel model,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] ContactEntity為空，無法發送LINE通知");
                    return;
                }

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    _logger.LogWarning($"[MyPay回傳] LINE ID為空");
                    return;
                }

                // 解析金額
                decimal amount = 0m;
                if (!string.IsNullOrEmpty(model.actual_cost) && decimal.TryParse(model.actual_cost, out var actualCost))
                {
                    amount = actualCost;
                }
                else if (!string.IsNullOrEmpty(model.cost) && decimal.TryParse(model.cost, out var cost))
                {
                    amount = cost;
                }

                // 解析時間
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                string message;
                if (feeType == FeeType.Dedication)
                {
                    // 奉獻類型
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);
                    message = BuildDedicationSuccessMessage(fullName, model.order_id, model.uid, amount, dedicationCategory, paymentTime);
                }
                else if (feeType == FeeType.Course)
                {
                    // 課程類型
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                    message = BuildCoursePaymentSuccessMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime);
                }
                else
                {
                    // 其他類型
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentSuccessMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime);
                }

                // 發送LINE訊息
                SendLineMessage(lineId, message);
                _logger.LogInformation($"[MyPay回傳] LINE通知已發送 - LineId: {lineId}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        #endregion

        #region 收費單類型與狀態判斷

        /// <summary>
        /// 收費單類型枚舉
        /// </summary>
        private enum FeeType { Dedication, Course, Other }

        /// <summary>
        /// 判斷是否為成功的交易狀態
        /// </summary>
        private bool IsSuccessfulPaymentStatus(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return false;
            switch (prc)
            {
                case "250": // 付款成功
                case "290": // 交易成功但資訊不符
                case "600": // 結帳完成
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 取得交易狀態訊息
        /// </summary>
        private string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return "付款狀態未知";
            switch (prc)
            {
                case "100": return "資料錯誤";
                case "200": return "資料正確，處理中";
                case "220": return "取消成功";
                case "230": return "退款成功";
                case "250": return "付款成功";
                case "260": return "交易成功，尚未付款完成";
                case "265": return "訂單綁定";
                case "270": return "交易成功，尚未付款完成（虛擬帳號）";
                case "275": return "交易成功，待審核（核貸中）";
                case "280": return "交易成功，尚未付款完成（儲值/WEBATM）";
                case "290": return "交易成功但資訊不符";
                case "300": return "交易失敗";
                case "380": return "逾期交易";
                case "400": return "系統錯誤";
                case "600": return "結帳完成";
                case "A0001": return "交易待確認";
                case "A0002": return "放棄交易";
                case "B200": return "執行成功";
                case "B500": return "執行失敗";
                default: return $"未知狀態碼：{prc}";
            }
        }

        /// <summary>
        /// 解析交易完成時間
        /// </summary>
        private DateTime ParseFinishTime(string finishtime)
        {
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length != 14) return DateTime.Now;
            try
            {
                int year = int.Parse(finishtime.Substring(0, 4));
                int month = int.Parse(finishtime.Substring(4, 2));
                int day = int.Parse(finishtime.Substring(6, 2));
                int hour = int.Parse(finishtime.Substring(8, 2));
                int minute = int.Parse(finishtime.Substring(10, 2));
                int second = int.Parse(finishtime.Substring(12, 2));
                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ParseFinishTime:解析時間失敗 - FinishTime: {finishtime}");
                return DateTime.Now;
            }
        }

        /// <summary>
        /// 取得付款方式名稱
        /// </summary>
        private string GetPaymentMethodName(string pfn)
        {
            if (string.IsNullOrWhiteSpace(pfn)) return "未知";
            switch (pfn)
            {
                case "0":
                case "all": return "全部支付工具";
                case "1":
                case "CREDITCARD": return "信用卡";
                case "3":
                case "CSTORECODE": return "超商代碼";
                case "6":
                case "E_COLLECTION": return "虛擬帳號";
                case "10":
                case "ALIPAY": return "支付寶";
                case "13":
                case "WECHAT": return "微信支付";
                case "15":
                case "LINEPAYON": return "LINE Pay";
                case "20":
                case "APPLEPAY": return "Apple Pay";
                case "21":
                case "GOOGLEPAY": return "Google Pay";
                case "24":
                case "C_REDEEM": return "信用卡紅利";
                case "27":
                case "PION": return "Pi 拍錢包";
                case "31":
                case "JKOON": return "街口支付";
                default: return $"付費方法 {pfn}";
            }
        }

        /// <summary>
        /// 判斷收費單類型
        /// </summary>
        private FeeType DetermineFeeType(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    _logger.LogInformation($"DetermineFeeType: 有課程 ID => 課程繳費");
                    return FeeType.Course;
                }

                string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? string.Empty;
                if (feeName.Contains("課程") || feeName.Contains("報名") || feeName.Contains("學費") || 
                    feeName.Contains("培訓") || feeName.Contains("研習"))
                {
                    _logger.LogInformation($"DetermineFeeType: 名稱含課程關鍵字 => 課程繳費");
                    return FeeType.Course;
                }

                string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseName))
                {
                    _logger.LogInformation($"DetermineFeeType: 有課程名稱欄位 => 課程繳費");
                    return FeeType.Course;
                }

                int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                if (categoryValue >= 100000000 && categoryValue <= 100000019)
                {
                    _logger.LogInformation($"DetermineFeeType: 類別屬奉獻 => 奉獻");
                    return FeeType.Dedication;
                }

                _logger.LogInformation($"DetermineFeeType: 預設 => 奉獻");
                return FeeType.Dedication;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DetermineFeeType例外，預設奉獻");
                return FeeType.Dedication;
            }
        }

        /// <summary>
        /// 取得課程名稱
        /// </summary>
        private string GetCourseName(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    var courseEntity = utility.RetrieveEntity("new_course", courseId);
                    if (courseEntity != null)
                    {
                        var name = utility.GetEntityStringAttribute(courseEntity, "new_name");
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }

                var courseNameField = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseNameField)) return courseNameField;

                return utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "課程";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCourseName例外");
                return "課程";
            }
        }

        /// <summary>
        /// 取得奉獻類別名稱
        /// </summary>
        private string GetDedicationCategoryName(int categoryValue)
        {
            switch (categoryValue)
            {
                case 100000010: return "主日奉獻";
                case 100000000: return "十一奉獻";
                case 100000002: return "感恩奉獻";
                case 100000006: return "建堂奉獻";
                case 100000007: return "宣教奉獻";
                case 100000019: return "愛心奉獻";
                case 100000008: return "特別獻金";
                default: return "奉獻";
            }
        }

        #endregion

        #region LINE 訊息建立

        /// <summary>
        /// 建立奉獻成功訊息
        /// </summary>
        private string BuildDedicationSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string dedicationCategory, DateTime paymentTime)
        {
            var msg = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}" +
                      $"付款資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"奉獻類別：{dedicationCategory}{Environment.NewLine}" +
                      $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}願上帝賜福與您！";
            return msg;
        }

        /// <summary>
        /// 建立課程繳費成功訊息
        /// </summary>
        private string BuildCoursePaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string courseName, string courseSchedule, string courseLocation, DateTime paymentTime)
        {
            var msg = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的課程繳費已成功完成！{Environment.NewLine}{Environment.NewLine}" +
                      $"課程資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"課程名稱：{courseName}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseSchedule)) msg += $"上課時間：{courseSchedule}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseLocation)) msg += $"上課地點：{courseLocation}{Environment.NewLine}";
            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}" +
                   $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"繳費金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}期待在課程中與您相見！";
            return msg;
        }

        /// <summary>
        /// 建立一般繳費成功訊息
        /// </summary>
        private string BuildGeneralPaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string itemName, DateTime paymentTime)
        {
            var msg = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的付款已成功完成！{Environment.NewLine}{Environment.NewLine}" +
                      $"付款資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"項目：{itemName}{Environment.NewLine}" +
                      $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}感謝您的支持！";
            return msg;
        }

        /// <summary>
        /// 發送LINE訊息
        /// </summary>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
                var pushUtility = new PushUtility(lineMessagingClient);
                pushUtility.SendMessage(lineId, message).Wait();
                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}");
                throw;
            }
        }

        #endregion
    }
}