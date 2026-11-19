/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P8-01b ChurchReport 奉獻類別清單與讀寫契約；測試只使用合成 Entity、
 * Core 領域值與本機原始碼，不建立 Host、不讀取秘密，也不連接任何 D365 組織。
 * 主要流程：以反射呼叫產品層內部純 resolver，注入登入 contact、教會類別快照、收費單 Entity
 * 與認獻預約欄位，檢查伺服器教會範圍、整批 ID 原子解析、lookup/legacy 同步寫入及拒絕條件；另以 Roslyn
 * 字串 token 掃描鎖住兩份衝突對照與 UI 類別名稱硬編碼移除，註解不納入 token 判斷。另以異教會受款人、
 * 舊表單解析狀態及 ATM 文案注入故障，保護 KeyIn 的登入同工 scope 和不可變類別名稱輸出。
 * 使用邊界與維護：資料庫 Guid 才是類別身分，同名異 ID 必須保留；request 不可提供權威教會、
 * 類別名稱或 legacy 值。每個案例的合成輸入只在該測試存活，沒有共享快取、背景工作或外部資源。
 * 故障注入與驗證：以跨教會類別、無 lookup、無 legacy value、未知舊標籤及衝突 switch 作故障輸入；
 * 決定性斷言是拒絕寫入、lookup 優先、未知類別不回退成什一／十一奉獻，以及全部測試 owner 無需清理外部資源。
 */
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

/// <summary>
/// 固定 ChurchReport 奉獻類別正式 ID、教會隔離、舊欄相容及顯示回退契約；不使用實機或秘密。
/// </summary>
public sealed class OfferingCategoryChurchReportTests
{
    private static readonly Guid ChurchA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChurchB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>故障注入舊 option 與 lookup 名稱不一致，斷言既有收費單文字以正式 lookup 名稱為先。</summary>
    [Fact]
    public void Read_LookupPreferredOverOptionSet()
    {
        var fee = new Entity("new_fee")
        {
            ["new_offeringcategoryid"] = new EntityReference("new_offeringcategory", CategoryId) { Name = "資料庫類別" },
            ["new_category"] = new OptionSetValue(100000000)
        };
        fee.FormattedValues["new_category"] = "舊選項標籤";

        DonationFeeQueryService.ConvertCategory(fee).Should().Be("資料庫類別");
    }

    /// <summary>
    /// 注入伺服器登入 contact 屬於 A 教會，並觀察實際 QueryExpression 的 account 條件；
    /// 再注入 B 類別至 A 範圍的寫入解析，斷言讀取查詢和寫入驗證均不跨教會。
    /// </summary>
    [Fact]
    public void Settings_ChurchA_CannotSeeChurchB()
    {
        var queryWasChurchScopedToA = false;
        var service = DispatchProxy.Create<IOrganizationService, OfferingCategoryScopeSdkProxy>();
        var proxy = (OfferingCategoryScopeSdkProxy)(object)service;
        proxy.RetrieveMultipleHandler = query =>
            {
                var expression = Assert.IsType<QueryExpression>(query);
                expression.EntityName.Should().Be("new_offeringcategory");
                var churchCondition = expression.Criteria.Conditions.Should().ContainSingle(
                    condition => condition.AttributeName == "new_accountid").Subject;
                churchCondition.Operator.Should().Be(ConditionOperator.Equal);
                churchCondition.Values.Should().ContainSingle().Which.Should().Be(ChurchA);
                queryWasChurchScopedToA = true;
                return new EntityCollection();
            };
        var loginContact = new Entity("contact")
        {
            ["parentcustomerid"] = new EntityReference("account", ChurchA)
        };

        var categories = DonationOfferingCategoryResolver.ReadForContact(service, loginContact);

        categories.Categories.Should().BeEmpty();
        queryWasChurchScopedToA.Should().BeTrue();
        proxy.RetrieveMultipleCalls.Should().Be(1);

        var list = new OfferingCategoryList(new[]
        {
            new OfferingCategory(CategoryId, ChurchB, "legacy-100000001", "B 類別", 1, null, 100000001, true)
        });

        var result = InvokeResolver("ResolveForWrite", ChurchA, list, CategoryId);

        result.Should().BeNull("跨教會提交必須被拒絕，而非將 B 類別交給呼叫端");
    }

    /// <summary>以 Roslyn 程式 token 確認兩份互相衝突的 option-to-text 實作都已移除。</summary>
    [Fact]
    public void OfferingText_ConflictingSwitchMapsRemoved()
    {
        var processor = ReadProductSource("Tools", "DonationFeePaymentProcessor.cs");
        var recurring = ReadProductSource("Tools", "RecurringDonationPaymentProcessor.cs");
        var helper = ReadProductSource("Services", "PaymentFeeTypeHelper.cs");

        GetStringTokens(processor).Should().NotContain("什一奉獻");
        GetStringTokens(recurring).Should().NotContain("什一奉獻");
        GetStringTokens(helper).Should().NotContain("100000019");
        processor.Should().NotContain("GetDedicationCategoryText");
        recurring.Should().NotContain("GetDedicationCategoryText");
    }

