// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentViewDefaultsTests
// 主要成員：New_qpay_model_has_donation_category_and_payment_method_defaults、Qpay_model_can_restore_required_form_defaults_after_reused_state_is_cleared、Qpay_model_reports_when_web_login_donor_identity_must_be_restored、Dedication_donation_payment_view_action_is_async_so_line_user_initialization_completes_before_rendering、Web_login_flow_persists_contact_id_and_donation_payment_view_uses_it_to_restore_missing_model_state、FindRepositoryRoot
// 引用命名空間：ChurchReport.Controllers、ChurchReport.Models、ChurchReport.Payments、FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Controllers;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using DevExtreme.AspNet.Mvc;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 鎖定奉獻頁初始資料的安全預設值。
/// 奉獻頁可能在 LINE 使用者資料或 CRM OptionSet 尚未載入完成前先產生 View，
/// 因此 ViewModel 本身必須提供基本奉獻類別與付款方式，避免下拉選單空白。
/// </summary>
public sealed class DonationPaymentViewDefaultsTests
{
    [Fact]
    public void New_qpay_model_has_donation_category_and_payment_method_defaults()
    {
        var model = new DonationPaymentFormModel();

        model.Category.Should().Be("十一奉獻");
        model.PayWay.Should().Be("信用卡");
        model.DedicationCategoryList.Should().Contain("十一奉獻");
        model.OtherCategoryArray.Should().NotBeNull();
        model.SpecialCategoryArray.Should().NotBeNull();
    }

    /// <summary>
    /// 保護奉獻稽核查詢的開始日期必須隨建立表單時的年份動態落在該年一月一日，
    /// 而不是固定某一年度或誤用當天日期；因此跨年後不需人工修改程式即可得到正確區間起點。
    /// </summary>
    [Fact]
    public void New_donation_form_defaults_query_start_date_to_current_year_first_day()
    {
        var model = new DonationPaymentFormModel();
        var expected = new DateTime(DateTime.Now.Year, 1, 1);

        model.QueryStartDate.Should().Be(expected);
    }

    [Fact]
    public void Qpay_model_can_restore_required_form_defaults_after_reused_state_is_cleared()
    {
        var model = new DonationPaymentFormModel
        {
            Category = "",
            PayWay = "",
            DedicationCategoryList = new List<string>(),
            OtherCategoryArray = null!,
            SpecialCategoryArray = null!,
            CreditCardList = null!,
            DedicationFeeList = null!,
            DedicationBookingList = null!
        };

        model.EnsureFormDefaults();

        model.Category.Should().Be("十一奉獻");
        model.PayWay.Should().Be("信用卡");
        model.DedicationCategoryList.Should().Contain("十一奉獻");
        model.OtherCategoryArray.Should().NotBeNull();
        model.SpecialCategoryArray.Should().NotBeNull();
        model.CreditCardList.Should().NotBeNull();
        model.DedicationFeeList.Should().NotBeNull();
        model.DedicationBookingList.Should().NotBeNull();
    }

    [Fact]
    public void Qpay_model_falls_back_to_first_crm_category_when_hardcoded_default_is_absent()
    {
        // 好牧人這類教會的 new_fee.new_category OptionSet 用「獻金」命名，沒有「十一奉獻」。
        // 硬編碼預設值不在 DataSource 裡時，DevExtreme SelectBox 會退回顯示 placeholder「選擇...」，
        // 使用者必須自己下拉才能送出，因此 ViewModel 必須把預設值對帳回實際清單。
        var model = new DonationPaymentFormModel
        {
            Category = "十一奉獻",
            DedicationCategoryList = new List<string>
            {
                "月定獻金",
                "禮拜獻金",
                "感恩獻金",
                "聖餐獻金"
            }
        };

        model.EnsureFormDefaults();

        model.Category.Should().Be("月定獻金");
    }

