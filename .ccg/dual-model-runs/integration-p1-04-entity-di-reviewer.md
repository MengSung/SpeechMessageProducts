# CCG reviewer Task: integration-p1-04-entity-di

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
唯讀程式碼審查：P1-04 Fee安全實作與雙目標抽取切片。只審查本提示附帶內容，不自行擴讀其他檔案（其他舊來源可能有機密）。不改檔，不執行程式，不洩漏合成輸入以外的資訊。
要求：保持公開namespace/API，修復原Fee兩個空方法，復用有效Query路徑；三新增查詢還原舊來源欄位/條件。SDK借入不Dispose、不跨租戶singleton，所有XML值安全編碼，所有結果ID非空/實體符合/不重複，合法同名不同ID保留；原SDK例外向host最終通知邊界原樣傳遞。不自建cache/背景工作。net462/net10 strict，tests net48載net462/net10，保留所有host測試。
證據：搬移前正常54/54；搬後net48 51/51、net10 118/118，Core兩目標build0警告0錯誤；Core target覆蓋率95.60%和100%。這不是實機測試。已知net10測試有host依賴System.Security.Permissions版本衝突，正在修復。全階段驗收0/12。
審查重點：正確性/相容/查詢安全/ID完整發布/資料隔離/生命週期/測試盲點/分頁。請輸出Critical/High/Warning/Info，各finding給具體來源行/失敗路徑/修正與測試，不要將分析當成實機或完整P1通過。以下包含完整來源與固定基準diff，避免外部HEAD前進使審查包空白。

## ToolUtility.Core/FeeOperations/FeeService.cs
```
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.FeeOperations
{
    /// <summary>
    /// 收費與認獻的純 SDK 查詢；所有條件樹、候選集合及識別碼驗證均屬單次操作。
    /// 本服務不做授權：呼叫端必須先在伺服器確認教會／使用者及資料範圍，字串名稱不構成權限。
    /// </summary>
    /// <remarks>
    /// 只借入已授權的 IOrganizationService，不取得 Dispose 責任；服務應由同一 host scope 擁有，
    /// 不可註冊跨租戶 singleton。相容 logger 不保存，沒有 cache、timer、queue 或背景 task。
    /// 回傳一個 SDK 查詢頁，保留 MoreRecords／PagingCookie，由原呼叫者繼續原有分頁流程；
    /// 發布前驗證整頁穩定 ID，不按名稱去重或改寫候選。SDK 故障原樣向最終 host 通知邊界傳遞。
    /// </remarks>
    public class FeeService : IFeeService
    {
        // 僅共享固定的結構欄位名稱；唯讀包裝不外洩底層陣列，且不包含教會、使用者或查詢資料。
        // 這三份有限常數活至組件卸載，不將每次呼叫的 XML／結果提升為共用狀態。
        private static readonly ReadOnlyCollection<string> DedicationBookingFields = Array.AsReadOnly(new[]
        {
            "new_dedication_bookingid", "new_name", "createdon", "new_dedication_booking_status"
        });
        private static readonly ReadOnlyCollection<string> FeeFields =
            Array.AsReadOnly(new[] { "new_feeid", "new_name", "createdon", "new_paid_period" });
        private static readonly ReadOnlyCollection<string> OrderFeeFields =
            Array.AsReadOnly(new[] { "new_feeid", "new_name", "createdon" });
        private readonly IOrganizationService _organizationService;

        /// <summary>
        /// 建立與呼叫端相同授權／連線生命週期的查詢器，不接管借入 SDK 服務。
        /// </summary>
        /// <param name="organizationService">伺服器已確認組織與身分的 SDK 服務；不可為 null。</param>
        public FeeService(IOrganizationService organizationService)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 保留既有建構 API，但不保存任意 host logger，避免無用參照延長 request 或憑證生命週期。
        /// 本類不吞例外，故不在純核心重建日誌／LINE client；最終 host 負責共用通知。
        /// </summary>
        /// <param name="logger">僅為來源相容保留，值不使用、不保存，也不 Dispose。</param>
        /// <param name="organizationService">借入的已授權 SDK 服務。</param>
        public FeeService(object? logger, IOrganizationService organizationService) : this(organizationService)
        {
        }

        /// <summary>
        /// 讀取啟用且已核准的認獻，使用現有 FetchXmlQueryService 的有效條件，不再回傳假空集合。
        /// 保留原上限 1000、createdon 倒序及投影欄位；名稱只供 lookup 顯示，ID 才是查詢條件。
        /// </summary>
        /// <param name="contactName">可含 XML 特殊字元的顯示名稱，不作授權。</param>
        /// <param name="contactId">非空資料庫聯絡人 GUID。</param>
        /// <returns>已完成整頁 ID 驗證、由呼叫端擁有的 SDK 查詢頁。</returns>
        public EntityCollection RetrieveDedicationBooking(string contactName, string contactId)
        {
            var fetch = CreateFetch("new_dedication_booking", DedicationBookingFields, "createdon", true, 1000);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Lookup("new_contact_new_dedication_booking", contactName, "contact", contactId, nameof(contactId)),
                Condition("new_dedication_booking_status", "eq", "100000001"),
                Condition("statecode", "eq", "0")));
            return Query(fetch, "new_dedication_booking", false);
        }

        /// <summary>
        /// 按認獻資料庫 ID 與已繳期數讀取啟用收費單，沿用有效舊查詢的 500 筆上限與建立時間排序。
        /// 所有外部文字由 XML writer 編碼，不能改變元素、條件或 OR／AND 結構。
        /// </summary>
        /// <param name="dedicationBookingName">認獻顯示名稱，不作識別或授權。</param>
        /// <param name="dedicationBookingId">非空認獻資料庫 GUID。</param>
        /// <param name="paidPeriod">原有期數條件文字，空白拒絕以免變成廣泛查詢。</param>
        /// <returns>完整驗證的單頁 SDK 收費結果，不保存於服務。</returns>
        public EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            RequireText(paidPeriod, nameof(paidPeriod));
            var fetch = CreateFetch("new_fee", FeeFields,
                "createdon", true, 500);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Lookup("new_dedication_booking_new_fee", dedicationBookingName, "new_dedication_booking",
                    dedicationBookingId, nameof(dedicationBookingId)),
                Condition("new_paid_period", "eq", paidPeriod), Condition("statecode", "eq", "0")));
            return Query(fetch, "new_fee", false);
        }

        /// <summary>
        /// 保留奉獻查詢既有欄位、名稱排序與分類非空條件，只修正 XML 安全及發布前 ID 驗證。
        /// 每次呼叫直接使用借入 SDK，不建立跨教會可變狀態或保留回傳集合。
        /// </summary>
        /// <param name="contactName">lookup 顯示名稱，可含特殊字元。</param>
        /// <param name="contactId">非空資料庫聯絡人 GUID。</param>
        /// <returns>原 SDK 單頁結果；同名不同 ID 完整保留，ID 衝突整頁拒絕。</returns>
        public EntityCollection RetrieveDedicationFee(string contactName, string contactId)
        {
            return Query(CreateDedicationFetch(contactName, contactId, false), "new_fee", true);
        }

        /// <summary>
        /// 在原奉獻查詢加上含端點日期與原付款狀態白名單；日期是呼叫端已決定的業務日期，
        /// 以 InvariantCulture 的西曆輸出，不作 UTC／時區偏移，也不修改 ambient culture。
        /// </summary>
        /// <param name="contactName">lookup 顯示名稱，不作授權。</param>
        /// <param name="contactId">非空聯絡人 GUID。</param>
        /// <param name="startDate">含端點起日，不大於迄日。</param>
        /// <param name="endDate">含端點迄日。</param>
        /// <returns>完整驗證的一頁結果；原 SDK 失敗不改成空集合。</returns>
        public EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate)
        {
            if (startDate.Date > endDate.Date) throw new ArgumentException("Invalid date range.", nameof(endDate));
            var fetch = CreateDedicationFetch(contactName, contactId, true);
            var status = new XElement("condition", new XAttribute("attribute", "new_pay_status"), new XAttribute("operator", "in"));
            foreach (var value in new[] { "100000001", "100000002", "100000003", "100000004", "100000006" })
                status.Add(new XElement("value", value));
            fetch.Element("entity")!.Element("filter")!.Add(status,
                Condition("new_pay_date", "on-or-after", startDate.ToString("yyyy-M-d", CultureInfo.InvariantCulture)),
                Condition("new_pay_date", "on-or-before", endDate.ToString("yyyy-M-d", CultureInfo.InvariantCulture)));
            return Query(fetch, "new_fee", true);
        }

        /// <summary>
        /// 依學習課紀錄 ID 查已繳收費單，還原舊來源的 pay_date 非空條件及投影／排序。
        /// 名稱參數原本未使用，保留相容但不把名稱插入查詢或當作授權界線。
        /// </summary>
        /// <param name="storLessonName">僅為舊 API 相容保留，不保存或使用。</param>
        /// <param name="storLessonId">非空學習課紀錄資料庫 GUID。</param>
        /// <returns>完整驗證、保留原分頁資訊的已繳收費頁。</returns>
        public EntityCollection GetPaidFee(string storLessonName, string storLessonId)
        {
            return Query(CreateLessonFeeFetch(storLessonId, true), "new_fee", false);
        }

        /// <summary>
        /// 依學習課紀錄 ID 查未繳收費單，還原舊來源 pay_date 為 null 的條件，不自行推導付款狀態。
        /// 同名不同資料庫 ID 保留，任何重複／欠缺 ID 在回傳前拒絕整頁。
        /// </summary>
        /// <param name="storLessonName">僅為舊 API 相容保留，不保存或使用。</param>
        /// <param name="storLessonId">非空學習課紀錄資料庫 GUID。</param>
        /// <returns>原投影及排序的完整 SDK 查詢頁。</returns>
        public EntityCollection GetNotYetPaidFee(string storLessonName, string storLessonId)
        {
            return Query(CreateLessonFeeFetch(storLessonId, false), "new_fee", false);
        }

        /// <summary>
        /// 使用同一訂單文字比對信用卡或 ATM 訂單欄位；保留 AND 內 OR 結構，名稱不是唯一鍵。
        /// 文字只作 XAttribute 值，不接受呼叫端 XML；空白拒絕，特殊字元可安全逐字還原。
        /// </summary>
        /// <param name="orderNumber">非空訂單號；仍須由 host 驗證該訂單屬於授權範圍。</param>
        /// <returns>已驗 ID 的單頁收費資料，不按訂單文字合併不同記錄。</returns>
        public EntityCollection RetrieveFeeByFetchXmlOrderNumber(string orderNumber)
        {
            RequireText(orderNumber, nameof(orderNumber));
            var fetch = CreateFetch("new_fee", OrderFeeFields, "new_name", false);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                new XElement("filter", new XAttribute("type", "or"),
                    Condition("new_q_pay_card_order_no", "eq", orderNumber),
                    Condition("new_q_pay_order_atm_no", "eq", orderNumber))));
            return Query(fetch, "new_fee", false);
        }

        /// <summary>建立單次奉獻查詢候選；字串陣列每次配置，不公開或保留跨操作的可變欄位集合。</summary>
        private static XElement CreateDedicationFetch(string name, string id, bool dateRange)
        {
            var fields = new List<string> { "new_feeid", "new_name", "createdon", "new_pay_date", "new_fee_shoud_pay",
                "new_fee_really_paid", "new_pay_way", "new_category", "new_others" };
            if (dateRange) fields.Add("new_paid_period");
            var fetch = CreateFetch("new_fee", fields, "new_name", false);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Lookup("new_contact_new_fee", name, "contact", id, "contactId"),
                new XElement("condition", new XAttribute("attribute", "new_category"), new XAttribute("operator", "not-null"))));
            return fetch;
        }

        /// <summary>依舊來源建立已繳／未繳的共用條件，僅差投影及 null 運算子，不插入未使用顯示名稱。</summary>
        private static XElement CreateLessonFeeFetch(string id, bool paid)
        {
            var validId = RequireId(id, "storLessonId");
            var fields = paid ? new[] { "new_feeid", "new_name", "new_pay_date", "new_fee_really_paid" }
                : new[] { "new_feeid", "new_name", "new_fee_amount" };
            var fetch = CreateFetch("new_fee", fields, "new_name", false);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Condition("new_stor_lessons_new_fee", "eq", validId),
                new XElement("condition", new XAttribute("attribute", "new_pay_date"), new XAttribute("operator", paid ? "not-null" : "null"))));
            return fetch;
        }

        /// <summary>只接受程式碼擁有的結構／欄位名稱；每次建立新的有限條件樹，不共享可變 XML。</summary>
        private static XElement CreateFetch(string entityName, IEnumerable<string> fields, string order, bool descending, int? top = null)
        {
            var entity = new XElement("entity", new XAttribute("name", entityName));
            foreach (var field in fields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", order), new XAttribute("descending", descending ? "true" : "false")));
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"));
            if (top.HasValue) fetch.Add(new XAttribute("top", top.Value));
            fetch.Add(entity);
            return fetch;
        }

        /// <summary>固定結構名稱與運算子只來自程式；資料值由 XML 編碼，不以字串拼接插入結構。</summary>
        private static XElement Condition(string field, string operation, string value)
        {
            return new XElement("condition", new XAttribute("attribute", field), new XAttribute("operator", operation), new XAttribute("value", value));
        }

        /// <summary>先驗非空資料庫 GUID 再包為原有 lookup 形式；顯示名稱安全編碼且不構成權限。</summary>
        private static XElement Lookup(string field, string name, string entityType, string id, string parameterName)
        {
            return new XElement("condition", new XAttribute("attribute", field), new XAttribute("operator", "eq"),
                new XAttribute("uiname", name ?? string.Empty), new XAttribute("uitype", entityType),
                new XAttribute("value", "{" + RequireId(id, parameterName) + "}"));
        }

        /// <summary>拒絕空／非法識別碼且不回顯輸入；統一成 D 格式，名稱或暫存索引不能代替資料庫 ID。</summary>
        private static string RequireId(string value, string parameterName)
        {
            Guid id;
            if (!Guid.TryParse(value, out id) || id == Guid.Empty) throw new ArgumentException("A non-empty database identifier is required.", parameterName);
            return id.ToString("D");
        }

        /// <summary>阻擋空白查詢條件；不回顯文字，原字元及大小寫由資料庫原契約決定。</summary>
        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty query value is required.", parameterName);
        }

        /// <summary>
        /// 保留原操作所用SDK入口，完成I/O後驗證整頁才一次回傳；HashSet僅活至本次呼叫結束。
        /// 不存結果、不釋放借入服務、不攔截真實SDK例外；資料衝突不含ID／欄位本文且不靜默去重。
        /// </summary>
        private EntityCollection Query(XElement fetch, string entityName, bool direct)
        {
            var expression = new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting));
            var candidate = direct ? _organizationService.RetrieveMultiple(expression)
                : ((RetrieveMultipleResponse)_organizationService.Execute(new RetrieveMultipleRequest { Query = expression })).EntityCollection;
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
            {
                if (row == null || row.Id == Guid.Empty || !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal)
                    || !identities.Add(row.Id)) throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            }
            return candidate;
        }
    }
}

```

