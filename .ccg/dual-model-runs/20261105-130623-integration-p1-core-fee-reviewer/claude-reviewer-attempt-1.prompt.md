ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-core-fee

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


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.