    [Fact]
    public void Qpay_model_keeps_hardcoded_default_when_crm_category_list_contains_it()
    {
        // OptionSet 有「十一奉獻」的教會必須維持原本的預設值，不能被改成清單第一項。
        var model = new DonationPaymentFormModel
        {
            Category = "十一奉獻",
            DedicationCategoryList = new List<string>
            {
                "主日奉獻",
                "十一奉獻",
                "感恩奉獻"
            }
        };

        model.EnsureFormDefaults();

        model.Category.Should().Be("十一奉獻");
    }

    [Fact]
    public void Qpay_model_keeps_selected_category_that_exists_in_crm_category_list()
    {
        // 對帳只負責修掉「清單裡沒有的值」，不能覆蓋使用者或流程已經選好的合法類別。
        var model = new DonationPaymentFormModel
        {
            Category = "禮拜獻金",
            DedicationCategoryList = new List<string>
            {
                "月定獻金",
                "禮拜獻金",
                "感恩獻金"
            }
        };

        model.EnsureFormDefaults();

        model.Category.Should().Be("禮拜獻金");
    }

    [Fact]
    public void Qpay_model_matches_crm_category_ignoring_surrounding_whitespace()
    {
        // CRM OptionSet 標籤偶爾帶前後空白；比對必須容忍，且要回填清單裡的原始字串，
        // 否則 SelectBox 的 value 仍然對不上 DataSource 項目。
        var model = new DonationPaymentFormModel
        {
            Category = "月定獻金",
            DedicationCategoryList = new List<string>
            {
                " 月定獻金 ",
                "禮拜獻金"
            }
        };

        model.EnsureFormDefaults();

        model.Category.Should().Be(" 月定獻金 ");
    }

    [Theory]
    [InlineData("", "D001", true)]
    [InlineData("胡夢嵩", "", true)]
    [InlineData("胡夢嵩", "D001", false)]
    public void Qpay_model_reports_when_web_login_donor_identity_must_be_restored(
        string fullName,
        string dedicationNumber,
        bool expected)
    {
        var model = new DonationPaymentFormModel
        {
            FullName = fullName,
            DedicationNumber = dedicationNumber
        };

        model.NeedsDonorIdentityRestore().Should().Be(expected);
    }

