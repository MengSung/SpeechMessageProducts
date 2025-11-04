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

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 高鉅金流 PayPage 回傳處理控制器
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        #region 常數定義
        // LINE Channel Access Token (用於發送 LINE 通知)
        private const string LINE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";
        
        // Dynamics365連線名稱 (用於 CRM 操作)
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";
        
        // 付款狀態常數: 信用卡已繳費
        private const int PAYMENT_STATUS_PAID = 100000001;
        
        // 付款方式常數: 信用卡
        private const int PAYMENT_METHOD_CREDIT_CARD = 100000001;
        #endregion

        private readonly ILogger<MyPayController> _logger;

        public MyPayController(ILogger<MyPayController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 接收高鉅金流 PayPage 交易完成回傳資訊
        /// POST /api/MyPay/return
        /// </summary>
        /// <param name="returnModel">高鉅金流回傳的表單資料</param>
        /// <returns>處理結果</returns>
        [HttpPost("MyPayReturn")]
        public async Task<IActionResult> PaymentReturn([FromForm] MyPayReturnModel returnModel)
        {
            _logger.LogInformation($"收到高鉅金流回傳，OrderID: {returnModel?.order_id}, 狀態: {returnModel?.state}");

            try
            {
                // 基本參數驗證
                if (returnModel == null)
                {
                    _logger.LogWarning("回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                // 驗證必要欄位是否存在
                // order_id: 訂單編號，用於識別特定的交易訂單
                // transaction_id: 金流平台產生的交易識別碼
                // hash: 用於驗證資料完整性的雜湊值
                if (string.IsNullOrEmpty(returnModel.order_id) ||
                    string.IsNullOrEmpty(returnModel.transaction_id) ||
                    string.IsNullOrEmpty(returnModel.hash))
                {
                    // 記錄警告訊息，包含訂單編號以便追蹤問題
                    _logger.LogWarning($"回傳資料缺少必要欄位: {returnModel.order_id}");
                    // 回傳 400 Bad Request 狀態碼給金流平台
                    //return BadRequest("回傳資料缺少必要欄位");
                }

                // 建立 QPayProcessor 實例來處理回傳
                QPayProcessor qpayProcessor = new QPayProcessor(null); // 注意：這裡需要根據實際 DI 設定調整

                // 1. 驗證 hash 值
                // hash 是金流平台提供的資料完整性驗證碼，用於確保回傳資料未被篡改
                // 透過比對我們計算的 hash 值與金流平台提供的 hash 值來驗證資料真實性
                //if (!qpayProcessor.VerifyMyPayHash(returnModel))
                //{
                //    // 驗證失敗表示資料可能被篡改或來源不可信，記錄警告以便安全稽核
                //    _logger.LogWarning($"回傳資訊驗證失敗: {returnModel.order_id}");
                //    // 回傳 400 Bad Request 拒絕處理，保護系統安全
                //    return BadRequest("驗證失敗");
                //}

                // 2. 處理回傳資訊並更新系統
                bool success = await qpayProcessor.ProcessMyPayReturn(returnModel);

                if (success)
                {
                    _logger.LogInformation($"成功處理回傳: {returnModel.order_id}");

                    // 根據高鉅金流官方文檔要求，成功處理後回傳 "888"
                    // 這讓金流平台知道我們已經成功接收並處理了回調通知
                    return Ok("888");
                }
                else
                {
                    // 系統處理回傳資訊時發生錯誤，記錄警告並回傳 500 錯誤
                    // 讓金流平台知道需要重新發送通知
                    _logger.LogWarning($"處理回傳失敗: {returnModel.order_id}");
                    return Ok("888");
                    //return StatusCode(500, "處理失敗");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"處理回傳異常: {returnModel?.order_id}");
                return StatusCode(500, "處理異常");
            }
        }

        /// <summary>
        /// 付款成功頁面 (供用戶查看結果)
        /// GET /api/MyPay/success
        /// </summary>
        /// <param name="order_id">訂單編號（貴特店系統的訂單編號）</param>
        /// <param name="uid">Payment Hub 之交易流水號</param>
        /// <param name="key">交易驗證碼</param>
        /// <param name="cost">總交易金額</param>
        /// <param name="actual_cost">實際交易金額</param>
        /// <param name="prc">交易回傳碼</param>
        /// <param name="pfn">付費方法</param>
        /// <param name="finishtime">交易完成時間(YYYYMMDDHHmmss)</param>
        /// <param name="cardno">卡號/VA/超商代碼</param>
        /// <param name="acode">銀行交易授權碼</param>
        /// <param name="echo_0">自訂回傳參數 1</param>
        /// <param name="echo_1">自訂回傳參數 2</param>
        /// <param name="echo_2">自訂回傳參數 3</param>
        /// <param name="echo_3">自訂回傳參數 4</param>
        /// <param name="echo_4">自訂回傳參數 5</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 驗證訂單編號是否存在
        /// 2. 連接到 Dynamics365 CRM 系統
        /// 3. 根據訂單號查詢對應的收費單 (new_fee 實體)
        /// 4. 判斷收費單類型（奉獻 vs 課程繳費）
        /// 5. 更新收費單欄位 (狀態、金額、日期等)
        /// 6. 儲存變更到 CRM
        /// 7. 根據類型發送不同的 LINE 通知
        /// 8. 根據類型返回不同的結果頁面
        /// 
        /// 根據高鉅金流規格文件：
        /// - 此為消費者導向的成功結果頁面
        /// - 回傳參數包含：order_id, uid, key, cost, prc, pfn, finishtime 等
        /// - 交易狀態代碼：250=付款成功, 290=交易成功但資訊不符, 600=結帳完成
        /// </remarks>
        [HttpGet("success")]
        public IActionResult PaymentSuccess(
      [FromQuery] string order_id = "",
            [FromQuery] string uid = "",
 [FromQuery] string key = "",
      [FromQuery] string cost = "",
      [FromQuery] string actual_cost = "",
            [FromQuery] string prc = "",
            [FromQuery] string pfn = "",
          [FromQuery] string finishtime = "",
      [FromQuery] string cardno = "",
         [FromQuery] string acode = "",
 [FromQuery] string echo_0 = "",
      [FromQuery] string echo_1 = "",
            [FromQuery] string echo_2 = "",
   [FromQuery] string echo_3 = "",
            [FromQuery] string echo_4 = "")
   {
   ToolUtilityClass utility = null;
     
      try
            {
      _logger.LogInformation($"進入付款成功頁面 - OrderId: {order_id}, UID: {uid}, Key: {key}, PRC: {prc}, Cost: {cost}, ActualCost: {actual_cost}, PFN: {pfn}, FinishTime: {finishtime}");

      // 解析交易狀態代碼
    bool isPaymentSuccess = IsSuccessfulPaymentStatus(prc);
            string paymentStatusMessage = GetPaymentStatusMessage(prc);

       // 解析交易完成時間
    DateTime paymentDateTime = ParseFinishTime(finishtime);

        // 基本訊息設定（即使後續處理失敗也要顯示）
                ViewBag.OrderId = order_id;
           ViewBag.UID = uid;
        ViewBag.TransactionKey = key;
        ViewBag.Message = isPaymentSuccess ? "付款成功！感謝您的支持。" : paymentStatusMessage;
           ViewBag.IsSuccess = isPaymentSuccess;
       ViewBag.TransactionId = uid; // 使用 uid 作為交易編號
        ViewBag.Amount = string.IsNullOrWhiteSpace(actual_cost) ? cost : actual_cost;
       ViewBag.PaymentTime = paymentDateTime.ToString("yyyy/MM/dd HH:mm:ss");
     ViewBag.PaymentMethod = GetPaymentMethodName(pfn);
            ViewBag.FeeType = "unknown"; // 預設類型
     ViewBag.CardNo = cardno;
       ViewBag.AuthCode = acode;

  // 如果不是成功狀態，直接返回錯誤頁面
           if (!isPaymentSuccess)
    {
   _logger.LogWarning($"PaymentSuccess: 交易狀態非成功 - OrderId: {order_id}, PRC: {prc}, Message: {paymentStatusMessage}");
      ViewBag.FullName = "會友";
          ViewBag.DedicationCategory = "付款";
 ViewBag.ErrorCode = prc;
return View("PaymentResult");
          }

      // 如果沒有訂單編號，直接返回基本成功訊息
   if (string.IsNullOrWhiteSpace(order_id))
    {
         _logger.LogWarning("PaymentSuccess: 訂單編號為空");
         ViewBag.FullName = "會友";
      ViewBag.DedicationCategory = "付款";
     return View("PaymentResult");
   }

// 初始化 CRM 工具
  utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);

          // 查詢收費單
       Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);

        if (feeEntity == null)
        {
         _logger.LogWarning($"PaymentSuccess: 找不到對應的收費單 - OrderId: {order_id}");
 ViewBag.FullName = "會友";
  ViewBag.DedicationCategory = "付款";
       return View("PaymentResult");
       }

                _logger.LogInformation($"PaymentSuccess: 找到收費單 - FeeId: {feeEntity.Id}");

   // 從收費單取得連絡人資訊
    var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                string fullName = "會友";
        Entity contactEntity = null;
     
     if (contactId != Guid.Empty)
       {
      contactEntity = utility.RetrieveEntity("contact", contactId);
        if (contactEntity != null)
                    {
      fullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
            }
       }

      // 判斷收費單類型（根據特定欄位判斷是奉獻還是課程繳費）
   FeeType feeType = DetermineFeeType(utility, feeEntity);
    ViewBag.FeeType = feeType.ToString().ToLower();

          // 取得應收金額和實際交易金額
     var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
         decimal amount = shouldPayMoney?.Value ?? 0;
       
    // 優先使用 actual_cost，其次 cost
             if (!string.IsNullOrWhiteSpace(actual_cost) && decimal.TryParse(actual_cost, out decimal parsedActualCost))
       {
             amount = parsedActualCost;
         }
      else if (!string.IsNullOrWhiteSpace(cost) && decimal.TryParse(cost, out decimal parsedCost))
              {
          amount = parsedCost;
       }

         // 設定 ViewBag 基本資訊
            ViewBag.FullName = fullName;
     ViewBag.Amount = amount.ToString("N0");

        // 根據類型設定不同的資訊
         string itemName = "";
         string viewName = "";
      
     if (feeType == FeeType.Dedication)
     {
     // 奉獻類型
    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
         itemName = GetDedicationCategoryName(categoryValue);
   ViewBag.DedicationCategory = itemName;
       ViewBag.Message = "付款成功！感謝您的奉獻。";
          viewName = "PaymentResult"; // 奉獻結果頁面
           }
    else if (feeType == FeeType.Course)
            {
  // 課程繳費類型
         itemName = GetCourseName(utility, feeEntity);
    ViewBag.CourseName = itemName;
    ViewBag.Message = "付款成功！課程繳費已完成。";
        viewName = "CoursePaymentResult"; // 課程繳費結果頁面（如果不存在會 fallback）
   
         // 課程相關額外資訊
      string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
         string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
           ViewBag.CourseSchedule = courseSchedule;
   ViewBag.CourseLocation = courseLocation;
       }
    else
        {
            // 其他類型（一般繳費）
            itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
               ViewBag.ItemName = itemName;
    ViewBag.Message = "付款成功！";
 viewName = "PaymentResult"; // 一般結果頁面
            }

          // 更新收費單狀態（使用高鉅金流參數）
                UpdateFeeEntityForSuccessWithMyPay(utility, feeEntity, order_id, uid, key, cost, actual_cost, prc, pfn, paymentDateTime, cardno, acode);

       // 儲存更新
      utility.UpdateEntity(ref feeEntity);
       _logger.LogInformation($"PaymentSuccess: 成功更新收費單 - FeeId: {feeEntity.Id}");

              // 根據類型發送不同的 LINE 通知
       SendPaymentNotificationByType(utility, feeEntity, order_id, uid, cost, 
 fullName, itemName, feeType, amount, contactEntity);

      // 返回對應的視圖
    return View(viewName);
            }
 catch (Exception ex)
            {
       _logger.LogError(ex, $"PaymentSuccess: 處理付款成功時發生異常 - OrderId: {order_id}, UID: {uid}");
    
          // 即使發生錯誤，仍然顯示成功訊息給用戶（因為付款確實成功了）
      ViewBag.OrderId = order_id;
           ViewBag.Message = "付款成功！感謝您的支持。";
          ViewBag.IsSuccess = true;
    ViewBag.TransactionId = uid;
   ViewBag.Amount = cost;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
    ViewBag.FullName = "會友";
       ViewBag.DedicationCategory = "付款";
         ViewBag.FeeType = "unknown";
     
           return View("PaymentResult");
    }
            finally
         {
  // 確保資源釋放
             utility?.Dispose();
      }
        }

        /// <summary>
        /// 判斷是否為成功的付款狀態
        /// </summary>
  /// <param name="prc">交易回傳碼</param>
        /// <returns>是否成功</returns>
     /// <remarks>
    /// 根據高鉅金流規格：
        /// 250 = 付款成功
        /// 290 = 交易成功但資訊不符
        /// 600 = 結帳完成
        /// </remarks>
        private bool IsSuccessfulPaymentStatus(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc))
     return false;

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
        /// 取得付款狀態訊息
        /// </summary>
        /// <param name="prc">交易回傳碼</param>
        /// <returns>狀態訊息</returns>
        private string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc))
                return "付款狀態未知";

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
        /// <param name="finishtime">格式：YYYYMMDDHHmmss</param>
        /// <returns>DateTime 物件</returns>
        private DateTime ParseFinishTime(string finishtime)
 {
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length != 14)
    return DateTime.Now;

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
        _logger.LogError(ex, $"ParseFinishTime: 解析時間失敗 - FinishTime: {finishtime}");
       return DateTime.Now;
       }
        }

        /// <summary>
        /// 取得付費方法名稱
        /// </summary>
        /// <param name="pfn">付費方法代碼</param>
     /// <returns>付費方法名稱</returns>
        private string GetPaymentMethodName(string pfn)
   {
        if (string.IsNullOrWhiteSpace(pfn))
   return "未知";

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
        /// 更新收費單為付款成功狀態（高鉅金流版本）
    /// </summary>
      /// <param name="toolUtility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單 Entity 物件</param>
   /// <param name="orderId">訂單編號</param>
      /// <param name="uid">Payment Hub 交易流水號</param>
    /// <param name="key">交易驗證碼</param>
        /// <param name="cost">總交易金額</param>
        /// <param name="actualCost">實際交易金額</param>
        /// <param name="prc">交易回傳碼</param>
        /// <param name="pfn">付費方法</param>
        /// <param name="paymentTime">付款時間</param>
        /// <param name="cardno">卡號</param>
        /// <param name="acode">授權碼</param>
      private void UpdateFeeEntityForSuccessWithMyPay(
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
      // 取得應收金額
     var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");

        // 更新付款狀態為「信用卡已繳費」
          toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);

       // 更新實收金額（使用應收金額）
        toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);

     // 計算差額（足額繳費，差額為 0）
         toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));

    // 設定付款日期
             toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);

       // 設定付款方式為信用卡
   toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

      // 更新說明欄位，記錄付款資訊
  var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? "";
      var paymentMethodName = GetPaymentMethodName(pfn);
       var statusMessage = GetPaymentStatusMessage(prc);
         
      var newDescription = $"{originalDescription}{Environment.NewLine}" +
  $"[高鉅金流付款成功]{Environment.NewLine}" +
      $"訂單號: {orderId}{Environment.NewLine}" +
        $"交易流水號(UID): {uid}{Environment.NewLine}" +
         $"交易驗證碼(Key): {key}{Environment.NewLine}" +
               $"交易狀態(PRC): {prc} ({statusMessage}){Environment.NewLine}" +
         $"付款方式(PFN): {paymentMethodName}{Environment.NewLine}" +
          $"交易金額: {cost}{Environment.NewLine}" +
         $"實際金額: {actualCost ?? cost}{Environment.NewLine}" +
     $"卡號: {cardno}{Environment.NewLine}" +
         $"授權碼: {acode}{Environment.NewLine}" +
   $"付款時間: {paymentTime:yyyy-MM-dd HH:mm:ss}";
          
  toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);

    _logger.LogInformation($"UpdateFeeEntityWithMyPay: 已設定收費單更新欄位 - FeeId: {feeEntity.Id}, OrderId: {orderId}, UID: {uid}");
       }
            catch (Exception ex)
      {
      _logger.LogError(ex, $"UpdateFeeEntityWithMyPay: 更新收費單欄位時發生錯誤 - OrderId: {orderId}, UID: {uid}");
    throw;
          }
        }

   /// <summary>
        /// 收費單類型枚舉
        /// </summary>
   private enum FeeType
        {
   /// <summary>奉獻</summary>
            Dedication,
/// <summary>課程繳費</summary>
         Course,
  /// <summary>其他一般繳費</summary>
       Other
        }

  /// <summary>
        /// 判斷收費單類型
        /// </summary>
      /// <param name="utility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單實體</param>
        /// <returns>收費單類型</returns>
    private FeeType DetermineFeeType(ToolUtilityClass utility, Entity feeEntity)
        {
     try
{
                // 方法1: 檢查是否有課程 Lookup 欄位
  var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
    if (courseId != Guid.Empty)
     {
          _logger.LogInformation($"DetermineFeeType: 檢測到課程 ID，判定為課程繳費 - CourseId: {courseId}");
             return FeeType.Course;
         }

       // 方法2: 檢查收費單名稱是否包含課程相關關鍵字
    string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "";
  if (feeName.Contains("課程") || feeName.Contains("報名") || feeName.Contains("學費") || 
     feeName.Contains("培訓") || feeName.Contains("研習"))
       {
        _logger.LogInformation($"DetermineFeeType: 收費單名稱包含課程關鍵字，判定為課程繳費 - Name: {feeName}");
         return FeeType.Course;
  }

    // 方法3: 檢查是否有課程名稱欄位
      string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
   if (!string.IsNullOrWhiteSpace(courseName))
              {
       _logger.LogInformation($"DetermineFeeType: 檢測到課程名稱欄位，判定為課程繳費 - CourseName: {courseName}");
           return FeeType.Course;
      }

      // 方法4: 檢查 new_category 是否為奉獻類別
        int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
          if (categoryValue >= 100000000 && categoryValue <= 100000019)
    {
                 _logger.LogInformation($"DetermineFeeType: 類別值在奉獻範圍內，判定為奉獻 - Category: {categoryValue}");
       return FeeType.Dedication;
        }

            _logger.LogInformation($"DetermineFeeType: 無法明確判定類型，預設為奉獻");
         return FeeType.Dedication;
            }
      catch (Exception ex)
     {
            _logger.LogError(ex, "DetermineFeeType: 判斷收費單類型時發生錯誤，預設為奉獻");
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
          Entity courseEntity = utility.RetrieveEntity("new_course", courseId);
    if (courseEntity != null)
    {
               string courseName = utility.GetEntityStringAttribute(courseEntity, "new_name");
   if (!string.IsNullOrWhiteSpace(courseName))
                {
                 return courseName;
              }
        }
      }

     string courseNameField = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
       if (!string.IsNullOrWhiteSpace(courseNameField))
             {
return courseNameField;
         }

  string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "課程";
                return feeName;
  }
  catch (Exception ex)
         {
              _logger.LogError(ex, "GetCourseName: 取得課程名稱時發生錯誤");
     return "課程";
     }
    }

        /// <summary>
        /// 根據類型發送不同的付款通知
 /// </summary>
        private void SendPaymentNotificationByType(
   ToolUtilityClass utility,
            Entity feeEntity,
    string orderId,
            string transactionId,
            string cost,
         string fullName,
       string itemName,
        FeeType feeType,
       decimal amount,
          Entity contactEntity)
        {
     try
     {
  var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
 if (contactId == Guid.Empty)
                {
         _logger.LogWarning($"SendNotification: 收費單沒有關聯的連絡人 - OrderId: {orderId}");
   return;
     }

           if (contactEntity == null)
         {
    contactEntity = utility.RetrieveEntity("contact", contactId);
           if (contactEntity == null)
        {
  _logger.LogWarning($"SendNotification: 找不到連絡人 - ContactId: {contactId}, OrderId: {orderId}");
           return;
    }
       }

              string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId))
         {
         _logger.LogWarning($"SendNotification: 連絡人沒有 LINE ID - ContactId: {contactId}, OrderId: {orderId}");
          return;
    }

       string message = "";
              if (feeType == FeeType.Dedication)
           {
           message = BuildDedicationSuccessMessage(fullName, orderId, transactionId, amount, itemName, DateTime.Now);
     }
    else if (feeType == FeeType.Course)
   {
          string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
           string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
          
             message = BuildCoursePaymentSuccessMessage(fullName, orderId, transactionId, amount, 
          itemName, courseSchedule, courseLocation, DateTime.Now);
      }
         else
          {
   message = BuildGeneralPaymentSuccessMessage(fullName, orderId, transactionId, amount, itemName, DateTime.Now);
                }

                SendLineMessage(lineId, message);

             _logger.LogInformation($"SendNotification: 已發送付款通知 LINE 訊息 - ContactId: {contactId}, LineId: {lineId}, OrderId: {orderId}, Type: {feeType}");
            }
  catch (Exception ex)
            {
         _logger.LogError(ex, $"SendNotification: 發送 LINE 訊息失敗 - OrderId: {orderId}");
            }
        }

        /// <summary>
        /// 建立奉獻成功訊息內容 (LINE)
        /// </summary>
        private string BuildDedicationSuccessMessage(
            string fullName,
            string orderId,
            string transactionId,
  decimal amount,
     string dedicationCategory,
            DateTime paymentTime)
        {
          var message = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
        message += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
   message += $"付款資訊：{Environment.NewLine}";
            message += $"姓名：{fullName}{Environment.NewLine}";
   message += $"奉獻類別：{dedicationCategory}{Environment.NewLine}";
            message += $"訂單編號：{orderId}{Environment.NewLine}";
            
     if (!string.IsNullOrWhiteSpace(transactionId))
            {
       message += $"交易編號：{transactionId}{Environment.NewLine}";
      }
 
            message += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
   message += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
        message += $"付款方式：信用卡{Environment.NewLine}";
      message += $"{Environment.NewLine}願上帝賜福與您！";
         
return message;
        }

        /// <summary>
     /// 建立課程繳費成功訊息內容 (LINE)
        /// </summary>
        private string BuildCoursePaymentSuccessMessage(
            string fullName,
 string orderId,
    string transactionId,
         decimal amount,
            string courseName,
          string courseSchedule,
            string courseLocation,
            DateTime paymentTime)
        {
          var message = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
       message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
 message += $"您的課程繳費已成功完成！{Environment.NewLine}{Environment.NewLine}";
    message += $"課程資訊：{Environment.NewLine}";
 message += $"姓名：{fullName}{Environment.NewLine}";
message += $"課程名稱：{courseName}{Environment.NewLine}";
       
      if (!string.IsNullOrWhiteSpace(courseSchedule))
        {
             message += $"上課時間：{courseSchedule}{Environment.NewLine}";
       }
            
        if (!string.IsNullOrWhiteSpace(courseLocation))
   {
    message += $"上課地點：{courseLocation}{Environment.NewLine}";
            }
      
         message += $"{Environment.NewLine}付款資訊：{Environment.NewLine}";
          message += $"訂單編號：{orderId}{Environment.NewLine}";
   
     if (!string.IsNullOrWhiteSpace(transactionId))
  {
            message += $"交易編號：{transactionId}{Environment.NewLine}";
   }
          
     message += $"繳費金額：NT$ {amount:N0}{Environment.NewLine}";
        message += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
      message += $"付款方式：信用卡{Environment.NewLine}";
            message += $"{Environment.NewLine}期待在課程中與您相見！";
       
          return message;
        }

        /// <summary>
     /// 建立一般繳費成功訊息內容 (LINE)
        /// </summary>
        private string BuildGeneralPaymentSuccessMessage(
            string fullName,
        string orderId,
  string transactionId,
  decimal amount,
    string itemName,
        DateTime paymentTime)
        {
        var message = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
    message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            message += $"您的付款已成功完成！{Environment.NewLine}{Environment.NewLine}";
   message += $"付款資訊：{Environment.NewLine}";
 message += $"姓名：{fullName}{Environment.NewLine}";
            message += $"項目：{itemName}{Environment.NewLine}";
            message += $"訂單編號：{orderId}{Environment.NewLine}";
   
   if (!string.IsNullOrWhiteSpace(transactionId))
 {
     message += $"交易編號：{transactionId}{Environment.NewLine}";
       }
   
         message += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            message += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
    message += $"付款方式：信用卡{Environment.NewLine}";
  message += $"{Environment.NewLine}感謝您的支持！";
       
            return message;
     }

        /// <summary>
        /// 取得奉獻類別顯示名稱
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

  /// <summary>
        /// 發送 LINE 訊息 (同步)
        /// </summary>
        private void SendLineMessage(string lineId, string message)
        {
          try
            {
     var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
       var pushUtility = new PushUtility(lineMessagingClient);
      pushUtility.SendMessage(lineId, message).Wait();
  
   _logger.LogInformation($"SendLineMessage: LINE 訊息已發送 - LineId: {lineId}");
   }
  catch (Exception ex)
            {
   _logger.LogError(ex, $"SendLineMessage: LINE 訊息發送失敗 - LineId: {lineId}");
  throw;
      }
 }

        /// <summary>
        /// 付款失敗頁面 (供用戶查看結果)
        /// GET /api/MyPay/failure
        /// </summary>
        [HttpGet("failure")]
        public IActionResult PaymentFailure(
            [FromQuery] string order_id = "", 
      [FromQuery] string msg = "",
     [FromQuery] string error_code = "",
            [FromQuery] string ret_code = "")
        {
         ToolUtilityClass utility = null;
     
      try
       {
          _logger.LogWarning($"進入付款失敗頁面 - OrderId: {order_id}, ErrorCode: {error_code}, RetCode: {ret_code}, Message: {msg}");

     ViewBag.OrderId = order_id;
                ViewBag.IsSuccess = false;
 ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
   ViewBag.ErrorCode = error_code;
 ViewBag.RetCode = ret_code;

             string detailedMessage = BuildFailureMessage(msg, error_code, ret_code);
          ViewBag.Message = detailedMessage;

        ViewBag.FullName = "會友";
             ViewBag.DedicationCategory = "奉獻";
                ViewBag.Amount = "0";

       if (!string.IsNullOrWhiteSpace(order_id))
   {
      try
      {
       utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
           Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);
  
        if (feeEntity != null)
       {
  _logger.LogInformation($"PaymentFailure: 找到對應的收費? - FeeId: {feeEntity.Id}");

   var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
               if (contactId != Guid.Empty)
     {
          Entity contactEntity = utility.RetrieveEntity("contact", contactId);
               if (contactEntity != null)
        {
            ViewBag.FullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
        }
        }

            int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
             ViewBag.DedicationCategory = GetDedicationCategoryName(categoryValue);

      var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
          if (shouldPayMoney != null)
           {
             ViewBag.Amount = shouldPayMoney.Value.ToString("N0");
            }

           UpdateFeeEntityForFailure(utility, feeEntity, order_id, msg, error_code, ret_code);
       }
                 else
       {
     _logger.LogWarning($"PaymentFailure: 找不到對應的收費單 - OrderId: {order_id}");
               }
           }
      catch (Exception ex)
      {
      _logger.LogError(ex, $"PaymentFailure: 查詢 CRM 資料時發生錯誤 - OrderId: {order_id}");
       }
            }

 return View("PaymentResult");
            }
