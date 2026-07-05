// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/FeeOperations/FeeService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class FeeService
// 主要成員：RetrieveDedicationBooking、RetrieveFee、RetrieveDedicationFee、RetrieveDedicationFeeByDateRange
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.FeeOperations
{
    public class FeeService : IFeeService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public FeeService(object logger, IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveDedicationBooking(string contactName, string contactId)
        {
            // Placeholder implementation
            return new EntityCollection();
        }

        public EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            // Placeholder implementation
            return new EntityCollection();
        }

        /// <summary>
        /// 根據連絡人查詢奉獻收費單
        /// </summary>
        public EntityCollection RetrieveDedicationFee(string contactName, string contactId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_fee_shoud_pay' />
                            <attribute name='new_fee_really_paid' />
                            <attribute name='new_pay_way' />
                            <attribute name='new_category' />
                            <attribute name='new_others' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_fee' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                              <condition attribute='new_category' operator='not-null' />
                            </filter>
                          </entity>
                        </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據連絡人和日期範圍查詢奉獻收費單
        /// </summary>
        public EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            string startDateString = $"'{startDate:yyyy-M-d}'";
            string endDateString = $"'{endDate:yyyy-M-d}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                                  <entity name='new_fee'>
                                    <attribute name='new_feeid' />
                                    <attribute name='new_name' />
                                    <attribute name='createdon' />
                                    <attribute name='new_pay_date' />
                                    <attribute name='new_fee_shoud_pay' />
                                    <attribute name='new_fee_really_paid' />
                                    <attribute name='new_pay_way' />
                                    <attribute name='new_category' />
                                    <attribute name='new_others' />
                                    <attribute name='new_paid_period' />
                                    <order attribute='new_name' descending='false' />
                                    <filter type='and'>
                                      <condition attribute='new_contact_new_fee' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                                      <condition attribute='new_category' operator='not-null' />
                                      <condition attribute='new_pay_status' operator='in'>
                                        <value>100000001</value>
                                        <value>100000002</value>
                                        <value>100000003</value>
                                        <value>100000004</value>
                                        <value>100000006</value>
                                      </condition>
                                      <condition attribute='new_pay_date' operator='on-or-after'  value={startDateString} />
                                      <condition attribute='new_pay_date' operator='on-or-before' value={endDateString} />
                                    </filter>
                                  </entity>
                                </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}