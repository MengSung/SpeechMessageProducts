using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// TSPG (台新金流) API 控制器
    /// 處理來自台新金流的 Webhook 通知和其他 API 操作
    /// </summary>
    /// <remarks>
    /// 此控制器負責整合台新金流支付系統，提供完整的支付處理功能：
    ///
    /// 主要功能：
    /// 1. Webhook 通知處理 - 接收並處理 TSPG 的前台和後台通知
    /// 2. 訂單管理 - 建立、查詢、取消訂單
    /// 3. 支付操作 - 退款、請款等
    /// 4. CRM 整合 - 自動更新 Dynamics365 收費單狀態
    /// 5. 通知服務 - 透過 LINE Bot 發送付款成功通知
    ///
    /// 架構特點：
    /// - 使用 ASP.NET Core Web API
    /// - 依賴注入 (TSPGWebhookHandler)
    /// - 統一的錯誤處理和日誌記錄
    /// - 支援前台通知 (post_back_url) 和後台通知 (result_url)
    /// - 完整的 DCC (動態貨幣轉換) 支援
    ///
    /// 安全考量：
    /// - 敏感資料 (如卡號) 只記錄部分資訊
    /// - 使用 HTTPS 確保資料傳輸安全
    /// - 驗證請求來源和參數完整性
    ///
    /// 依賴服務：
    /// - TspgToolkit: 台新金流 SDK
    /// - ToolUtilityClass: CRM 操作工具
    /// - Line.Messaging: LINE Bot API
    /// - TSPGWebhookHandler: Webhook 處理服務
    /// </remarks>
    [Route("api/[controller]")]
    [ApiController]
    public class TSPGController : ControllerBase
    {
        #region 常數定義
        // LINE Channel Access Token (用於發送 LINE 通知)
        // 此 Token 用於驗證 LINE Bot API 呼叫，應妥善保管避免洩露
        private const string LINE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";
        // Dynamics365連線名稱 (用於 CRM 操作)
        // 指定 CRM 連線配置名稱，對應 appsettings.json 中的連線字串
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";
        // 狀態常數: 信用卡已繳費
        // CRM 中 new_pay_status 欄位的選項集值，表示付款已完成
        private const int PAYMENT_STATUS_PAID = 100000001;
        //付款方式常數: 信用卡
        // CRM 中 new_pay_way 欄位的選項集值，表示使用信用卡付款
        private const int PAYMENT_METHOD_CREDIT_CARD = 100000001;
        #endregion

        #region 私有欄位
        // Webhook 處理器 (依賴注入)
        // 用於處理 TSPG Webhook 通知的服務類別
        private readonly TSPGWebhookHandler _webhookHandler;
        #endregion

        #region 建構函式
        /// <summary>
        /// 建構子，注入 Webhook Handler
        /// ASP.NET Core 依賴注入容器會自動提供 TSPGWebhookHandler 實例
        /// </summary>
        /// <param name="webhookHandler">TSPG Webhook 處理器實例</param>
        public TSPGController(TSPGWebhookHandler webhookHandler)
        {
            _webhookHandler = webhookHandler;
        }
        #endregion

        #region Webhook端點
        /// <summary>
        /// 付款完成返回頁面端點 (post_back_url - 前台通知)
        /// 用戶付款完成後的返回頁面，TSPG會將交易結果透過HTTP POST或GET方式傳送至此
        /// 此為前台通知，持卡人網頁會被重新導向至此
        /// </summary>
        /// <remarks>
        /// 處理流程：
        /// 1. 解析 TSPG 傳送的前台通知參數 (Form 或 QueryString)
        /// 2. 記錄完整的通知資訊到日誌系統
        /// 3. 根據 retCode 或 state 判斷付款是否成功
        /// 4. 根據付款狀態重新導向到成功或失敗頁面
        ///
        /// TSPG 前台通知參數範例：
        /// - ret_code: "00" (成功) 或其他錯誤碼
        /// - order_no: 訂單編號
        /// - transaction_id: 交易編號
        /// - auth_id_resp: 授權碼
        /// - state: "1" (成功)
        /// </remarks>
        [HttpGet("post-back")]
        [HttpPost("post-back")]
        public IActionResult PostBack()
        {
            try
            {
                var notification = ParsePostBackNotification(); //解析前台通知所有參數
                LogPostBackNotification(notification); // 記錄日誌

                bool isSuccess = IsPaymentSuccess(notification.RetCode, notification.State); // 判斷付款是否成功

                // 根據付款狀態導向對應頁面
                return isSuccess
                    ? HandleSuccessfulPaymentReturn(notification)
                    : HandleFailedPaymentReturn(notification); // 修正：失敗時導向失敗處理
            }
            catch (Exception ex)
            {
                LogError("PostBack", "付款返回處理例外", ex);
                return Redirect("/payment-error");
            }
        }

        /// <summary>
        /// 付款結果通知端點 (後台通知 - result_url)
        /// 接收來自 TSPG 的付款結果通知 (JSON 格式)
        /// 規格參考：4.9 信用卡授權交易回應後台通知
        /// </summary>
        /// <remarks>
        /// 處理流程：
        /// 1. 非同步讀取 HTTP 請求的 JSON Body 內容
        /// 2. 記錄完整的原始 JSON 請求到日誌系統
        /// 3. 解析 JSON 結構，提取所有必要參數
        /// 4. 根據 retCode 判斷交易是否成功 (00=成功)
        /// 5. 若成功：更新 CRM 收費單狀態並發送 LINE 通知
        /// 6. 若失敗：僅記錄失敗資訊，不更新 CRM
        /// 7. 回應 TSPG 狀態確認 (TSPG 會重試失敗的通知)
        ///
        /// TSPG 後台通知 JSON 結構：
        /// {
        ///   "ver": "1.0",
        ///   "mid": "特店代號",
        ///   "tid": "端末代號",
        ///   "pay_type": 1,
        ///   "tx_type": 1,
        ///   "params": {
        ///     "ret_code": "00",
        ///     "order_no": "訂單編號",
        ///     "auth_id_resp": "授權碼",
        ///     "rrn": "交易編號",
        ///     "tx_amt": 交易金額(分),
        ///     ...
        ///   }
        /// }
        /// </remarks>
        [HttpPost("result-url")]
        [HttpGet("result-url")]
        public async Task<IActionResult> ResultUrl()
        {
            string requestBody = null;
            try
            {
                requestBody = await ReadRequestBodyAsync(); //讀取 JSON內容
                LogInfo("PaymentNotify", $"收到後台通知: {requestBody}");

                var notification = ParseBackendNotification(requestBody); //解析所有參數
                bool isSuccess = notification.RetCode == "00";

                if (isSuccess)
                {
                    UpdateFeeEntityByOrderNo(notification); // 更新收費單與發送通知
                    LogInfo("PaymentNotify", $"付款成功處理完成 - 訂單: {notification.OrderNo}");
                    return Ok(new { status = "success", message = "通知已接收並處理" });
                }
                else
                {
                    LogInfo("PaymentNotify", $"付款失敗 - 訂單: {notification.OrderNo}, 錯誤: {notification.RetMsg}");
                    return Ok(new { status = "received", message = "付款失敗通知已接收" });
                }
            }
            catch (Exception ex)
            {
                LogError("PaymentNotify", "處理例外", ex);
                return StatusCode(500, new { status = "error", message = $"處理錯誤: {ex.Message}" });
            }
        }
        #endregion

        #region API 操作端點
        /// <summary>
        /// 建立付款訂單 (呼叫 TspgToolkit.OrderCreate)
        /// </summary>
        /// <param name="request">付款請求物件，包含訂單資訊</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 驗證請求模型 (ModelState)
        /// 2. 呼叫 TspgToolkit.OrderCreate 建立訂單
        /// 3. 使用 CreateApiResponse 統一處理回應
        ///
        /// 請求物件 (TSPGPaymentRequest) 應包含：
        /// - 訂單基本資訊 (金額、商品等)
        /// - 回呼網址 (post_back_url, result_url)
        /// - 客戶資訊等
        ///
        /// 成功回應包含付款網址，用戶將被導向 TSPG 付款頁面
        /// 失敗時返回錯誤資訊
        /// </remarks>
        [HttpPost("create-payment")]
        public IActionResult CreatePayment([FromBody] TSPGPaymentRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = TspgToolkit.OrderCreate(request); // 呼叫金流建立訂單
                return CreateApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleApiError("建立付款", ex);
            }
        }

        /// <summary>
        /// 查詢訂單狀態 (呼叫 TspgToolkit.OrderQuery)
        /// 對應台新規格：4.5 信用卡其他交易 - 交易類別 7:查詢
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 驗證訂單編號參數
        /// 2. 呼叫 TspgToolkit.OrderQuery 查詢訂單
        /// 3. 返回統一格式的查詢結果
        ///
        /// 台新規格參數說明 (4.5 信用卡其他交易)：
        /// 輸入參數：
        /// - order_no: 訂單號碼 (必要)
        /// - result_flag: 回傳訊息標記 (可選)
        ///   * 0: 不查詢交易詳情
        ///   * 1: 查詢交易詳情
        ///
        /// 回傳參數 (當 result_flag=1 時)：
        /// - ret_code: 交易結果回應碼 (參照 5.1)
        /// - ret_msg: 回傳訊息
        /// - auth_id_resp: 授權碼
        /// - rrn: 調單號碼
        /// - order_status: 訂單狀態碼 (參照 5.2)
        /// - auth_type: 授權方式 (SSL/3D)
        /// - cur: 幣別 (NTD)
        /// - purchase_date: 採購日期 (yyyy-MM-dd HH:mm:ss)
        /// - tx_amt: 交易金額 (包含兩位小數)
        /// - settle_amt: 請款金額
        /// - settle_seq: 請款批號
        /// - settle_date: 請款日期
        /// - refund_trans_amt: 退貨金額
        /// - refund_rrn: 退貨調單編號
        /// - refund_auth_id_resp: 退貨授權碼
        /// - refund_date: 退貨日期
        /// - install_period: 分期期數
        /// - ch_amt: DCC 交易金額 (DCC 交易時回傳)
        /// - ch_currency: 持卡人母國幣別
        /// - ex_rate: 轉換匯率
        /// - markup_rate: 貼水費率(%)
        ///
        /// 回應範例：
        /// {
        ///   "success": true,
        ///   "order_id": "NO01234567",
        ///   "status_code": "00",
        ///   "message": "查詢成功",
        ///   "data": {
        ///     "ver": "1.0.0",
        ///     "mid": "999000123456789",
        ///     "tid": "T0000000",
        ///     "pay_type": 1,
        ///     "tx_type": 7,
        ///     "params": {
        ///       "ret_code": "00",
        ///       "order_no": "NO01234567",
        ///       "auth_id_resp": "001241",
        ///       "rrn": "128417503172",
        ///       "order_status": "01",
        ///       "auth_type": "SSL",
        ///       "cur": "NTD",
        ///       "purchase_date": "2024-01-15 14:30:25",
        ///       "tx_amt": "120000"
        ///     }
        ///   }
        /// }
        ///
        /// 用於檢查訂單付款狀態或除錯
        /// </remarks>
        [HttpGet("query-order/{orderId}")]
        public IActionResult QueryOrder(string orderId)
        {
            try
            {
                // 1. 驗證訂單編號
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    LogWarning("QueryOrder", "訂單編號為空");
                    return BadRequest(new
                    {
                        success = false,
                        message = "訂單編號不能為空",
                        error_code = "INVALID_PARAM"
                    });
                }

                LogInfo("QueryOrder", $"開始查詢訂單 - OrderId: {orderId}");

                // 2. 呼叫 TSPG 查詢 API
                var response = TspgToolkit.OrderQuery(orderId);

                // 3. 記錄查詢結果
                if (response != null)
                {
                    LogInfo("QueryOrder", $"查詢完成 - OrderId: {orderId}, Code: {response.code}, Message: {response.msg}");
                }
                else
                {
                    LogWarning("QueryOrder", $"查詢回應為 null - OrderId: {orderId}");
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "查詢失敗，無回應資料",
                        order_id = orderId
                    });
                }

                // 4. 判斷查詢是否成功
                bool isSuccess = response.code == "0000" || response.code == "00";

                // 5. 建立回應物件
                var result = new
                {
                    success = isSuccess,
                    order_id = response.uid ?? orderId,
                    status_code = response.code,
                    message = response.msg ?? (isSuccess ? "查詢成功" : "查詢失敗"),
                    data = response,
                    query_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                // 6. 根據結果返回適當的 HTTP 狀態碼
                if (isSuccess)
                {
                    return Ok(result);
                }
                else
                {
                    // 查詢失敗但 API 有回應，返回 200 但 success=false
                    LogWarning("QueryOrder", $"訂單查詢失敗 - OrderId: {orderId}, Code: {response.code}");
                    return Ok(result);
                }
            }
            catch (Exception ex)
            {
                LogError("QueryOrder", $"查詢訂單發生例外 - OrderId: {orderId}", ex);
                return HandleApiError("查詢訂單", ex);
            }
        }

        /// <summary>
        /// 查詢訂單詳細資訊 (包含完整交易記錄)
        /// 對應台新規格：4.5 信用卡其他交易 - 交易類別 7:查詢 (result_flag=1)
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <param name="includeHistory">是否包含歷史記錄</param>
        /// <remarks>
        /// 此端點提供更詳細的訂單資訊查詢
        /// 包含：
        /// - 訂單基本資訊
        /// - 授權資訊
        /// - 請款資訊
        /// - 退貨資訊
        /// - 分期資訊
        /// - DCC 交易資訊
        /// - CRM 收費單狀態 (如果存在)
        ///
        /// 適用場景：
        /// - 客服查詢完整交易記錄
        /// - 對帳作業
        /// - 交易稽核
        /// </remarks>
        [HttpGet("query-order-detail/{orderId}")]
        public IActionResult QueryOrderDetail(string orderId, [FromQuery] bool includeHistory = false)
        {
            ToolUtilityClass toolUtility = null;
            try
            {
                // 1. 驗證訂單編號
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "訂單編號不能為空"
                    });
                }

                LogInfo("QueryOrderDetail", $"開始查詢訂單詳情 - OrderId: {orderId}, IncludeHistory: {includeHistory}");

                // 2. 查詢 TSPG 訂單狀態
                var tspgResponse = TspgToolkit.OrderQuery(orderId);

                if (tspgResponse == null)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "無法查詢 TSPG 訂單資訊"
                    });
                }

                bool isTspgSuccess = tspgResponse.code == "0000" || tspgResponse.code == "00";

                // 3. 查詢 CRM 收費單資訊 (如果存在)
                Entity feeEntity = null;
                object crmInfo = null;

                try
                {
                    toolUtility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                    feeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderId);

                    if (feeEntity != null)
                    {
                        var payStatus = toolUtility.GetOptionSetAttribute(feeEntity, "new_pay_status");
                        var payWay = toolUtility.GetOptionSetAttribute(feeEntity, "new_pay_way");
                        var shouldPay = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                        var reallyPaid = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_really_paid");
                        var payDate = toolUtility.GetEntityDateTimeAttribute(feeEntity, "new_pay_date");

                        crmInfo = new
                        {
                            fee_id = feeEntity.Id.ToString(),
                            pay_status = payStatus,
                            pay_status_name = GetPaymentStatusName(payStatus),
                            pay_way = payWay,
                            pay_way_name = GetPaymentMethodName(payWay),
                            should_pay_amount = shouldPay?.Value,
                            really_paid_amount = reallyPaid?.Value,
                            pay_date = payDate != DateTime.MinValue ? payDate.ToString("yyyy-MM-dd HH:mm:ss") : null
                        };

                        LogInfo("QueryOrderDetail", $"找到對應的 CRM 收費單 - FeeId: {feeEntity.Id}");
                    }
                    else
                    {
                        LogWarning("QueryOrderDetail", $"找不到對應的 CRM 收費單 - OrderNo: {orderId}");
                    }
                }
                catch (Exception crmEx)
                {
                    LogWarning("QueryOrderDetail", $"查詢 CRM 資料時發生錯誤: {crmEx.Message}");
                }

                // 4. 建立完整回應
                var detailResponse = new
                {
                    success = isTspgSuccess,
                    order_id = orderId,
                    tspg_data = new
                    {
                        status_code = tspgResponse.code,
                        message = tspgResponse.msg,
                        transaction_id = tspgResponse.transaction_id,
                        order_no = tspgResponse.order_no,
                        full_response = tspgResponse
                    },
                    crm_data = crmInfo,
                    query_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    include_history = includeHistory
                };

                LogInfo("QueryOrderDetail", $"訂單詳情查詢完成 - OrderId: {orderId}");

                return Ok(detailResponse);
            }
            catch (Exception ex)
            {
                LogError("QueryOrderDetail", $"查詢訂單詳情發生例外 - OrderId: {orderId}", ex);
                return HandleApiError("查詢訂單詳情", ex);
            }
            finally
            {
                toolUtility?.Dispose();
            }
        }

        /// <summary>
        /// 批次查詢多筆訂單狀態
        /// </summary>
        /// <param name="orderIds">訂單編號清單 (以逗號分隔)</param>
        /// <remarks>
        /// 用於批次查詢多筆訂單的狀態
        /// 適用場景：
        /// - 對帳作業
        /// - 批次訂單狀態檢查
        /// - 報表生成
        ///
        /// 請求範例：
        /// GET /api/tspg/query-orders?orderIds=ORDER001,ORDER002,ORDER003
        ///
        /// 回應範例：
        /// {
        ///   "success": true,
        ///   "total_count": 3,
        ///   "success_count": 2,
        ///   "failed_count": 1,
        ///   "orders": [
        ///     {
        ///       "order_id": "ORDER001",
        ///       "success": true,
        ///       "status_code": "00",
        ///       "message": "查詢成功"
        ///     },
        ///     ...
        ///   ]
        /// }
        /// </remarks>
        [HttpGet("query-orders")]
        public IActionResult QueryMultipleOrders([FromQuery] string orderIds)
        {
            try
            {
                // 1. 驗證參數
                if (string.IsNullOrWhiteSpace(orderIds))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "訂單編號清單不能為空"
                    });
                }

                // 2. 解析訂單編號清單
                var orderIdList = orderIds.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();

                if (orderIdList.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "沒有有效的訂單編號"
                    });
                }

                // 限制批次查詢數量
                if (orderIdList.Count > 100)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "單次最多只能查詢 100 筆訂單"
                    });
                }

                LogInfo("QueryMultipleOrders", $"開始批次查詢 {orderIdList.Count} 筆訂單");

                // 3. 逐筆查詢
                var results = new List<object>();
                int successCount = 0;
                int failedCount = 0;

                foreach (var orderId in orderIdList)
                {
                    try
                    {
                        var response = TspgToolkit.OrderQuery(orderId);
                        bool isSuccess = response != null && (response.code == "0000" || response.code == "00");

                        if (isSuccess) successCount++;
                        else failedCount++;

                        results.Add(new
                        {
                            order_id = orderId,
                            success = isSuccess,
                            status_code = response?.code ?? "9999",
                            message = response?.msg ?? "查詢失敗",
                            transaction_id = response?.transaction_id
                        });
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        results.Add(new
                        {
                            order_id = orderId,
                            success = false,
                            status_code = "9999",
                            message = $"查詢異常: {ex.Message}",
                            transaction_id = (string)null
                        });

                        LogWarning("QueryMultipleOrders", $"查詢訂單 {orderId} 時發生錯誤: {ex.Message}");
                    }
                }

                // 4. 建立回應
                var batchResponse = new
                {
                    success = true,
                    total_count = orderIdList.Count,
                    success_count = successCount,
                    failed_count = failedCount,
                    orders = results,
                    query_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                LogInfo("QueryMultipleOrders", $"批次查詢完成 - 總數: {orderIdList.Count}, 成功: {successCount}, 失敗: {failedCount}");

                return Ok(batchResponse);
            }
            catch (Exception ex)
            {
                LogError("QueryMultipleOrders", "批次查詢訂單發生例外", ex);
                return HandleApiError("批次查詢訂單", ex);
            }
        }
        #endregion

        #region 測試與健康檢查
        /// <summary>
        /// API 健康狀態檢查 (可用於監控)
        /// </summary>
        /// <remarks>
        /// 提供系統健康檢查端點
        /// 用於：
        /// - 負載平衡器健康檢查
        /// - 監控系統狀態檢查
        /// - 容器化部署的健康探針
        ///
        /// 回應包含：
        /// - status: "healthy" (固定值)
        /// - timestamp: 當前時間
        /// - version: API 版本
        /// - service: 服務名稱
        ///
        /// 此端點不依賴外部服務，始終返回成功狀態
        /// </remarks>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.Now,
                version = "1.0.0",
                service = "TSPG API Controller"
            });
        }

        /// <summary>
        /// 測試 Webhook端點 (產生測試資料)
        /// </summary>
        /// <remarks>
        /// 提供測試用的 Webhook 資料生成端點
        /// 用於：
        /// - 開發階段測試 Webhook 處理邏輯
        /// - 模擬 TSPG 通知資料
        /// - 驗證通知解析和處理流程
        ///
        /// 生成的測試資料包含：
        /// - 基本交易資訊 (訂單號、交易號等)
        /// - 付款狀態 (state = "1" 表示成功)
        /// - 測試金額 (100 元)
        /// - 測試時間戳記
        ///
        /// 注意：此為測試端點，不應在生產環境使用
        /// 生產環境應只接受來自 TSPG 的真實通知
        /// </remarks>
        [HttpPost("test-webhook")]
        public IActionResult TestWebhook()
        {
            var testNotification = new TSPGPaymentNotification
            {
                StoreUid = "test_store",
                OrderId = $"TEST_{DateTime.Now:yyyyMMddHHmmss}",
                TransactionId = $"TXN_{DateTime.Now:yyyyMMddHHmmss}",
                State = "1",
                Cost = 100,
                ActualCost = 100,
                Currency = "TWD",
                PayType = "credit",
                UserName = "測試用戶",
                UserEmail = "test@example.com",
                PayTime = DateTime.Now,
                ReturnMessage = "付款成功",
                Hash = "test_hash"
            };

            return Ok(new { success = true, message = "測試 Webhook 資料已建立", test_data = testNotification });
        }
        #endregion

        #region 通知解析方法
        /// <summary>
        /// 解析前台通知參數 (Form 或 QueryString)
        /// </summary>
        /// <returns>TSPGPaymentNotification物件</returns>
        /// <remarks>
        /// 從 HTTP 請求中解析 TSPG 前台通知的所有參數
        /// 支援 GET (QueryString) 和 POST (Form) 兩種提交方式
        /// 參數來源優先順序：POST Form > QueryString
        ///
        /// 解析的參數包括：
        /// - 基本參數：s_mid, ret_code, tx_type, order_no, order_id, ret_msg, auth_id_resp, state, transaction_id
        /// - 特殊參數：first_6_digit_of_pan, last_4_digit_of_pan, carrierId2 (需事先向台新申請)
        /// - DCC 參數：ch_amt, ch_currency, ex_rate, markup_rate
        /// - 其他參數：hash/signature, cost/amt, actual_cost, pay_type, currency/cur
        ///
        /// 金額處理：TSPG 金額以分為單位，解析時除以100轉換為元
        /// </remarks>
        private TSPGPaymentNotification ParsePostBackNotification()
        {
            //依據台新規格，解析所有可能參數
            return new TSPGPaymentNotification
            {
                S_Mid = GetParam("s_mid"),
                RetCode = GetParam("ret_code"),
                TxType = GetParam("tx_type"),
                OrderNo = GetParam("order_no"),
                OrderId = GetParam("order_id") ?? GetParam("order_no"),
                RetMsg = GetParam("ret_msg"),
                AuthIdResp = GetParam("auth_id_resp"),
                State = GetParam("state"),
                TransactionId = GetParam("transaction_id"),
                First6DigitOfPan = GetParam("first_6_digit_of_pan"),
                Last4DigitOfPan = GetParam("last_4_digit_of_pan"),
                CarrierId2 = GetParam("carrierId2"),
                ChAmt = GetDecimalParam("ch_amt"),
                ChCurrency = GetParam("ch_currency"),
                ExRate = GetDecimalParam("ex_rate"),
                MarkupRate = GetDecimalParam("markup_rate"),
                Hash = GetParam("hash") ?? GetParam("signature"),
                Cost = GetDecimalParam("cost") ?? GetDecimalParam("amt") ?? 0,
                ActualCost = GetDecimalParam("actual_cost") ?? (GetDecimalParam("cost") ?? GetDecimalParam("amt") ?? 0),
                PayType = GetParam("pay_type"),
                Currency = GetParam("currency") ?? GetParam("cur")
            };
        }

        /// <summary>
        /// 解析後台通知（JSON 格式）
        /// </summary>
        /// <param name="requestBody">JSON 字串</param>
        /// <returns>TSPGPaymentNotification物件</returns>
        /// <remarks>
        /// 解析 TSPG 後台通知的 JSON 格式資料
        /// JSON 結構包含外層和 params 巢狀物件
        ///
        /// 外層欄位：
        /// - ver: 版本號
        /// - s_mid/mid: 特店代號
        /// - tx_type: 交易類型
        /// - tid: 端末代號
        /// - pay_type: 付款類別
        /// - params: 參數物件 (包含詳細交易資訊)
        ///
        /// params 欄位：
        /// - ret_code: 回應碼
        /// - order_no: 訂單編號
        /// - auth_id_resp: 授權碼
        /// - rrn: 交易編號
        /// - tx_amt: 交易金額 (分)
        /// - purchase_date: 交易日期
        /// - 其他參數...
        ///
        /// 處理邏輯：
        /// 1. 使用 Newtonsoft.Json 反序列化 JSON
        /// 2. 提取外層基本欄位
        /// 3. 解析 params 物件中的詳細參數
        /// 4. 記錄後台通知日誌
        /// </remarks>
        private TSPGPaymentNotification ParseBackendNotification(string requestBody)
        {
            //依據台新規格，解析所有外層與 params參數
            dynamic jsonData = Newtonsoft.Json.JsonConvert.DeserializeObject(requestBody);
            var notification = new TSPGPaymentNotification();

            // 外層基本欄位
            notification.StoreUid = jsonData.ver?.ToString();
            notification.S_Mid = jsonData.s_mid?.ToString() ?? jsonData.mid?.ToString();
            notification.TxType = jsonData.tx_type?.ToString();

            string tid = jsonData.tid?.ToString();
            int? payType = jsonData.pay_type;
            int? txType = jsonData.tx_type;

            // params參數清單
            var paramsData = jsonData.@params;
            if (paramsData != null)
            {
                ParseBackendParamsData(notification, paramsData);
            }

            LogBackendNotification(notification, tid, payType, txType, requestBody);
            return notification;
        }

        /// <summary>
        /// 解析後台通知的 params 資料 (所有欄位)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <param name="paramsData">params 動態物件</param>
        /// <remarks>
        /// 解析 JSON params 物件中的所有交易參數
        ///
        /// 必要參數：
        /// - ret_code: 回應碼 ("00"=成功)
        /// - ret_msg: 回應訊息
        /// - order_no: 訂單編號
        /// - auth_id_resp: 授權碼
        /// - rrn: 交易編號 (Retrieval Reference Number)
        ///
        /// 條件參數：
        /// - carrierId2: 載具資訊
        /// - order_status: 訂單狀態
        /// - cur: 貨幣代碼
        ///
        /// 日期處理：
        /// - purchase_date: 解析為 DateTime 物件
        ///
        /// 金額處理：
        /// - tx_amt: TSPG 以分為單位，除以100轉換為元
        /// - 設定 Cost 和 ActualCost
        ///
        /// 卡號資訊：
        /// - first_6_digit_of_pan: 卡號前6碼
        /// - last_4_digit_of_pan: 卡號後4碼
        ///
        /// DCC 參數：交由 ParseDccParameters 處理
        /// </remarks>
        private void ParseBackendParamsData(TSPGPaymentNotification notification, dynamic paramsData)
        {
            // 必要參數
            notification.RetCode = paramsData.ret_code?.ToString();
            notification.RetMsg = paramsData.ret_msg?.ToString();
            notification.OrderNo = paramsData.order_no?.ToString();
            notification.OrderId = notification.OrderNo;
            notification.AuthIdResp = paramsData.auth_id_resp?.ToString();
            notification.TransactionId = paramsData.rrn?.ToString();

            // 條件參數
            notification.CarrierId2 = paramsData.carrierId2?.ToString();
            notification.State = paramsData.order_status?.ToString();
            notification.Currency = paramsData.cur?.ToString();

            // 日期處理
            string purchaseDate = paramsData.purchase_date?.ToString();
            if (!string.IsNullOrEmpty(purchaseDate) && DateTime.TryParse(purchaseDate, out var parsedDate))
            {
                notification.PayTime = parsedDate;
            }

            // 金額處理
            string txAmtStr = paramsData.tx_amt?.ToString();
            if (!string.IsNullOrEmpty(txAmtStr) && decimal.TryParse(txAmtStr, out var txAmt))
            {
                notification.Cost = txAmt / 100; // 金額包含兩位小數
                notification.ActualCost = notification.Cost;
            }

            // 卡號資訊
            notification.First6DigitOfPan = paramsData.first_6_digit_of_pan?.ToString();
            notification.Last4DigitOfPan = paramsData.last_4_digit_of_pan?.ToString();

            // DCC交易參數
            ParseDccParameters(notification, paramsData);
        }

        /// <summary>
        /// 解析 DCC交易參數 (DCC 金額、幣別、匯率、貼水)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <param name="paramsData">params 動態物件</param>
        /// <remarks>
        /// 解析動態貨幣轉換 (DCC) 相關參數
        ///
        /// DCC 參數：
        /// - ch_amt: DCC 金額 (以分為單位)
        /// - ch_currency: DCC 幣別代碼
        /// - ex_rate: 匯率
        /// - markup_rate: 貼水率 (百分比)
        ///
        /// DCC 允許持卡人在外國使用本地貨幣結帳
        /// 系統會顯示本地貨幣金額和原始貨幣金額供比較
        /// </remarks>
        private void ParseDccParameters(TSPGPaymentNotification notification, dynamic paramsData)
        {
            string chAmtStr = paramsData.ch_amt?.ToString();
            if (!string.IsNullOrEmpty(chAmtStr) && decimal.TryParse(chAmtStr, out var chAmt))
            {
                notification.ChAmt = chAmt;
            }
            notification.ChCurrency = paramsData.ch_currency?.ToString();
            string exRateStr = paramsData.ex_rate?.ToString();
            if (!string.IsNullOrEmpty(exRateStr) && decimal.TryParse(exRateStr, out var exRate))
            {
                notification.ExRate = exRate;
            }
            string markupRateStr = paramsData.markup_rate?.ToString();
            if (!string.IsNullOrEmpty(markupRateStr) && decimal.TryParse(markupRateStr, out var markupRate))
            {
                notification.MarkupRate = markupRate;
            }
        }

        /// <summary>
        /// 讀取請求內容 (支援 UTF-8)
        /// </summary>
        /// <returns>請求內容字串</returns>
        /// <remarks>
        /// 非同步讀取 HTTP 請求的 Body 內容
        /// 使用 UTF-8 編碼確保中文字符正確處理
        /// 使用 StreamReader 進行安全的串流讀取
        /// </remarks>
        private async Task<string> ReadRequestBodyAsync()
        {
            using (var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8))
            {
                return await reader.ReadToEndAsync();
            }
        }
        #endregion

        #region 參數取得方法

        /// <summary>
        /// 從 Request 中取得參數值（支援 GET 和 POST）
        /// </summary>
        /// <param name="key">參數名稱</param>
        /// <returns>參數值，若不存在則返回 null</returns>
        /// <remarks>
        /// 統一的參數取得方法，支援兩種 HTTP 請求方式：
        ///
        /// 1. POST 請求 (Form 資料)：
        ///    - 檢查 Request.HasFormContentType
        ///    - 從 Request.Form 取得參數值
        ///    - 適用於前台通知的 Form 提交
        ///
        /// 2. GET 請求 (QueryString)：
        ///    - 從 Request.Query 取得參數值
        ///    - 適用於前台通知的 GET 請求或測試
        ///
        /// 優先順序：POST Form > GET QueryString
        /// 這樣設計確保 POST 資料優先於 QueryString
        /// </remarks>
        private string GetParam(string key)
        {
            if (Request.Method == "POST" && Request.HasFormContentType && Request.Form.ContainsKey(key))
            {
                return Request.Form[key].ToString();
            }
            if (Request.Query.ContainsKey(key))
            {
                return Request.Query[key].ToString();
            }
            return null;
        }

        /// <summary>
        /// 從 Request 中取得 decimal參數值
        /// </summary>
        /// <param name="key">參數名稱</param>
        /// <returns>decimal? 參數值，若不存在或解析失敗則返回 null</returns>
        /// <remarks>
        /// 將字串參數轉換為 decimal 型別
        /// 處理流程：
        /// 1. 使用 GetParam 取得字串值
        /// 2. 檢查字串是否為空或空白
        /// 3. 使用 decimal.TryParse 進行安全轉換
        /// 4. 轉換成功返回 decimal 值，否則返回 null
        ///
        /// 安全考量：
        /// - 使用 TryParse 避免拋出例外
        /// - 允許 null 返回，呼叫端需處理
        /// - 適用於金額、匯率等數值參數
        /// </remarks>
        private decimal? GetDecimalParam(string key)
        {
            var value = GetParam(key);
            if (!string.IsNullOrWhiteSpace(value) && decimal.TryParse(value, out var result))
            {
                return result;
            }
            return null;
        }
        #endregion

        #region 業務邏輯處理
        /// <summary>
        /// 判斷付款是否成功 (根據 retCode 或 state)
        /// </summary>
        /// <param name="retCode">TSPG 回應碼，"00" 表示成功</param>
        /// <param name="state">交易狀態，"1" 表示成功</param>
        /// <returns>true=成功，false=失敗</returns>
        /// <remarks>
        /// 成功條件：
        /// 1. state == "1" (TSPG 交易狀態成功)
        /// 2. retCode == "00" (TSPG 回應碼成功)
        /// 3. retCode == "0000" (TSPG 回應碼成功，4位格式)
        ///
        /// 任一條件滿足即視為成功
        /// </remarks>
        private bool IsPaymentSuccess(string retCode, string state)
        {
            retCode = (retCode ?? string.Empty).Trim();
            return string.Equals(state, "1") ||
                string.Equals(retCode, "00", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(retCode, "0000", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 更新收費單狀態 (依據訂單號查詢並更新付款資訊)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件，包含所有交易資訊</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 驗證訂單編號是否存在
        /// 2. 連接到 Dynamics365 CRM 系統
        /// 3. 根據訂單號查詢對應的收費單 (new_fee 實體)
        /// 4. 若找不到收費單，記錄警告並結束
        /// 5. 更新收費單欄位 (狀態、金額、日期等)
        /// 6. 儲存變更到 CRM
        /// 7. 發送 LINE 付款成功通知給連絡人
        /// 8. 記錄成功日誌
        ///
        /// 異常處理：記錄錯誤但不拋出例外，避免影響 TSPG 通知處理
        /// 資源管理：確保 ToolUtilityClass 正確釋放
        /// </remarks>
        private void UpdateFeeEntityByOrderNo(TSPGPaymentNotification notification)
        {
            ToolUtilityClass toolUtility = null;
            try
            {
                var orderNo = notification.OrderNo ?? notification.OrderId;
                if (string.IsNullOrEmpty(orderNo))
                {
                    LogWarning("UpdateFeeEntity", "訂單編號為空，無法更新收費單");
                    return;
                }
                toolUtility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderNo);
                if (feeEntity == null)
                {
                    LogWarning("UpdateFeeEntity", $"找不到對應的收費單 - OrderNo: {orderNo}");
                    return;
                }
                UpdateFeeEntityFields(toolUtility, feeEntity, notification);
                toolUtility.UpdateEntity(ref feeEntity);
                LogInfo("UpdateFeeEntity", $"成功更新收費單 - OrderNo: {orderNo}, FeeId: {feeEntity.Id}");
                // 發送 LINE 通知
                var amount = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                SendPaymentNotificationToContact(toolUtility, feeEntity, notification, amount.Value);
            }
            catch (Exception ex)
            {
                LogError("UpdateFeeEntity", "更新收費單失敗", ex);
            }
            finally
            {
                toolUtility?.Dispose();
            }
        }

        /// <summary>
        /// 更新收費單欄位 (付款狀態、金額、日期、說明)
        /// </summary>
        /// <param name="toolUtility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單 Entity 物件</param>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <remarks>
        /// 更新欄位清單：
        /// - new_pay_status: 設定為 PAYMENT_STATUS_PAID (已繳費)
        /// - new_fee_really_paid: 設定為應收金額 (TODO: 應使用實際付款金額)
        /// - new_difference_fee_paid: 設定為 0 (差額)
        /// - new_pay_date: 設定為當前日期時間
        /// - new_pay_way: 設定為 PAYMENT_METHOD_CREDIT_CARD (信用卡)
        /// - new_description: 附加 TSPG 付款成功資訊
        ///
        /// 注意：目前實作中實收金額使用應收金額，這可能不正確
        /// </remarks>
        private void UpdateFeeEntityFields(ToolUtilityClass toolUtility, Entity feeEntity, TSPGPaymentNotification notification)
        {
            var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
            var orderNo = notification.OrderNo ?? notification.OrderId;
            // 更新付款狀態
            toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
            // 更新實收金額（TODO: 應該使用實際金額而非應收金額）
            toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
            // 計算差額
            toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
            // 設定付款日期和方式
            toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", DateTime.Now);
            toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);
            // 更新說明
            var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description");
            var newDescription = $"{originalDescription}{Environment.NewLine}" +
                $"[TSPG付款成功] 訂單號:{orderNo},交易號:{notification.TransactionId}, " +
                $"金額:{shouldPayMoney}, 授權碼:{notification.AuthIdResp}, 時間:{DateTime.Now}";
            toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
        }

        /// <summary>
        /// 發送付款通知給連絡人 (LINE)
        /// </summary>
        /// <param name="toolUtility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單 Entity 物件</param>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <param name="amount">付款金額</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 從收費單取得關聯的連絡人 ID (new_contact_new_fee)
        /// 2. 驗證連絡人是否存在
        /// 3. 從連絡人實體取得 LINE ID (new_lineid)
        /// 4. 驗證 LINE ID 是否存在
        /// 5. 從連絡人取得姓名 (fullname)
        /// 6. 建立付款成功訊息內容
        /// 7. 透過 LINE Bot API 發送訊息
        /// 8. 記錄發送結果
        ///
        /// 異常處理：記錄錯誤但不拋出例外，避免影響主要付款流程
        /// </remarks>
        private void SendPaymentNotificationToContact(ToolUtilityClass toolUtility, Entity feeEntity,
            TSPGPaymentNotification notification, decimal amount)
        {
            try
            {
                var contactId = toolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty)
                {
                    LogWarning("SendNotification", "收費單沒有關聯的連絡人");
                    return;
                }
                Entity contactEntity = toolUtility.RetrieveEntity("contact", contactId);
                if (contactEntity == null)
                {
                    LogWarning("SendNotification", $"找不到連絡人 - ContactId: {contactId}");
                    return;
                }
                string lineId = toolUtility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrEmpty(lineId))
                {
                    LogWarning("SendNotification", $"連絡人沒有 LINE ID - ContactId: {contactId}");
                    return;
                }
                string fullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");
                var orderNo = notification.OrderNo ?? notification.OrderId;
                var message = BuildPaymentSuccessMessage(fullName, orderNo, amount, notification);
                SendLineMessage(lineId, message);
                LogInfo("SendNotification", $"已發送付款通知 LINE 訊息 - ContactId: {contactId}, LineId: {lineId}");
            }
            catch (Exception ex)
            {
                LogError("SendNotification", "發送 LINE 訊息失敗", ex);
            }
        }

        /// <summary>
        /// 建立付款成功訊息 (LINE)
        /// </summary>
        /// <param name="fullName">收款人姓名</param>
        /// <param name="orderNo">訂單編號</param>
        /// <param name="amount">金額</param>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <returns>訊息內容</returns>
        /// <remarks>
        /// 訊息格式包含：
        /// - 問候語與姓名
        /// - 付款成功確認
        /// - 感謝奉獻
        /// - 詳細付款資訊 (訂單號、金額、時間、方式)
        /// - 選用資訊 (授權碼、交易編號)
        /// - 祝福語
        ///
        /// 使用 Environment.NewLine 確保跨平台換行
        /// </remarks>
        private string BuildPaymentSuccessMessage(string fullName, string orderNo, decimal amount,
            TSPGPaymentNotification notification)
        {
            var message = $"【TSPG付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            message += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            message += $"付款資訊：{Environment.NewLine}";
            message += $"訂單編號：{orderNo}{Environment.NewLine}";
            message += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            message += $"付款時間：{DateTime.Now:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            message += $"付款方式：信用卡{Environment.NewLine}";
            if (!string.IsNullOrEmpty(notification.AuthIdResp))
            {
                message += $"授權碼：{notification.AuthIdResp}{Environment.NewLine}";
            }
            if (!string.IsNullOrEmpty(notification.TransactionId))
            {
                message += $"交易編號：{notification.TransactionId}{Environment.NewLine}";
            }
            message += $"{Environment.NewLine}願上帝賜福與您！";
            return message;
        }

        /// <summary>
        /// 發送 LINE 訊息 (同步)
        /// </summary>
        /// <param name="lineId">LINE ID</param>
        /// <param name="message">訊息內容</param>
        /// <remarks>
        /// 使用 Line.Messaging 套件發送推播訊息
        /// 處理流程：
        /// 1. 建立 LineMessagingClient 實例 (使用 Channel Access Token)
        /// 2. 建立 PushUtility 實例
        /// 3. 同步發送訊息 (Wait())
        /// 4. 記錄發送結果
        ///
        /// 異常處理：記錄錯誤並重新拋出，確保上層知道發送失敗
        /// </remarks>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
                var pushUtility = new PushUtility(lineMessagingClient);
                pushUtility.SendMessage(lineId, message).Wait();
                LogInfo("SendLineMessage", $"LINE 訊息已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                LogError("SendLineMessage", "LINE 訊息發送失敗", ex);
                throw;
            }
        }
        #endregion

        #region 返回處理方法
        /// <summary>
        /// 處理付款成功的返回 (導向成功頁面)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <returns>Redirect 結果</returns>
        /// <remarks>
        /// 處理流程：
        /// 1. 記錄付款成功資訊
        /// 2. 更新收費單狀態 (重複確保，因為後台通知可能尚未處理)
        /// 3. 重新查詢收費單以取得最新資訊
        /// 4. 建立成功頁面的查詢字串參數
        /// 5. 重新導向到前端成功頁面 (/payment-success)
        ///
        /// 查詢字串參數包含：
        /// - order_id: 訂單編號
        /// - transaction_id: 交易編號
        /// - amount: 付款金額
        /// - auth_code: 授權碼
        /// - tx_type: 交易類型
        /// - DCC 相關參數 (如適用)
        ///
        /// 異常處理：記錄錯誤並導向錯誤頁面
        /// 資源管理：確保 ToolUtilityClass 正確釋放
        /// </remarks>
        private IActionResult HandleSuccessfulPaymentReturn(TSPGPaymentNotification notification)
        {
            ToolUtilityClass toolUtility = null;
            try
            {
                LogInfo("PaymentReturn", $"付款成功 - 訂單: {notification.OrderNo}, 授權碼: {notification.AuthIdResp}");
                UpdateFeeEntityByOrderNo(notification);
                var orderNo = notification.OrderNo ?? notification.OrderId;
                toolUtility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderNo);
                var queryString = BuildSuccessQueryString(notification, toolUtility, feeEntity);
                return Redirect($"/payment-success?{queryString}");
            }
            catch (Exception ex)
            {
                LogError("PaymentReturn", "處理付款成功返回失敗", ex);
                return Redirect("/payment-error");
            }
            finally
            {
                toolUtility?.Dispose();
            }
        }

        /// <summary>
        /// 處理付款失敗的返回 (導向失敗頁面)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <returns>Redirect 結果</returns>
        /// <remarks>
        /// 處理流程：
        /// 1. 記錄付款失敗資訊
        /// 2. 建立失敗頁面的查詢字串參數
        /// 3. 重新導向到前端失敗頁面 (/payment-failed)
        ///
        /// 查詢字串參數包含：
        /// - order_id: 訂單編號
        /// - error: 錯誤訊息
        /// - ret_code: TSPG 回應碼
        ///
        /// 注意：失敗時不更新 CRM，只記錄日誌
        /// </remarks>
        private IActionResult HandleFailedPaymentReturn(TSPGPaymentNotification notification)
        {
            LogInfo("PaymentReturn", $"付款失敗 - 訂單: {notification.OrderNo}, 錯誤: {notification.RetMsg}");
            var errorMsg = notification.RetMsg ?? "付款失敗";
            var orderId = notification.OrderNo ?? notification.OrderId ?? "UNKNOWN";
            var retCode = notification.RetCode ?? "";
            return Redirect($"/payment-failed?order_id={Uri.EscapeDataString(orderId)}" +
                $"&error={Uri.EscapeDataString(errorMsg)}" +
                $"&ret_code={Uri.EscapeDataString(retCode)}");
        }

        /// <summary>
        /// 建立成功頁面查詢字串 (包含 DCC 資訊)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <param name="toolUtility">CRM 工具</param>
        /// <param name="feeEntity">收費單 Entity</param>
        /// <returns>查詢字串</returns>
        /// <remarks>
        /// 建立前端成功頁面所需的 URL 參數
        /// 基本參數：
        /// - order_id: 訂單編號
        /// - transaction_id: 交易編號
        /// - amount: 付款金額 (從 CRM 取得)
        /// - auth_code: 授權碼
        /// - tx_type: 交易類型
        ///
        /// DCC 參數 (動態貨幣轉換)：
        /// - dcc_amount: DCC 金額
        /// - dcc_currency: DCC 幣別
        /// - exchange_rate: 匯率
        ///
        /// 使用 Uri.EscapeDataString 進行 URL 編碼
        /// </remarks>
        private string BuildSuccessQueryString(TSPGPaymentNotification notification,
            ToolUtilityClass toolUtility, Entity feeEntity)
        {
            var orderId = notification.OrderNo ?? notification.OrderId;
            var txnId = notification.TransactionId ?? "";
            var amount = Convert.ToInt32(toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay").Value).ToString();
            var authCode = notification.AuthIdResp ?? "";
            var txType = notification.TxType ?? "";
            var queryString = $"order_id={Uri.EscapeDataString(orderId)}" +
                $"&transaction_id={Uri.EscapeDataString(txnId)}" +
                $"&amount={amount}" +
                $"&auth_code={Uri.EscapeDataString(authCode)}" +
                $"&tx_type={Uri.EscapeDataString(txType)}";
            // DCC 資訊
            if (notification.ChAmt.HasValue)
            {
                queryString += $"&dcc_amount={notification.ChAmt.Value}" +
                    $"&dcc_currency={Uri.EscapeDataString(notification.ChCurrency ?? "")}" +
                    $"&exchange_rate={notification.ExRate ?? 0}";
            }
            return queryString;
        }
        #endregion

        #region API 回應輔助方法
        /// <summary>
        /// 建立 API 回應 (含付款網址)
        /// 根據 TSPG 回應物件的狀態，建立對應的 HTTP 回應
        /// 成功時返回 200 OK 與付款網址，失敗時返回 400 BadRequest 與錯誤資訊
        /// </summary>
        /// <param name="response">TSPG 回應物件，包含 code、uid、url、msg 等屬性</param>
        /// <returns>
        /// IActionResult:
        /// - 成功 (code == "0000"): Ok() 包含 { success: true, order_id, payment_url, message }
        /// - 失敗: BadRequest() 包含 { success: false, error_code, message }
        /// </returns>
        /// <remarks>
        /// 此方法用於統一處理 TSPG API 呼叫的回應格式
        /// 主要用於建立付款訂單的 API 端點
        ///
        /// 成功回應範例：
        /// {
        ///   "success": true,
        ///   "order_id": "ORDER001",
        ///   "payment_url": "https://payment.tspg.com/pay/...",
        ///   "message": "訂單建立成功"
        /// }
        ///
        /// 失敗回應範例：
        /// {
        ///   "success": false,
        ///   "error_code": "1001",
        ///   "message": "參數錯誤"
        /// }
        /// </remarks>
        private IActionResult CreateApiResponse(dynamic response)
        {
            // 檢查 TSPG 回應碼是否為成功 (0000)
            if (response.code == "0000")
            {
                // 成功回應：返回付款網址等資訊
                return Ok(new
                {
                    success = true,
                    order_id = response.uid,      // 訂單編號
                    payment_url = response.url,   // 付款網址 (用戶將被導向此網址進行付款)
                    message = response.msg        // 成功訊息
                });
            }

            // 失敗回應：返回錯誤資訊
            return BadRequest(new
            {
                success = false,
                error_code = response.code, // TSPG 錯誤碼
                message = response.msg  // 錯誤訊息
            });
        }

        /// <summary>
        /// 建立簡單 API 回應 (不含付款網址)
        /// </summary>
        /// <param name="response">TSPG 回應物件</param>
        /// <returns>IActionResult</returns>
        /// <remarks>
        /// 用於查詢、取消、退款等操作的統一回應格式
        /// 不包含 payment_url，只返回基本操作結果
        ///
        /// 回應格式：
        /// {
        ///   "success": true/false,
        ///   "order_id": "訂單編號",
        ///   "message": "操作結果訊息"
        /// }
        /// </remarks>
        private IActionResult CreateSimpleApiResponse(dynamic response)
        {
            return Ok(new
            {
                success = response.code == "0000",
                order_id = response.uid,
                message = response.msg
            });
        }

        /// <summary>
        /// 處理 API 錯誤 (統一格式)
        /// </summary>
        /// <param name="operation">操作名稱</param>
        /// <param name="ex">例外</param>
        /// <returns>IActionResult</returns>
        /// <remarks>
        /// 當 TSPG API 呼叫發生例外時的統一錯誤處理
        /// 返回 500 Internal Server Error 與通用錯誤訊息
        /// 記錄詳細錯誤資訊到日誌系統
        ///
        /// 回應格式：
        /// {
        ///   "success": false,
        ///   "message": "系統錯誤，請稍後再試"
        /// }
        ///
        /// 注意：不暴露內部錯誤細節給用戶端
        /// </remarks>
        private IActionResult HandleApiError(string operation, Exception ex)
        {
            LogError("API", $"{operation}失敗", ex);
            return StatusCode(500, new
            {
                success = false,
                message = "系統錯誤，請稍後再試"
            });
        }

        /// <summary>
        /// 取得付款狀態顯示名稱
        /// </summary>
        /// <param name="statusCode">付款狀態代碼</param>
        /// <returns>狀態顯示名稱</returns>
        private string GetPaymentStatusName(int statusCode)
        {
            switch (statusCode)
            {
                case 100000000: return "未繳費";
                case 100000001: return "已繳費";
                case 100000002: return "部分繳費";
                case 100000003: return "已退款";
                default: return $"未知狀態({statusCode})";
            }
        }

        /// <summary>
        /// 取得付款方式顯示名稱
        /// </summary>
        /// <param name="methodCode">付款方式代碼</param>
        /// <returns>方式顯示名稱</returns>
        private string GetPaymentMethodName(int methodCode)
        {
            switch (methodCode)
            {
                case 100000000: return "現金";
                case 100000001: return "信用卡";
                case 100000002: return "匯款";
                case 100000003: return "支票";
                case 100000004: return "LINE Pay";
                case 100000005: return "其他";
                default: return $"未知方式({methodCode})";
            }
        }
        #endregion

        #region 日誌記錄方法
        /// <summary>
        /// 記錄前台通知 (格式化日誌)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <remarks>
        /// 記錄 TSPG 前台通知的所有重要資訊
        /// 使用 BuildPostBackLogMessage 建立格式化的日誌字串
        /// 透過 System.Diagnostics.Trace.WriteLine 輸出到追蹤系統
        /// </remarks>
        private void LogPostBackNotification(TSPGPaymentNotification notification)
        {
            var logMessage = BuildPostBackLogMessage(notification);
            System.Diagnostics.Trace.WriteLine(logMessage);
        }

        /// <summary>
        /// 建立前台通知日誌訊息 (格式化)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <returns>日誌字串</returns>
        /// <remarks>
        /// 日誌格式：[TSPG PostBackUrl] 訂單: XXX, 交易號: XXX, 狀態: XXX, 結果碼: XXX, 交易類型: XXX
        /// 額外資訊：
        /// - 卡號資訊 (前6碼+後4碼，隱藏中間數字)
        /// - 載具資訊
        /// - DCC 交易資訊 (金額、幣別、匯率)
        ///
        /// 隱私保護：卡號只顯示前6碼和後4碼
        /// </remarks>
        private string BuildPostBackLogMessage(TSPGPaymentNotification notification)
        {
            var message = $"[TSPG PostBackUrl] " +
                $"訂單: {notification.OrderNo ?? notification.OrderId}, " +
                $"交易號: {notification.TransactionId}, " +
                $"狀態: {notification.State}, " +
                $"結果碼: {notification.RetCode}, " +
                $"交易類型: {notification.TxType}";
            if (!string.IsNullOrEmpty(notification.First6DigitOfPan) || !string.IsNullOrEmpty(notification.Last4DigitOfPan))
            {
                message += $", 卡號: {notification.First6DigitOfPan}******{notification.Last4DigitOfPan}";
            }
            if (!string.IsNullOrEmpty(notification.CarrierId2))
            {
                message += $", 載具: {notification.CarrierId2}";
            }
            if (notification.ChAmt.HasValue)
            {
                message += $", DCC金額: {notification.ChAmt} {notification.ChCurrency}, 匯率: {notification.ExRate}";
            }
            return message;
        }

        /// <summary>
        /// 記錄後台通知 (格式化日誌)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <param name="tid">端末代號</param>
        /// <param name="payType">付款類別</param>
        /// <param name="txType">交易類別</param>
        /// <param name="rawJson">原始 JSON</param>
        /// <remarks>
        /// 記錄 TSPG 後台通知的詳細資訊
        /// 包含格式化的通知摘要和完整的原始 JSON
        /// 有助於除錯和稽核追蹤
        /// </remarks>
        private void LogBackendNotification(TSPGPaymentNotification notification, string tid,
            int? payType, int? txType, string rawJson)
        {
            var logMessage = BuildBackendLogMessage(notification, tid, payType, txType);
            System.Diagnostics.Trace.WriteLine(logMessage);
            System.Diagnostics.Trace.WriteLine($"[TSPG Backend Notification] 原始JSON: {rawJson}");
        }

        /// <summary>
        /// 建立後台通知日誌訊息 (格式化)
        /// </summary>
        /// <param name="notification">TSPGPaymentNotification物件</param>
        /// <param name="tid">端末代號</param>
        /// <param name="payType">付款類別</param>
        /// <param name="txType">交易類別</param>
        /// <returns>日誌字串</returns>
        /// <remarks>
        /// 日誌格式包含：
        /// - 基本交易資訊 (訂單號、交易號、授權碼等)
        /// - 回應狀態 (結果碼、訊息)
        /// - 交易類型資訊 (交易類型、端末、付款類別)
        /// - 金額資訊
        /// - 卡號資訊 (隱私保護)
        /// - 載具資訊
        /// - DCC 資訊 (金額、幣別、匯率、貼水)
        ///
        /// 隱私保護：卡號只顯示前6碼和後4碼
        /// </remarks>
        private string BuildBackendLogMessage(TSPGPaymentNotification notification, string tid,
            int? payType, int? txType)
        {
            var message = $"[TSPG Backend Notification] " +
                $"訂單: {notification.OrderNo}, " +
                $"調單號: {notification.TransactionId}, " +
                $"授權碼: {notification.AuthIdResp}, " +
                $"結果碼: {notification.RetCode}, " +
                $"訊息: {notification.RetMsg}, " +
                $"交易類型: {notification.TxType}, " +
                $"端末: {tid}, " +
                $"付款類別: {payType}";
            if (notification.Cost > 0)
            {
                message += $", 金額: {notification.Cost}";
            }
            if (!string.IsNullOrEmpty(notification.First6DigitOfPan) || !string.IsNullOrEmpty(notification.Last4DigitOfPan))
            {
                message += $", 卡號: {notification.First6DigitOfPan}******{notification.Last4DigitOfPan}";
            }
            if (!string.IsNullOrEmpty(notification.CarrierId2))
            {
                message += $", 載具: {notification.CarrierId2}";
            }
            if (notification.ChAmt.HasValue)
            {
                message += $", DCC金額: {notification.ChAmt} {notification.ChCurrency}, " +
                    $"匯率: {notification.ExRate}, 貼水: {notification.MarkupRate}%";
            }
            return message;
        }

        /// <summary>
        /// 記錄資訊 (一般訊息)
        /// </summary>
        /// <param name="method">方法名稱</param>
        /// <param name="message">訊息內容</param>
        /// <remarks>
        /// 記錄一般資訊訊息
        /// 格式：[TSPG 方法名稱] 訊息內容
        /// 用於追蹤正常處理流程
        /// </remarks>
        private void LogInfo(string method, string message)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] {message}");
        }

        /// <summary>
        /// 記錄警告 (警告訊息)
        /// </summary>
        /// <param name="method">方法名稱</param>
        /// <param name="message">訊息內容</param>
        /// <remarks>
        /// 記錄警告訊息
        /// 格式：[TSPG 方法名稱] 警告: 訊息內容
        /// 用於記錄可預期的異常情況，如找不到資料等
        /// </remarks>
        private void LogWarning(string method, string message)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] 警告: {message}");
        }

        /// <summary>
        /// 記錄錯誤 (例外訊息與堆疊)
        /// </summary>
        /// <param name="method">方法名稱</param>
        /// <param name="message">錯誤訊息</param>
        /// <param name="ex">例外</param>
        /// <remarks>
        /// 記錄錯誤訊息和完整的堆疊追蹤
        /// 格式：
        /// [TSPG 方法名稱] 錯誤訊息: 例外訊息
        /// [TSPG 方法名稱] 堆疊: 堆疊追蹤
        ///
        /// 用於記錄未預期的錯誤情況
        /// 有助於問題診斷和除錯
        /// </remarks>
        private void LogError(string method, string message, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] {message}: {ex.Message}");
            if (ex.StackTrace != null)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG {method}] 堆疊: {ex.StackTrace}");
            }
        }

        #endregion
    }
}