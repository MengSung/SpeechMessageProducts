using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// FetchXML 查詢服務介面
    /// </summary>
    public interface IFetchXmlQueryService
    {
        /// <summary>
        /// 根據聯絡人查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId);

        /// <summary>
        /// 根據課程查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(string lessonName, string lessonId);

        /// <summary>
        /// 根據聯絡人查詢認獻記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId);

        /// <summary>
        /// 根據主日日期查詢聚會統計記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate);

        /// <summary>
        /// 根據認獻預約和繳費期間查詢收費單 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod);

        /// <summary>
        /// 查詢所有需要點名的小組名單 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveListByFetchXml();

        /// <summary>
        /// 查詢所有小組名單集合 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveSmallGroupListCollectionByFetchXml();
    }
}
