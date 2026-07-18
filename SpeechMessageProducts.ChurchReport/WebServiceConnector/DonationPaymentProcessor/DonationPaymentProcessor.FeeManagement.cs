// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentProcessor
// 主要成員：CreateFeeAsync、CreateFee、SetFeeParameter、UpdateFee、SetFeeUpdateParameter、SaveKeyInDedication、GetContactForKeyIn、SendDedicationNotificationAsync、ResolveDedicationNotificationLineId、BuildDedicationNotificationLineRetryKey
// 引用命名空間：ChurchReport.Models、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
    public partial class DonationPaymentProcessor
    {
        #region ===== 建立收費單（主要入口）=====

        /// <summary>
        /// 非同步建立收費單並處理付款流程
        /// </summary>
        public async Task<string> CreateFeeAsync(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                // 設定產品名稱
                DonationPaymentFormModel.FullName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname");
                var orderDate = DateTime.Now.ToString("yyyyMMddhhmmssfff");

                // 根據付款方式路由到對應處理方法
                return DonationPaymentFormModel.PayWay switch
                {
                    "信用卡" or "銀聯卡" or null => await ProcessCreditCardPayment(LineLoginContact, DonationPaymentFormModel, orderDate),
                    "信用卡定期定額(每個月)" => await ProcessRecurringPayment(LineLoginContact, DonationPaymentFormModel, orderDate),
                    "行動支付" => await ProcessMobilePayment(LineLoginContact, DonationPaymentFormModel, orderDate),
                    "LinePay" => await ProcessLinePayPayment(LineLoginContact, DonationPaymentFormModel, orderDate),
                    "ATM轉帳/匯款" => await ProcessAtmPayment(LineLoginContact, DonationPaymentFormModel, orderDate),
                    _ => "不支援的付款方式!"
                };
            }
            catch (Exception ex)
            {
                var errorMsg = $"建立收費單失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] {errorMsg}\n{ex.StackTrace}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        #endregion

        #region ===== 建立收費單核心方法 =====

        /// <summary>
        /// 建立收費單實體
        /// </summary>
        public Guid CreateFee(Entity aContact, DonationPaymentFormModel DonationPaymentFormModel, bool KeyinMode)
        {
            try
            {
                var feeEntity = new Entity("new_fee");

                // 設定收費單參數
                var swSetParam = System.Diagnostics.Stopwatch.StartNew();
                SetFeeParameter(aContact, feeEntity, DonationPaymentFormModel, KeyinMode);
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
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        /// <summary>
        /// 設定收費單參數
        /// </summary>
        public void SetFeeParameter(Entity aContact, Entity aFeeToCreated, DonationPaymentFormModel DonationPaymentFormModel, bool KeyinMode)
        {
            try
            {
                // 基本資訊
                var fullName = ToolUtility.GetEntityStringAttribute(ref aContact, "fullname") ?? "";
                ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_name", fullName + "奉獻");
                ToolUtility.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 金額設定
                SetFeeAmounts(ref aFeeToCreated, DonationPaymentFormModel, KeyinMode);

                // 付款資訊
                SetFeePaymentInfo(ref aFeeToCreated, DonationPaymentFormModel, KeyinMode);

                // 奉獻分類
                SetFeeCategoryInfo(ref aFeeToCreated, DonationPaymentFormModel);

                // 其他資訊
                SetFeeAdditionalInfo(ref aFeeToCreated, aContact, DonationPaymentFormModel);
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
        public async Task<string> SaveKeyInDedication(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                // [PERF-DEDICATION] temporary timing to locate the ~96s slow CRM round-trip. Remove after diagnosis.
                var swGetContact = System.Diagnostics.Stopwatch.StartNew();
                var contact = GetContactForKeyIn(DonationPaymentFormModel);
                swGetContact.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] GetContact elapsed = {swGetContact.ElapsedMilliseconds} ms");

                if (contact == null)
                {
                    return "錯誤:找不到會友!";
                }

                var swCreateFee = System.Diagnostics.Stopwatch.StartNew();
                var feeId = CreateFee(contact, DonationPaymentFormModel, true);
                swCreateFee.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] CreateFee elapsed = {swCreateFee.ElapsedMilliseconds} ms");

                // 發送 LINE 通知給奉獻者
                var swNotify = System.Diagnostics.Stopwatch.StartNew();
                var lineNotificationResult = await SendDedicationNotificationAsync(contact, DonationPaymentFormModel, feeId);
                swNotify.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] SendDedicationNotificationAsync elapsed = {swNotify.ElapsedMilliseconds} ms");

                // 手動輸入奉獻的主流程仍以「收費單已建立」為準；
                // LINE 發送結果只附加在成功訊息後方，讓同工立即知道通知是否真的送出。
                return BuildSuccessMessage(contact, DonationPaymentFormModel) + lineNotificationResult;
            }
            catch (Exception ex)
            {
                var errorMsg = $"儲存手動奉獻失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        /// <summary>
        /// 鍵入奉獻專用的會友查詢。
        /// 同一奉獻編號可能掛在大量會友上，原本整批撈回所有欄位再在記憶體比對姓名，
        /// 曾造成上傳卡住近百秒；改為奉獻編號＋姓名直接在 CRM 端雙條件過濾，
        /// 同編號不同姓名也能即時找到正確會友。
        /// </summary>
        private Entity GetContactForKeyIn(DonationPaymentFormModel DonationPaymentFormModel)
        {
            if (!string.IsNullOrEmpty(DonationPaymentFormModel.DedicationNumber))
            {
                // 編號有值但姓名空白時，原邏輯必然比對不到任何人，直接視為查無會友
                if (string.IsNullOrEmpty(DonationPaymentFormModel.FullName))
                {
                    return null;
                }

                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet(
                        "contactid",
                        "fullname",
                        "pager",
                        "new_personal_id",
                        "new_lineid",
                        "new_lineid_backup",
                        "parentcustomerid",
                        "ownerid"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("pager", ConditionOperator.Equal, DonationPaymentFormModel.DedicationNumber),
                            new ConditionExpression("fullname", ConditionOperator.Equal, DonationPaymentFormModel.FullName),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    },
                    TopCount = 1
                };
                query.AddOrder("contactid", OrderType.Ascending);

                var matches = m_ToolUtilityClass.m_Crm2011OrganizationService.RetrieveMultiple(query);
                return matches.Entities.Count > 0 ? matches.Entities[0] : null;
            }

            // 沒有奉獻編號時沿用原本的查詢邏輯（姓名＋電話、僅姓名）
            return GetContact(DonationPaymentFormModel);
        }

        /// <summary>
        /// 發送手動輸入奉獻完成後的 LINE 通知給奉獻者。
        ///
        /// 這段流程和一般線上 ATM 建單的「虛擬帳號付款資訊」不同：
        /// - 一般 ATM 建單會在 <c>ProcessAtm</c> 內直接把虛擬帳號送給奉獻者。
        /// - 手動輸入奉獻是後台同工補登既有奉獻資料，這裡送的是「奉獻已登記」確認訊息。
        ///
        /// 兩者仍然有相同的 LINE 發送規則：
        /// 1. 優先使用 CRM 主要欄位 <c>new_lineid</c>。
        /// 2. 若主要欄位空白，改用綁定流程保存的備援欄位 <c>new_lineid_backup</c>。
        /// 3. 這是付款/奉獻確認通知，失敗不應被靜默吞掉；必須走 <see cref="PushUtility.SendReliableMessageAsync"/>
        ///    讓共用 LINE workflow 保留 retry key 與錯誤語意。
        ///
        /// 注意：CRM 查詢、奉獻文案、是否允許主流程繼續，都是 ChurchReport 的產品規則；
        /// 共用 LINE 專案只負責真正把訊息送到 LINE，不反向依賴 ChurchReport。
        /// </summary>
        private static readonly TimeSpan DedicationLineNotificationDisplayTimeout = TimeSpan.FromMilliseconds(500);

        private async Task<string> SendDedicationNotificationAsync(Entity contact, DonationPaymentFormModel donationPaymentFormModel, Guid feeId)
        {
            try
            {
                // 取得奉獻者的 LINE User ID。這裡不可只看 new_lineid：
                // 歷史資料或 LINE 綁定搬移流程可能只留下 new_lineid_backup，
                // 若沒有 fallback，畫面會顯示奉獻建立成功，但奉獻者完全收不到 LINE。
                var lineUserId = ResolveDedicationNotificationLineId(contact);

                if (string.IsNullOrEmpty(lineUserId))
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知略過：奉獻者尚未綁定 LINE。ContactId={contact.Id}, FeeId={feeId}");
                    // 未綁定 LINE 不是奉獻建檔失敗，但必須回到畫面提示同工後續人工處理。
                    return BuildDedicationLineNotificationResult("LINE 發送結果：發送失敗。失敗原因：奉獻者尚未綁定 LINE。");
                }

                // 建立奉獻確認訊息
                var message = BuildDedicationNotificationMessage(contact, donationPaymentFormModel);

                // 付款/奉獻確認屬於「應送達」通知，不使用會吞例外的 legacy SendMessage。
                // retry key 固定由 fee id 與內容摘要組成：同一筆補登重試時可讓 LINE API 降低重複通知風險。
                var retryKey = BuildDedicationNotificationLineRetryKey(feeId, donationPaymentFormModel);
                var sendTask = SendDedicationNotificationLineAsync(lineUserId, message, retryKey);

                // 只等待短暫顯示回應：LINE API 若無回應不應卡住上傳主流程。
                // 若超時，仍讓奉獻收費單保存完成，但留下 trace 供維運追查。
                var timeoutTask = Task.Delay(DedicationLineNotificationDisplayTimeout);
                var completed = await Task.WhenAny(sendTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知發送超時，略過等待繼續完成上傳。ContactId={contact.Id}, FeeId={feeId}, TimeoutMs={DedicationLineNotificationDisplayTimeout.TotalMilliseconds}");
                    _ = sendTask.ContinueWith(
                        task =>
                        {
                            if (task.IsFaulted)
                            {
                                System.Diagnostics.Trace.WriteLine(
                                    $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知背景完成失敗。ContactId={contact.Id}, FeeId={feeId}, Error={task.Exception}");
                            }
                        },
                        TaskContinuationOptions.ExecuteSynchronously);

                    // 超時時不阻擋奉獻儲存，但畫面要讓使用者知道 LINE 未確認送達。
                    return BuildDedicationLineNotificationResult("LINE 發送結果：發送失敗。失敗原因：LINE API 逾時未回應。");
                }
                else
                {
                    // 重要：SendReliableMessageAsync 會把 provider rejection / validation failure 往外丟；
                    // await sendTask 可以確保非超時錯誤會進入 catch，而不是只因 Task.WhenAny 完成就誤判成功。
                    await sendTask;
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 已成功發送手動輸入奉獻通知。ContactId={contact.Id}, FeeId={feeId}");
                    return BuildDedicationLineNotificationResult("LINE 發送結果：成功發送。");
                }
            }
            catch (Exception ex)
            {
                // 發送失敗不影響奉獻記錄，只記錄錯誤
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知失敗。ContactId={contact.Id}, FeeId={feeId}, Error={ex}");
                return BuildDedicationLineNotificationResult($"LINE 發送結果：發送失敗。失敗原因：{FormatLineNotificationFailureReason(ex)}。");
            }
        }

        protected virtual async Task SendDedicationNotificationLineAsync(string lineUserId, string message, string retryKey)
        {
            await PushUtility.SendReliableMessageAsync(lineUserId, message, retryKey);
        }

        private static string BuildDedicationLineNotificationResult(string message)
        {
            return $"<br/><strong>{message}</strong>";
        }

        /// <summary>
        /// 解析手動輸入奉獻通知要使用的 LINE user id。
        /// 這個 helper 故意和 ATM 建單通知的解析規則保持一致，避免同一位奉獻者在
        /// 「線上 ATM 建單」可收到通知，但「後台手動輸入 ATM 奉獻」收不到通知。
        /// </summary>
        protected virtual string ResolveDedicationNotificationLineId(Entity contact)
        {
            var primaryLineId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid");
            if (!string.IsNullOrWhiteSpace(primaryLineId))
            {
                return primaryLineId;
            }

            var backupLineId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid_backup");
            if (!string.IsNullOrWhiteSpace(backupLineId))
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知使用 new_lineid_backup。ContactId={contact.Id}");
                return backupLineId;
            }

            return string.Empty;
        }

        /// <summary>
        /// 建立手動輸入奉獻通知的 retry key。
        /// LINE retry key 的目的不是取代資料庫交易，而是讓同一筆通知在網路重試時有穩定識別。
        /// 這個值最後會進入 HTTP header，因此回傳標準 UUID 字串；
        /// 不把中文奉獻類別、付款方式或冒號分隔的產品語意字串放進 header，
        /// 避免不同 HTTP client、proxy 或 LINE API 對 header 格式的處理不一致，
        /// 反而讓通知在進入 LINE 前就失敗。
        /// </summary>
        private static string BuildDedicationNotificationLineRetryKey(Guid feeId, DonationPaymentFormModel donationPaymentFormModel)
        {
            if (feeId == Guid.Empty)
            {
                throw new ArgumentException("Fee id is required for dedication LINE retry key.", nameof(feeId));
            }

            return BuildDeterministicLineRetryKey(
                $"churchreport:keyin-dedication:{feeId:N}:{donationPaymentFormModel.Amount}");
        }

        /// <summary>
        /// 把產品端可讀的 retry key seed 轉成 LINE provider-safe 的 UUID。
        ///
        /// 設計理由：
        /// - ChurchReport 需要用 fee/order/amount 這類業務資料決定「同一筆通知」。
        /// - LINE 實際收到的是 HTTP header；header 值越單純越不容易因格式被拒。
        /// - 使用 SHA256 前 16 bytes 建立 Guid，可讓相同 seed 永遠得到相同 UUID，
        ///   同時避免把訂單號、ATM 虛擬帳號、奉獻類別或付款方式直接暴露在 header。
        ///
        /// 這是 ChurchReport 產品層的 helper，不放進共用 LINE 專案；
        /// 共用 LINE 專案不應知道 feeId、ATM 虛擬帳號或奉獻流程語意。
        /// </summary>
        private static string BuildDeterministicLineRetryKey(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                throw new ArgumentException("Retry key seed is required.", nameof(seed));
            }

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed.Trim()));
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, guidBytes.Length);

            return new Guid(guidBytes).ToString("D");
        }

        /// <summary>
        /// 建立奉獻確認 LINE 訊息內容
        /// </summary>
        private string BuildDedicationNotificationMessage(Entity contact, DonationPaymentFormModel donationPaymentFormModel)
        {
            var message = "🙏 奉獻確認通知\n" +
                         "━━━━━━━━━\n" +
                         $"✨ 感謝您的奉獻！\n\n" +
                         $"📅 日期：{donationPaymentFormModel.DedicationDate:yyyy/MM/dd}\n" +
                         $"👤 姓名：{donationPaymentFormModel.FullName}\n" +
                         $"🏷️ 類別：{donationPaymentFormModel.Category}\n";

            // 如果有其他類別說明
            if (!string.IsNullOrEmpty(donationPaymentFormModel.Others))
            {
                message += $"📝 其他類別：{donationPaymentFormModel.Others}\n";
            }

            message += $"💰 金額：NT$ {donationPaymentFormModel.Amount:N0}\n" +
                      $"💳 方式：{donationPaymentFormModel.PayWay}\n";

            // 如果有奉獻地點
            if (!string.IsNullOrEmpty(donationPaymentFormModel.DedicateLocation))
            {
                message += $"📍 地點：{donationPaymentFormModel.DedicateLocation}\n";
            }

            // 如果有備註
            if (!string.IsNullOrEmpty(donationPaymentFormModel.Explain))
            {
                message += $"\n💬 備註：{donationPaymentFormModel.Explain}\n";
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
        private void SetFeeAmounts(ref Entity aFeeToCreated, DonationPaymentFormModel DonationPaymentFormModel, bool KeyinMode)
        {
            // 應收金額
            ToolUtility.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", new Money(DonationPaymentFormModel.Amount));

            // 實收金額（根據付款方式和輸入模式決定）
            var reallyPaidAmount = ShouldSetFullAmount(DonationPaymentFormModel.PayWay, KeyinMode) ? DonationPaymentFormModel.Amount : 0;
            ToolUtility.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(reallyPaidAmount));

            // 大寫金額
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_big_chinese_number",
                MoneyToChinese(DonationPaymentFormModel.Amount.ToString()));
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
        private void SetFeePaymentInfo(ref Entity aFeeToCreated, DonationPaymentFormModel DonationPaymentFormModel, bool KeyinMode)
        {
            // 付款方式
            SetPayMethod(DonationPaymentFormModel.PayWay, ref aFeeToCreated);

            // 付款狀態
            var payStatus = DeterminePayStatus(DonationPaymentFormModel.PayWay, KeyinMode);
            SetPayStatus(payStatus, ref aFeeToCreated);

            // 帳戶後六碼
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_last_six_digit", DonationPaymentFormModel.LastSixDigit);

            // 收費日期
            ToolUtility.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", DonationPaymentFormModel.DedicationDate.ToLocalTime());
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
        private void SetFeeCategoryInfo(ref Entity aFeeToCreated, DonationPaymentFormModel DonationPaymentFormModel)
        {
            // 奉獻類別
            SetFeePayCategory(DonationPaymentFormModel.Category, ref aFeeToCreated);

            // 奉獻其他類別
            if (DonationPaymentFormModel.Others != "" && DonationPaymentFormModel.Others != null)
            {
                ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_others", DonationPaymentFormModel.Others);
            }

            // 收入類別
            SetIncomeCategory(DonationPaymentFormModel.Category, ref aFeeToCreated);
        }

        /// <summary>
        /// 設定收費單額外資訊
        /// </summary>
        private void SetFeeAdditionalInfo(ref Entity aFeeToCreated, Entity aContact, DonationPaymentFormModel DonationPaymentFormModel)
        {
            // 設定輸入奉獻人員
            if (m_LoginContact != null)
            {
                ToolUtility.SetEntityLookUpAttribute(ref aFeeToCreated, "new_keyin_contact_new_fee", "contact", m_LoginContact.Id);
            }

            // 奉獻地點
            var dedicateLocation = DonationPaymentFormModel.DedicateLocation
                ?? ToolUtility.GetEntityLookupDisplayName(ref aContact, "parentcustomerid");
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_dedicate_location", dedicateLocation);

            // 奉獻備註
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_explain", DonationPaymentFormModel.Explain);

            // 週報專用備註
            ToolUtility.SetEntityStringAttribute(ref aFeeToCreated, "new_weekly_note", DonationPaymentFormModel.WeeklyNote);
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
                    System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] 指派負責人失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 建立成功訊息
        /// </summary>
        private string BuildSuccessMessage(Entity aContact, DonationPaymentFormModel DonationPaymentFormModel)
        {
            return "上傳成功<br/>" +
                   "--------------------<br/>" +
                   $"日期    : {DonationPaymentFormModel.DedicationDate.ToShortDateString()}<br/>" +
                   $"姓名    : {DonationPaymentFormModel.FullName}<br/>" +
                   $"奉獻編號: {ToolUtility.GetEntityStringAttribute(ref aContact, "pager")}<br/>" +
                   $"身分證字號: {ToolUtility.GetEntityStringAttribute(ref aContact, "new_personal_id")}<br/>" +
                   $"電話    : {DonationPaymentFormModel.Mobile}<br/>" +
                   $"類別    : {DonationPaymentFormModel.Category}<br/>" +
                   $"奉獻地點: {DonationPaymentFormModel.DedicateLocation}<br/>" +
                   $"付款方式: {DonationPaymentFormModel.PayWay}<br/>" +
                   $"金額    : {DonationPaymentFormModel.Amount}<br/>" +
                   $"備註    : {DonationPaymentFormModel.Explain}<br/>";
        }

        #endregion
    }
}