## ToolUtility.Core/FeeOperations/IFeeService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.FeeOperations
{
    /// <summary>
    /// 單次授權範圍的收費查詢契約；呼叫者先驗證身分／教會，再提供資料庫 ID。
    /// 所有結果是操作擁有的 SDK 查詢頁，保留分頁資訊；不按名稱去重，ID 衝突整頁拒絕。
    /// 實作不得持有跨使用者狀態或接管借入連線，真正故障傳至 host 共用通知邊界。
    /// </summary>
    public interface IFeeService
    {
        /// <summary>
        /// 依認獻 ID 與期數讀啟用收費頁；文字安全組裝，不允許輸入改變條件樹。
        /// </summary>
        /// <param name="dedicationBookingName">僅供lookup顯示，不作識別或授權。</param>
        /// <param name="dedicationBookingId">非空認獻資料庫GUID。</param>
        /// <param name="paidPeriod">原查詢的非空期數文字。</param>
        /// <returns>保留原投影、排序與500筆上限的SDK頁。</returns>
        EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod);

        /// <summary>依聯絡人ID讀啟用且已核准認獻；不得以空集合代替SDK查詢或失敗。</summary>
        /// <param name="contactName">僅供lookup顯示，不作權限依據。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <returns>保留原建立時間倒序及1000筆上限的SDK頁。</returns>
        EntityCollection RetrieveDedicationBooking(string contactName, string contactId);

        /// <summary>讀分類非空的奉獻收費頁；整頁驗穩定ID後才回傳，不改合法同名記錄。</summary>
        /// <param name="contactName">可含特殊字元的lookup顯示名稱。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <returns>呼叫者擁有的原SDK頁，不在實作保存。</returns>
        EntityCollection RetrieveDedicationFee(string contactName, string contactId);

        /// <summary>
        /// 讀含端點日期內的奉獻頁，套用原付款狀態白名單；日期採西曆但不作時區偏移。
        /// </summary>
        /// <param name="contactName">lookup顯示名稱，不作權限。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <param name="startDate">呼叫者已決定的業務起日。</param>
        /// <param name="endDate">不早於起日的業務迄日。</param>
        /// <returns>完整驗證、保留原分頁資訊的一頁結果。</returns>
        EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate);

        /// <summary>依課程紀錄ID及pay_date非空查已繳頁，不推導或改寫付款狀態。</summary>
        /// <param name="storLessonName">只為來源相容保留，實作不使用或保存。</param>
        /// <param name="storLessonId">伺服器已授權的非空課程紀錄GUID。</param>
        /// <returns>含繳費日期及實繳額的SDK頁，合法同名不同ID均保留。</returns>
        EntityCollection GetPaidFee(string storLessonName, string storLessonId);

        /// <summary>依課程紀錄ID及pay_date為null查未繳頁，保留舊來源欄位及排序。</summary>
        /// <param name="storLessonName">只為相容保留，不當識別或授權。</param>
        /// <param name="storLessonId">伺服器已授權的非空課程紀錄GUID。</param>
        /// <returns>含應繳額的SDK頁，重複或欠缺穩定ID時整頁拒絕。</returns>
        EntityCollection GetNotYetPaidFee(string storLessonName, string storLessonId);

        /// <summary>以卡或ATM欄位OR比對訂單文字，安全編碼所有XML特殊字元且保留原欄位／排序。</summary>
        /// <param name="orderNumber">非空訂單文字；所屬權限仍由host先驗證。</param>
        /// <returns>完整ID驗證的SDK頁，不以文字合併不同資料庫記錄。</returns>
        EntityCollection RetrieveFeeByFetchXmlOrderNumber(string orderNumber);
    }
}

```

## ToolUtility.Core/ToolUtility.Core.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net462;net10.0</TargetFrameworks>
    <RootNamespace>ToolUtilityNameSpace</RootNamespace>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <!-- Framework SDK 傳遞引用 6.0.8，直接使用中央安全修補版；net10 則使用內建 JSON。 -->
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 僅取得 SDK 型別；純資料核心不建立 ServiceClient 或擁有連線。 -->
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 舊 SDK 帶入的傳遞版本有已知弱點；使用中央表的安全版本，維持完整 audit。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
  </ItemGroup>
</Project>

```

## ToolUtility.Tests/CoreTargetFrameworkTests.cs
```
using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }
    }
}

```

## ToolUtility.Tests/FeeOperations/FeeCharacterizationTests.cs
```
using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 在 FeeService 尚未搬移前鎖定現行有效查詢與結果傳遞。
    /// 全部使用合成教會資料與獨立 SDK 替身；不連 D365，不將空實作視為正確業務。
    /// </summary>
    public sealed class FeeCharacterizationTests
    {
        private const string ContactId = "11111111-1111-4111-8111-111111111111";

        /// <summary>
        /// 保護奉獻查詢的欄位、排序、聯絡人與分類條件；注入同名但不同 ID 的資料，
        /// 決定性斷言為實際 FetchXML 符合黃金檔且兩筆合法記錄均完整保留。
        /// </summary>
        [Fact]
        public void RetrieveDedicationFee_ExistingImplementation_MatchesGoldenAndPreservesSameNameRows()
        {
            var expected = CreateSameNameRows();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(
                    "fee-dedication.xml", Assert.IsType<FetchExpression>(query).Query))
                .Returns(expected);
            var sut = new FeeService(new object(), client.Object);

            var result = sut.RetrieveDedicationFee("Synthetic Contact", ContactId);

            Assert.Same(expected, result);
            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 保護日期區間查詢的既有付款狀態白名單與含端點日期條件；注入固定日期和空結果，
        /// 斷言完整 XML 而非只比對片段，且空集合來自 SDK 而非服務自行製造。
        /// </summary>
        [Fact]
        public void RetrieveDedicationFeeByDateRange_ExistingImplementation_MatchesGolden()
        {
            var expected = new EntityCollection();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(
                    "fee-dedication-date-range.xml", Assert.IsType<FetchExpression>(query).Query))
                .Returns(expected);
            var sut = new FeeService(new object(), client.Object);

            // 原產品目前依 ambient culture 格式化；此特性基準固定西曆，避免測試機預設曆法改變 golden。
            // 僅設定當次執行脈絡，finally 還原；不可改 DefaultThreadCurrentCulture 污染並行案例。
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var result = sut.RetrieveDedicationFeeByDateRange(
                    "Synthetic Contact", ContactId, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                Assert.Same(expected, result);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 保護 SDK 失敗不被改成空集合或成功；注入同一例外實例，
        /// 斷言原始例外與呼叫次數，讓最終 host 能以同一事件交給共用通知入口。
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RetrieveDedicationFee_SdkFails_PropagatesOriginalException(bool useDateRange)
        {
            var failure = new InvalidOperationException("Synthetic SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(failure);
            var sut = new FeeService(new object(), client.Object);

            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                if (useDateRange)
                    sut.RetrieveDedicationFeeByDateRange(
                        "Synthetic Contact", ContactId, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                else
                    sut.RetrieveDedicationFee("Synthetic Contact", ContactId);
            });

            Assert.Same(failure, actual);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 建立無機密的兩筆合法同名資料；每案重新配置集合，防止測試間共享可變 Entity。
        /// </summary>
        private static EntityCollection CreateSameNameRows()
        {
            return new EntityCollection(new[]
            {
                new Entity("new_fee", new Guid("22222222-2222-4222-8222-222222222222"))
                {
                    ["new_name"] = "Synthetic Fee"
                },
                new Entity("new_fee", new Guid("33333333-3333-4333-8333-333333333333"))
                {
                    ["new_name"] = "Synthetic Fee"
                }
            });
        }
    }
}

```

