// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/DonationPaymentReturnPresenter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentReturnPresenter
// 主要成員：PresentSuccess、PresentFailure
// 引用命名空間：System、Microsoft.AspNetCore.Mvc
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

/// <summary>
/// 奉獻付款回傳頁面的 MVC 呈現器。
/// 這個類別只負責 ChurchReport 的 ViewBag 與 View 路徑，刻意不放到共用金流核心，
/// 因為不同產品會有各自的頁面、欄位名稱與顯示文案。
/// </summary>
public sealed class DonationPaymentReturnPresenter
{
    private const string PaymentResultView = "~/Views/PaymentReturn/PaymentResult.cshtml";

    /// <summary>
    /// 設定付款成功頁需要的 ViewBag 欄位，讓 processor 不再散落重複的畫面設定。
    /// </summary>
    public IActionResult PresentSuccess(
        Controller controller,
        string fullName,
        string amount,
        string orderId,
        string transactionId,
        string dedicationCategory,
        string message)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        controller.ViewBag.IsSuccess = true;
        controller.ViewBag.Message = message;
        controller.ViewBag.FullName = fullName;
        controller.ViewBag.Amount = amount;
        controller.ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
        controller.ViewBag.OrderId = orderId;
        controller.ViewBag.TransactionId = transactionId;
        controller.ViewBag.PaymentMethod = "信用卡";
        controller.ViewBag.DedicationCategory = dedicationCategory;

        return controller.View(PaymentResultView);
    }

    /// <summary>
    /// 設定付款失敗頁需要的 ViewBag 欄位，保留 ChurchReport 現有付款結果頁。
    /// </summary>
    public IActionResult PresentFailure(
        Controller controller,
        string fullName,
        string orderId,
        string errorDetails,
        string message)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        controller.ViewBag.IsSuccess = false;
        controller.ViewBag.Message = message;
        controller.ViewBag.FullName = fullName;
        controller.ViewBag.OrderId = orderId;
        controller.ViewBag.ErrorDetails = errorDetails;

        return controller.View(PaymentResultView);
    }
}
