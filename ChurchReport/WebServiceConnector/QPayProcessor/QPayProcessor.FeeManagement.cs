using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 收費單管理模組
    /// 
    /// 【職責】
    /// - 建立收費單
    /// - 設定收費單參數
    /// - 更新收費單狀態
    /// - 處理 ATM 轉帳
    /// - 手動輸入奉獻
    /// 
    /// 【設計原則】
    /// - 單一職責：專注於收費單生命週期管理
    /// - 開放封閉：易於擴展新的付款方式
    /// </summary>
    public partial class QPayProcessor
    {
        #region ===== 建立收費單（主要入口）=====

        /// <summary>
        /// 非同步建立收費單並處理付款流程
        /// </summary>
        public async Task<string> CreateFeeAsync(Entity LineLoginContact, QpayModel QpayModel)
        {
            try
            {
                // 設定產品名稱
                QpayModel.FullName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                var orderDate = DateTime.Now.ToString("yyyyMMddhhmmssfff");

                // 根據付款方式路由到對應處理方法
                return QpayModel.PayWay switch
                {
                    "信用卡" or "銀聯卡" or null => await ProcessCreditCardPayment(LineLoginContact, QpayModel, orderDate),
                    "信用卡定期定額(每個月)" => await ProcessRecurringPayment(LineLoginContact, QpayModel, orderDate),
                    "行動支付" => await ProcessMobilePayment(LineLoginContact, QpayModel, orderDate),
                    "LinePay" => await ProcessLinePayPayment(LineLoginContact, QpayModel, orderDate),
                    "ATM轉帳/匯款" => await ProcessAtmPayment(LineLoginContact, QpayModel, orderDate),
                    _ => "不支援的付款方式!"
                };
            }
            catch (Exception ex)
            {
                var errorMsg = $"建立收費單失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] {errorMsg}\n{ex.StackTrace}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        #endregion

        #region ===== 建立收費單核心方法 =====

        /// <summary>
        /// 建立收費單實體
        /// </summary>
        public Guid CreateFee(Entity aContact, QpayModel QpayModel, bool KeyinMode)
        {
            try
            {
                var feeEntity = new Entity("new_fee");

                // 設定收費單參數
                var swSetParam = System.Diagnostics.Stopwatch.StartNew();
                SetFeeParameter(aContact, feeEntity, QpayModel, KeyinMode);
                swSetParam.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION]   CreateFee.SetFeeParameter = {swSetParam.ElapsedMilliseconds} ms");

                // 建立收費單
                var swCreate = System.Diagnostics.Stopwatch.StartNew();
                var feeId = ToolUtility.CreateEntity(feeEntity);
                swCreate.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION]   CreateFee.CreateEntity = {swCreate.ElapsedMilliseconds} ms");

                var swRetrieve = System.Diagnostics.Stopwatch.StartNew();
                var retrievedFee = ToolUtility.RetrieveEntity("new_fee", feeId);
                swRetrieve.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION]   CreateFee.RetrieveEntity = {swRetrieve.ElapsedMilliseconds} ms");

                // 指派負責人
                var swAssign = System.Diagnostics.Stopwatch.StartNew();
                AssignFeeOwner(retrievedFee, aContact);
                swAssign.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION]   CreateFee.AssignFeeOwner = {swAssign.ElapsedMilliseconds} ms");

                return feeId;
            }
            catch (Exception ex)
            {
                var errorMsg = $"建立收費單實體失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        /// <summary>
        /// 設定收費單參數
        /// </summary>
        public void SetFeeParameter(Entity aContact, Entity aFeeToCreated, QpayModel QpayModel, bool KeyinMode)
        {
            try
            {
                // 基本資訊
                var fullName = ToolUtility.GetEntityStringAttribute(ref aContact, "fullname") ?? "";
                ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_name", fullName + "奉獻");
                ToolUtility.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 金額設定
                SetFeeAmounts(ref aFeeToCreated, QpayModel, KeyinMode);

                // 付款資訊
                SetFeePaymentInfo(ref aFeeToCreated, QpayModel, KeyinMode);

                // 奉獻分類
                SetFeeCategoryInfo(ref aFeeToCreated, QpayModel);

                // 其他資訊
                SetFeeAdditionalInfo(ref aFeeToCreated, aContact, QpayModel);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"設定收費單參數失敗: {ex.Message}", ex);
            }
        }

        #endregion

        #region ===== 更新收費單 =====

        /// <summary>
        /// 更新收費單或認獻單
        /// </summary>
        public void UpdateFee(ref Entity aFeeToUpdate, string CardOrderNo, string OrderId, string AtmOrderNo, string AtmPayNo)
        {
            try
            {
                SetFeeUpdateParameter(aFeeToUpdate, CardOrderNo, OrderId, AtmOrderNo, AtmPayNo);
                ToolUtility.UpdateEntity(aFeeToUpdate);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"更新收費單失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 設定更新收費單所需的參數
        /// </summary>
        public void SetFeeUpdateParameter(Entity aFeeToCreated, string CardOrderNo, string OrderId, string AtmOrderNo, string AtmPayNo)
        {
            try
            {
                // 信用卡訂單編號
                if (!string.IsNullOrEmpty(CardOrderNo))
                {
                    ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_q_pay_card_order_no", CardOrderNo);

                    if (Configuration["PAY_PROVIDER"] == "高鉅金流")
                    {
                        ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_q_pay_order_number", OrderId);
                    }
                }

                // ATM 訂單編號
                if (!string.IsNullOrEmpty(AtmOrderNo))
                {
                    ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_q_pay_order_atm_no", AtmOrderNo);

                    var atmPayNumber = ToolUtility.GetEntityStringAttribute(aFeeToCreated, "new_atm_pay_number") +
                                      DateTime.Now.ToString() + " = " + AtmOrderNo + " : " + AtmPayNo + Environment.NewLine;
                    ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_atm_pay_number", atmPayNumber);
                    ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_atm_pay_no", AtmPayNo);
                    ToolUtility.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_atm_expire_date",
                        DateTime.Now.AddDays(10).ToLocalTime());
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"設定更新參數失敗: {ex.Message}", ex);
            }
        }

        #endregion

        #region ===== 手動輸入奉獻 =====

        /// <summary>
        /// 儲存手動輸入的奉獻資料
        /// </summary>
        public async Task<string> SaveKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                // [PERF-DEDICATION] temporary timing to locate the ~96s slow CRM round-trip. Remove after diagnosis.
                var swGetContact = System.Diagnostics.Stopwatch.StartNew();
                var contact = GetContact(QpayModel);
                swGetContact.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] GetContact elapsed = {swGetContact.ElapsedMilliseconds} ms");

                if (contact == null)
                {
                    return "錯誤:找不到會友!";
                }

                var swCreateFee = System.Diagnostics.Stopwatch.StartNew();
                var feeId = CreateFee(contact, QpayModel, true);
                swCreateFee.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] CreateFee elapsed = {swCreateFee.ElapsedMilliseconds} ms");

                // 發送 LINE 通知給奉獻者
                var swNotify = System.Diagnostics.Stopwatch.StartNew();
                await SendDedicationNotificationAsync(contact, QpayModel);
                swNotify.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] SendDedicationNotificationAsync elapsed = {swNotify.ElapsedMilliseconds} ms");

                return BuildSuccessMessage(contact, QpayModel);
            }
            catch (Exception ex)
            {
                var errorMsg = $"儲存手動奉獻失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        /// <summary>
        /// 發送奉獻確認 LINE 通知給奉獻者
        /// </summary>
        private async Task SendDedicationNotificationAsync(Entity contact, QpayModel qpayModel)
        {
            try
            {
                // 取得奉獻者的 LINE User ID
                var lineUserId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid");

                if (string.IsNullOrEmpty(lineUserId))
                {
                    System.Diagnostics.Trace.WriteLine($"[QPayProcessor] 會友沒有綁定 LINE，無法發送通知");
                    return;
                }

                // 建立奉獻確認訊息
                var message = BuildDedicationNotificationMessage(contact, qpayModel);

                // 加入 8 秒超時：LINE API 若無回應不應卡住上傳主流程
                var sendTask = m_PushUtility.SendMessage(lineUserId, message);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
                var completed = await Task.WhenAny(sendTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    System.Diagnostics.Trace.WriteLine($"[QPayProcessor] LINE 通知發送超時（8秒），略過通知繼續完成上傳");
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine($"[QPayProcessor] 已成功發送奉獻通知給 {qpayModel.FullName}");
                }
            }
            catch (Exception ex)
            {
                // 發送失敗不影響奉獻記錄，只記錄錯誤
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] 發送 LINE 通知失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立奉獻確認 LINE 訊息內容
        /// </summary>
        private string BuildDedicationNotificationMessage(Entity contact, QpayModel qpayModel)
        {
            var message = "🙏 奉獻確認通知\n" +
                         "━━━━━━━━━\n" +
                         $"✨ 感謝您的奉獻！\n\n" +
                         $"📅 日期：{qpayModel.DedicationDate:yyyy/MM/dd}\n" +
                         $"👤 姓名：{qpayModel.FullName}\n" +
                         $"🏷️ 類別：{qpayModel.Category}\n";

            // 如果有其他類別說明
            if (!string.IsNullOrEmpty(qpayModel.Others))
            {
                message += $"📝 其他類別：{qpayModel.Others}\n";
            }

            message += $"💰 金額：NT$ {qpayModel.Amount:N0}\n" +
                      $"💳 方式：{qpayModel.PayWay}\n";

            // 如果有奉獻地點
            if (!string.IsNullOrEmpty(qpayModel.DedicateLocation))
            {
                message += $"📍 地點：{qpayModel.DedicateLocation}\n";
            }

            // 如果有備註
            if (!string.IsNullOrEmpty(qpayModel.Explain))
            {
                message += $"\n💬 備註：{qpayModel.Explain}\n";
            }

            message += "\n━━━━━━━━━\n" +
                      "願神賜福與您！\n" +
                      "您的奉獻已完成登記";

            return message;
        }

        #endregion

        #region ===== 私有輔助方法 =====

        /// <summary>
        /// 設定收費單金額
        /// </summary>
        private void SetFeeAmounts(ref Entity aFeeToCreated, QpayModel QpayModel, bool KeyinMode)
        {
            // 應收金額
            ToolUtility.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", new Money(QpayModel.Amount));

            // 實收金額（根據付款方式和輸入模式決定）
            var reallyPaidAmount = ShouldSetFullAmount(QpayModel.PayWay, KeyinMode) ? QpayModel.Amount : 0;
            ToolUtility.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(reallyPaidAmount));

            // 大寫金額
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number",
                MoneyToChinese(QpayModel.Amount.ToString()));
        }

        /// <summary>
        /// 判斷是否應該設定足額實收
        /// </summary>
        private bool ShouldSetFullAmount(string payWay, bool keyinMode)
        {
            return payWay switch
            {
                "現金" or "銀行轉帳" => true,
                "信用卡" when keyinMode => true,
                _ => false
            };
        }

        /// <summary>
        /// 設定收費單付款資訊
        /// </summary>
        private void SetFeePaymentInfo(ref Entity aFeeToCreated, QpayModel QpayModel, bool KeyinMode)
        {
            // 付款方式
            SetPayMethod(QpayModel.PayWay, ref aFeeToCreated);

            // 付款狀態
            var payStatus = DeterminePayStatus(QpayModel.PayWay, KeyinMode);
            SetPayStatus(payStatus, ref aFeeToCreated);

            // 帳戶後六碼
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_last_six_digit", QpayModel.LastSixDigit);

            // 收費日期
            ToolUtility.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", QpayModel.DedicationDate.ToLocalTime());
        }

        /// <summary>
        /// 判斷付款狀態
        /// </summary>
        private string DeterminePayStatus(string payWay, bool keyinMode)
        {
            return payWay switch
            {
                "現金" => "現金已繳費",
                "銀行轉帳" => "銀行轉帳已繳費",
                "信用卡" when keyinMode => "信用卡已繳費",
                _ => "新建立"
            };
        }

        /// <summary>
        /// 設定收費單分類資訊
        /// </summary>
        private void SetFeeCategoryInfo(ref Entity aFeeToCreated, QpayModel QpayModel)
        {
            // 奉獻類別
            SetFeePayCategory(QpayModel.Category, ref aFeeToCreated);

            // 奉獻其他類別
            if (QpayModel.Others != "" && QpayModel.Others != null)
            {
                ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_others", QpayModel.Others);
            }

            // 收入類別
            SetIncomeCategory(QpayModel.Category, ref aFeeToCreated);
        }

        /// <summary>
        /// 設定收費單額外資訊
        /// </summary>
        private void SetFeeAdditionalInfo(ref Entity aFeeToCreated, Entity aContact, QpayModel QpayModel)
        {
            // 設定輸入奉獻人員
            if (m_LoginContact != null)
            {
                ToolUtility.SetEntityLookUpAttribute(ref aFeeToCreated, "new_keyin_contact_new_fee", "contact", m_LoginContact.Id);
            }

            // 奉獻地點
            var dedicateLocation = QpayModel.DedicateLocation
                ?? ToolUtility.GetEntityLookupDisplayName(ref aContact, "parentcustomerid");
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", dedicateLocation);

            // 奉獻備註
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_explain", QpayModel.Explain);

            // 週報專用備註
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_weekly_note", QpayModel.WeeklyNote);
        }

        /// <summary>
        /// 指派收費單負責人
        /// </summary>
        private void AssignFeeOwner(Entity retrievedFee, Entity aContact)
        {
            if (retrievedFee != null && aContact != null)
            {
                try
                {
                    ToolUtility.AssignOwner("new_fee", retrievedFee, ToolUtility.GetOwnerId(aContact));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[QPayProcessor] 指派負責人失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 建立成功訊息
        /// </summary>
        private string BuildSuccessMessage(Entity aContact, QpayModel QpayModel)
        {
            return "上傳成功<br/>" +
                   "--------------------<br/>" +
                   $"日期    : {QpayModel.DedicationDate.ToShortDateString()}<br/>" +
                   $"姓名    : {QpayModel.FullName}<br/>" +
                   $"奉獻編號: {ToolUtility.GetEntityStringAttribute(ref aContact, "pager")}<br/>" +
                   $"身分證字號: {ToolUtility.GetEntityStringAttribute(ref aContact, "new_personal_id")}<br/>" +
                   $"電話    : {QpayModel.Mobile}<br/>" +
                   $"類別    : {QpayModel.Category}<br/>" +
                   $"奉獻地點: {QpayModel.DedicateLocation}<br/>" +
                   $"付款方式: {QpayModel.PayWay}<br/>" +
                   $"金額    : {QpayModel.Amount}<br/>" +
                   $"備註    : {QpayModel.Explain}<br/>";
        }

        #endregion
    }
}
