/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P6-01 四種 QR 純內容解析接入的行為不變契約；以連結的既有合成
 * 黃金檔及舊工具的原字串運算式當獨立對照，不以新 Codec 自己產生期望值。
 *
 * 主要功能與流程：逐列比較每個解析欄位，額外保護主日 at 格式中的底線屬性；
 * 明列全零 ID、空必要欄、超長、課程／小組 at 注入、空內容及缺欄六類核定差異，
 * 並檢查錯誤固定且不回顯內容。Roslyn 只讀四個工具來源，拒絕自行 Split／Guid 解析回歸。
 *
 * 使用邊界與維護：所有 GUID、姓名與內容都是離線合成資料，不啟動網站、不讀秘密、
 * 不連 D365 或 LINE。來源／黃金文字與語法樹只活在當次案例；檔案讀取立即關閉，
 * 無快取、背景工作或需排空的 owner。LegacyInlineQrParser 只存在測試中，不得供產品呼叫。
 */
using ChurchReport.Tools;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.QrCodes;

/// <summary>以獨立舊解析、固定差異清單與來源語法樹保護四種掃碼接入；不執行真實簽到。</summary>
public sealed class QrScanContentTests
{
    private const string Identity = "11111111-2222-3333-4444-555555555555";

    /// <summary>
    /// 讀同一份舊產生端黃金，每個欄位須與原解析相等；錯用底線切 at 屬性或改動 enroll 分支必失敗。
    /// 黃金由 csproj 連結而非複製，檔案讀取後不保留 handle，不把離線結果當作頁面或 D365 驗收。
    /// </summary>
    [Fact]
    public void QrScan_GoldenContents_NewParserMatchesLegacyInlineParser()
    {
        var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Golden/qr-content-legacy.tsv"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var fields = row.Split('\t');
            Assert.Equal(5, fields.Length);
            Assert.Equal(Parse(fields[0], fields[4], legacy: true), Parse(fields[0], fields[4], legacy: false));
        }
        var atContent = Identity + "@new_attribute_with_underscores@On";
        Assert.Equal(new[] { Identity, "new_attribute_with_underscores", "On", "True" },
            Parse("meeting-at", atContent, legacy: false));
        Assert.Equal(Parse("meeting-at", atContent, legacy: true), Parse("meeting-at", atContent, legacy: false));
    }

    /// <summary>
    /// 逐筆注入 06A 的六類刻意差異；原解析成功／原例外型別使用手寫期望，
    /// 新解析一律固定 FormatException。黃金測試另保護表外正常輸入不可出現差異。
    /// </summary>
    [Fact]
    public void QrScan_DeclaredDifferences_AreExactlyTheListedInputs()
    {
        var longField = new string('x', 2049);
        (string Kind, string? Content, string[]? Output, Type? Error)[] cases =
        [
            ("personal", Guid.Empty.ToString("D"), [Guid.Empty.ToString("D")], null),
            ("lesson", Identity + "_ _On", [Identity, " ", "On"], null),
            ("lesson", Identity + "_1_" + longField, [Identity, "1", longField], null),
            ("group", Identity + "_On@private", [Identity, "On@private"], null),
            ("lesson", null, null, typeof(NullReferenceException)),
            ("lesson", Identity, null, typeof(IndexOutOfRangeException))
        ];
        Assert.Equal(6, cases.Length);
        foreach (var item in cases)
        {
            if (item.Error is null)
                Assert.Equal(item.Output, Parse(item.Kind, item.Content!, legacy: true));
            else
                Assert.IsType(item.Error, Record.Exception(() => Parse(item.Kind, item.Content!, legacy: true)));
            var error = Assert.Throws<FormatException>(() => Parse(item.Kind, item.Content!, legacy: false));
            Assert.Equal("QR content is not in a supported format.", error.Message);
        }
    }

    /// <summary>
    /// 注入空值、缺欄與不合法 GUID，必須在純解析邊界拒絕且不回顯內容；
    /// 本案沒有任何資料相依，因此不能發出課程、聚會或週報的讀寫。
    /// </summary>
    /// <param name="kind">四種產品解析入口。</param>
    /// <param name="content">僅屬當次案例的合成無效內容。</param>
    [Theory]
    [InlineData("lesson", null)]
    [InlineData("lesson", "")]
    [InlineData("lesson", " ")]
    [InlineData("lesson", Identity)]
    [InlineData("meeting-under", Identity + "_only")]
    [InlineData("group", Identity)]
    [InlineData("personal", "private-invalid-guid")]
    [InlineData("lesson", "private-invalid-guid_1_On")]
    public void QrScan_Malformed_ThrowsFormatExceptionWithoutEchoingContent(string kind, string? content)
    {
        var error = Assert.Throws<FormatException>(() => Parse(kind, content!, legacy: false));
        Assert.Equal("QR content is not in a supported format.", error.Message);
        if (!string.IsNullOrWhiteSpace(content)) Assert.DoesNotContain(content, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 從本工作樹四檔完整語法樹檢查委派，拒絕原內容的 Split 及 arr[0] Guid 建構；
    /// 註解不參與判斷。讀取來源立即關閉，語法樹僅由當次測試持有，不跨工作樹或建立產品資源。
    /// </summary>
    [Fact]
    public void QrUtilities_Source_NoInlineQrParsingRemains()
    {
        foreach (var name in new[] { "QrCodeUtility", "SundayQrCodeUtility", "PersonalQrCodeUtility", "SmallGroupQrCodeUtility" })
        {
            var path = Path.Combine(Root(), "SpeechMessageProducts.ChurchReport/Tools", name + ".cs");
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            Assert.DoesNotContain(invocations, call =>
                call.Expression is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == "Split" &&
                member.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "QrCodeIdString");
            Assert.DoesNotContain(syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>(), creation =>
                creation.Type.ToString() is "Guid" or "System.Guid" &&
                creation.ArgumentList?.Arguments.Any(argument => argument.Expression is ElementAccessExpressionSyntax element &&
                    element.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "arr" &&
                    element.ArgumentList.Arguments.ToString() == "0") == true);
            Assert.Contains(invocations, call => call.Expression is MemberAccessExpressionSyntax member &&
                member.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "QrScanContent");
        }
    }

    /// <summary>只將四種入口的輸出轉為可逐欄比較的純值；新舊路徑不共享解析邏輯或期望產生器。</summary>
    private static string[] Parse(string kind, string content, bool legacy)
    {
        switch (kind)
        {
            case "lesson":
            case "enroll":
                Guid oldLesson;
                string oldIndex, oldOnboard;
                if (legacy) LegacyInlineQrParser.ReadLesson(content, out oldLesson, out oldIndex, out oldOnboard);
                else QrScanContent.ReadLesson(content, out oldLesson, out oldIndex, out oldOnboard);
                return [oldLesson.ToString("D"), oldIndex, oldOnboard];
            case "meeting-at":
            case "meeting-under":
                Guid oldMeeting;
                string oldAttribute, oldMeetingOnboard;
                bool oldAt;
                if (legacy) LegacyInlineQrParser.ReadMeeting(content, out oldMeeting, out oldAttribute, out oldMeetingOnboard, out oldAt);
                else QrScanContent.ReadMeeting(content, out oldMeeting, out oldAttribute, out oldMeetingOnboard, out oldAt);
                return [oldMeeting.ToString("D"), oldAttribute, oldMeetingOnboard, oldAt.ToString()];
            case "personal":
                return [(legacy ? LegacyInlineQrParser.ReadPersonal(content) : QrScanContent.ReadPersonal(content)).ToString("D")];
            case "group":
                Guid oldGroup;
                string oldGroupOnboard;
                if (legacy) LegacyInlineQrParser.ReadSmallGroup(content, out oldGroup, out oldGroupOnboard);
                else QrScanContent.ReadSmallGroup(content, out oldGroup, out oldGroupOnboard);
                return [oldGroup.ToString("D"), oldGroupOnboard];
            default:
                throw new InvalidDataException("Unknown synthetic QR kind.");
        }
    }

    /// <summary>只向上找到當前測試所屬方案，不讀父工作樹或任何設定；找不到時明確失敗。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Current solution was not found.");
    }

    /// <summary>
    /// 保留四個舊工具的原切字串、GUID、at 優先與 enroll 判斷運算式，只加 out 輸出供比較；
    /// 不加入新驗證、不攔截原例外、不呼叫新 Codec，也不帶入舊工具的 D365／推播副作用。
    /// </summary>
    private static class LegacyInlineQrParser
    {
        /// <summary>原課程運算式；非 enroll 缺第三欄仍由陣列索引拋出原例外，不預先驗證。</summary>
        internal static void ReadLesson(string QrCodeIdString, out Guid lessonId, out string classIndex, out string onboardType)
        {
            string[] arr = QrCodeIdString.Split('_');
            Guid aGuid = new Guid(arr[0]);
            string m_ClassIndex = arr.Length >= 2 ? arr[1] : string.Empty;
            string m_OnboardType = string.Empty;
            if (!m_ClassIndex.Contains("enroll")) m_OnboardType = arr[2];
            lessonId = aGuid;
            classIndex = m_ClassIndex;
            onboardType = m_OnboardType;
        }

        /// <summary>原主日運算式；只要內容含 at 就以 at 切，保留底線屬性與原欄位順序。</summary>
        internal static void ReadMeeting(string QrCodeIdString, out Guid meetingId, out string attribute, out string onboardType, out bool usesAtSeparator)
        {
            string[] arr;
            if (QrCodeIdString.Contains("@")) arr = QrCodeIdString.Split('@');
            else arr = QrCodeIdString.Split('_');
            Guid aGuid = new Guid(arr[0]);
            meetingId = aGuid;
            attribute = arr[1];
            onboardType = arr[2];
            usesAtSeparator = QrCodeIdString.Contains("@");
        }

        /// <summary>原個人運算式；分隔後只消費第一欄作 GUID，尾欄不是身分。</summary>
        internal static Guid ReadPersonal(string QrCodeIdString)
        {
            string[] arr;
            if (QrCodeIdString.Contains("@")) arr = QrCodeIdString.Split('@');
            else arr = QrCodeIdString.Split('_');
            Guid aGuid = new Guid(arr[0]);
            return aGuid;
        }

        /// <summary>原小組運算式；保留第一欄 GUID 與第二欄簽到代碼，缺欄直接拋原索引例外。</summary>
        internal static void ReadSmallGroup(string QrCodeIdString, out Guid weeklyReportId, out string onboardType)
        {
            string[] arr = QrCodeIdString.Split('_');
            Guid aGuid = new Guid(arr[0]);
            weeklyReportId = aGuid;
            onboardType = arr[1];
        }
    }
}
