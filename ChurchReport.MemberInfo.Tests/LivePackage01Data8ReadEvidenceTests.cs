// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage01Data8ReadEvidenceTests.cs
// 用途：以明確 opt-in 的方式取得 P7.1 Package01 Data8 唯讀真機證據。
//
// 此測試預設略過，只有 Lenovo operator 明確提供 test-owned GUID、日期、paid
// period、CRM_PASSWORD 與啟用旗標時才會執行。測試只呼叫六個 read capability，
// 不開啟 ChurchReport consumer flag、不建立任何寫入 request，也不輸出 endpoint、
// OrganizationId、帳號、密碼、token、cookie、CRM payload 或完整例外。
// ============================================================================

using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChurchReport.Services;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using Xunit;
using Xunit.Abstractions;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// P7.1 的 operator-only Data8 read evidence lane。所有外部資源都由同一個測試
/// 方法建立與釋放，避免真機驗證自身留下 pool、WCF service、timer 或 logger factory。
/// </summary>
public sealed class LivePackage01Data8ReadEvidenceTests
{
    private const string ProfileAlias = "sunnyvalechback";
    private readonly ITestOutputHelper _output;

    /// <summary>建立受 xUnit 擁有的安全輸出 sink；不保存 credential 或 CRM 回應。</summary>
    public LivePackage01Data8ReadEvidenceTests(ITestOutputHelper output)
        => _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>
    /// 依六個固定 operation 逐一執行只讀資料查詢。所有 operation 都使用同一個
    /// Embedded + Data8 profile，但每次呼叫仍由 ProductClient 建立短生命週期 request，
    /// 因此不能跨 operation 分享 lease、session、credential 或 response。
    /// </summary>
    [P7Data8LiveFact]
    public async Task Live_package01_data8_reads_emit_sanitized_evidence()
    {
        var statuses = new List<OperationEvidence>(capacity: 6);
        var outcome = "no-go";
        var reason = "runtime-failure";
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;

        try
        {
            var fixture = ReadFixture();
            var configuration = CreateDevelopmentConfiguration();
            var options = new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = ProfileAlias
            };

            CrmConnectionEmbeddedProfileMapper.TryCreate(
                configuration,
                options,
                out var profiles,
                out var catalog,
                out var profileError)
                .Should().BeTrue(profileError);
            profiles.TryGetValue(ProfileAlias, out var profile).Should().BeTrue();
            catalog.TryGetValue(ProfileAlias, out var organization).Should().BeTrue();
            profile.Should().NotBeNull();
            organization.Should().NotBeNull();
            profile!.CeVersion.Should().Be(CeVersion.Ce91);

            CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
                configuration,
                organization!.ServiceUri,
                out var settings,
                out var settingsError)
                .Should().BeTrue(settingsError);
            settings.Should().NotBeNull();

            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            runtime = new EmbeddedData8Runtime(
                profiles,
                catalog,
                ProfileAlias,
                new OnPremiseData8ConnectorClientFactory(settings!),
                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
                loggerFactory);
            var client = new Package01FeeReadClient(
                runtime.Executor,
                loggerFactory.CreateLogger<Package01FeeReadClient>());

            await ExecuteFeeAsync(
                statuses,
                "fee.dedication.retrieve.by.contact",
                () => client.RetrieveDedicationFeesByContactAsync(
                    ProfileAlias,
                    "p7.1-live-evidence",
                    fixture.ContactId));
            await ExecuteFeeAsync(
                statuses,
                "fee.dedication.retrieve.by.contact.date.range",
                () => client.RetrieveDedicationFeesByContactDateRangeAsync(
                    ProfileAlias,
                    "p7.1-live-evidence",
                    fixture.ContactId,
                    fixture.StartDate,
                    fixture.EndDate));
            await ExecuteFeeAsync(
                statuses,
                "fees.retrieve.by.dedication.period",
                () => client.RetrieveFeesByDedicationPeriodAsync(
                    ProfileAlias,
                    "p7.1-live-evidence",
                    fixture.DedicationBookingId,
                    fixture.PaidPeriod));
            await ExecuteStorAsync(
                statuses,
                "fees.editor.load.by.disciplelesson",
                () => client.RetrieveFeeEditorRowsByDiscipleLessonAsync(
                    ProfileAlias,
                    "p7.1-live-evidence",
                    fixture.DiscipleLessonId));
            await ExecuteStorAsync(
                statuses,
                "lessons.stor.retrieve.by.contact",
                () => client.RetrieveStorLessonsByContactAsync(
                    ProfileAlias,
                    "p7.1-live-evidence",
                    fixture.ContactId));
            await ExecuteStorAsync(
                statuses,
                "lessons.stor.retrieve.by.disciplelesson",
                () => client.RetrieveStorLessonsByDiscipleLessonAsync(
                    ProfileAlias,
                    "p7.1-live-evidence",
                    fixture.DiscipleLessonId));