    /// <summary>未知奉獻類別缺少 lookup 與 legacy 標籤時，只能使用通用名稱，不能偽裝成什一或十一奉獻。</summary>
    [Fact]
    public void OfferingText_UnknownFallsBackToGenericText_NotTithe()
    {
        var fee = new Entity("new_fee");

        DonationFeeQueryService.ConvertCategory(fee).Should().Be("奉獻");
    }

    /// <summary>
    /// 只檢查程式字串 token 與兩個舊頁面選擇器，確保類別由伺服器教會清單提供，
    /// 提交值為資料庫 ID 而非固定名稱；本案例離線讀取原始碼，不啟動 MVC 或 CRM。
    /// </summary>
    [Fact]
    public void Form_CategoryListFromChurch_NoHardCodedNames()
    {
        var assembler = ReadProductSource("Services", "DonationPaymentModelAssembler.cs");
        var names = new[] { "主日奉獻", "十一奉獻", "感恩奉獻", "建堂奉獻", "宣教奉獻", "愛心奉獻", "特別奉獻" };

        GetStringTokens(assembler).Should().NotContain(names);

        foreach (var viewName in new[] { "DedicationView.cshtml", "DedicationInofView.cshtml" })
        {
            var view = ReadProductSource("Views", "Home", viewName);
            view.Should().Contain(".ValueExpr(\"Id\")");
            view.Should().Contain(".DisplayExpr(\"Name\")");
            view.Should().NotContain(".Value(\"十一奉獻\")");
        }

        typeof(DedicationModel).GetProperty(nameof(DedicationModel.DedicationCategoryList))!
            .PropertyType.Should().Be(typeof(System.Collections.Generic.List<DonationOfferingCategoryOption>));
        typeof(DedicationInfoModel).GetProperty(nameof(DedicationInfoModel.DedicationCategoryList))!
            .PropertyType.Should().Be(typeof(System.Collections.Generic.List<DonationOfferingCategoryOption>));
    }

    /// <summary>
    /// 注入正式可選與 metadata 不可提交兩種項目，檢查舊 Home 表單的下拉資料來源會排除不可提交項目；
    /// 避免空 Guid 顯示後被當成有效操作，並以原始碼契約確認兩個 Razor 畫面一致套用篩選。
    /// </summary>
    [Fact]
    public void Form_LegacyHomeViewsExcludeNonSelectableFallbackOptions()
    {
        var options = new[]
        {
            new DonationOfferingCategoryOption(CategoryId.ToString("D"), "正式奉獻", true),
            new DonationOfferingCategoryOption(string.Empty, "舊 metadata 項目", false)
        };

        options.Where(option => option.IsSelectable)
            .Select(option => option.Id)
            .Should().Equal(CategoryId.ToString("D"));

        foreach (var viewName in new[] { "DedicationView.cshtml", "DedicationInofView.cshtml" })
        {
            var view = ReadProductSource("Views", "Home", viewName);
            view.Should().Contain(".Where(option => option.IsSelectable)");
        }
    }

