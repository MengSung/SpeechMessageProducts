// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/AuthenticationContactReadBootstrapTests.cs
// 用途：以 TDD 鎖定認證聯絡人 typed-read 的 disabled-by-default bootstrap。測試沒有 CE、HTTP、
//       Data8、Session、profile host、credential 或背景工作；只用 in-memory IConfiguration 與
//       不擁有資源的 injected client 驗證 gate 排序。
//
// 隔離與生命週期契約：
// 1. false gate 必須早於 BindOptions、profile 驗證、host／client 解析與任何 I/O 返回 null。
// 2. true gate 仍必須有 deployment-owned ProfileAlias；injected client 不能跳過這個隔離邊界，
//    也不會取得、Dispose 或保存 injected client 的生命週期。
// 3. 這是 local-only composition guard，不接入 AuthenticationController，不能被視為登入切換、CE
//    evidence、traffic enablement、P7.5 或 P8 完成。
// ============================================================================

using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using AuthenticationClient = SpeechMessage.Dynamics.ProductClient.Authentication.IAuthenticationContactReadClient;
using AuthenticationResult = SpeechMessage.Dynamics.ProductClient.Authentication.AuthenticationContactReadResult;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 authentication contact read 的 deployment gate 與 process-host boundary。
/// 所有 configuration 和 fake 皆由單一測試擁有且短生命週期；測試不保存使用者、lookup、claims、password、
/// session、client、provider 或 cancellation state，故不會製造跨使用者或跨測試資料洩漏。
/// </summary>
public sealed class AuthenticationContactReadBootstrapTests
{
    /// <summary>
    /// 保護缺少或 false gate 時 helper 在 profile／host／client／I/O 前回傳 null。故障注入是完全空白的
    /// deployment configuration；決定性斷言是 gate 為 false 且 factory 不需已啟動的 process host，避免
    /// disabled deployment 因 bootstrap 意外配置 connector、handler、pool 或 credential graph。
    /// </summary>
    [Fact]
    public void Authentication_contact_read_is_disabled_by_default_before_profile_host_or_client_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        DonationDynamicsAccessBootstrap.IsAuthenticationContactReadEnabled(configuration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreateAuthenticationContactReadClient(configuration)
            .Should().BeNull();
    }

    /// <summary>
    /// 保護 gate=true 仍先驗證 deployment-owned ProfileAlias，不能由 injected client、browser、Session 或
    /// ProductClient request 補上 routing scope。故障注入是缺少 ProfileAlias 的 true gate；決定性斷言為
    /// fixed InvalidOperationException，且 injected fake 沒有機會被使用、dispose 或轉交另一個 request。
    /// </summary>
    [Fact]
    public void Authentication_contact_read_rejects_blank_profile_before_injected_client_or_host_resolution()
    {
        var configuration = CreateConfiguration(enabled: true, profileAlias: null);
        var injected = new DisabledAuthenticationContactReadClient();

        var create = () => DonationDynamicsAccessBootstrap.TryCreateAuthenticationContactReadClient(
            configuration,
            injected);

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
    }

