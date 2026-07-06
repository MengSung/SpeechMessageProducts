// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.DedicationBooking.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentProcessor
// 主要成員：CreateDedicationBooking、SetDedicationBookingParameter、SetDedicationBookingAmounts、SetDedicationBookingDates、AssignDedicationBookingOwner
// 引用命名空間：ChurchReport.Models、Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using System;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 認獻單管理模組
    ///
    /// 【職責】
    /// - 建立認獻單
    /// - 設定認獻單參數
    /// - 定期定額扣款處理
    ///
    /// 【設計原則】
    /// - 單一職責：專注於認獻單生命週期管理
    /// - 開放封閉：易於擴展新的認獻類型
    /// </summary>
    public partial class DonationPaymentProcessor
    {
        #region ===== 建立認獻單 =====

        /// <summary>
        /// 建立認獻單實體
        /// </summary>
        public Guid CreateDedicationBooking(Entity aContact, DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                var dedicationBookingEntity = new Entity("new_dedication_booking");

                // 設定認獻單參數
                SetDedicationBookingParameter(aContact, dedicationBookingEntity, DonationPaymentFormModel);

                // 建立認獻單
                var dedicationBookingId = ToolUtility.CreateEntity(dedicationBookingEntity);
                var retrievedDedicationBooking = ToolUtility.RetrieveEntity("new_dedication_booking", dedicationBookingId);

                // 指派負責人
                AssignDedicationBookingOwner(retrievedDedicationBooking, aContact);

                return dedicationBookingId;
            }
            catch (Exception ex)
            {
                var errorMsg = $"建立認獻單失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        /// <summary>
        /// 設定認獻單參數
        /// </summary>
        public void SetDedicationBookingParameter(Entity aContact, Entity aDedicationBookingToCreated, DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                // 基本資訊
                var fullName = ToolUtility.GetEntityStringAttribute(ref aContact, "fullname") + "奉獻";
                ToolUtility.SetEntityStringAttribute(ref aDedicationBookingToCreated, "new_name", fullName);

                // 連絡人關聯
                ToolUtility.SetEntityLookUpAttribute(ref aDedicationBookingToCreated, "new_contact_new_dedication_booking", "contact", aContact.Id);

                // 認獻單狀態 = 尚未啟動
                ToolUtility.SetOptionSetAttribute(ref aDedicationBookingToCreated, "new_dedication_booking_status", 100000000);

                // 金額設定
                SetDedicationBookingAmounts(ref aDedicationBookingToCreated, DonationPaymentFormModel);

                // 日期設定
                SetDedicationBookingDates(ref aDedicationBookingToCreated, DonationPaymentFormModel);

                // 奉獻類別
                SetPayCategory(DonationPaymentFormModel.Category, "new_dedication_category", ref aDedicationBookingToCreated);

                // 奉獻備註
                ToolUtility.SetEntityStringAttribute(ref aDedicationBookingToCreated, "new_explain", DonationPaymentFormModel.Explain);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"設定認獻單參數失敗: {ex.Message}", ex);
            }
        }

        #endregion

        #region ===== 私有輔助方法 =====

        /// <summary>
        /// 設定認獻單金額
        /// </summary>
        private void SetDedicationBookingAmounts(ref Entity aDedicationBookingToCreated, DonationPaymentFormModel DonationPaymentFormModel)
        {
            // 每期金額
            ToolUtility.SetEntityMoneyAttribute(ref aDedicationBookingToCreated, "new_amount_per_stage", new Money(DonationPaymentFormModel.Amount));

            // 總期數
            ToolUtility.SetEntityStringAttribute(ref aDedicationBookingToCreated, "new_total_stages", DonationPaymentFormModel.DeductTotalNumber);

            // 應收金額（每期金額 × 總期數）
            var totalAmount = DonationPaymentFormModel.Amount * TransferToDeductTotalNum(DonationPaymentFormModel.DeductTotalNumber);
            ToolUtility.SetEntityMoneyAttribute(ref aDedicationBookingToCreated, "new_dedication_amount", new Money(totalAmount));
        }

        /// <summary>
        /// 設定認獻單日期
        /// </summary>
        private void SetDedicationBookingDates(ref Entity aDedicationBookingToCreated, DonationPaymentFormModel DonationPaymentFormModel)
        {
            // 開始日期
            ToolUtility.SetEntityDateTimeAttribute(ref aDedicationBookingToCreated, "new_dedication_start_date", DateTime.Now);

            // 結束日期（根據總期數計算）
            var deductMonths = TransferToDeductTotalNum(DonationPaymentFormModel.DeductTotalNumber);
            ToolUtility.SetEntityDateTimeAttribute(ref aDedicationBookingToCreated, "new_dedication_end_date", DateTime.Now.AddMonths(deductMonths));
        }

        /// <summary>
        /// 指派認獻單負責人
        /// </summary>
        private void AssignDedicationBookingOwner(Entity retrievedDedicationBooking, Entity aContact)
        {
            if (retrievedDedicationBooking != null && aContact != null)
            {
                try
                {
                    ToolUtility.AssignOwner("new_dedication_booking", retrievedDedicationBooking, ToolUtility.GetOwnerId(aContact));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] 指派認獻單負責人失敗: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