    /// <summary>以排序和啟用狀態故障資料驗證表單預設為排序第一個啟用類別。</summary>
    [Fact]
    public void Form_DefaultCategory_FirstActiveBySortOrder()
    {
        var list = new OfferingCategoryList(new[]
        {
            new OfferingCategory(CategoryId, ChurchA, "later", "後順位", 2, null, 100000002, true),
            new OfferingCategory(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), ChurchA, "inactive", "停用", 0, null, 100000003, false),
            new OfferingCategory(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), ChurchA, "first", "先順位", 1, null, 100000001, true)
        });

        InvokeResolver("GetDefaultCategory", list).Should()
            .Be(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
    }

    /// <summary>確認空教會類別清單才走 OptionSet metadata 相容路徑，且查詢故障不回到寫死清單。</summary>
    [Fact]
    public void Form_ChurchWithoutCategories_UsesOptionSetMetadata()
    {
        var source = ReadProductSource("Services", "DonationPaymentModelAssembler.cs");
        source.Should().Contain("ReadOptionsForContact");
        var resolver = ReadProductSource("Services", "DonationOfferingCategoryResolver.cs");
        resolver.Should().Contain("GetOptionSetMapping");
        resolver.Should().Contain("using var metadataCache");
        resolver.Should().Contain("OfferingCategoryReader");
        GetStringTokens(source).Should().NotContain(new[] { "主日奉獻", "十一奉獻", "感恩奉獻", "建堂奉獻" });
    }

    /// <summary>建立收費單候選並斷言正式 lookup 與選填相容欄位由同一伺服器類別投影寫入。</summary>
    [Fact]
    public void Write_LookupAndLegacyOptionBothSet()
    {
        var category = Category();
        var fee = new Entity("new_fee");

        InvokeResolver("ApplyToEntity", fee, category, "new_offeringcategoryid", "new_category");

        Assert.Equal(CategoryId, fee.GetAttributeValue<EntityReference>("new_offeringcategoryid").Id);
        Assert.Equal(100000001, fee.GetAttributeValue<OptionSetValue>("new_category").Value);
    }

    /// <summary>故障注入缺少舊 option value，依 D8-9 必填規則斷言任何 CRM Create 前即拒絕。</summary>
    [Fact]
    public void Write_CategoryWithoutLegacyValue_FollowsRequiredLevelRule()
    {
        var category = new OfferingCategory(CategoryId, ChurchA, "custom-no-legacy", "自訂", 1, null, null, true);
        var fee = new Entity("new_fee");

        var action = () => InvokeResolver("ApplyToEntity", fee, category, "new_offeringcategoryid", "new_category");

        action.Should().Throw<TargetInvocationException>()
            .Where(exception => exception.InnerException is InvalidOperationException);
        fee.Attributes.Should().BeEmpty("拒絕必須發生在任何欄位部分發布之前");
    }

    /// <summary>故障注入 A 清單含 B 所屬類別，斷言 resolver 再次核對伺服器教會範圍並拒絕。</summary>
    [Fact]
    public void Write_CategoryFromOtherChurch_Rejected()
    {
        var category = new OfferingCategory(CategoryId, ChurchB, "legacy-100000001", "B 類別", 1, null, 100000001, true);
        var result = InvokeResolver("ResolveForWrite", ChurchA, new OfferingCategoryList(new[] { category }), CategoryId);

        result.Should().BeNull();
    }

    /// <summary>
    /// 保護人工補單只能使用登入同工的教會類別範圍，且受款人必須屬於同一教會。
    /// 以合成登入者／受款人 contact 注入同教會及跨教會情境，並核對產品入口先驗證登入範圍；
    /// 不啟動 CRM 或付款依賴，也不保留 contact 參照。
    /// </summary>
    [Fact]
    public void KeyIn_CategoryScopeUsesLoginContact_AndRejectsDonorFromOtherChurch()
    {
        var loginContact = Contact(ChurchA);
        var sameChurchDonor = Contact(ChurchA);
        var otherChurchDonor = Contact(ChurchB);

        InvokeResolver("ValidateKeyInDonor", loginContact, sameChurchDonor)
            .Should().BeNull();

        var exception = Xunit.Record.Exception(() =>
            InvokeResolver("ValidateKeyInDonor", loginContact, otherChurchDonor));
        exception.Should().BeOfType<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<InvalidOperationException>();

        var feeManagement = ReadProductSource(
            "WebServiceConnector",
            "DonationPaymentProcessor",
            "DonationPaymentProcessor.FeeManagement.cs");
        var keyInStart = feeManagement.IndexOf(
            "public async Task<string> SaveKeyInDedication",
            StringComparison.Ordinal);
        var keyInEnd = feeManagement.IndexOf(
            "#endregion",
            keyInStart,
            StringComparison.Ordinal);
        keyInStart.Should().BeGreaterThanOrEqualTo(0);
        keyInEnd.Should().BeGreaterThan(keyInStart);
        var keyInSource = feeManagement.Substring(keyInStart, keyInEnd - keyInStart);
        keyInSource.Should().Contain("m_LoginContact");
        keyInSource.Should().Contain("ValidateKeyInDonor");
        keyInSource.Should().NotContain("ResolveOfferingCategory(contact,");
    }

    /// <summary>
    /// 保護建單入口不信任表單物件或 Session 可能殘留的已解析類別。
    /// 讀取產品建單方法的實際程式區段，斷言必須以本次正式 Guid 重新解析，且不可用 null-coalescing
    /// 延續舊 ResolvedCategory；這個來源檢查只鎖接線，領域驗證由合成 resolver 測試負責。
    /// </summary>
    [Fact]
    public void Write_DirectCreateRevalidatesCategoryInsteadOfTrustingCachedResolution()
    {
        var feeManagement = ReadProductSource(
            "WebServiceConnector",
            "DonationPaymentProcessor",
            "DonationPaymentProcessor.FeeManagement.cs");
        var createStart = feeManagement.IndexOf(
            "public Guid CreateFee(",
            StringComparison.Ordinal);
        var createEnd = feeManagement.IndexOf(
            "public void SetFeeParameter(",
            createStart,
            StringComparison.Ordinal);
        createStart.Should().BeGreaterThanOrEqualTo(0);
        createEnd.Should().BeGreaterThan(createStart);
        var createSource = feeManagement.Substring(createStart, createEnd - createStart);

        createSource.Should().Contain("ResolveOfferingCategory");
        createSource.Should().NotContain("ResolvedCategory ??=");
    }

    /// <summary>
    /// 故障注入第二個類別缺少必填舊選項值，斷言整批解析失敗而不回傳第一筆部分結果；
    /// 呼叫端必須先完成本驗證，再執行任何 CRM Create。
    /// </summary>
    [Fact]
    public void Write_AllCategoriesInvalid_RejectsWholeBatch()
    {
        var valid = Category();
        var invalid = new OfferingCategory(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ChurchA,
            "missing-legacy",
            "無舊值",
            2,
            null,
            null,
            true);
        var list = new OfferingCategoryList(new[] { valid, invalid });

        var action = () => InvokeResolver(
            "ResolveAllForWrite",
            ChurchA,
            list,
            new[] { valid.Id, invalid.Id });

        action.Should().Throw<TargetInvocationException>()
            .Where(exception => exception.InnerException is InvalidOperationException);
    }

    /// <summary>故障注入舊 option 標籤與 lookup 名稱不同，斷言定期奉獻文字以類別資料庫名稱為先。</summary>
    [Fact]
    public void Message_CategoryText_FromCategoryName()
    {
        var booking = new Entity("new_dedication_booking")
        {
            ["new_offeringcategoryid"] = new EntityReference("new_offeringcategory", CategoryId) { Name = "資料庫類別" },
            ["new_dedication_category"] = new OptionSetValue(100000001)
        };
        booking.FormattedValues["new_dedication_category"] = "舊標籤";

        DonationFeeQueryService.ConvertCategory(booking).Should().Be("資料庫類別");

        var line = new DonationLineItemInput
        {
            Category = CategoryId.ToString("D"),
            Amount = 250,
            ResolvedCategory = Category()
        };
        var atmInfo = DonationFeeGroupText.AtmInfo(
            "合成會友",
            new[] { line },
            "00000000000000",
            "2026/11/30");
        atmInfo.LineMessage.Should().Contain("名稱 : 感恩類別");
        atmInfo.LineMessage.Should().NotContain(CategoryId.ToString("D"));
    }

    private static OfferingCategory Category() =>
        new(CategoryId, ChurchA, "legacy-100000001", "感恩類別", 1, null, 100000001, true);

    private static Entity Contact(Guid churchId) =>
        new("contact")
        {
            ["parentcustomerid"] = new EntityReference("account", churchId)
        };

    private static object? InvokeResolver(string methodName, params object[] arguments)
    {
        var resolver = typeof(DonationPaymentFormModel).Assembly.GetType("ChurchReport.Services.DonationOfferingCategoryResolver");
        resolver.Should().NotBeNull("P8-01b 必須有單一 server-side category trust-boundary resolver");
        var method = resolver!.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"resolver 必須提供 {methodName}");
        try
        {
            return method!.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            throw;
        }
    }

    private static string ReadProductSource(params string[] pathParts)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { repositoryRoot, "SpeechMessageProducts.ChurchReport" }.Concat(pathParts).ToArray()));
    }

    private static string[] GetStringTokens(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.StringLiteralToken))
            .Select(token => token.ValueText)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 SpeechMessageProducts.sln，無法讀取本工作樹原始碼。");
    }

    /// <summary>
    /// 記錄類別讀取的唯一 SDK 呼叫並交由測試檢查其 query scope；未配置的 SDK 入口皆失敗，
    /// 避免此離線案例意外演變成外部連線或把未驗證的資料當成空成功。
    /// </summary>
    public class OfferingCategoryScopeSdkProxy : DispatchProxy
    {
        /// <summary>由目前測試檢查的單次 RetrieveMultiple 查詢。</summary>
        public Func<QueryBase, EntityCollection>? RetrieveMultipleHandler { get; set; }

        /// <summary>本 proxy 接收的 RetrieveMultiple 次數，供案例排除隱藏的第二次讀取。</summary>
        public int RetrieveMultipleCalls { get; private set; }

        /// <summary>只實作測試明確配置的同步查詢入口，其餘 CRM API 一律拒絕。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IOrganizationService.RetrieveMultiple)
                || args is not { Length: 1 }
                || args[0] is not QueryBase query
                || RetrieveMultipleHandler is null)
            {
                throw new NotSupportedException("此離線 SDK 替身只允許指定的奉獻類別查詢。");
            }

            RetrieveMultipleCalls++;
            return RetrieveMultipleHandler(query);
        }
    }
}