## ToolUtility.Tests/FeeOperations/FeeQueryContractTests.cs
```
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 保護 P1-04 新增收費查詢及既有查詢安全契約。所有資料、ID與故障均合成；
    /// 每案獨立借入 SDK 替身，沒有實機連線、共享快取或持續背景工作。
    /// </summary>
    public sealed class FeeQueryContractTests
    {
        private const string ScopeId = "11111111-1111-4111-8111-111111111111";
        private const string SpecialText = "Synthetic <&\"' value";

        /// <summary>
        /// 鎖定三個舊來源補回方法的完整條件及欄位；注入同名不同ID兩筆資料，
        /// 以完整golden及兩筆ID斷言證明沒有按名稱合併或丟失。
        /// </summary>
        /// <param name="operation">待新增的三個查詢之一。</param>
        /// <param name="golden">唯讀合成XML資源。</param>
        [Theory]
        [InlineData("Paid", "fee-paid.xml")]
        [InlineData("Unpaid", "fee-unpaid.xml")]
        [InlineData("Order", "fee-order.xml")]
        public void NewQuery_MatchesGoldenAndPreservesSameNameRows(string operation, string golden)
        {
            var expected = Rows();
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(golden, xml));
            var sut = new FeeService(new object(), client.Object);

            var result = Invoke(sut, operation);

            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 課程ID是查詢界線；注入空白、空GUID及XML注入字串均須在I/O前拒絕，
        /// 不可將XML編碼誤當成授權或合法識別碼驗證。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        /// <param name="invalidId">無效的合成識別值。</param>
        [Theory]
        [InlineData("Paid", "")]
        [InlineData("Unpaid", " ")]
        [InlineData("Paid", "00000000-0000-0000-0000-000000000000")]
        [InlineData("Unpaid", "<&\"'")]
        public void LessonQuery_InvalidId_RejectsBeforeIo(string operation, string invalidId)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => Invoke(sut, operation, invalidId));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 舊API保留但未使用的課程名稱可含XML字元，不能被加入查詢或拿來當識別／權限；
        /// 完整golden仍相等，證明兩個方法只按伺服器提供的課程ID查詢。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        public void LessonQuery_DisplayNameCannotChangeScope(string operation)
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal(
                operation == "Paid" ? "fee-paid.xml" : "fee-unpaid.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, Invoke(sut, operation, ScopeId, SpecialText).Entities.Count);
        }

        /// <summary>
        /// 訂單號是文字而非GUID；含所有XML特殊字元仍須只產生原有兩個OR條件，
        /// 解碼後值逐字相等，不能新增節點或讓卡／ATM任一分支遺失。
        /// </summary>
        [Fact]
        public void OrderQuery_XmlSpecialCharacters_RoundTripWithoutInjection()
        {
            var client = Client(Rows(), xml =>
            {
                var entity = XDocument.Parse(xml).Root!.Element("entity")!;
                var conditions = entity.Element("filter")!.Element("filter")!.Elements("condition").ToArray();
                Assert.Equal("or", (string?)entity.Element("filter")!.Element("filter")!.Attribute("type"));
                Assert.Equal(2, conditions.Length);
                Assert.Equal(new[] { "new_q_pay_card_order_no", "new_q_pay_order_atm_no" },
                    conditions.Select(condition => (string?)condition.Attribute("attribute")));
                Assert.All(conditions, condition => Assert.Equal(SpecialText, (string?)condition.Attribute("value")));
            });
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, sut.RetrieveFeeByFetchXmlOrderNumber(SpecialText).Entities.Count);
        }

        /// <summary>
        /// 空訂單號不得擴張為廣泛查詢，並且拒絕前不碰SDK。
        /// </summary>
        /// <param name="orderNumber">合成無效輸入。</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void OrderQuery_EmptyInput_RejectsBeforeIo(string? orderNumber)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => sut.RetrieveFeeByFetchXmlOrderNumber(orderNumber!));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 每個新增方法注入SDK故障，原始同一Exception必須傳回host，不能回空集合、
        /// 吞例外或包成含私密本文的新訊息。Core不是最終通知邊界。
        /// </summary>
        /// <param name="operation">新增查詢名稱。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        public void NewQuery_SdkFault_PropagatesSameException(string operation)
        {
            var fault = new InvalidOperationException("Synthetic private SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation)));
        }

        /// <summary>
        /// 空殼認獻／收費方法必須沿用已鎖定的有效查詢，而不是自行回空集合。
        /// SDK request、上限及欄位與有效來源golden完全一致。
        /// </summary>
        /// <param name="operation">原有空方法。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        public void FormerPlaceholder_UsesEstablishedQuery(string operation)
        {
            var expected = operation == "Booking" ? new EntityCollection(new[]
            {
                new Entity("new_dedication_booking", new Guid("22222222-2222-4222-8222-222222222222"))
            }) : Rows();
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(
                operation == "Booking" ? "query-dedication-booking.xml" : "query-fee-by-booking.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(expected, Invoke(sut, operation));
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 注入同一非空資料庫ID兩次；所有受改查詢都必須拒絕整個候選，
        /// 不可保留一筆、名稱去重或將部分結果交給上層。
        /// </summary>
        /// <param name="operation">Fee全部公開查詢入口。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void Query_DuplicateStableId_RejectsPublication(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            candidate.Entities[1].Id = candidate.Entities[0].Id;
            var client = Client(candidate, _ => { });
            var sut = new FeeService(new object(), client.Object);

            var fault = Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation));

            Assert.DoesNotContain(candidate.Entities[0].Id.ToString(), fault.Message);
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 欠缺真實資料庫 ID 的正式列或混入其他實體列均拒絕；不能產生draft key讓正式資料通過。
        /// </summary>
        /// <param name="operation">Fee全部查詢入口。</param>
        /// <param name="missingId">注入空ID或錯誤實體型別。</param>
        [Theory]
        [InlineData("Paid", true)]
        [InlineData("Unpaid", false)]
        [InlineData("Order", true)]
        [InlineData("Booking", false)]
        [InlineData("Fee", true)]
        [InlineData("Dedication", false)]
        [InlineData("DateRange", true)]
        public void Query_InvalidStableIdentity_RejectsPublication(string operation, bool missingId)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            if (missingId) candidate.Entities[1].Id = Guid.Empty;
            else candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = Client(candidate, _ => { });

            Assert.Throws<InvalidOperationException>(() => Invoke(new FeeService(new object(), client.Object), operation));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 所有lookup顯示名稱及期數文字只可當XML值；含特殊字元解碼後須逐字相等，
        /// 條件數不增加，證明修復既有入口而非只保護新增訂單方法。
        /// </summary>
        /// <param name="operation">使用外部文字的既有查詢。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void ExistingQuery_SpecialDisplayName_IsOnlyAnAttributeValue(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            var client = Client(candidate, xml =>
            {
                var lookups = XDocument.Parse(xml).Descendants("condition")
                    .Where(condition => condition.Attribute("uiname") != null).ToArray();
                Assert.Single(lookups);
                Assert.Equal(SpecialText, (string?)lookups[0].Attribute("uiname"));
            });

            var sut = new FeeService(new object(), client.Object);
            if (operation == "Fee")
                sut.RetrieveFee(SpecialText, ScopeId, SpecialText);
            else
                Invoke(sut, operation, ScopeId, SpecialText);
        }

        /// <summary>
        /// SDK每次提供獨立操作資料；第一個呼叫者修改集合不可污染下一次回應，
        /// 並以兩次真實SDK呼叫證明服務沒有偷偷快取前一範圍的可變結果。
        /// </summary>
        [Fact]
        public void Query_CallerMutation_DoesNotBecomeNextOperationState()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = Rows();
                    return response;
                });
            var sut = new FeeService(new object(), client.Object);
            var first = Invoke(sut, "Paid");
            first.Entities[0]["new_name"] = "Synthetic caller mutation";
            first.Entities.RemoveAt(1);

            var second = Invoke(sut, "Paid");

            Assert.NotSame(first, second);
            Assert.Equal(2, second.Entities.Count);
            Assert.Equal("Synthetic Fee", second.Entities[0]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Exactly(2));
        }

        /// <summary>建構時拒絕沒有授權SDK的服務，不能延至呼叫才以NullReferenceException掩蓋設定失敗。</summary>
        [Fact]
        public void Constructor_NullService_RejectsImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => new FeeService(null!));
        }

        /// <summary>
        /// 非西曆環境不能改變資料庫日期條件；在本案局部注入泰國曆並finally還原，
        /// 應仍符合已核對的西曆golden，不能靠改主機全域文化換取通過。
        /// </summary>
        [Fact]
        public void DateRange_ThaiCulture_UsesGregorianQuery()
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal("fee-dedication-date-range.xml", xml));
            var sut = new FeeService(new object(), client.Object);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                Invoke(sut, "DateRange");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// A/B各自借入SDK，交錯執行1000次不混查詢或資料，操作全部排空後不留活動工作；
        /// IDisposable借入服務不能被Fee關閉。固定八路排程避免測試本身無界fan-out。
        /// </summary>
        [Fact]
        public void Query_ParallelScopes_DoNotLeakOrDisposeBorrowedService()
        {
            var firstClient = Client(Rows(), xml => Assert.Contains(ScopeId, xml));
            var secondId = "44444444-4444-4444-8444-444444444444";
            var secondRows = Rows();
            secondRows.Entities[0].Id = new Guid("55555555-5555-4555-8555-555555555555");
            var secondClient = Client(secondRows, xml => Assert.Contains(secondId, xml));
            var firstDisposable = firstClient.As<IDisposable>();
            var secondDisposable = secondClient.As<IDisposable>();
            var first = new FeeService(new object(), firstClient.Object);
            var second = new FeeService(new object(), secondClient.Object);
            var active = 0;
            var completed = 0;

            Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    var isFirst = index % 2 == 0;
                    var result = Invoke(isFirst ? first : second, "Paid", isFirst ? ScopeId : secondId);
                    Assert.Equal(isFirst ? Rows().Entities[0].Id : secondRows.Entities[0].Id, result.Entities[0].Id);
                    Interlocked.Increment(ref completed);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

            Assert.Equal(1000, completed);
            Assert.Equal(0, active);
            firstDisposable.Verify(service => service.Dispose(), Times.Never);
            secondDisposable.Verify(service => service.Dispose(), Times.Never);
        }

        /// <summary>
        /// 建立每案獨立SDK替身；只保留該案候選，callback收到的XML立即驗證，不保存請求或credential。
        /// </summary>
        private static Mock<IOrganizationService> Client(EntityCollection candidate, Action<string> assertXml)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => assertXml(
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(response);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => assertXml(Assert.IsType<FetchExpression>(query).Query))
                .Returns(candidate);
            return client;
        }

        /// <summary>合成的兩筆同名不同ID資料，沒有靜態可變資料或真實個資。</summary>
        private static EntityCollection Rows(string logicalName = "new_fee") => new EntityCollection(new[]
        {
            new Entity(logicalName, new Guid("22222222-2222-4222-8222-222222222222")) { ["new_name"] = "Synthetic Fee" },
            new Entity(logicalName, new Guid("33333333-3333-4333-8333-333333333333")) { ["new_name"] = "Synthetic Fee" }
        });

        /// <summary>依固定測試名稱呼叫真正服務，不在替身複製查詢或驗證邏輯。</summary>
        private static EntityCollection Invoke(FeeService sut, string operation, string id = ScopeId, string name = "Synthetic Contact")
        {
            switch (operation)
            {
                case "Paid": return sut.GetPaidFee(name, id);
                case "Unpaid": return sut.GetNotYetPaidFee(name, id);
                case "Order": return sut.RetrieveFeeByFetchXmlOrderNumber("Synthetic-Order");
                case "Booking": return sut.RetrieveDedicationBooking(name, id);
                case "Fee": return sut.RetrieveFee("Synthetic Booking", id, "3");
                case "Dedication": return sut.RetrieveDedicationFee(name, id);
                case "DateRange": return sut.RetrieveDedicationFeeByDateRange(name, id, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                default: throw new ArgumentException("Unknown synthetic operation.", nameof(operation));
            }
        }
    }
}

```

## ToolUtility.Tests/QueryOperations/FetchXmlFeeCharacterizationTests.cs
```
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.QueryOperations
{
    /// <summary>
    /// 鎖定目前有實作的認獻／收費查詢來源，作為 Fee 空方法修復與後續抽取的相容證據。
    /// 只使用每案獨立的 SDK 替身與合成資料，不將離線結果冒充 D365 整合成功。
    /// </summary>
    public sealed class FetchXmlFeeCharacterizationTests
    {
        /// <summary>
        /// 保護完整欄位、上限、排序、啟用狀態及 lookup 條件；注入一筆固定 ID 的資料，
        /// 斷言實際送出的 SDK request 與 golden 相等，回傳集合不經名稱去重或改寫。
        /// </summary>
        /// <param name="fee">選擇收費或認獻查詢。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingFetchQuery_MatchesGoldenAndReturnsSdkRows(bool fee)
        {
            var expected = new EntityCollection(new[]
            {
                new Entity(fee ? "new_fee" : "new_dedication_booking",
                    new Guid("22222222-2222-4222-8222-222222222222"))
            });
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = expected;
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => GoldenQueryAssertions.Equal(
                    fee ? "query-fee-by-booking.xml" : "query-dedication-booking.xml",
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(response);
            var sut = new FetchXmlQueryService(new object(), client.Object);

            var result = fee
                ? sut.RetrieveFeeByFetchXml("Synthetic Booking", "11111111-1111-4111-8111-111111111111", "3")
                : sut.RetrieveDedicationBookingByFetchXml("Synthetic Contact", "11111111-1111-4111-8111-111111111111");

            Assert.Same(expected, result);
            Assert.Equal(expected.Entities[0].Id, Assert.Single(result.Entities).Id);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 注入 SDK 故障；原始同一例外必須向最終 host 傳遞，不能以空集合掩蓋失敗。
        /// </summary>
        /// <param name="fee">選擇收費或認獻查詢。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingFetchQuery_SdkFault_PropagatesOriginalException(bool fee)
        {
            var failure = new InvalidOperationException("Synthetic SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Throws(failure);
            var sut = new FetchXmlQueryService(new object(), client.Object);

            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                if (fee)
                    sut.RetrieveFeeByFetchXml("Synthetic Booking", "11111111-1111-4111-8111-111111111111", "3");
                else
                    sut.RetrieveDedicationBookingByFetchXml("Synthetic Contact", "11111111-1111-4111-8111-111111111111");
            });

            Assert.Same(failure, actual);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }
    }
}

```

## ToolUtility.Tests/TestHelpers/GoldenQueryAssertions.cs
```
using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 比對內嵌的合成查詢黃金資料，鎖定跨目標搬移前後的 XML 條件樹。
    /// 不比較無語義的引號與縮排；元素、順序、屬性及欄位值必須完全相同。
    /// </summary>
    internal static class GoldenQueryAssertions
    {
        /// <summary>
        /// 以固定資源名稱讀取預期查詢，拒絕 DTD 與過大 XML 後比較正規化內容。
        /// 每次讀取獨立擁有資源串流及 reader，成功或斷言失敗皆確定釋放，不保存查詢資料。
        /// </summary>
        /// <param name="fileName">測試作者指定的資源檔名，不接受使用者輸入。</param>
        /// <param name="actualXml">受測服務實際送到 SDK 替身的 XML。</param>
        internal static void Equal(string fileName, string actualXml)
        {
            using (var stream = typeof(GoldenQueryAssertions).Assembly.GetManifestResourceStream(
                "ToolUtility.Tests.Golden.ToolUtility." + fileName))
            {
                Assert.NotNull(stream);
                using (var reader = XmlReader.Create(stream!, CreateSettings()))
                {
                    var expected = XDocument.Load(reader).ToString(SaveOptions.DisableFormatting);
                    using (var text = new StringReader(actualXml))
                    using (var actual = XmlReader.Create(text, CreateSettings()))
                    {
                        Assert.Equal(expected, XDocument.Load(actual).ToString(SaveOptions.DisableFormatting));
                    }
                }
            }
        }

        /// <summary>
        /// 建立單次 reader 的安全設定；固定上限足以容納測試查詢，禁止外部解析。
        /// </summary>
        private static XmlReaderSettings CreateSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            };
        }
    }
}

```

## ToolUtility.Tests/ToolUtility.Tests.csproj
```
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
  </PropertyGroup>

  <ItemGroup>
    <!-- 測試框架 -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <!-- Mock 框架 -->
    <PackageReference Include="Moq" />
    
    <!-- 斷言庫 -->
    <PackageReference Include="FluentAssertions" />
    
    <!-- 程式碼覆蓋率 -->
    <PackageReference Include="coverlet.collector">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.msbuild">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 專案參考 -->
    <ProjectReference Include="..\ToolUtility\ToolUtility.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ToolUtility.Core\ToolUtility.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- CRM SDK 套件（用於測試） -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
    <!-- 既有 host 測試完整保留在 net10；Framework 宿主只編譯純 Core 契約，不引用 net10 host。 -->
    <Compile Remove="**\*.cs" />
    <Compile Include="FeeOperations\*.cs" />
    <Compile Include="TestHelpers\GoldenQueryAssertions*.cs" />
    <Compile Include="CoreTargetFrameworkTests.cs" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 測試直接消費 SDK，需同樣以中央安全版本覆蓋其舊傳遞相依。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- 黃金查詢是合成資料，內嵌後兩個測試宿主均不依賴執行目錄或網路。 -->
    <EmbeddedResource Include="Golden\ToolUtility\*.xml"
                      LogicalName="ToolUtility.Tests.Golden.ToolUtility.%(Filename)%(Extension)" />
  </ItemGroup>

</Project>

```

