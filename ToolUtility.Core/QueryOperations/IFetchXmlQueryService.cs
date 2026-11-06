using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 七個舊Fetch入口的純SDK契約；host先驗授權scope，各結果是本次操作擁有的SDK頁，不是共享快照。
    /// 整頁正式ID／表／重複／top驗證成功才回傳；保留分頁metadata，不按名稱去重或把top頁宣稱完整全表。
    /// 真正SDK故障由最終host共用告警，介面不保存logger、credential、連線或結果。
    /// </summary>
    public interface IFetchXmlQueryService
    {
        /// <summary>依聯絡人正式GUID讀學習課，top1000，保留原無classification限制查詢；名稱只作安全編碼顯示提示。</summary>
        EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId);

        /// <summary>依課程正式GUID讀學習課，top1000，保留三種enroll排除及statuscode非2；不使用名稱作身份。</summary>
        EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(string lessonName, string lessonId);

        /// <summary>以非空聯絡人GUID讀啟用且核准的認獻單頁，委派唯一Fee邏輯；顯示名稱不能擴張scope。</summary>
        EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId);

        /// <summary>以caller已決定的西曆日期查聚會統計，top100且保留原狀態；不在此推導教會時區。</summary>
        EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate);

        /// <summary>依正式認獻GUID及非空期數讀啟用收費單頁，top500；所有外部文字只能作XML屬性值。</summary>
        EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod);

        /// <summary>讀原點名名單的一頁，top500；保留原purpose/app/status/state，不套用小組集合特有排除條件。</summary>
        EntityCollection RetrieveListByFetchXml();

        /// <summary>讀含原兩個lookup欄位及測試名稱排除的小組單頁，top500；穩定資料庫ID衝突整頁拒絕。</summary>
        EntityCollection RetrieveSmallGroupListCollectionByFetchXml();
    }
}
