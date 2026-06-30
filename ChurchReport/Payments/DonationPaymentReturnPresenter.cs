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
    private const string PaymentResultView = "~/Views/QPayCard/PaymentResult.cshtml";

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