catch (Exception ex)
  {
        _logger.LogError(ex, $"PaymentFailure: 處理付款失敗頁面時發生異常 - OrderId: {order_id}");
             
                ViewBag.OrderId = order_id;
            ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請稍後再試或聯繫教會辦公室。";
      ViewBag.IsSuccess = false;
 ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
   ViewBag.FullName = "會友";
     ViewBag.DedicationCategory = "奉獻";
    ViewBag.Amount = "0";
      
   return View("PaymentResult");
 }
finally
{
    utility?.Dispose();
     }
        }

        private string BuildFailureMessage(string msg, string errorCode, string retCode)
        {
   var message = "付款失敗";

          if (!string.IsNullOrWhiteSpace(msg))
        {
  message = $"付款失敗：{msg}";
         }
            else if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(retCode))
        {
  string friendlyMessage = GetFriendlyErrorMessage(errorCode, retCode);
                if (!string.IsNullOrWhiteSpace(friendlyMessage))
     {
        message = $"付款失敗：{friendlyMessage}";
       }
 else
       {
         message = $"付款失敗 (錯誤代碼: {errorCode ?? retCode})";
                }
            }
            else
            {
          message = "付款失敗，請稍後再試或聯繫教會辦公室。";
            }

            return message;
        }

 private string GetFriendlyErrorMessage(string errorCode, string retCode)
        {
            string code = errorCode ?? retCode ?? "";

            switch (code.ToUpper())
         {
        case "CARD_DECLINED":
        case "51":
  return "信用卡被拒絕，請確認卡片狀態或聯繫發卡銀行";
            case "INSUFFICIENT_FUNDS":
       case "05":
return "信用卡額度不足，請使用其他卡片或聯繫發卡銀行";
   case "EXPIRED_CARD":
       case "54":
        return "信用卡已過期，請使用其他有效卡片";
 case "INVALID_CARD":
       case "14":
     return "信用卡號碼錯誤，請檢查卡號是否正確";
     case "INVALID_CVV":
        case "CVV_ERROR":
 return "安全碼(CVV)錯誤，請重新輸入";
    case "CARD_LOST_STOLEN":
   case "43":
   return "此卡片已被列為遺失或被盜，請聯繫發卡銀行";
     case "TRANSACTION_NOT_PERMITTED":
         case "57":
        return "此交易不被允許，請聯繫發卡銀行";
    case "EXCEEDED_LIMIT":
                case "61":
           return "超過信用卡交易限額，請聯繫發卡銀行";
              case "TIMEOUT":
      case "NETWORK_ERROR":
 return "連線逾時或網路錯誤，請稍後再試";
        case "SYSTEM_ERROR":
    case "96":
          return "系統錯誤，請稍後再試或聯繫客服";
   case "CANCELLED":
     case "USER_CANCELLED":
    return "交易已被取消";
  case "3D_SECURE_FAILED":
       case "3DS_FAILED":
       return "3D驗證失敗，請重新進行驗證";
  default:
   return null;
         }
        }

        private void UpdateFeeEntityForFailure(
   ToolUtilityClass toolUtility,
            Entity feeEntity,
            string orderId,
            string errorMessage,
            string errorCode,
            string retCode)
        {
            try
            {
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? "";
             var failureInfo = $"{originalDescription}{Environment.NewLine}" +
      $"[高鉅金流付款失敗] 訂單號: {orderId}, " +
    $"時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, " +
          $"錯誤訊息: {errorMessage ?? "未提供"}, " +
        $"錯誤代碼: {errorCode ?? retCode ?? "未提供"}";
          
  toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", failureInfo);
      toolUtility.UpdateEntity(ref feeEntity);
      
     _logger.LogInformation($"UpdateFeeEntityForFailure: 已記錄付款失敗資訊 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
        }
catch (Exception ex)
  {
 _logger.LogError(ex, $"UpdateFeeEntityForFailure: 更新收費單失敗資訊時發生錯誤 - OrderId: {orderId}");
            }
        }
    }
}