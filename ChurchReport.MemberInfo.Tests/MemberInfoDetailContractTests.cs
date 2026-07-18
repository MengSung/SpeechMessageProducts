using ChurchReport.ViewModels;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoDetailContractTests
{
    // ViewModel 用反射驗證真正的 CLR 契約；Controller 與 Razor 則讀取原始碼，提供不需啟動 CRM 的快速整合護欄。
    // 這三個層次必須一起存在，否則欄位可能只加在其中一層，編譯仍成功但彈窗會遺漏或無法顯示資料。
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ControllerText = File.ReadAllText(
        Path.Combine(RepositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "MemberInfoController.cs"));
    private static readonly string ViewText = File.ReadAllText(
        Path.Combine(RepositoryRoot, "SpeechMessageProducts.ChurchReport", "Views", "MemberInfo", "_MemberDetailPopup.cshtml"));

    [Fact]
    public void ViewModel_ExposesGenderAndNullableBirthDate()
    {
        // 直接按公開屬性名稱取型別，鎖定 Controller 與 Razor 共同依賴的資料契約。
        // 生日必須可為 null，才能誠實表達 CRM 未填值；若誤改為 DateTime，預設日期會被當成真實生日送到畫面。
        var genderProperty = typeof(MemberInfoDetailViewModel).GetProperty("Gender");
        var birthDateProperty = typeof(MemberInfoDetailViewModel).GetProperty("BirthDate");

        genderProperty.Should().NotBeNull();
        genderProperty!.PropertyType.Should().Be(typeof(string));
        birthDateProperty.Should().NotBeNull();
        birthDateProperty!.PropertyType.Should().Be(typeof(DateTime?));
    }

    [Fact]
    public void Controller_RetrievesAndMapsGenderAndBirthDate()
    {
        // 查詢欄位、CRM 型別轉換與 ViewModel 指派是一條完整資料鏈：性別要解析 OptionSet 顯示文字，不能把數字代碼直接曝光；
        // birthdate 則要讀成 nullable DateTime，並把 CRM 可能出現的 Year 1 哨兵值正規化成「未設定」。
        // 這些契約可防止只新增畫面欄位卻忘記 ColumnSet／mapping，造成所有使用者永遠看到空狀態或錯誤日期。
        ControllerText.Should().Contain("\"gendercode\"");
        ControllerText.Should().Contain("\"birthdate\"");
        ControllerText.Should().Contain("Gender = GetOptionSetText(contact, \"gendercode\")");
        ControllerText.Should().Contain("var birthDate = contact.GetAttributeValue<DateTime?>(\"birthdate\")");
        ControllerText.Should().Contain("birthDate.Value.Year <= 1");
        ControllerText.Should().Contain("BirthDate = birthDate");
    }

    [Fact]
    public void View_RendersGenderAndFormattedBirthDateWithEmptyStates()
    {
        // 彈窗必須同時提供可辨識標籤、固定 yyyy/MM/dd 日期格式，以及缺值時一致的「未設定」文案。
        // 這避免 nullable 欄位被直接取 Value 而拋錯，也避免性別空字串與生日空白在畫面上看起來像版面壞掉。
        ViewText.Should().Contain("<div class=\"member-info-label\">性別</div>");
        ViewText.Should().Contain("<div class=\"member-info-label\">生日</div>");
        ViewText.Should().Contain("string.IsNullOrWhiteSpace(Model.Gender)");
        ViewText.Should().Contain("Model.BirthDate.Value.ToString(\"yyyy/MM/dd\")");
        ViewText.Should().Contain("（未設定）");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate SpeechMessageProducts.sln from test output directory.");
    }
}