## ToolUtility.Tests\Golden\ToolUtility\comparison-contract.xml
```xml
<fetch>
  <entity name="synthetic">
    <attribute name="syntheticid" />
    <order attribute="syntheticid" descending="false" />
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\fee-dedication-date-range.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <attribute name="new_pay_date" />
    <attribute name="new_fee_shoud_pay" />
    <attribute name="new_fee_really_paid" />
    <attribute name="new_pay_way" />
    <attribute name="new_category" />
    <attribute name="new_others" />
    <attribute name="new_paid_period" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <condition attribute="new_contact_new_fee" operator="eq" uiname="Synthetic Contact" uitype="contact" value="{11111111-1111-4111-8111-111111111111}" />
      <condition attribute="new_category" operator="not-null" />
      <condition attribute="new_pay_status" operator="in">
        <value>100000001</value>
        <value>100000002</value>
        <value>100000003</value>
        <value>100000004</value>
        <value>100000006</value>
      </condition>
      <condition attribute="new_pay_date" operator="on-or-after" value="2027-1-2" />
      <condition attribute="new_pay_date" operator="on-or-before" value="2027-3-4" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\fee-dedication.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <attribute name="new_pay_date" />
    <attribute name="new_fee_shoud_pay" />
    <attribute name="new_fee_really_paid" />
    <attribute name="new_pay_way" />
    <attribute name="new_category" />
    <attribute name="new_others" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <condition attribute="new_contact_new_fee" operator="eq" uiname="Synthetic Contact" uitype="contact" value="{11111111-1111-4111-8111-111111111111}" />
      <condition attribute="new_category" operator="not-null" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\fee-order.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <filter type="or">
        <condition attribute="new_q_pay_card_order_no" operator="eq" value="Synthetic-Order" />
        <condition attribute="new_q_pay_order_atm_no" operator="eq" value="Synthetic-Order" />
      </filter>
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\fee-paid.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="new_pay_date" />
    <attribute name="new_fee_really_paid" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <condition attribute="new_stor_lessons_new_fee" operator="eq" value="11111111-1111-4111-8111-111111111111" />
      <condition attribute="new_pay_date" operator="not-null" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\fee-unpaid.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="new_fee_amount" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <condition attribute="new_stor_lessons_new_fee" operator="eq" value="11111111-1111-4111-8111-111111111111" />
      <condition attribute="new_pay_date" operator="null" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\query-dedication-booking.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="1000">
  <entity name="new_dedication_booking">
    <attribute name="new_dedication_bookingid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <attribute name="new_dedication_booking_status" />
    <order attribute="createdon" descending="true" />
    <filter type="and">
      <condition attribute="new_contact_new_dedication_booking" operator="eq" uiname="Synthetic Contact" uitype="contact" value="{11111111-1111-4111-8111-111111111111}" />
      <condition attribute="new_dedication_booking_status" operator="eq" value="100000001" />
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests\Golden\ToolUtility\query-fee-by-booking.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="500">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <attribute name="new_paid_period" />
    <order attribute="createdon" descending="true" />
    <filter type="and">
      <condition attribute="new_dedication_booking_new_fee" operator="eq" uiname="Synthetic Booking" uitype="new_dedication_booking" value="{11111111-1111-4111-8111-111111111111}" />
      <condition attribute="new_paid_period" operator="eq" value="3" />
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>

```

## 固定基準00b210ba Query委派與host專案diff
```diff
diff --git a/ToolUtility/QueryOperations/FetchXmlQueryService.cs b/ToolUtility/QueryOperations/FetchXmlQueryService.cs
index 54ea2ffd9..e1b7dbb45 100644
--- a/ToolUtility/QueryOperations/FetchXmlQueryService.cs
+++ b/ToolUtility/QueryOperations/FetchXmlQueryService.cs
@@ -28,6 +28,7 @@ namespace ToolUtilityNameSpace.QueryOperations
     {
         private readonly object _logger;
         private readonly IOrganizationService _organizationService;
+        private readonly ToolUtilityNameSpace.FeeOperations.FeeService _feeService;
 
         // ? 預設查詢限制，防止返回過多資料
         private const int DEFAULT_TOP_LIMIT = 5000;
@@ -37,6 +38,7 @@ namespace ToolUtilityNameSpace.QueryOperations
         {
             _logger = logger ?? throw new ArgumentNullException(nameof(logger));
             _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
+            _feeService = new ToolUtilityNameSpace.FeeOperations.FeeService(_organizationService);
         }
 
         /// <summary>
@@ -206,42 +208,13 @@ namespace ToolUtilityNameSpace.QueryOperations
         /// 根據聯絡人查詢奉獻預約 (使用 FetchXML)
         /// ? Phase 3.1: 優化 - 添加 top 限制
         /// </summary>
+        /// <remarks>
+        /// 認獻查詢委派唯一Fee實作；保留既有SDK request、上限、欄位及排序，補齊安全XML與穩定ID驗證。
+        /// 真正SDK例外原樣傳向最終host共用通知；不在此保留結果、關閉借入服務或另建LINE client。
+        /// </remarks>
         public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId)
         {
-            try
-            {
-                contactName = $"'{contactName}'";
-                contactId = $"'{{{contactId}}}'";
-
-                // ? 優化: 添加 top 限制、按創建日期倒序
-                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='{SMALL_QUERY_LIMIT}'>
-                          <entity name='new_dedication_booking'>
-                            <attribute name='new_dedication_bookingid' />
-                            <attribute name='new_name' />
-                            <attribute name='createdon' />
-                            <attribute name='new_dedication_booking_status' />
-                            <order attribute='createdon' descending='true' />
-                            <filter type='and'>
-                              <condition attribute='new_contact_new_dedication_booking' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
-                              <condition attribute='new_dedication_booking_status' operator='eq' value='100000001' />
-                              <condition attribute='statecode' operator='eq' value='0' />
-                            </filter>
-                          </entity>
-                        </fetch>";
-
-                var fetchRequest = new RetrieveMultipleRequest
-                {
-                    Query = new FetchExpression(fetchXml)
-                };
-
-                var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
-                return response.EntityCollection;
-            }
-            catch (Exception ex)
-            {
-                SafeLogError(ex, "RetrieveDedicationBookingByFetchXml 發生錯誤");
-                throw;
-            }
+            return _feeService.RetrieveDedicationBooking(contactName, contactId);
         }
 
         /// <summary>
@@ -289,42 +262,13 @@ namespace ToolUtilityNameSpace.QueryOperations
         /// 根據奉獻預約和已付期數查詢收費單 (使用 FetchXML)
         /// ? Phase 3.1: 優化 - 添加 top 限制
         /// </summary>
+        /// <remarks>
+        /// 收費查詢委派唯一Fee實作，避免Facade兩條路徑分別維護條件或把空實作視為成功。
+        /// 真正SDK例外原樣傳向最終host共用通知；不在此保留結果、關閉借入服務或另建LINE client。
+        /// </remarks>
         public EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
         {
-            try
-            {
-                dedicationBookingName = $"'{dedicationBookingName}'";
-                dedicationBookingId = $"'{{{dedicationBookingId}}}'";
-
-                // ? 優化: 添加 top 限制
-                var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='500'>
-                          <entity name='new_fee'>
-                            <attribute name='new_feeid' />
-                            <attribute name='new_name' />
-                            <attribute name='createdon' />
-                            <attribute name='new_paid_period' />
-                            <order attribute='createdon' descending='true' />
-                            <filter type='and'>
-                              <condition attribute='new_dedication_booking_new_fee' operator='eq' uiname={dedicationBookingName} uitype='new_dedication_booking' value={dedicationBookingId} />
-                              <condition attribute='new_paid_period' operator='eq' value='{paidPeriod}' />
-                              <condition attribute='statecode' operator='eq' value='0' />
-                            </filter>
-                          </entity>
-                        </fetch>";
-
-                var fetchRequest = new RetrieveMultipleRequest
-                {
-                    Query = new FetchExpression(fetchXml)
-                };
-
-                var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
-                return response.EntityCollection;
-            }
-            catch (Exception ex)
-            {
-                SafeLogError(ex, "RetrieveFeeByFetchXml 發生錯誤");
-                throw;
-            }
+            return _feeService.RetrieveFee(dedicationBookingName, dedicationBookingId, paidPeriod);
         }
 
         /// <summary>
diff --git a/ToolUtility/ToolUtility.csproj b/ToolUtility/ToolUtility.csproj
index b694c6b5b..7651442b5 100644
--- a/ToolUtility/ToolUtility.csproj
+++ b/ToolUtility/ToolUtility.csproj
@@ -49,6 +49,7 @@
   </ItemGroup>
 
   <ItemGroup>
+    <ProjectReference Include="..\ToolUtility.Core\ToolUtility.Core.csproj" />
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />    
     <ProjectReference Include="..\PowerPlatform.Dataverse.Client\PowerPlatform.Dataverse.Client.csproj" />
   </ItemGroup>

```

# 本輪增補審查範圍與前次 Warning 實證處理
前次Fee review run20261105-130623為dualModelComplete=true、無Critical/High。請繼續審查最新版而非只重複舊摘要。
1. 以下附上唯讀舊來源三方法與固定基準原Fee日期白名單，允許獨立核對golden，舊來源僅作相容證據，不照搬XML注入。
2. 整個active ToolUtility及ChurchReport搜尋沒有三個新增Fee API舊副本／有效消費者；只存在新Core。正式計畫要求在Core補API供後續階段，不要求新增未使用的host facade方法；現有有效Booking/Fee已委派到Core。
3. 現有ChurchReport唯一RetrieveFeeByFetchXml呼叫在Tools/RecurringDonationPaymentProcessor.cs傳固定期數"001"，故空白拒絕不影響其流程。補明確空白期數拒絕測試，文件記為安全fail-closed差異，不恢復未跳脫輸入。
4. 新Fee三入口golden案例已加入MoreRecords=true、PagingCookie及Same(reference)明確斷言。
5. 新增EntityQueryService抽取：搬前3/3characterization；新增NoCache API先CS1061，之後舊安全行為12失敗/4通過（含XML注入/重複ID/Dispose後操作）再修；搬前21/21含原2案通過後git mv，保留原namespace/API/optional ctor/Dispose。無cache，SDK借入，清除借用參照不Dispose外部SDK，GetService前已開始同步操作允許完成，host須drain後關閉SDK。Strict Core兩目標0警告0錯誤。新增型別化logicalname驗證，所有集合先驗ID再發布。
6. 新增DI AddToolUtility(services,configuration,product)，舊無參數overload保留ChurchReport且標Obsolete(階段6)。同容器不同product/config拒絕，重複相同註冊冪等；manager、options、facade使用同一配置來源，不另建provider。8/8真實DI/pool替身測試通過，實際子池key白箱只讀比對，不為測試擴公開secret-bearing API。
7. 套件圖修復：net462 JSON6.0.8→中央6.0.11；net10內建JSON，Pkcs10.0.14。host DirectoryServices9搭SDK Permissions6導致MSB3277，升中央Permissions9.0.0版本對齊後0 MSB3277，未抑制任何警告。10.0.14嘗試導致Drawing.Common9/10衝突，未採，保留失敗證據。
8. 最新ToolUtility雙宿主152/152、77/77，0略過；覆蓋率重跑中，先前僅Fee95.60%/100不能當Entity覆蓋證據。沒有實機證據，P1仍0/12。請重點檢查新Entity/DI正確性/相容/範圍隔離/Dispose競態/SDK故障及完整發布，不能把本切片審查宣稱整包完成。
9. 請勿擴讀原host Core/Facade檔案，該舊檔含機密註解；提供的切片已排除。只審閱本提示內附的固定快照。

## 唯讀舊來源：QueryService.cs / GetPaidFee
```csharp

        public EntityCollection GetPaidFee(string storLessonName, string storLessonId)
        {
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_fee'>
                        <attribute name='new_feeid' />
                        <attribute name='new_name' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_fee_really_paid' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_stor_lessons_new_fee' operator='eq' value='{storLessonId}' />
                          <condition attribute='new_pay_date' operator='not-null' />
                        </filter>
                      </entity>
                    </fetch>";

            var request = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
            return ((RetrieveMultipleResponse)_organizationService.Execute(request)).EntityCollection;
        }

```

## 唯讀舊來源：QueryService.cs / GetNotYetPaidFee
```csharp

        public EntityCollection GetNotYetPaidFee(string storLessonName, string storLessonId)
        {
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_fee'>
                        <attribute name='new_feeid' />
                        <attribute name='new_name' />
                        <attribute name='new_fee_amount' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_stor_lessons_new_fee' operator='eq' value='{storLessonId}' />
                          <condition attribute='new_pay_date' operator='null' />
                        </filter>
                      </entity>
                    </fetch>";

            var request = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
            return ((RetrieveMultipleResponse)_organizationService.Execute(request)).EntityCollection;
        }

```

## 唯讀舊來源：ToolUtilityClass.FetchXmlAndString.cs / RetrieveFeeByFetchXmlOrderNumber
```csharp

    public EntityCollection RetrieveFeeByFetchXmlOrderNumber(string OrderNumber)
    {
        try
        {
            OrderNumber = @"'" + OrderNumber + @"'";

            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_fee'>
                        <attribute name='new_feeid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <filter type='or'>
                            <condition attribute='new_q_pay_card_order_no' operator='eq' value=" + OrderNumber + @" />
                            <condition attribute='new_q_pay_order_atm_no' operator='eq' value=" + OrderNumber + @" />
                          </filter>
                        </filter>
                      </entity>
                    </fetch>";

            var fetchRequest = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
            return ((RetrieveMultipleResponse)m_Crm2011OrganizationService.Execute(fetchRequest)).EntityCollection;
        }
        catch (Exception e)
        {
            throw;
        }
    }

```

