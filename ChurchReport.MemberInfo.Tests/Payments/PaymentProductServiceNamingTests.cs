/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 ChurchReport 的付款後產品流程服務不再以單一 provider 命名。 CRM 更新、LINE 通知、付款訊息組裝都屬於 ChurchReport 產品流程， 
 * 服務名稱應表達「產品付款後處理」而不是誤導成 MyPay 金流協定實作。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentProductServiceNamingTests。
 * - 保護 CRM 更新、費別、回呼日誌、訊息與通知服務的中性型別名稱。 在真實產品組件逐一查詢固定清單，任一服務遺失就失敗；不建立服務實例或保存付款資料。
 * - 保護供應商名稱不回流到產品流程服務。 在已載入的產品組件逐項查詢舊服務名稱，模擬重新引入相容包裝；斷言全部不存在，不能透過不存在的組件取得假綠燈。
 * 測試案例入口包含 Product_payment_services_use_provider_neutral_names、Legacy_mypay_product_service_names_are_removed。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 的付款後產品流程服務不再以單一 provider 命名。
/// CRM 更新、LINE 通知、付款訊息組裝都屬於 ChurchReport 產品流程，
/// 服務名稱應表達「產品付款後處理」而不是誤導成 MyPay 金流協定實作。
/// </summary>
public sealed class PaymentProductServiceNamingTests
{
    /// <summary>
    /// 保護 CRM 更新、費別、回呼日誌、訊息與通知服務的中性型別名稱。
    /// 在真實產品組件逐一查詢固定清單，任一服務遺失就失敗；不建立服務實例或保存付款資料。
    /// </summary>
    [Fact]
    public void Product_payment_services_use_provider_neutral_names()
    {
        var expectedServiceNames = new[]
        {
            "ChurchReport.Services.PaymentCrmService",
            "ChurchReport.Services.PaymentFeeTypeHelper",
            "ChurchReport.Services.PaymentCallbackLogger",
            "ChurchReport.Services.PaymentMessageBuilder",
            "ChurchReport.Services.PaymentNotificationService"
        };

        foreach (var expectedServiceName in expectedServiceNames)
        {
            typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType(expectedServiceName).Should().NotBeNull(
                "ChurchReport 付款後流程服務應以 provider-neutral 名稱呈現，避免把 CRM/LINE/收費單流程誤認為 MyPay 協定實作");
        }
    }

    /// <summary>
    /// 保護供應商名稱不回流到產品流程服務。
    /// 在已載入的產品組件逐項查詢舊服務名稱，模擬重新引入相容包裝；斷言全部不存在，不能透過不存在的組件取得假綠燈。
    /// </summary>
    [Fact]
    public void Legacy_mypay_product_service_names_are_removed()
    {
        var legacyServiceNames = new[]
        {
            "ChurchReport.Services.MyPayCrmService",
            "ChurchReport.Services.MyPayFeeTypeHelper",
            "ChurchReport.Services.MyPayLogger",
            "ChurchReport.Services.MyPayMessageBuilder",
            "ChurchReport.Services.MyPayNotificationService"
        };

        foreach (var legacyServiceName in legacyServiceNames)
        {
            typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType(legacyServiceName).Should().BeNull(
                "這些服務已經只消費 provider-neutral payment result，不應保留 MyPay 開頭的產品流程型別名稱");
        }
    }
}
