/*
 * 檔案整體說明
 *
 * 目的與定位：奉獻付款回傳頁面的 MVC 呈現器。 這個類別只負責 ChurchReport 的 ViewBag 與 View 路徑，刻意不放到共用金流核心， 因為不同產品會有各自的頁面、欄位名稱與顯示文案。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentReturnPresenter。
 * - 設定付款成功頁需要的 ViewBag 欄位，讓 processor 不再散落重複的畫面設定。
 * - 設定付款失敗頁需要的 ViewBag 欄位，保留 ChurchReport 現有付款結果頁。
 * 主要操作入口包含 PresentSuccess、PresentFailure。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Startup.cs、
 * SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
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