## 固定基準00b210ba 舊 Fee 原始來源（含日期狀態白名單）
```csharp
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
```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Core/EntityOperations/EntityQueryService.cs
```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 不保存查詢結果的純 SDK 實體讀取器；授權、組織與服務身分必須由 host 先驗證。
    /// 每次建立操作局部候選，整頁穩定 ID 驗證後才回傳，不以名稱去重或發布部分結果。
    /// </summary>
    /// <remarks>
    /// SDK 只借入，不接管 Dispose／連線或憑證。服務應與原授權 scope 同生命週期，禁止跨租戶 singleton。
    /// 不建立 cache、timer、queue、Task.Run 或隱藏重試；SDK 的逾時由 host gateway／連線 owner 控制。
    /// Dispose 停止新操作並清除借用參照；已取得局部參照的同步操作可完成，host 須先 drain 才關閉 SDK。
    /// 真實 SDK 例外原樣上傳最終 host 共用通知邊界，不在純核心記錄原訊息或另建 LINE client。
    /// </remarks>
    public class EntityQueryService : IEntityQueryService, IDisposable
    {
        // 僅共享有限的唯讀結構名稱，不含使用者、組織、查詢條件或 SDK 結果。
        private static readonly ReadOnlyCollection<string> TaskFields = Array.AsReadOnly(new[]
        {
            "subject", "statecode", "prioritycode", "scheduledend", "createdby",
            "regardingobjectid", "activityid", "description"
        });
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>
        /// 保留舊 optional 建構式但不保存未使用的任意 logger，避免延長 request／憑證參照。
        /// 為相容允許未初始化 SDK；首次操作會明確拒絕，不將設定失敗當成查無資料。
        /// </summary>
        /// <param name="logger">只為來源相容保留，不使用、不保存，也不 Dispose。</param>
        /// <param name="organizationService">呼叫端已授權且擁有的 SDK；null 僅供舊建構相容。</param>
        public EntityQueryService(object? logger = null, IOrganizationService? organizationService = null)
        {
            _organizationService = organizationService;
        }

        /// <summary>
        /// 依舊單筆契約讀完整欄位，不走快取。SDK 回應 ID／實體必須與請求完全相符，否則拒絕發布。
        /// </summary>
        /// <param name="entityName">伺服器決定的合法資料庫 logical name，不構成授權。</param>
        /// <param name="entityId">已授權的非空資料庫 GUID。</param>
        /// <returns>本次操作擁有且已驗證的單筆實體。</returns>
        public Entity RetrieveEntity(string entityName, Guid entityId)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(entityId));
            var candidate = service.Retrieve(entityName, entityId, new ColumnSet(true));
            if (candidate == null || candidate.Id != entityId || !string.Equals(candidate.LogicalName, entityName, StringComparison.Ordinal))
                throw new InvalidOperationException("The SDK returned a mismatched database identity.");
            return candidate;
        }

        /// <summary>
        /// 還原舊來源直接 SDK 讀取的明確入口；每次重新查詢，不回傳前次 caller 修改的物件。
        /// 與 RetrieveEntity 共用相同識別驗證及故障傳遞，不複製第二套資料／快取邏輯。
        /// </summary>
        /// <param name="entityName">受信任的合法 logical name，含 XML 特殊字元的結構名稱會被拒絕。</param>
        /// <param name="entityId">伺服器已授權的非空資料庫 GUID。</param>
        /// <returns>本次 SDK 的完整欄位實體，生命週期只屬於呼叫端操作。</returns>
        public Entity RetrieveEntityNoCache(string entityName, Guid entityId) => RetrieveEntity(entityName, entityId);

        /// <summary>
        /// 保留 TopCount=1、AllColumns 與啟用條件的單筆欄位查詢；型別化值不拼接成 XML。
        /// 此 API 的一筆語義不代表按顯示名稱合併資料；空頁回 null，SDK 故障不回 null。
        /// </summary>
        /// <param name="entityName">已授權的資料庫實體名稱。</param>
        /// <param name="fieldName">伺服器決定的資料庫欄位名稱。</param>
        /// <param name="fieldValue">依原 SDK 型別化傳入的查詢值，不保存於服務。</param>
        /// <returns>完整驗頁後的首筆資料，或真正空頁的 null。</returns>
        public Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            RequireLogicalName(fieldName, nameof(fieldName));
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            var candidate = ValidatePage(service.RetrieveMultiple(query), entityName);
            return candidate.Entities.Count == 0 ? null : candidate.Entities[0];
        }

        /// <summary>
        /// 執行呼叫端已授權的查詢，保留既有「PageInfo 為 null 才補 5000／第一頁」相容行為。
        /// 不自動遍歷無界頁面；單頁 MoreRecords／PagingCookie 原樣交由原分頁 owner 繼續處理。
        /// </summary>
        /// <param name="query">操作擁有的查詢，不可在執行期間由其他執行緒改動。</param>
        /// <returns>整頁穩定 ID 驗證後的 SDK 候選，不保存或共享。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var service = GetService();
#if NETFRAMEWORK
            if (query == null) throw new ArgumentNullException(nameof(query));
#else
            ArgumentNullException.ThrowIfNull(query);
#endif
            if (query is QueryExpression expression && expression.PageInfo == null)
                expression.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            return ValidatePage(service.RetrieveMultiple(query), ExpectedEntity(query));
        }

        /// <summary>
        /// 保留依名稱查第一個啟用帳戶 ID 的相容 API，只投影 accountid，不作所有記錄的名稱去重。
        /// host 必須先完成業務授權；未查到回 Guid.Empty，SDK 故障與識別衝突仍直接拋出。
        /// </summary>
        /// <param name="accountName">依原資料庫語義查詢的名稱，不是授權或唯一識別。</param>
        /// <returns>已驗證的首筆資料庫 ID，或真正空頁時的 Guid.Empty。</returns>
        public Guid RetrieveAccountByName(string accountName)
        {
            var service = GetService();
            var query = new QueryByAttribute("account") { ColumnSet = new ColumnSet("accountid"), TopCount = 1 };
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(accountName, 0);
            var candidate = ValidatePage(service.RetrieveMultiple(query), "account");
            return candidate.Entities.Count == 0 ? Guid.Empty : candidate.Entities[0].Id;
        }

        /// <summary>
        /// 保留工作欄位、100 筆上限、排序與啟用條件；主旨只能作 XML 屬性值，不能注入結構。
        /// 完整驗證同一消費頁的穩定 ID，合法同名但不同 ID 的工作不被刪減。
        /// </summary>
        /// <param name="subject">原查詢文字；null 依舊 API 視為空文字，不構成授權。</param>
        /// <returns>完整驗證的單頁工作結果；SDK 例外不被空集合遮蔽。</returns>
        public EntityCollection RetrieveTaskBySubject(string subject)
        {
            var service = GetService();
            var entity = new XElement("entity", new XAttribute("name", "task"));
            foreach (var field in TaskFields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", "createdon"), new XAttribute("descending", "true")),
                new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "subject"), new XAttribute("operator", "eq"),
                        new XAttribute("value", subject ?? string.Empty)),
                    new XElement("condition", new XAttribute("attribute", "statecode"), new XAttribute("operator", "eq"), new XAttribute("value", "0"))));
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"), new XAttribute("top", "100"), entity);
            var response = (RetrieveMultipleResponse)service.Execute(new RetrieveMultipleRequest
                { Query = new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)) });
            return ValidatePage(response.EntityCollection, "task");
        }

        /// <summary>
        /// 保留完整 RetrieveMultipleRequest（含 SDK 選項）並驗證全部回應 ID，完成前不發布部分頁。
        /// 不攔截或重新包裝 SDK 真實故障，避免丟失 host 共用通知入口所需的原 Exception。
        /// </summary>
        /// <param name="request">呼叫者操作擁有的請求及已授權 Query，不保存於服務。</param>
        /// <returns>原分頁資訊與欄位均保留的完整頁。</returns>
        public EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request)
        {
            var service = GetService();
#if NETFRAMEWORK
            if (request == null) throw new ArgumentNullException(nameof(request));
#else
            ArgumentNullException.ThrowIfNull(request);
#endif
            if (request.Query == null) throw new ArgumentException("A query is required.", nameof(request));
            var response = (RetrieveMultipleResponse)service.Execute(request);
            return ValidatePage(response.EntityCollection, ExpectedEntity(request.Query));
        }

        /// <summary>
        /// 冪等停止新呼叫並清除保存的借入服務參照；不 Dispose 呼叫端擁有的 SDK，也不阻塞等待外部 I/O。
        /// 已借到局部參照的同步呼叫可完成，host 必須 drain 後才處置其原連線 owner。
        /// </summary>
        /// <param name="disposing">保留既有可覆寫清理契約；本類無自有 native／managed disposable。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>由本服務 owner 確定性停止新操作；重複呼叫安全，不轉移借入 SDK 的所有權。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>取得單次借用參照；Dispose 前已開始的同步 I/O 不被暗中取消，新操作則立即拒絕。</summary>
        private IOrganizationService GetService()
        {
            var service = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(EntityQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return service ?? throw new InvalidOperationException("OrganizationService is not initialized.");
        }

        /// <summary>只接受 SDK logical name 字元，不把 XML 編碼當作任意結構名稱的合法性或授權。</summary>
        private static void RequireLogicalName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A logical name is required.", parameter);
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("An invalid logical name was supplied.", parameter);
        }

        /// <summary>型別化查詢可驗根實體；不解析或重寫呼叫者已授權的任意 FetchXML／SDK 查詢。</summary>
        private static string? ExpectedEntity(QueryBase query) => query is QueryExpression expression ? expression.EntityName
            : query is QueryByAttribute attributes ? attributes.EntityName : null;

        /// <summary>
        /// 候選頁只活至本次操作：驗全部 ID 非空／不重複及可知根實體，才原子式回傳。
        /// HashSet 與候選沒有 static／cache 引用；衝突不回顯 ID 或本文，也不改動原候選。
        /// </summary>
        private static EntityCollection ValidatePage(EntityCollection? candidate, string? entityName)
        {
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
                if (row == null || row.Id == Guid.Empty || string.IsNullOrEmpty(row.LogicalName)
                    || (entityName != null && !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal))
                    || !identities.Add(row.Id))
                    throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            return candidate;
        }
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Core/EntityOperations/IEntityQueryService.cs
```csharp
#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 同一已授權 host scope 的純 SDK 查詢契約；不擁有連線、不保存結果或個別使用者狀態。
    /// 單筆核對請求 ID，多筆驗證整個消費頁穩定 ID 後才回傳；SDK 故障交回 host 最終通知邊界。
    /// </summary>
    public interface IEntityQueryService
    {
        /// <summary>依資料庫 ID 直接讀完整欄位，回應 ID 與實體不符時拒絕，不走共用快取。</summary>
        /// <param name="entityName">伺服器已授權的合法實體 logical name。</param>
        /// <param name="entityId">非空的正式資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證實體。</returns>
        Entity RetrieveEntity(string entityName, Guid entityId);

        /// <summary>明確的不經快取入口；每次進 SDK，不能讀回上次 caller 修改或其他 scope 的 Entity。</summary>
        /// <param name="entityName">合法實體名稱，特殊 XML 字元不得作結構名稱。</param>
        /// <param name="entityId">伺服器已授權的非空資料庫 GUID。</param>
        /// <returns>新一次 SDK 完整欄位結果；故障不回空或假資料。</returns>
        Entity RetrieveEntityNoCache(string entityName, Guid entityId);

        /// <summary>保留啟用條件與 TopCount=1 單筆語義，以型別化值查詢，不把名稱當唯一資料列 ID。</summary>
        /// <param name="entityName">受信任實體 logical name。</param>
        /// <param name="fieldName">受信任欄位 logical name。</param>
        /// <param name="fieldValue">原資料庫比對值，不保存於服務。</param>
        /// <returns>驗證完整頁後的第一筆，或真正空頁的 null。</returns>
        Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue);

        /// <summary>執行已授權操作局部查詢，保留 SDK 分頁資訊並拒絕同頁重複或欠缺正式 ID。</summary>
        /// <param name="query">由呼叫者擁有且執行期間不被並行修改的 Query。</param>
        /// <returns>完整驗證的單頁，呼叫端負責後續分頁，不共享可變集合。</returns>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>保留依名稱取第一個啟用帳戶 ID 的相容 API，不是多筆集合的名稱去重。</summary>
        /// <param name="accountName">原資料庫名稱條件；不能構成身分或授權。</param>
        /// <returns>首筆正式 ID，或真正空頁的 Guid.Empty；故障直接拋出。</returns>
        Guid RetrieveAccountByName(string accountName);

        /// <summary>安全 XML 查啟用工作，保留100筆上限／原投影／排序，同名不同 ID 的列完整保留。</summary>
        /// <param name="subject">只作屬性值的主旨文字，特殊字元不能注入條件。</param>
        /// <returns>整頁 ID 驗證後的操作局部 SDK 結果。</returns>
        EntityCollection RetrieveTaskBySubject(string subject);

        /// <summary>原樣提交已授權的 RetrieveMultipleRequest，候選完成且全頁 ID 驗證後才發布。</summary>
        /// <param name="request">含 Query 的操作局部 SDK 請求，呼叫期間不可並行修改。</param>
        /// <returns>保留欄位與分頁資訊的完整頁；原 SDK 例外傳至 host。</returns>
        EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request);
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ServiceCollectionExtensions
// 主要成員：AddToolUtility
// 引用命名空間：Microsoft.Extensions.DependencyInjection、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.DependencyInjection
{
    /// <summary>
    /// 產品級 ToolUtility 組合根；產品與組態只能由受信任的 host 提供，不能來自 request。
    /// 一個容器只屬於一個產品，manager／pool 是容器擁有的 singleton，資料操作維持 scoped。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 註冊 ToolUtility 服務到 DI 容器
        /// ToolUtilityClass 與其 Provider 都使用 Scoped 生命週期；每個 request
        /// 取得自己的 Dataverse 連線租約，request 結束時由 DI 確定性釋放。
        /// </summary>
        /// <param name="services">服務集合</param>
        /// <returns>服務集合</returns>
        [Obsolete("請改用 AddToolUtility(configuration, product) 明確指定產品；此相容多載預定於階段 6 移除。")]
        public static IServiceCollection AddToolUtility(this IServiceCollection services)
        {
            // 舊 API 沒有組態參數，延至解析時才從原容器取得組態；不可為取得設定另建 ServiceProvider。
            return RegisterServices(services, null, "ChurchReport");
        }

        /// <summary>
        /// 以明確的產品與組態註冊資料服務，讓實際 DataverseConnectionKey 包含正確產品。
        /// 組態只活至 host 容器結束，不保存 HttpContext、Session 或個別使用者值。
        /// 同一容器的衝突產品／組態會在任何新增註冊前拒絕，避免第一份 singleton 靜默勝出。
        /// </summary>
        /// <param name="services">由 host 擁有且尚在建構中的服務集合；非並行註冊 API。</param>
        /// <param name="configuration">此產品的 host 組態；讀取必要連線設定時仍須通過 manager 驗證。</param>
        /// <param name="product">伺服器組合根選定的非空產品識別，不接受呼叫端路由作為權限。</param>
        /// <returns>同一服務集合；相同組態實例與產品重複註冊為冪等。</returns>
        public static IServiceCollection AddToolUtility(
            this IServiceCollection services, IConfiguration configuration, string product)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return RegisterServices(services, configuration, product);
        }

        /// <summary>
        /// 共用新舊 API 的唯一註冊流程，不建立隱藏容器或捕獲 scoped service。
        /// null 組態只供無參數相容入口，所有需組態的 factory 透過同一解析函式取得來源。
        /// </summary>
        private static IServiceCollection RegisterServices(
            IServiceCollection services, IConfiguration configuration, string product)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(product))
                throw new ArgumentException("A non-empty product identifier is required.", nameof(product));
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType != typeof(ProductRegistration)) continue;
                var previous = (ProductRegistration)descriptor.ImplementationInstance;
                if (!string.Equals(previous.Product, product, StringComparison.Ordinal)
                    || !ReferenceEquals(previous.Configuration, configuration))
                    throw new InvalidOperationException("ToolUtility is already registered for a different product or configuration.");
                return services;
            }

            services.AddSingleton(new ProductRegistration(product, configuration));
            if (configuration != null) services.TryAddSingleton<IConfiguration>(configuration);

            // 此委派只捕獲 host 組態，不保存 provider；每個 factory 仍在原容器依原生命週期執行。
            IConfiguration ResolveConfiguration(IServiceProvider provider) =>
                configuration ?? provider.GetRequiredService<IConfiguration>();

            // Manager 與 pool 都是程序級資源擁有者；它們只能依賴 Singleton，
            // 絕不捕獲 request-scoped service。TryAdd 讓測試或其他產品可先提供
            // 自己的假連線服務，而不會意外建立第二個組合根實例。
            services.TryAddSingleton<ICrmConnectionService, CrmConnectionService>();
            // 未由產品組合根明確 opt-in 時，統一診斷一律 fail closed。這個 fallback
            // 不讀取 appsettings 的 Enabled，避免共用 ToolUtility 被其他 Host 誤開檔案追蹤。
            services.TryAddSingleton<DiagnosticTraceOptions>(_ =>
                DiagnosticTraceOptions.CreateDisabled(Environment.CurrentDirectory));
            services.TryAddSingleton<DataverseTraceOptions>(sp =>
            {
                var options = DataverseTraceOptions.FromDiagnosticOptions(
                    sp.GetRequiredService<DiagnosticTraceOptions>());
                options.Validate();
                return options;
            });
            // Trace 是唯一擁有背景佇列、檔案與程序內 HMAC salt 的 singleton；它不依賴
            // scoped service，也不保存 HttpContext，request 關聯只在 middleware 的 AsyncLocal 範圍內存在。
            services.TryAddSingleton<DataverseTrace>();
            services.TryAddSingleton<DataversePoolOptions>(sp =>
            {
                var options = new DataversePoolOptions();
                ResolveConfiguration(sp)
                    .GetSection("Dataverse:Pool")
                    .Bind(options);
                options.Validate();
                return options;
            });
            services.TryAddSingleton<DataverseConnectionManager>(sp =>
            {
                var effectiveConfiguration = ResolveConfiguration(sp);
                return new DataverseConnectionManager(
                    sp.GetRequiredService<ICrmConnectionService>(),
                    effectiveConfiguration,
                    product,
                    ResolveEnvironmentName(effectiveConfiguration),
                    sp.GetRequiredService<DataversePoolOptions>());
            });
            services.TryAddSingleton<IDataverseConnectionManager>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>());
            services.TryAddSingleton<IBoundedClientPool>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>().Pool);
            services.TryAddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
            services.TryAddSingleton<ICrmConnectionPool>(sp =>
                new ConnectionPoolStatsAdapter(sp.GetRequiredService<IDataverseConnectionManager>()));
            services.TryAddScoped<IDataverseGateway, DataverseGateway>();
            services.TryAddScoped<IOrganizationService, GatewayOrganizationService>();

            services.AddScoped<ToolUtilityClass>(sp => new ToolUtilityClass(
                sp.GetRequiredService<IOrganizationService>(),
                sp.GetRequiredService<IToolUtilityTracer>(),
                ResolveConfiguration(sp)));
            services.AddScoped<IToolUtilityProvider, ToolUtilityProvider>();
            return services;
        }

        /// <summary>
        /// 註冊期間的有限不可變標記，只含 host 擁有的產品與組態參照，不是使用者狀態快取。
        /// 與 IServiceCollection／容器同生命週期，不擁有組態或任何 IDisposable。
        /// </summary>
        private sealed class ProductRegistration
        {
            /// <summary>保存首次註冊的受信任邊界，供後續註冊比對，不讀取或複製機密內容。</summary>
            internal ProductRegistration(string product, IConfiguration configuration)
            {
                Product = product;
                Configuration = configuration;
            }

            /// <summary>唯一的 host 產品識別，不由 request 改寫。</summary>
            internal string Product { get; }

            /// <summary>原 host 組態；null 表示延用既有 DI 解析，不代表缺少連線驗證。</summary>
            internal IConfiguration Configuration { get; }
        }

        /// <summary>
        /// 解析主機環境名稱而不讓 ToolUtility 組件直接依賴 ASP.NET Core。
        /// Web Host 會把環境名稱放入組態；測試與非 Web 組合根則使用明確的
        /// DOTNET/ASPNETCORE 環境變數，最後才回到 Production。這個值只進入
        /// Pool Key，不承載使用者身分，也不會使不同環境共用 client。
        /// </summary>
        private static string ResolveEnvironmentName(IConfiguration configuration)
        {
            return configuration["ASPNETCORE_ENVIRONMENT"]
                ?? configuration["DOTNET_ENVIRONMENT"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Production";
        }
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Tests/EntityOperations/EntityQueryCharacterizationTests.cs
```csharp
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 在 EntityOperations 搬移前鎖定三個代表方法的完整 SDK 請求與結果語義。
    /// 所有 ID、名稱與資料為合成值；只替換外部 SDK，不建立網路、快取或使用者狀態。
    /// </summary>
    public sealed class EntityQueryCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-4111-8111-111111111111");

        /// <summary>
        /// 單筆查詢必須保留實體名稱、資料庫 ID 及 AllColumns；不得意外走 host cache。
        /// 回傳同一操作擁有的 Entity，原有欄位不遺失。
        /// </summary>
        [Fact]
        public void RetrieveEntity_PreservesIdColumnsAndResult()
        {
            var expected = new Entity("contact", RecordId) { ["fullname"] = "Synthetic Contact" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId,
                It.Is<ColumnSet>(columns => columns.AllColumns))).Returns(expected);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveEntity("contact", RecordId));
            client.VerifyAll();
        }

        /// <summary>
        /// 欄位查詢保留 TopCount=1、完整欄位、業務欄位與啟用條件；特殊文字必為型別化值，
        /// 不能因搬移變成 XML 插入或把顯示名稱當唯一識別碼。
        /// </summary>
        [Fact]
        public void RetrieveEntityByField_PreservesTypedConditions()
        {
            const string value = "Synthetic <&\"' value";
            var expected = new Entity("contact", RecordId);
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("contact", actual.EntityName);
                    Assert.Equal(1, actual.TopCount);
                    Assert.True(actual.ColumnSet.AllColumns);
                    Assert.Equal(new[] { "fullname", "statecode" }, actual.Attributes);
                    Assert.Equal(new object[] { value, 0 }, actual.Values);
                }).Returns(new EntityCollection(new[] { expected }));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveEntityByField("contact", "fullname", value));
            client.VerifyAll();
        }

        /// <summary>
        /// 工作查詢保留 100 筆上限、原欄位投影、createdon倒序、主旨與statecode條件。
        /// 完整 XML 與獨立黃金檔比較；同名不同資料庫 ID 兩筆工作均保留。
        /// </summary>
        [Fact]
        public void RetrieveTaskBySubject_MatchesGoldenAndPreservesSameNameRows()
        {
            var expected = new EntityCollection(new[]
            {
                new Entity("task", RecordId) { ["subject"] = "Synthetic Task" },
                new Entity("task", new Guid("22222222-2222-4222-8222-222222222222")) { ["subject"] = "Synthetic Task" }
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => GoldenQueryAssertions.Equal("entity-task.xml",
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = expected;
                    return response;
                });
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var actual = service.RetrieveTaskBySubject("Synthetic Task");
                Assert.Same(expected, actual);
                Assert.Equal(2, actual.Entities.Count);
                Assert.NotEqual(actual.Entities[0].Id, actual.Entities[1].Id);
            }
        }
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Tests/EntityOperations/EntityQueryContractTests.cs
```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 保護不經快取的實體讀取、整頁穩定 ID 與安全 XML 邊界。
    /// 每案只借入合成 SDK，故障原樣傳遞給 host；沒有真正 D365 或 LINE 操作。
    /// </summary>
    public sealed class EntityQueryContractTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-4111-8111-111111111111");

        /// <summary>
        /// 相同 ID 連查兩次必須兩次進 SDK，第二次讀到更新值而非第一個 caller 修改後的物件。
        /// 所有欄位沿用舊來源 AllColumns，不把此方法變成 host cache 的別名。
        /// </summary>
        [Fact]
        public void RetrieveEntityNoCache_RepeatedId_ReadsFreshSdkResult()
        {
            var generation = 0;
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId,
                It.Is<ColumnSet>(columns => columns.AllColumns)))
                .Returns(() => new Entity("contact", RecordId) { ["fullname"] = ++generation });
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var first = service.RetrieveEntityNoCache("contact", RecordId);
                first["fullname"] = "Synthetic caller mutation";
                var second = service.RetrieveEntityNoCache("contact", RecordId);
                Assert.NotSame(first, second);
                Assert.Equal(2, second["fullname"]);
            }
            client.Verify(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Exactly(2));
        }

        /// <summary>
        /// 不經快取入口的 SDK 故障必須維持原 Exception 實例，不回空、不重試或輸出敏感本文。
        /// </summary>
        [Fact]
        public void RetrieveEntityNoCache_SdkFault_PropagatesOriginalException()
        {
            var failure = new InvalidOperationException("Synthetic private SDK fault");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Throws(failure);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(
                    () => service.RetrieveEntityNoCache("contact", RecordId)));
        }

        /// <summary>
        /// XML特殊字元作為實體 logical name 並非合法資料庫識別，必須在 I/O 前拒絕；
        /// 此方法用型別化 Retrieve 而不是把名稱拼成 FetchXML，不能靠 escape 放行任意結構。
        /// </summary>
        /// <param name="entityName">非法 logical name。</param>
        [Theory]
        [InlineData("<&\"'")]
        [InlineData("")]
        [InlineData(" ")]
        public void RetrieveEntityNoCache_InvalidEntityName_RejectsBeforeIo(string entityName)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<ArgumentException>(() => service.RetrieveEntityNoCache(entityName, RecordId));
            client.VerifyNoOtherCalls();
        }

        /// <summary>空 GUID 不能變成廣泛查詢或 draft key，拒絕時 SDK 不得被呼叫。</summary>
        [Fact]
        public void RetrieveEntityNoCache_EmptyId_RejectsBeforeIo()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<ArgumentException>(() => service.RetrieveEntityNoCache("contact", Guid.Empty));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// SDK 回錯 ID 或實體是完整性故障，不可把其他資料回傳給原查詢者。
        /// </summary>
        /// <param name="wrongEntity">注入錯誤實體或不同非空 ID。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetrieveEntityNoCache_IdentityMismatch_RejectsPublication(bool wrongEntity)
        {
            var candidate = new Entity(wrongEntity ? "account" : "contact",
                wrongEntity ? RecordId : new Guid("22222222-2222-4222-8222-222222222222"));
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Returns(candidate);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => service.RetrieveEntityNoCache("contact", RecordId));
        }

        /// <summary>
        /// 工作主旨文字含全部 XML 特殊字元仍只能成為一個屬性值；新增節點／條件或改變 statecode
        /// 都屬可重現注入。本案檢查解碼原值與精確條件數，不只驗證查詢字串包含輸入。
        /// </summary>
        [Fact]
        public void RetrieveTaskBySubject_XmlSpecialCharacters_CannotInjectConditions()
        {
            const string subject = "Synthetic <&\"' value";
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request =>
                {
                    var xml = Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query;
                    var conditions = XDocument.Parse(xml).Descendants("condition").ToArray();
                    Assert.Equal(2, conditions.Length);
                    Assert.Equal(subject, (string?)conditions[0].Attribute("value"));
                    Assert.Equal("0", (string?)conditions[1].Attribute("value"));
                }).Returns(Response(new EntityCollection()));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Empty(service.RetrieveTaskBySubject(subject).Entities);
        }

        /// <summary>
        /// 通用查詢與 Execute 兩條路徑都要驗整頁 ID；注入相同非空 ID 後必須整頁拒絕，
        /// 且原候選不被靜默刪減，讓上層保有可診斷的原始失敗。
        /// </summary>
        /// <param name="execute">使用 Execute 或 RetrieveMultiple。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultiQuery_DuplicateId_RejectsEntireCandidate(bool execute)
        {
            var candidate = new EntityCollection(new[] { new Entity("contact", RecordId), new Entity("contact", RecordId) });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => Run(service, execute));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 空列 ID 是 SDK 完整性故障；合法不同 ID 的同名列在相同入口均需保留兩筆。
        /// </summary>
        /// <param name="missingId">是否注入空 GUID。</param>
        /// <param name="execute">SDK 執行路徑。</param>
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void MultiQuery_StableIds_AreRequiredAndSameNamesArePreserved(bool missingId, bool execute)
        {
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId) { ["fullname"] = "Synthetic Same Name" },
                new Entity("contact", missingId ? Guid.Empty : new Guid("22222222-2222-4222-8222-222222222222"))
                    { ["fullname"] = "Synthetic Same Name" }
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                if (missingId) Assert.Throws<InvalidOperationException>(() => Run(service, execute));
                else Assert.Equal(2, Run(service, execute).Entities.Count);
            }
        }

        /// <summary>
        /// Dispose 只停止新操作並釋放服務保存的借用參照，不關閉呼叫端 SDK。
        /// 以可計數的 IDisposable SDK 故障注入，關閉後不允許再做任何資料讀取。
        /// </summary>
        [Fact]
        public void Dispose_StopsNewOperationsWithoutDisposingBorrowedClient()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = client.As<IDisposable>();
            var service = new EntityQueryService(new object(), client.Object);
            service.Dispose();
            service.Dispose();
            Assert.Throws<ObjectDisposedException>(() => service.RetrieveEntity("contact", RecordId));
            disposable.Verify(value => value.Dispose(), Times.Never);
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 兩個已授權範圍交錯 1000 次必須只收到自己的 SDK 結果；八路固定上限避免測試無界 fan-out。
        /// 全部操作完成後 active 歸零，服務 Dispose 不釋放借入 client，也不留下背景工作。
        /// </summary>
        [Fact]
        public void NoCache_ParallelScopes_DoNotShareResultsAndDrain()
        {
            var otherId = new Guid("22222222-2222-4222-8222-222222222222");
            var first = new Mock<IOrganizationService>(MockBehavior.Strict);
            var second = new Mock<IOrganizationService>(MockBehavior.Strict);
            first.As<IDisposable>();
            second.As<IDisposable>();
            first.Setup(client => client.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", RecordId) { ["fullname"] = "Synthetic A" });
            second.Setup(client => client.Retrieve("contact", otherId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", otherId) { ["fullname"] = "Synthetic B" });
            var active = 0;
            var completed = 0;
            using (var a = new EntityQueryService(new object(), first.Object))
            using (var b = new EntityQueryService(new object(), second.Object))
                Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var isFirst = index % 2 == 0;
                        var row = (isFirst ? a : b).RetrieveEntityNoCache("contact", isFirst ? RecordId : otherId);
                        Assert.Equal(isFirst ? "Synthetic A" : "Synthetic B", row["fullname"]);
                        row["fullname"] = "Synthetic caller mutation";
                        Interlocked.Increment(ref completed);
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            Assert.Equal(0, active);
            Assert.Equal(1000, completed);
            first.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
            second.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
        }

        /// <summary>
        /// 依名稱查帳戶保留原 projection／TopCount／啟用條件；真正空頁才回空 GUID。
        /// 同名資料由既有單筆契約取首筆，而不是改寫或刪除候選中的其他合法 ID。
        /// </summary>
        /// <param name="empty">注入真正空頁或合法資料。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AccountNameQuery_PreservesProjectionAndEmptySemantics(bool empty)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("account", actual.EntityName);
                    Assert.Equal(1, actual.TopCount);
                    Assert.Equal(new[] { "accountid" }, actual.ColumnSet.Columns);
                    Assert.False(actual.ColumnSet.AllColumns);
                    Assert.Equal(new[] { "name", "statecode" }, actual.Attributes);
                    Assert.Equal(new object[] { "Synthetic Account", 0 }, actual.Values);
                }).Returns(empty ? new EntityCollection() : new EntityCollection(new[] { new Entity("account", RecordId) }));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Equal(empty ? Guid.Empty : RecordId, service.RetrieveAccountByName("Synthetic Account"));
        }

        /// <summary>
        /// QueryExpression 的既有空 PageInfo 會補第一頁 5000，上層分頁旗標與 cookie 原樣保留。
        /// 以真 SDK 請求物件進替身，比對完整頁選項而非只檢查程式碼文字。
        /// </summary>
        [Fact]
        public void QueryExpression_NullPageInfo_PreservesDefaultAndResponsePaging()
        {
            var expected = new EntityCollection { MoreRecords = true, PagingCookie = "Synthetic cookie" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryExpression>(query);
                    Assert.Equal(5000, actual.PageInfo.Count);
                    Assert.Equal(1, actual.PageInfo.PageNumber);
                }).Returns(expected);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveMultiple(new QueryExpression("contact") { PageInfo = null }));
        }

        /// <summary>
        /// 未初始化、null Query、null Request 與缺 Query 的 Request 都必須明確失敗，不能回空資料。
        /// 這些預期拒絕不發 LINE，借入 SDK 替身不應收到任何 I/O。
        /// </summary>
        [Fact]
        public void InvalidOperationInputs_RejectWithoutSdkIo()
        {
            using (var uninitialized = new EntityQueryService())
                Assert.Throws<InvalidOperationException>(() => uninitialized.RetrieveEntity("contact", RecordId));
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                Assert.Throws<ArgumentNullException>(() => service.RetrieveMultiple(null!));
                Assert.Throws<ArgumentNullException>(() => service.ExecuteRetrieveMultiple(null!));
                Assert.Throws<ArgumentException>(() => service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest()));
                client.Setup(value => value.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
                Assert.Null(service.RetrieveEntityByField("contact", "fullname", "Synthetic absent contact"));
            }
            client.Verify(value => value.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            client.VerifyNoOtherCalls();
        }

        /// <summary>建立操作局部 SDK 回應，不保留於 static 或共用快取。</summary>
        private static RetrieveMultipleResponse Response(EntityCollection candidate)
        {
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            return response;
        }

        /// <summary>由測試固定路徑呼叫真實服務，不在替身重做產品驗證邏輯。</summary>
        private static EntityCollection Run(EntityQueryService service, bool execute) => execute
            ? service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = new QueryByAttribute("contact") })
            : service.RetrieveMultiple(new QueryByAttribute("contact"));
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Tests/DependencyInjection/ProductRegistrationTests.cs
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ToolUtility.Tests.DependencyInjection
{
    /// <summary>
    /// 驗證產品組合根真正傳入連線池的完整隔離鍵，而非只檢查註冊文字。
    /// 外部連線由合成 SDK 替身取代；容器是唯一 pool owner，每案結束必確定性 Dispose。
    /// </summary>
    public sealed class ProductRegistrationTests
    {
        /// <summary>
        /// 兩個產品使用相同合成組織／身分時，實際租約仍進入不同 Product 鍵。
        /// 若 factory 仍寫死 ChurchReport，本案必失敗；釋放後租約／等待者歸零，容器再關閉 client。
        /// </summary>
        [Fact]
        public void AddToolUtility_ProductParameter_PartitionsConnectionKey()
        {
            var aClient = Client();
            var bClient = Client();
            var aServices = Services(aClient);
            var bServices = Services(bClient);
            aServices.AddToolUtility(Configuration(), "ProductA");
            bServices.AddToolUtility(Configuration(), "ProductB");

            using (var a = aServices.BuildServiceProvider())
            using (var b = bServices.BuildServiceProvider())
            {
                var aManager = a.GetRequiredService<IDataverseConnectionManager>();
                var bManager = b.GetRequiredService<IDataverseConnectionManager>();
                using (var first = aManager.Acquire())
                using (var second = bManager.Acquire())
                {
                    Assert.NotSame(first.Service, second.Service);
                    var aKey = ActualKey(aManager);
                    var bKey = ActualKey(bManager);
                    Assert.Equal("ProductA", aKey.Product);
                    Assert.Equal("ProductB", bKey.Product);
                    Assert.NotEqual(aKey, bKey);
                    Assert.Equal(aKey.OrganizationUrl, bKey.OrganizationUrl);
                    Assert.Equal(aKey.EffectiveIdentity, bKey.EffectiveIdentity);
                }
                Assert.Equal(0, aManager.GetMetrics().Leased);
                Assert.Equal(0, bManager.GetMetrics().Waiting);
                Assert.Equal(1, aManager.GetMetrics().TotalReleases);
                Assert.Equal(1, bManager.GetMetrics().TotalReleases);
                aClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
                bClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
            }
            aClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Once);
            bClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Once);
        }

        /// <summary>
        /// 原本無參數的公開 overload 必須保留 ChurchReport 鍵與 DI 組態讀取，
        /// 不能在註冊期間另建容器或連線，亦不能讓未解析服務逃過容器關閉。
        /// </summary>
        [Fact]
        public void AddToolUtility_LegacyOverload_UsesChurchReport()
        {
            var client = Client();
            var services = Services(client);
            services.AddSingleton<IConfiguration>(Configuration());
            services.AddToolUtility();
            using (var provider = services.BuildServiceProvider())
            {
                var manager = provider.GetRequiredService<IDataverseConnectionManager>();
                using (manager.Acquire())
                    Assert.Equal("ChurchReport", ActualKey(manager).Product);
                Assert.Equal(0, manager.GetMetrics().Leased);
            }
            client.As<IDisposable>().Verify(service => service.Dispose(), Times.Once);
        }

        /// <summary>
        /// 同一容器先後註冊不同產品必須拒絕，不能讓 TryAddSingleton 靜默沿用第一個產品。
        /// 失敗前後服務描述數相等，避免部分註冊污染現有組合根。
        /// </summary>
        [Fact]
        public void AddToolUtility_DifferentProductsInOneContainer_RejectsWithoutMutation()
        {
            var services = Services(Client());
            var configuration = Configuration();
            services.AddToolUtility(configuration, "ProductA");
            var count = services.Count;

            Assert.Throws<InvalidOperationException>(() => services.AddToolUtility(configuration, "ProductB"));
            Assert.Equal(count, services.Count);
        }

        /// <summary>
        /// 完全相同組態與產品的重複註冊必須冪等，不能建立第二份 singleton owner 或 scoped provider。
        /// </summary>
        [Fact]
        public void AddToolUtility_SameRegistration_IsIdempotent()
        {
            var services = Services(Client());
            var configuration = Configuration();
            services.AddToolUtility(configuration, "ProductA");
            var count = services.Count;

            services.AddToolUtility(configuration, "ProductA");

            Assert.Equal(count, services.Count);
        }

        /// <summary>
        /// 產品名稱是受信任組合根的必要邊界；空白不得回退另一產品或改動容器。
        /// </summary>
        /// <param name="product">合成的無效產品值。</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void AddToolUtility_InvalidProduct_RejectsBeforeRegistration(string? product)
        {
            var services = new ServiceCollection();
            Assert.Throws<ArgumentException>(() => services.AddToolUtility(Configuration(), product!));
            Assert.Empty(services);
        }

        /// <summary>
        /// 同產品使用不同組態物件再註冊也須明確拒絕，避免 manager 與 facade 各自讀不同的身分／組織。
        /// </summary>
        [Fact]
        public void AddToolUtility_ConflictingConfiguration_RejectsWithoutMutation()
        {
            var services = Services(Client());
            services.AddToolUtility(Configuration(), "ProductA");
            var count = services.Count;
            Assert.Throws<InvalidOperationException>(() => services.AddToolUtility(Configuration(), "ProductA"));
            Assert.Equal(count, services.Count);
        }

        /// <summary>合成組態不含實際連線或機密；容量設一避免產生無用預熱 client。</summary>
        private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://synthetic.invalid/organization",
                ["CrmConnection:Username"] = "synthetic-service",
                ["Dataverse:Pool:MinSize"] = "1",
                ["Dataverse:Pool:MaxN"] = "1",
                ["DOTNET_ENVIRONMENT"] = "P1OfflineTest"
            }).Build();

        /// <summary>建立只有確定性 Dispose 的合成 SDK；不啟動外部服務、不保存使用者資料。</summary>
        private static Mock<IOrganizationService> Client()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Loose);
            client.As<IDisposable>();
            return client;
        }

        /// <summary>只替換真正外部 I/O factory，保留 manager／pool 的完整產品鍵與租約流程。</summary>
        private static ServiceCollection Services(Mock<IOrganizationService> client)
        {
            var factory = new Mock<ICrmConnectionService>(MockBehavior.Strict);
            factory.Setup(service => service.CreateOnPremiseClient(
                "https://synthetic.invalid/organization", "synthetic-service", string.Empty)).Returns(client.Object);
            var services = new ServiceCollection();
            services.AddSingleton(factory.Object);
            return services;
        }

        /// <summary>
        /// 白箱只讀已建立子池的真正鍵，避免為測試擴大會揭露組織／服務身分的公開診斷 API。
        /// 讀取只發生於本案已取得租約、無並行新增的固定時點，不保留任何 pool 參照於案外。
        /// </summary>
        private static DataverseConnectionKey ActualKey(IDataverseConnectionManager manager)
        {
            var pool = Assert.IsType<DataverseConnectionManager>(manager).Pool;
            var field = typeof(BoundedClientPool).GetField("_subPools", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var dictionary = Assert.IsAssignableFrom<IDictionary>(field!.GetValue(pool));
            return Assert.IsType<DataverseConnectionKey>(Assert.Single(dictionary.Keys.Cast<object>()));
        }
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Tests/FeeOperations/FeeQueryContractTests.cs
```csharp
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 保護 P1-04 新增收費查詢及既有查詢安全契約。所有資料、ID與故障均合成；
    /// 每案獨立借入 SDK 替身，沒有實機連線、共享快取或持續背景工作。
    /// </summary>
    public sealed class FeeQueryContractTests
    {
        private const string ScopeId = "11111111-1111-4111-8111-111111111111";
        private const string SpecialText = "Synthetic <&\"' value";

        /// <summary>
        /// 鎖定三個舊來源補回方法的完整條件及欄位；注入同名不同ID兩筆資料，
        /// 以完整golden及兩筆ID斷言證明沒有按名稱合併或丟失。
        /// </summary>
        /// <param name="operation">待新增的三個查詢之一。</param>
        /// <param name="golden">唯讀合成XML資源。</param>
        [Theory]
        [InlineData("Paid", "fee-paid.xml")]
        [InlineData("Unpaid", "fee-unpaid.xml")]
        [InlineData("Order", "fee-order.xml")]
        public void NewQuery_MatchesGoldenAndPreservesSameNameRows(string operation, string golden)
        {
            var expected = Rows();
            expected.MoreRecords = true;
            expected.PagingCookie = "Synthetic paging cookie";
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(golden, xml));
            var sut = new FeeService(new object(), client.Object);

            var result = Invoke(sut, operation);

            Assert.Same(expected, result);
            Assert.True(result.MoreRecords);
            Assert.Equal("Synthetic paging cookie", result.PagingCookie);
            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 課程ID是查詢界線；注入空白、空GUID及XML注入字串均須在I/O前拒絕，
        /// 不可將XML編碼誤當成授權或合法識別碼驗證。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        /// <param name="invalidId">無效的合成識別值。</param>
        [Theory]
        [InlineData("Paid", "")]
        [InlineData("Unpaid", " ")]
        [InlineData("Paid", "00000000-0000-0000-0000-000000000000")]
        [InlineData("Unpaid", "<&\"'")]
        public void LessonQuery_InvalidId_RejectsBeforeIo(string operation, string invalidId)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => Invoke(sut, operation, invalidId));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 舊API保留但未使用的課程名稱可含XML字元，不能被加入查詢或拿來當識別／權限；
        /// 完整golden仍相等，證明兩個方法只按伺服器提供的課程ID查詢。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        public void LessonQuery_DisplayNameCannotChangeScope(string operation)
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal(
                operation == "Paid" ? "fee-paid.xml" : "fee-unpaid.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, Invoke(sut, operation, ScopeId, SpecialText).Entities.Count);
        }

        /// <summary>
        /// 訂單號是文字而非GUID；含所有XML特殊字元仍須只產生原有兩個OR條件，
        /// 解碼後值逐字相等，不能新增節點或讓卡／ATM任一分支遺失。
        /// </summary>
        [Fact]
        public void OrderQuery_XmlSpecialCharacters_RoundTripWithoutInjection()
        {
            var client = Client(Rows(), xml =>
            {
                var entity = XDocument.Parse(xml).Root!.Element("entity")!;
                var conditions = entity.Element("filter")!.Element("filter")!.Elements("condition").ToArray();
                Assert.Equal("or", (string?)entity.Element("filter")!.Element("filter")!.Attribute("type"));
                Assert.Equal(2, conditions.Length);
                Assert.Equal(new[] { "new_q_pay_card_order_no", "new_q_pay_order_atm_no" },
                    conditions.Select(condition => (string?)condition.Attribute("attribute")));
                Assert.All(conditions, condition => Assert.Equal(SpecialText, (string?)condition.Attribute("value")));
            });
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, sut.RetrieveFeeByFetchXmlOrderNumber(SpecialText).Entities.Count);
        }

        /// <summary>
        /// 空訂單號不得擴張為廣泛查詢，並且拒絕前不碰SDK。
        /// </summary>
        /// <param name="orderNumber">合成無效輸入。</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void OrderQuery_EmptyInput_RejectsBeforeIo(string? orderNumber)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => sut.RetrieveFeeByFetchXmlOrderNumber(orderNumber!));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 每個新增方法注入SDK故障，原始同一Exception必須傳回host，不能回空集合、
        /// 吞例外或包成含私密本文的新訊息。Core不是最終通知邊界。
        /// </summary>
        /// <param name="operation">新增查詢名稱。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        public void NewQuery_SdkFault_PropagatesSameException(string operation)
        {
            var fault = new InvalidOperationException("Synthetic private SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation)));
        }

        /// <summary>
        /// 空殼認獻／收費方法必須沿用已鎖定的有效查詢，而不是自行回空集合。
        /// SDK request、上限及欄位與有效來源golden完全一致。
        /// </summary>
        /// <param name="operation">原有空方法。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        public void FormerPlaceholder_UsesEstablishedQuery(string operation)
        {
            var expected = operation == "Booking" ? new EntityCollection(new[]
            {
                new Entity("new_dedication_booking", new Guid("22222222-2222-4222-8222-222222222222"))
            }) : Rows();
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(
                operation == "Booking" ? "query-dedication-booking.xml" : "query-fee-by-booking.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(expected, Invoke(sut, operation));
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 注入同一非空資料庫ID兩次；所有受改查詢都必須拒絕整個候選，
        /// 不可保留一筆、名稱去重或將部分結果交給上層。
        /// </summary>
        /// <param name="operation">Fee全部公開查詢入口。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void Query_DuplicateStableId_RejectsPublication(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            candidate.Entities[1].Id = candidate.Entities[0].Id;
            var client = Client(candidate, _ => { });
            var sut = new FeeService(new object(), client.Object);

            var fault = Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation));

            Assert.DoesNotContain(candidate.Entities[0].Id.ToString(), fault.Message);
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 欠缺真實資料庫 ID 的正式列或混入其他實體列均拒絕；不能產生draft key讓正式資料通過。
        /// </summary>
        /// <param name="operation">Fee全部查詢入口。</param>
        /// <param name="missingId">注入空ID或錯誤實體型別。</param>
        [Theory]
        [InlineData("Paid", true)]
        [InlineData("Unpaid", false)]
        [InlineData("Order", true)]
        [InlineData("Booking", false)]
        [InlineData("Fee", true)]
        [InlineData("Dedication", false)]
        [InlineData("DateRange", true)]
        public void Query_InvalidStableIdentity_RejectsPublication(string operation, bool missingId)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            if (missingId) candidate.Entities[1].Id = Guid.Empty;
            else candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = Client(candidate, _ => { });

            Assert.Throws<InvalidOperationException>(() => Invoke(new FeeService(new object(), client.Object), operation));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 所有lookup顯示名稱及期數文字只可當XML值；含特殊字元解碼後須逐字相等，
        /// 條件數不增加，證明修復既有入口而非只保護新增訂單方法。
        /// </summary>
        /// <param name="operation">使用外部文字的既有查詢。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void ExistingQuery_SpecialDisplayName_IsOnlyAnAttributeValue(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            var client = Client(candidate, xml =>
            {
                var lookups = XDocument.Parse(xml).Descendants("condition")
                    .Where(condition => condition.Attribute("uiname") != null).ToArray();
                Assert.Single(lookups);
                Assert.Equal(SpecialText, (string?)lookups[0].Attribute("uiname"));
                if (operation == "Fee")
                    Assert.Equal(SpecialText, (string?)XDocument.Parse(xml).Descendants("condition")
                        .Single(condition => (string?)condition.Attribute("attribute") == "new_paid_period").Attribute("value"));
            });

            var sut = new FeeService(new object(), client.Object);
            if (operation == "Fee")
                sut.RetrieveFee(SpecialText, ScopeId, SpecialText);
            else
                Invoke(sut, operation, ScopeId, SpecialText);
        }

        /// <summary>
        /// SDK每次提供獨立操作資料；第一個呼叫者修改集合不可污染下一次回應，
        /// 並以兩次真實SDK呼叫證明服務沒有偷偷快取前一範圍的可變結果。
        /// </summary>
        [Fact]
        public void Query_CallerMutation_DoesNotBecomeNextOperationState()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = Rows();
                    return response;
                });
            var sut = new FeeService(new object(), client.Object);
            var first = Invoke(sut, "Paid");
            first.Entities[0]["new_name"] = "Synthetic caller mutation";
            first.Entities.RemoveAt(1);

            var second = Invoke(sut, "Paid");

            Assert.NotSame(first, second);
            Assert.Equal(2, second.Entities.Count);
            Assert.Equal("Synthetic Fee", second.Entities[0]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Exactly(2));
        }

        /// <summary>建構時拒絕沒有授權SDK的服務，不能延至呼叫才以NullReferenceException掩蓋設定失敗。</summary>
        [Fact]
        public void Constructor_NullService_RejectsImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => new FeeService(null!));
        }

        /// <summary>
        /// 期數是必要業務條件；明確鎖定空白拒絕而非悄悄擴張查詢。
        /// 現有 ChurchReport 消費者固定傳入001，此邊界不改變其有效流程；拒絕前不碰SDK。
        /// </summary>
        /// <param name="period">無效的合成期數。</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void RetrieveFee_EmptyPeriod_RejectsBeforeIo(string period)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(client.Object);
            Assert.Throws<ArgumentException>(() => sut.RetrieveFee("Synthetic Booking", ScopeId, period));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 非西曆環境不能改變資料庫日期條件；在本案局部注入泰國曆並finally還原，
        /// 應仍符合已核對的西曆golden，不能靠改主機全域文化換取通過。
        /// </summary>
        [Fact]
        public void DateRange_ThaiCulture_UsesGregorianQuery()
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal("fee-dedication-date-range.xml", xml));
            var sut = new FeeService(new object(), client.Object);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                Invoke(sut, "DateRange");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// A/B各自借入SDK，交錯執行1000次不混查詢或資料，操作全部排空後不留活動工作；
        /// IDisposable借入服務不能被Fee關閉。固定八路排程避免測試本身無界fan-out。
        /// </summary>
        [Fact]
        public void Query_ParallelScopes_DoNotLeakOrDisposeBorrowedService()
        {
            var firstClient = Client(Rows(), xml => Assert.Contains(ScopeId, xml));
            var secondId = "44444444-4444-4444-8444-444444444444";
            var secondRows = Rows();
            secondRows.Entities[0].Id = new Guid("55555555-5555-4555-8555-555555555555");
            var secondClient = Client(secondRows, xml => Assert.Contains(secondId, xml));
            var firstDisposable = firstClient.As<IDisposable>();
            var secondDisposable = secondClient.As<IDisposable>();
            var first = new FeeService(new object(), firstClient.Object);
            var second = new FeeService(new object(), secondClient.Object);
            var active = 0;
            var completed = 0;

            Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    var isFirst = index % 2 == 0;
                    var result = Invoke(isFirst ? first : second, "Paid", isFirst ? ScopeId : secondId);
                    Assert.Equal(isFirst ? Rows().Entities[0].Id : secondRows.Entities[0].Id, result.Entities[0].Id);
                    Interlocked.Increment(ref completed);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

            Assert.Equal(1000, completed);
            Assert.Equal(0, active);
            firstDisposable.Verify(service => service.Dispose(), Times.Never);
            secondDisposable.Verify(service => service.Dispose(), Times.Never);
        }

        /// <summary>
        /// 建立每案獨立SDK替身；只保留該案候選，callback收到的XML立即驗證，不保存請求或credential。
        /// </summary>
        private static Mock<IOrganizationService> Client(EntityCollection candidate, Action<string> assertXml)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => assertXml(
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(response);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => assertXml(Assert.IsType<FetchExpression>(query).Query))
                .Returns(candidate);
            return client;
        }

        /// <summary>合成的兩筆同名不同ID資料，沒有靜態可變資料或真實個資。</summary>
        private static EntityCollection Rows(string logicalName = "new_fee") => new EntityCollection(new[]
        {
            new Entity(logicalName, new Guid("22222222-2222-4222-8222-222222222222")) { ["new_name"] = "Synthetic Fee" },
            new Entity(logicalName, new Guid("33333333-3333-4333-8333-333333333333")) { ["new_name"] = "Synthetic Fee" }
        });

        /// <summary>依固定測試名稱呼叫真正服務，不在替身複製查詢或驗證邏輯。</summary>
        private static EntityCollection Invoke(FeeService sut, string operation, string id = ScopeId, string name = "Synthetic Contact")
        {
            switch (operation)
            {
                case "Paid": return sut.GetPaidFee(name, id);
                case "Unpaid": return sut.GetNotYetPaidFee(name, id);
                case "Order": return sut.RetrieveFeeByFetchXmlOrderNumber("Synthetic-Order");
                case "Booking": return sut.RetrieveDedicationBooking(name, id);
                case "Fee": return sut.RetrieveFee("Synthetic Booking", id, "3");
                case "Dedication": return sut.RetrieveDedicationFee(name, id);
                case "DateRange": return sut.RetrieveDedicationFeeByDateRange(name, id, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                default: throw new ArgumentException("Unknown synthetic operation.", nameof(operation));
            }
        }
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Tests/CoreTargetFrameworkTests.cs
```csharp
using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Same(assembly, typeof(EntityQueryService).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }
    }
}

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Tests/ToolUtility.Tests.csproj
```csharp
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
  </PropertyGroup>

  <ItemGroup>
    <!-- 測試框架 -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <!-- Mock 框架 -->
    <PackageReference Include="Moq" />
    
    <!-- 斷言庫 -->
    <PackageReference Include="FluentAssertions" />
    
    <!-- 程式碼覆蓋率 -->
    <PackageReference Include="coverlet.collector">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.msbuild">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 專案參考 -->
    <ProjectReference Include="..\ToolUtility\ToolUtility.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ToolUtility.Core\ToolUtility.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- CRM SDK 套件（用於測試） -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
    <!-- 既有 host 測試完整保留在 net10；Framework 宿主只編譯純 Core 契約，不引用 net10 host。 -->
    <Compile Remove="**\*.cs" />
    <Compile Include="FeeOperations\*.cs" />
    <Compile Include="EntityOperations\EntityQueryCharacterizationTests.cs" />
    <Compile Include="EntityOperations\EntityQueryContractTests.cs" />
    <Compile Include="TestHelpers\GoldenQueryAssertions*.cs" />
    <Compile Include="CoreTargetFrameworkTests.cs" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 測試直接消費 SDK，需同樣以中央安全版本覆蓋其舊傳遞相依。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
    <PackageReference Include="System.Security.Permissions" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- 黃金查詢是合成資料，內嵌後兩個測試宿主均不依賴執行目錄或網路。 -->
    <EmbeddedResource Include="Golden\ToolUtility\*.xml"
                      LogicalName="ToolUtility.Tests.Golden.ToolUtility.%(Filename)%(Extension)" />
  </ItemGroup>

</Project>

```

## 最新版（同路徑舊快照若重複以此段為準） ToolUtility.Core/ToolUtility.Core.csproj
```csharp
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net462;net10.0</TargetFrameworks>
    <RootNamespace>ToolUtilityNameSpace</RootNamespace>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <!-- Framework SDK 傳遞引用 6.0.8，直接使用中央安全修補版；net10 則使用內建 JSON。 -->
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 僅取得 SDK 型別；純資料核心不建立 ServiceClient 或擁有連線。 -->
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 舊 SDK 帶入的傳遞版本有已知弱點；使用中央表的安全版本，維持完整 audit。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
  </ItemGroup>
</Project>

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