    /// <summary>
    /// 保護唯有 reviewed gate 與非空 deployment profile 皆成立時，factory 才可回傳主 DI 已擁有的 injected
    /// stateless facade。故障注入以無 transport fake 取代正式 executor；決定性斷言為 reference identity，
    /// 確保 helper 不 per-request new provider、HTTP handler、Data8 pool 或 credential graph。
    /// </summary>
    [Fact]
    public void Authentication_contact_read_accepts_an_injected_client_only_after_gate_and_profile_validation()
    {
        var configuration = CreateConfiguration(enabled: true, profileAlias: "crm91");
        var injected = new DisabledAuthenticationContactReadClient();

        DonationDynamicsAccessBootstrap.IsAuthenticationContactReadEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreateAuthenticationContactReadClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護 checked-in deployment settings 始終將新 capability 保持 false。故障注入是設定漏掉 key 或把它預設
    /// 為 true；決定性斷言同時檢查正式與 development settings，讓 rollback 僅需維持 false，不會讓 legacy
    /// AuthenticationController 在未有 credential policy、session 設計與 CE evidence 前自動改走 typed read。
    /// </summary>
    [Fact]
    public void Authentication_contact_read_checked_in_gates_remain_false()
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            ReadChurchReportSource(fileName).Should().Contain(
                "\"AuthenticationContactReadEnabled\": false");
        }
    }

    /// <summary>
    /// 保護 false gate 的程式排序可由 source 直接審查。故障注入是 helper 在 gate 前 BindOptions、讀取 profile、
    /// 解析 host 或接受 injected facade；決定性斷言以 method-local slice 比較 index，確保 disabled path 沒有
    /// resource allocation、session hydration 或 outbound I/O，也沒有 request-time legacy fallback。
    /// </summary>
    [Fact]
    public void Authentication_contact_read_factory_checks_false_gate_before_profile_host_client_or_io_paths()
    {
        var source = ReadChurchReportSource("Services", "DonationDynamicsAccessBootstrap.cs");
        var method = SliceMethod(
            source,
            "public static IAuthenticationContactReadClient? TryCreateAuthenticationContactReadClient(");
        var gate = method.IndexOf("if (!IsAuthenticationContactReadEnabled(configuration))", StringComparison.Ordinal);
        var nullReturn = method.IndexOf("return null;", StringComparison.Ordinal);

        gate.Should().BeGreaterOrEqualTo(0);
        nullReturn.Should().BeGreaterThan(gate);
        foreach (var laterPath in new[]
                 {
                     "BindOptions(configuration)",
                     "EnsureNonEmptyProductProfile(",
                     "injectedClient is not null",
                     "CreateAuthenticationContactReadExecutor("
                 })
        {
            method.IndexOf(laterPath, StringComparison.Ordinal).Should().BeGreaterThan(
                nullReturn,
                because: $"false gate 必須在 {laterPath} 前停止，不得建立或保留資源");
        }
    }

    /// <summary>
    /// 建立只包含 deployment-owned gate 與 profile 的短生命週期設定。呼叫端無法透過這個 helper 傳入 endpoint、
    /// organization、credential、browser lookup 或 Session 值；ConfigurationRoot 沒有外部 handle，測試結束後
    /// 不會形成跨案例 retained state。
    /// </summary>
    /// <param name="enabled">是否以明確 true 設定 reviewed gate。</param>
    /// <param name="profileAlias">deployment-owned profile alias；null 用於注入缺漏設定故障。</param>
    /// <returns>本測試私有的 in-memory configuration。</returns>
    private static IConfiguration CreateConfiguration(bool enabled, string? profileAlias)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:AuthenticationContactReadEnabled"] = enabled ? "true" : "false",
                ["DynamicsAccess:ProfileAlias"] = profileAlias
            })
            .Build();

    /// <summary>
    /// 以固定 project-relative path 讀取 ChurchReport source。路徑不受 browser、environment 或 test input 控制，
    /// 讀取後 framework 立刻釋放 handle；此 helper 不快取 source，避免平行測試跨 worktree 重用檔案內容。
    /// </summary>
    /// <param name="directoryOrFileName">固定目錄或檔名片段。</param>
    /// <param name="fileName">選用的固定檔名；省略時第一個片段即為根目錄下檔案。</param>
    /// <returns>單一測試呼叫的 source text snapshot。</returns>
    private static string ReadChurchReportSource(string directoryOrFileName, string? fileName = null)
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var path = fileName is null
            ? Path.Combine(projectRoot, directoryOrFileName)
            : Path.Combine(projectRoot, directoryOrFileName, fileName);
        File.Exists(path).Should().BeTrue(because: $"ChurchReport source 必須存在：{path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 擷取單一 bootstrap method 的完整本文，避免同檔其他 legacy factory 字串讓排序契約誤通過。marker 是
    /// compile-time 常數，不是 caller input；找不到或大括號不平衡立即失敗，不會掃描或保存其他 repository 檔案。
    /// </summary>
    /// <param name="source">目前測試持有的短生命週期 source snapshot。</param>
    /// <param name="marker">預定 public factory signature 的固定來源標記。</param>
    /// <returns>只包含該 factory 的 method source。</returns>
    private static string SliceMethod(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterOrEqualTo(0, because: "authentication contact read bootstrap factory 必須存在");
        var bodyStart = source.IndexOf('{', start);
        bodyStart.Should().BeGreaterThan(start);
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException("authentication contact read bootstrap method 的大括號不平衡。");
    }

    /// <summary>
    /// 從目前輸出目錄向上尋找同時具有 solution 與 ChurchReport 專案的 worktree root。此方法不使用 global/static
    /// cache，避免另一個 checkout 或平行 test 的來源混入；找不到即 fail closed。
    /// </summary>
    /// <returns>目前 worktree 的唯一 solution 根路徑。</returns>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.ChurchReport")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到目前 worktree 的 ChurchReport solution root。");
    }

    /// <summary>
    /// 不擁有任何 resource 的 injected typed facade。兩個方法若被意外呼叫即 fail closed；bootstrap 測試只將
    /// 它作為 reference identity sentinel，絕不建立 contact、transport、Session、cache 或 cancellation registration。
    /// </summary>
    private sealed class DisabledAuthenticationContactReadClient : AuthenticationClient
    {
        /// <summary>
        /// 防止 bootstrap test 意外進入 account lookup。任何呼叫都代表 gate/order contract 遭破壞，因此立即
        /// 拋出；方法不捕捉、保存或註冊 cancellation token，也不配置外部資源。
        /// </summary>
        public Task<AuthenticationResult> RetrieveByAccountAsync(
            string profileAlias,
            string workloadSubjectId,
            string accountLookupValue,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled bootstrap fake must not dispatch account lookup.");

        /// <summary>
        /// 防止 bootstrap test 意外進入 LINE lookup。任何呼叫都代表 gate/order contract 遭破壞，因此立即
        /// 拋出；方法不捕捉、保存或註冊 cancellation token，也不配置外部資源。
        /// </summary>
        public Task<AuthenticationResult> RetrieveByLineIdAsync(
            string profileAlias,
            string workloadSubjectId,
            string lineIdLookupValue,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled bootstrap fake must not dispatch LINE lookup.");
    }
}