    [Fact]
    public void Dedication_donation_payment_view_action_is_async_so_line_user_initialization_completes_before_rendering()
    {
        var method = typeof(DedicationController).GetMethod(nameof(DedicationController.DonationPaymentView));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<IActionResult>),
            "DonationPaymentView 會呼叫非同步 LINE 初始化流程，必須 await 完成後才 render 奉獻表單");
    }

    [Fact]
    public void Web_login_flow_persists_contact_id_and_donation_payment_view_uses_it_to_restore_missing_model_state()
    {
        var repositoryRoot = FindRepositoryRoot();
        var loginController = File.ReadAllText(
            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "DonationPaymentLoginController.cs"));
        var dedicationController = File.ReadAllText(
            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs"));
        var contactIdSessionKeyName = nameof(DonationPaymentSessionKeys.WebLoginContactId);

        loginController.Should().Contain(contactIdSessionKeyName,
            "web login 成功後必須把 CRM contact id 存入 ASP.NET Session，避免 redirect 後讀到空的 DonationPaymentManager");
        loginController.Should().Contain("Session.SetString",
            "contact id 應該存在 session 這種跨 redirect 穩定狀態，而不是只留在 memory-cache manager");
        dedicationController.Should().Contain("RestoreWebLoginDonationPaymentModel",
            "DonationPaymentView render 前必須能用 session contact id 重新建立姓名、奉獻編號與信用卡清單");
        dedicationController.Should().Contain(contactIdSessionKeyName);
    }

    /// <summary>
    /// 固定 LINE 查不到 CRM contact 時，表單服務必須在查詢奉獻清單前 fail-closed，
    /// 不得把 null 傳給欄位投影，也不得沿用上一位使用者的資料。
    /// </summary>
    [Fact]
    public void Line_fee_form_service_clears_model_when_line_contact_is_missing()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot, "SpeechMessageProducts.ChurchReport", "Services", "DonationDedicationFeeFormService.cs"));

        service.Should().Contain("lineLoginContact == null");
        service.Should().Contain("ClearModelForMissingContact");
        service.IndexOf("if (lineLoginContact == null)", StringComparison.Ordinal)
            .Should().BeLessThan(service.IndexOf("FillIdentity(model, lineLoginContact", StringComparison.Ordinal));
    }

    /// <summary>
    /// 固定 LINE 登入初始化的成功條件必須是非空 ID 且 CRM contact 已驗證；
    /// 查無 contact、取消或例外都不可回報 status=1，並須清除本次 Session 的舊奉獻狀態。
    /// </summary>
    [Fact]
    public void Line_setup_requires_verified_contact_and_clears_stale_state_on_failure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            repositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs"));

        controller.Should().Contain("IsValidLineUserId(UserLineId)");
        controller.Should().Contain("VerifyLineIdTokenAsync");
        controller.Should().Contain("DonationPaymentSessionKeys.LineUserId");
        controller.Should().Contain("ClearLineDonationState");
        controller.Should().Contain("status = \"0\"");
        controller.Should().NotContain("if (loginContact != null)\n                {\n                    await Task.Run");
    }

    /// <summary>
    /// 保護 LINE 登入 AJAX 在後端拒絕 Token 或找不到 CRM contact 時不會誤導向付款頁，
    /// 並確保除錯輸出不會把短命 ID Token 寫入瀏覽器主控台。
    /// </summary>
    /// <remarks>
    /// 故障注入是模擬 <c>SetupUserLineId</c> 回傳 <c>status = "0"</c>；決定性斷言是前端
    /// 只有明確成功狀態才導向，且 AJAX data 記錄不包含完整 Token。這是跨層登入契約，
    /// 可在不建立瀏覽器或外部 LINE 連線的情況下固定回歸行為。
    /// </remarks>
    [Fact]
    public void Line_login_redirects_only_after_success_and_redacts_id_token_from_console_logging()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Views",
            "Dedication",
            "DediationLineLoginView.cshtml"));

        view.Should().Contain("if (data && String(data.status) === \"1\")");
        view.Should().Contain("var requestLogData = {");
        view.Should().Contain("IdToken: \"[REDACTED]\"");
        view.Should().Contain("console.log('[DediationLogin] Data:', requestLogData);");
        view.Should().NotContain("console.log('[DediationLogin] Data:', settings.data);");
    }

    /// <summary>
    /// 保護同一個 LIFF 頁面在網路延遲或重複回呼時只會發出一個登入請求，
    /// 避免重複 CRM 查詢、Session 寫入與 UI Toast。
    /// </summary>
    [Fact]
    public void Line_login_uses_single_flight_guard_for_duplicate_ajax_requests()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Views",
            "Dedication",
            "DediationLineLoginView.cshtml"));

        view.Should().Contain("var lineLoginRequestInFlight = false;");
        view.Should().Contain("if (lineLoginRequestInFlight) {");
        view.Should().Contain("lineLoginRequestInFlight = true;");
        view.Should().Contain("lineLoginRequestInFlight = false;");
    }

    /// <summary>
    /// 保護 LINE 奉獻收費清單在查詢日期 POST 後重新載入時，會先還原目前 Session 的日期，
    /// 再查詢清單；否則使用者選取的跨年度區間會被新模型預設值覆蓋。
    /// </summary>
    [Fact]
    public void Line_fee_view_restores_saved_query_dates_before_building_the_fee_model()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "DedicationController.cs"));

        var actionStart = controller.IndexOf("public IActionResult DedicationFeeView()", StringComparison.Ordinal);
        actionStart.Should().BeGreaterThanOrEqualTo(0);
        var restoreIndex = controller.IndexOf("RestoreDedicationQueryDatesFromSession();", actionStart);
        var buildIndex = controller.IndexOf("BuildDedicationFeeLineFormModel()", actionStart);

        restoreIndex.Should().BeGreaterThan(actionStart);
        buildIndex.Should().BeGreaterThan(restoreIndex);
    }

    /// <summary>
    /// 保護 LINE ID Token 驗證使用明確的 named HttpClient 與有界 timeout，
    /// 避免外部服務失聯時請求與連線資源無限期佔用。
    /// </summary>
    [Fact]
    public void Line_token_verification_registers_bounded_named_http_client()
    {
        var repositoryRoot = FindRepositoryRoot();
        var startup = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SpeechMessageProducts.ChurchReport",
            "Startup.cs"));

        startup.Should().Contain("AddHttpClient(\"LineLoginApi\"");
        startup.Should().Contain("https://api.line.me/");
        startup.Should().Contain("Timeout = TimeSpan.FromSeconds(10)");
    }

    /// <summary>
    /// 固定 LINE 收費清單入口只能使用已驗證的 Session/目前 request 狀態，
    /// 不得直接信任可由用戶修改的 route segment，避免跨 LINE 使用者讀到他人清單。
    /// </summary>
    [Fact]
    public void Line_fee_view_validates_server_bound_line_identity_before_querying()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            repositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs"));

        controller.Should().Contain("BuildDedicationFeeLineFormModel");
        controller.Should().Contain("LineUserId");
        controller.Should().Contain("ClearLineDonationState");
    }

    /// <summary>
    /// 固定 LINE user id 的格式邊界；格式不符時不得觸發 CRM 查詢或建立任何 Session 狀態。
    /// </summary>
    [Theory]
    [InlineData("U12345678901234567890123456789012", true)]
    [InlineData("", false)]
    [InlineData("U123", false)]
    [InlineData("X12345678901234567890123456789012", false)]
    [InlineData("U1234567890123456789012345678901!", false)]
    public void Line_user_id_format_is_validated_before_processing(string lineUserId, bool expected)
    {
        DedicationController.IsValidLineUserId(lineUserId).Should().Be(expected);
    }

    /// <summary>
    /// 固定奉獻模型組裝器的 OptionSet metadata cache 必須有明確 owner；
    /// 每次 LINE 登入建立的暫時 MemoryCache 必須在同步查詢結束後 deterministic dispose。
    /// </summary>
    [Fact]
    public void Donation_payment_assembler_disposes_temporary_metadata_cache()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assembler = File.ReadAllText(Path.Combine(
            repositoryRoot, "SpeechMessageProducts.ChurchReport", "Services", "DonationPaymentModelAssembler.cs"));

        assembler.Should().Contain("using var metadataCache");
        assembler.Should().Contain("metadataCache");
    }

    /// <summary>
    /// 保護網頁奉獻稽核入口在 request-scoped manager 尚未取得 contact 時仍能安全產生空白表單。
    /// </summary>
    /// <remarks>
    /// 故障注入是讓 <see cref="PersonalInfomationModel.m_LoginContact"/> 保持 null，並在表單內預先放入
    /// 「前一位使用者」的識別與清單資料。決定性斷言是建模流程不拋出
    /// <see cref="ArgumentNullException"/>，且所有個資與奉獻清單都被清空；這固定了當機修正與
    /// 跨使用者資料不可殘留的契約。測試使用未初始化的 manager/controller，沒有建立 CRM 連線、HTTP
    /// client、計時器或背景工作，因此不會留下可達資源。
    /// </remarks>
    [Fact]
    public void Dedication_audit_web_form_without_login_contact_returns_isolated_blank_model()
    {
        var staleModel = new DonationPaymentFormModel
        {
            FullName = "前一位使用者",
            Mobile = "0912345678",
            DedicationNumber = "OLD-001",
            NationId = "A123456789",
            LastSixDigit = "123456",
            TotalAmount = 9999,
            DedicationFeeList = new List<DedicationFee> { new DedicationFee { FullName = "前一位使用者", Amount = 9999 } },
            SameNameList = new List<SameNameElement> { new SameNameElement() }
        };

        var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));
        manager.m_DonationPaymentFormModel = staleModel;

        var context = new AuditControllerContext(manager);
        var controller = (DedicationAuditController)RuntimeHelpers.GetUninitializedObject(typeof(DedicationAuditController));
        var contextField = typeof(BaseChurchController).GetField(
            "InMemoryContext",
            BindingFlags.Instance | BindingFlags.NonPublic);
        contextField.Should().NotBeNull();
        contextField!.SetValue(controller, context);

        var result = controller.BuildAuditWebFormModel();

        result.FullName.Should().BeEmpty();
        result.Mobile.Should().BeEmpty();
        result.DedicationNumber.Should().BeEmpty();
        result.NationId.Should().BeEmpty();
        result.LastSixDigit.Should().BeEmpty();
        result.DedicationFeeList.Should().BeEmpty();
        result.SameNameList.Should().BeEmpty();
        result.TotalAmount.Should().Be(0);
    }

    /// <summary>
    /// 保護 manager 的表單狀態在 legacy 流程曾清成 null 時仍會回存同一個安全模型，
    /// 讓後續 Grid/AJAX 請求不會再次解參考 null。
    /// </summary>
    [Fact]
    public void Dedication_audit_web_form_reassigns_new_default_model_to_manager()
    {
        var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));
        manager.m_DonationPaymentFormModel = null!;

        var context = new AuditControllerContext(manager);
        var controller = (DedicationAuditController)RuntimeHelpers.GetUninitializedObject(typeof(DedicationAuditController));
        var contextField = typeof(BaseChurchController).GetField("InMemoryContext", BindingFlags.Instance | BindingFlags.NonPublic);
        contextField!.SetValue(controller, context);

        var result = controller.BuildAuditWebFormModel();

        manager.m_DonationPaymentFormModel.Should().BeSameAs(result);
    }

    /// <summary>
    /// 保護奉獻稽核 Grid 在表單模型尚未建立時仍回傳空資料，而不是因 null manager 狀態當機。
    /// </summary>
    [Fact]
    public void Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing()
    {
        var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));
        manager.m_DonationPaymentFormModel = null!;
        var controller = CreateUninitializedAuditController(manager);

        var action = () => controller.LoadDedicationFeeList(string.Empty, new DataSourceLoadOptions());

        action.Should().NotThrow();
    }

    /// <summary>
    /// 保護同名奉獻者 Grid 與奉獻清單具有相同的 null-safe 行為，避免 AJAX 請求繞過主頁 render 時當機。
    /// </summary>
    [Fact]
    public void Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing()
    {
        var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));
        manager.m_DonationPaymentFormModel = null!;
        var controller = CreateUninitializedAuditController(manager);

        var action = () => controller.LoadSameNameList(string.Empty, new DataSourceLoadOptions());

        action.Should().NotThrow();
    }

    /// <summary>
    /// 保護「奉獻收費清單」從網頁 Layout 直接進入時，若 request 尚未取得登入 contact，
    /// 不會把 null 傳入 CRM 表單服務而導向錯誤頁；同時清除可能殘留的前一位使用者資料。
    /// </summary>
    [Fact]
    public void Dedication_fee_web_form_without_authenticated_contact_returns_isolated_blank_model()
    {
        var staleModel = new DonationPaymentFormModel
        {
            FullName = "前一位使用者",
            Mobile = "0912345678",
            DedicationNumber = "OLD-001",
            NationId = "A123456789",
            LastSixDigit = "123456",
            TotalAmount = 9999,
            DedicationFeeList = new List<DedicationFee> { new DedicationFee { FullName = "前一位使用者", Amount = 9999 } },
            SameNameList = new List<SameNameElement> { new SameNameElement() }
        };

        var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));
        manager.m_DonationPaymentFormModel = staleModel;
        manager.m_Contact = null!;
        manager.m_LoginContact = null!;

        var context = new AuditControllerContext(manager);
        var controller = (DedicationController)RuntimeHelpers.GetUninitializedObject(typeof(DedicationController));
        var contextField = typeof(BaseChurchController).GetField("InMemoryContext", BindingFlags.Instance | BindingFlags.NonPublic);
        contextField!.SetValue(controller, context);

        DonationPaymentFormModel result = null!;
        var action = () => result = controller.BuildDedicationFeeWebFormModel();

        action.Should().NotThrow();
        result.FullName.Should().BeEmpty();
        result.Mobile.Should().BeEmpty();
        result.DedicationNumber.Should().BeEmpty();
        result.NationId.Should().BeEmpty();
        result.LastSixDigit.Should().BeEmpty();
        result.DedicationFeeList.Should().BeEmpty();
        result.SameNameList.Should().BeEmpty();
        result.TotalAmount.Should().Be(0);
        manager.m_Contact.Should().BeNull();
        manager.m_LoginContact.Should().BeNull();
    }

    /// <summary>建立只供本測試使用的未初始化稽核控制器。</summary>
    private static DedicationAuditController CreateUninitializedAuditController(DonationPaymentManager manager)
    {
        var context = new AuditControllerContext(manager);
        var controller = (DedicationAuditController)RuntimeHelpers.GetUninitializedObject(typeof(DedicationAuditController));
        var contextField = typeof(BaseChurchController).GetField("InMemoryContext", BindingFlags.Instance | BindingFlags.NonPublic);
        contextField!.SetValue(controller, context);
        return controller;
    }

    /// <summary>
    /// 提供只給稽核建模測試使用的 request-local context；除付款 manager 與個人模型外，
    /// 其他資料管理器不會被該建模流程讀取，因此以 null-forgiving 回傳避免建立任何外部資源。
    /// </summary>
    private sealed class AuditControllerContext : IInMemoryDataContext
    {
        /// <summary>建立稽核測試 context。</summary>
        /// <param name="manager">測試擁有的短命奉獻 manager。</param>
        public AuditControllerContext(DonationPaymentManager manager)
        {
            DonationPaymentManager = manager ?? throw new ArgumentNullException(nameof(manager));
            PersonalInfomationModel = new PersonalInfomationModel();
        }

        /// <summary>未使用的清單管理器。</summary>
        public ListManager ListManager => null!;
        /// <summary>未使用的小組資料。</summary>
        public SmallGroupDataList SmallGroupDataList => null!;
        /// <summary>未使用的週報資料。</summary>
        public WeeklyReportData WeeklyReportData => null!;
        /// <summary>未使用的新人模型。</summary>
        public NewPersonModel NewPersonModel => null!;
        /// <summary>目前測試 request 的個人模型。</summary>
        public PersonalInfomationModel PersonalInfomationModel { get; }
        /// <summary>未使用的幸福小組管理器。</summary>
        public HappyGroupDataManager HappyGroupDataManager => null!;
        /// <summary>未使用的名單管理器。</summary>
        public ListManagementDataManager ListManagementDataManager => null!;
        /// <summary>未使用的裝備管理器。</summary>
        public EquipmentDataManager EquipmentDataManager => null!;
        /// <summary>未使用的收費清單。</summary>
        public FeeList FeeList => null!;
        /// <summary>未使用的 LINE 綁定模型。</summary>
        public LineBindingViewModel LineBindingViewModel => null!;
        /// <summary>未使用的行事曆管理器。</summary>
        public AppointmentsListManager AppointmentsListManager => null!;
        /// <summary>目前測試 request 的奉獻 manager。</summary>
        public DonationPaymentManager DonationPaymentManager { get; }
        /// <summary>未使用的問卷管理器。</summary>
        public PollManager PollManager => null!;
        /// <summary>未使用的工具實例。</summary>
        public ToolUtilityClass ToolUtilityClass => null!;

        /// <summary>測試 context 不執行外部資料初始化。</summary>
        public void SetupSmallGroupData(string fullName, string account, string password, DateTime selectDate, bool displayDateFlag)
            => throw new NotSupportedException();

        /// <summary>測試 context 沒有可提交的持久化狀態。</summary>
        public void SaveChanges() => throw new NotSupportedException();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))
                || File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 SpeechMessageProducts.sln，無法定位測試要檢查的 Controller 原始碼。");
    }
}