            outcome = statuses.All(status => status.Status == "succeeded") ? "go" : "no-go";
            reason = outcome == "go" ? string.Empty : "one-or-more-operations-failed";
        }
        catch (Exception)
        {
            // 真機 provider 可能包含秘密與完整 endpoint 的原始例外；此 evidence lane 僅保留固定分類。
            outcome = "no-go";
            reason = "runtime-failure";
        }
        finally
        {
            if (runtime is not null)
            {
                try
                {
                    await runtime.DisposeAsync();
                }
                catch (Exception)
                {
                    outcome = "no-go";
                    reason = "cleanup-failure";
                }
            }

            loggerFactory?.Dispose();
        }

        _output.WriteLine(
            "P7_1_EVIDENCE_JSON=" +
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                outcome,
                mode = "Embedded",
                profileAlias = ProfileAlias,
                ceVersion = "9.1",
                reason,
                operations = statuses
            }));

        outcome.Should().Be("go", because: "all six P7.1 Data8 read operations must complete and clean up");
    }

    /// <summary>執行 fee branch 並只保留 operation ID 與結果列數，不保留 CRM 欄位或資料內容。</summary>
    private static async Task ExecuteFeeAsync(
        ICollection<OperationEvidence> statuses,
        string operationId,
        Func<Task<IReadOnlyList<SpeechMessage.Dynamics.ProductClient.Models.FeeRecordDto>>> operation)
    {
        try
        {
            var rows = await operation().ConfigureAwait(false);
            statuses.Add(new OperationEvidence(operationId, "succeeded", rows.Count));
        }
        catch (Exception)
        {
            statuses.Add(new OperationEvidence(operationId, "failed", null));
        }
    }

    /// <summary>執行 stor-lesson branch 並只保留 operation ID 與結果列數。</summary>
    private static async Task ExecuteStorAsync(
        ICollection<OperationEvidence> statuses,
        string operationId,
        Func<Task<IReadOnlyList<SpeechMessage.Dynamics.ProductClient.Models.StorLessonRecordDto>>> operation)
    {
        try
        {
            var rows = await operation().ConfigureAwait(false);
            statuses.Add(new OperationEvidence(operationId, "succeeded", rows.Count));
        }
        catch (Exception)
        {
            statuses.Add(new OperationEvidence(operationId, "failed", null));
        }
    }

    /// <summary>讀取 operator 提供的 test-owned scalar；不接受 endpoint、profile 或 credential 參數。</summary>
    private static LiveFixture ReadFixture()
    {
        return new LiveFixture(
            ReadGuid("P7_1_CONTACT_ID"),
            ReadGuid("P7_1_DEDICATION_BOOKING_ID"),
            ReadGuid("P7_1_DISCIPLE_LESSON_ID"),
            ReadUtc("P7_1_START_DATE"),
            ReadUtc("P7_1_END_DATE"),
            ReadBounded("P7_1_PAID_PERIOD"));
    }

    /// <summary>建立不啟用 reload watcher 的開發設定；credential 仍只由受控組合根讀取。</summary>
    private static IConfiguration CreateDevelopmentConfiguration()
    {
        var root = FindRepositoryRoot();
        return new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "SpeechMessageProducts.ChurchReport"))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
    }

    /// <summary>從測試輸出目錄向上找目前 worktree；不寫入或記錄絕對路徑。</summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.ChurchReport")) &&
                Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Embedded")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("The current worktree root was not found.");
    }

    /// <summary>讀取 GUID 環境變數；空值或格式錯誤只回傳固定錯誤。</summary>
    private static Guid ReadGuid(string name)
        => Guid.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException("P7.1 fixture GUID is invalid.");

    /// <summary>讀取 UTC 日期，禁止依 Lenovo 本機時區重新解釋。</summary>
    private static DateTime ReadUtc(string name)
        => DateTimeOffset.TryParse(
                Environment.GetEnvironmentVariable(name),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value)
            ? value.UtcDateTime
            : throw new InvalidOperationException("P7.1 fixture date is invalid.");

    /// <summary>讀取有界 paid period；不接受空值或過長文字。</summary>
    private static string ReadBounded(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            ? value
            : throw new InvalidOperationException("P7.1 fixture scalar is invalid.");
    }

    private sealed record LiveFixture(
        Guid ContactId,
        Guid DedicationBookingId,
        Guid DiscipleLessonId,
        DateTime StartDate,
        DateTime EndDate,
        string PaidPeriod);

    private sealed record OperationEvidence(string OperationId, string Status, int? RowCount);
}

/// <summary>
/// 真機測試的 opt-in gate。探索階段只檢查必要環境變數是否存在，不讀取或輸出任何秘密值。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P7Data8LiveFactAttribute : FactAttribute
{
    private const string EnableVariable = "SPEECHMESSAGE_P7_1_LIVE";
    private static readonly string[] RequiredVariables =
    [
        EnableVariable,
        "CRM_PASSWORD",
        "P7_1_CONTACT_ID",
        "P7_1_DEDICATION_BOOKING_ID",
        "P7_1_DISCIPLE_LESSON_ID",
        "P7_1_START_DATE",
        "P7_1_END_DATE",
        "P7_1_PAID_PERIOD"
    ];

    /// <summary>未完整設定時明確略過，避免一般 test discovery 誤連 CE。</summary>
    public P7Data8LiveFactAttribute()
    {
        if (!RequiredVariables.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))) ||
            !string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            Skip = "P7.1 live evidence is opt-in and requires sanitized operator fixture variables.";
        }
    }
}
