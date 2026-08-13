// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AuthenticationContactReadClientTests.cs
// 用途：以先行 RED 測試定義 ORG-CALL-00055／00056 的 typed ProductClient API。替身完全在記憶體
//       內運作，不建立 CE、HTTP、Data8 client、connector lease、Session、cache、token 或背景工作。
//
// 隔離與生命週期契約：
// 1. client 每次呼叫必須新建 immutable OperationExecutionRequest 與 DTO 結果；A/B 交錯完成時，
//    profile、workload、lookup value、wire row 與公開 DTO 均不可互相重用或可變。
// 2. 取消 token 必須原樣下傳；替身不註冊 callback，因此不延長 CancellationTokenSource 的生命週期。
// 3. credential／password 不得出現在 wire record、DTO、JSON 或 source；zero／duplicate／secret 異常必須
//    以 fixed result status fail closed，不能改走 legacy SDK、重試或回傳部分 contact 資料。
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using AuthenticationClient = SpeechMessage.Dynamics.ProductClient.Authentication.AuthenticationContactReadClient;
using AuthenticationDto = SpeechMessage.Dynamics.ProductClient.Authentication.AuthenticationContactReadDto;
using AuthenticationResult = SpeechMessage.Dynamics.ProductClient.Authentication.AuthenticationContactReadResult;
using AuthenticationStatus = SpeechMessage.Dynamics.ProductClient.Authentication.AuthenticationContactReadStatus;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 authentication contact read ProductClient 的封閉輸入、固定 operation、取消傳遞、秘密遮罩及 A/B 隔離。
/// 本類別刻意直接參考預定 public API，讓 implementation 開始前的編譯錯誤成為精確 RED 證據；所有 fake 都是
/// 單一測試 private instance，測試結束後不留下 profile、contact、response 或資源 owner。
/// </summary>
public sealed class AuthenticationContactReadClientTests
{
    /// <summary>
    /// 保護 account 路徑只使用 fixed capability，且完整傳遞 server-owned profile、workload、正規化 lookup
    /// 與原 cancellation token。故障注入是純記憶體的單筆 allowlisted wire row；決定性斷言確認結果只發布
    /// contact locator/display/active DTO，不能向 executor 夾帶 password、profile override 或任意 CRM query。
    /// </summary>
    [Fact]
    public async Task Retrieve_by_account_async_forwards_the_exact_closed_request_and_cancellation_token()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = new RecordingExecutor(_ => CreateReadResult(CreateRecord("A")));
        var client = CreateClient(executor);

        var result = await client.RetrieveByAccountAsync(
            " profile-A ",
            " workload-A ",
            " ORG-55 ",
            cancellationSource.Token);

        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationIds.AuthenticationContactRetrieveByAccount);
        executor.LastRequest.ProfileAlias.Should().Be("profile-A");
        executor.LastRequest.WorkloadSubjectId.Should().Be("workload-A");
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["accountLookupValue"] = "ORG-55"
        });
        executor.LastCancellationToken.Should().Be(cancellationSource.Token);
        result.Status.Should().Be(AuthenticationStatus.Found);
        result.Contact.Should().NotBeNull();
        result.Contact!.AccountLocator.Should().Be("account-A");
        result.Contact.DisplayName.Should().Be("display-A");
        result.Contact.IsActive.Should().BeTrue();
    }

    /// <summary>
    /// 保護 LINE 路徑只傳送 lineIdLookupValue，且 zero 與 duplicate 一律發布 fixed、去識別化分類。故障注入
    /// 分別為空集合和兩筆不同 marker；決定性斷言要求結果沒有 Contact，防止實作以第一筆資料、上一個 request
    /// 或 legacy fallback 猜選登入主體並造成 A/B identity 泄漏。
    /// </summary>
    [Fact]
    public async Task Retrieve_by_line_id_async_fails_closed_for_zero_or_duplicate_rows()
    {
        var zeroExecutor = new RecordingExecutor(_ => CreateReadResult(
            OperationIds.AuthenticationContactRetrieveByLineId));
        var duplicateExecutor = new RecordingExecutor(_ => CreateReadResult(
            OperationIds.AuthenticationContactRetrieveByLineId,
            CreateRecord("A"),
            CreateRecord("B")));
        var zeroClient = CreateClient(zeroExecutor);
        var duplicateClient = CreateClient(duplicateExecutor);

        var notFound = await zeroClient.RetrieveByLineIdAsync(
            "profile-A", "workload-A", "ORG-56");
        var ambiguous = await duplicateClient.RetrieveByLineIdAsync(
            "profile-A", "workload-A", "ORG-56");

        zeroExecutor.LastRequest!.CapabilityOperationId.Should().Be(OperationIds.AuthenticationContactRetrieveByLineId);
        zeroExecutor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["lineIdLookupValue"] = "ORG-56"
        });
        notFound.Status.Should().Be(AuthenticationStatus.NotFound);
        notFound.Contact.Should().BeNull();
        ambiguous.Status.Should().Be(AuthenticationStatus.Ambiguous);
        ambiguous.Contact.Should().BeNull();
    }

    /// <summary>
    /// 驗證 authentication contact 的跨層 response envelope 與固定 Data8 查詢使用相同的兩筆上限。
    /// 此測試刻意嘗試在純本機 factory 建立第三筆安全投影；若 envelope 接受它，即使目前 connector
    /// 的 <c>TopCount = 2</c> 正常，也會讓未來 transport 或測試替身把不必要的 contact 資料保留至
    /// ProductClient 邊界。決定性斷言是 factory 必須在沒有 CE、connector、lease、Session 或背景資源
    /// 的情況下立即拒絕第三筆，確保 duplicate 的分類預算與 retained-data 預算一致。
    /// </summary>
    [Fact]
    public void Authentication_response_envelope_rejects_a_third_contact_record()
    {
        Action create = () => OperationResponseData.ForAuthenticationContactReadRecords(
            OperationIds.AuthenticationContactRetrieveByAccount,
            "9.1",
            [CreateRecord("A"), CreateRecord("B"), CreateRecord("C")]);

        create.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 保護 executor 回應的 capability ID 必須在判讀 zero／duplicate 基數之前精確符合目前 public API。故障注入是
    /// account API 收到 LINE capability 的空集合與兩筆集合；決定性斷言是兩種形狀都只能發布
    /// <see cref="AuthenticationStatus.ProfileUnavailable"/> 且沒有 Contact，不能以 NotFound 或 Ambiguous 回顯
    /// 另一 operation 的存在性／資料品質訊號。替身全程只持有本測試的 immutable wire rows，沒有 CE、client、lease、
    /// Session、cache 或 background resource，因此此回歸可直接證明 ProductClient 的 response-boundary fail-closed 順序。
    /// </summary>
    [Fact]
    public async Task Retrieve_by_account_async_rejects_mismatched_operation_before_zero_or_duplicate_classification()
    {
        var zeroExecutor = new RecordingExecutor(_ => CreateReadResult(
            OperationIds.AuthenticationContactRetrieveByLineId));
        var duplicateExecutor = new RecordingExecutor(_ => CreateReadResult(
            OperationIds.AuthenticationContactRetrieveByLineId,
            CreateRecord("A"),
            CreateRecord("B")));
        var zeroClient = CreateClient(zeroExecutor);
        var duplicateClient = CreateClient(duplicateExecutor);

        var zeroResult = await zeroClient.RetrieveByAccountAsync("profile-A", "workload-A", "ORG-55");
        var duplicateResult = await duplicateClient.RetrieveByAccountAsync("profile-A", "workload-A", "ORG-55");

        zeroResult.Status.Should().Be(AuthenticationStatus.ProfileUnavailable);
        zeroResult.Contact.Should().BeNull();
        duplicateResult.Status.Should().Be(AuthenticationStatus.ProfileUnavailable);
        duplicateResult.Contact.Should().BeNull();
    }

    /// <summary>
    /// 保護空白 locator 在 executor、connector lease、host 或 I/O 之前形成固定 invalid-input 結果。故障注入是
    /// 空白 account／LINE value；決定性斷言是兩個 fake 的 call count 保持零，證明 client 不會由前次 request
    /// 補用 lookup、建立 retry 或將 caller input 寫入共用 cache。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_rejects_blank_lookup_before_executor_resolution()
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = CreateClient(executor);

        var account = await client.RetrieveByAccountAsync("profile-A", "workload-A", "   ");
        var lineId = await client.RetrieveByLineIdAsync("profile-A", "workload-A", "");

        account.Status.Should().Be(AuthenticationStatus.InvalidInput);
        account.Contact.Should().BeNull();
        lineId.Status.Should().Be(AuthenticationStatus.InvalidInput);
        lineId.Contact.Should().BeNull();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 wire record、公開 DTO、result 與 JSON 沒有 password／secret／credential 成員。故障注入是 known
    /// legacy secret field 名稱；決定性斷言以 reflection 和實際 serialization 同時檢查，防止日後新增欄位時
    /// 即使 mapper 忘記使用，仍可被 logging、debugger、cache 或另一 caller 取出。
    /// </summary>
    [Fact]
    public void Wire_DTO_and_result_do_not_expose_a_password_or_secret_field()
    {
        var protectedTypes = new[]
        {
            typeof(AuthenticationContactReadRecord),
            typeof(AuthenticationDto),
            typeof(AuthenticationResult)
        };

        foreach (var type in protectedTypes)
        {
            type.GetProperties().Select(property => property.Name).Should().NotContain(name =>
                name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("new_app_pass", StringComparison.OrdinalIgnoreCase));
        }

        JsonSerializer.Serialize(CreateRecord("A")).Should().NotContain("new_app_pass");
    }

    /// <summary>
    /// 保護 interleaved A/B 完成順序不會使 request-local DTO 或 result 被另一呼叫重用。故障注入刻意先完成 B
    /// 再完成 A；決定性斷言將 marker、result 與 Contact instance 全部分開比對，證明 client 沒有 static、
    /// singleton mutable collection、last-result cache、Session 或其他跨 request retained state。
    /// </summary>
    [Fact]
    public async Task Retrieve_async_keeps_interleaved_A_and_B_results_immutable_and_request_local()
    {
        var executor = new InterleavingExecutor();
        var client = CreateClient(executor);

        var aTask = client.RetrieveByAccountAsync("profile-A", "workload-A", "ORG-55");
        var bTask = client.RetrieveByLineIdAsync("profile-B", "workload-B", "ORG-56");

        executor.CompleteLineId(CreateReadResult(
            OperationIds.AuthenticationContactRetrieveByLineId,
            CreateRecord("B")));
        var b = await bTask;
        executor.CompleteAccount(CreateReadResult(
            OperationIds.AuthenticationContactRetrieveByAccount,
            CreateRecord("A")));
        var a = await aTask;

        a.Status.Should().Be(AuthenticationStatus.Found);
        b.Status.Should().Be(AuthenticationStatus.Found);
        a.Contact!.DisplayName.Should().Be("display-A");
        b.Contact!.DisplayName.Should().Be("display-B");
        a.Should().NotBeSameAs(b);
        a.Contact.Should().NotBeSameAs(b.Contact);
        a.Contact.AccountLocator.Should().NotBe(b.Contact.AccountLocator);
    }

    /// <summary>
    /// 建立只持有測試 private executor 的 client。NullLogger 不配置 sink、stream、timer 或背景 worker，故測試
    /// 不取得任何需 Dispose 的 product resource；實際 connector／transport 的 owner 仍是 production executor。
    /// </summary>
    /// <param name="executor">本測試專屬的純記憶體 operation executor。</param>
    /// <returns>未保存任何 request-specific state 的 authentication read client。</returns>
    private static AuthenticationClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<AuthenticationClient>.Instance);

    /// <summary>
    /// 建立唯一合法的 authentication response branch。factory 接受的 records 只包含 allowlisted contact scalar，
    /// 沒有 Entity、FetchXML、profile、endpoint、token、cookie 或 secret，且 envelope 必須自行防禦性複製集合。
    /// </summary>
    /// <param name="records">本次測試私有、可供 factory snapshot 的固定 wire rows。</param>
    /// <returns>符合 account／LINE 共用 read branch 的成功 operation 結果。</returns>
    private static OperationExecutionResult CreateReadResult(
        params AuthenticationContactReadRecord[] records)
        => CreateReadResult(OperationIds.AuthenticationContactRetrieveByAccount, records);

    /// <summary>
    /// 建立指定固定 capability 的唯一合法 authentication response branch。這個 overload 只供 A/B operation
    /// discriminator 測試使用；它不讓 production caller 指定任意 operation，也不增加 profile、secret 或
    /// transport state 的所有權。
    /// </summary>
    /// <param name="operationId">已由本測試固定選定的 account 或 LINE capability ID。</param>
    /// <param name="records">本次測試私有、可供 factory snapshot 的固定 wire rows。</param>
    /// <returns>符合指定固定 operation 的成功結果。</returns>
    private static OperationExecutionResult CreateReadResult(
        string operationId,
        params AuthenticationContactReadRecord[] records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForAuthenticationContactReadRecords(
                operationId,
                "9.1",
                records));

    /// <summary>
    /// 建立包含可辨識 A/B marker 的安全 wire row。每筆資料只在目前測試方法使用，供 ProductClient 建立新 DTO；
    /// ContactId 是成功 read 的定位投影而非授權、profile 或 credential selector，不能被 caller 回送作 routing authority。
    /// </summary>
    /// <param name="marker">區分兩個交錯測試呼叫的不可重複 synthetic marker。</param>
    /// <returns>不含秘密欄位或外部資源的 immutable wire record。</returns>
    private static AuthenticationContactReadRecord CreateRecord(string marker)
        => new()
        {
            ContactId = marker == "A"
                ? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                : Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            AccountLocator = $"account-{marker}",
            DisplayName = $"display-{marker}",
            IsActive = true
        };

    /// <summary>
    /// 記錄單次 dispatch 的 request-local fake executor。它只保留最後 request 與 cancellation token 供同一個
    /// 測試斷言，沒有 static state、connector、network、Session、cache、timer 或 cancellation registration；
    /// case 結束後整個物件及其資料都可被 GC 回收。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 建立本測試專屬的無狀態結果委派。委派同步使用而不捕捉另一 request 的資料，因此不形成跨 A/B 的
        /// completion 或 response ownership。
        /// </summary>
        /// <param name="handler">依收到的 immutable request 回傳固定測試結果的委派。</param>
        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>取得 executor 實際接到的呼叫數，作為 fail-fast 路徑未進入下游的決定性證據。</summary>
        public int CallCount { get; private set; }

        /// <summary>取得同一測試內最後一筆 request；它不是產品 cache，絕不跨 test instance 重用。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>取得原樣收到的取消 token；替身不註冊 callback，故無 cancellation registration 需釋放。</summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// 記錄 request 後回傳純記憶體結果。此方法不建立 I/O、Task.Run、timer、lease 或 stream，使測試只驗證
        /// ProductClient boundary；production executor 的 connector cleanup 仍由其唯一 owner 處理。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_handler(request));
        }
    }

    /// <summary>
    /// 控制 A/B 非同步完成順序的 test-local executor。兩個 TaskCompletionSource 各自對應固定 operation ID，
    /// 使用 RunContinuationsAsynchronously 避免 completion 在呼叫端 lock／stack 上重入；沒有 connector、
    /// shared DTO、cache、timer 或可釋放資源。
    /// </summary>
    private sealed class InterleavingExecutor : IDynamicsOperationExecutor
    {
        private readonly TaskCompletionSource<OperationExecutionResult> _accountCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OperationExecutionResult> _lineIdCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 依固定 capability operation 分流 pending response。未知 operation 立即 fail closed，不借用另一個
        /// completion，避免 fake 掩蓋產品端錯誤 routing；本方法不保存 cancellation token 或註冊 callback。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => request.CapabilityOperationId switch
            {
                OperationIds.AuthenticationContactRetrieveByAccount => _accountCompletion.Task,
                OperationIds.AuthenticationContactRetrieveByLineId => _lineIdCompletion.Task,
                _ => throw new InvalidOperationException("Unexpected authentication contact read operation.")
            };

        /// <summary>完成 account request；呼叫端提供的 result 僅屬於本測試的 A completion。</summary>
        /// <param name="result">不含外部資源的封閉 operation result。</param>
        public void CompleteAccount(OperationExecutionResult result) => _accountCompletion.SetResult(result);

        /// <summary>完成 LINE ID request；呼叫端提供的 result 僅屬於本測試的 B completion。</summary>
        /// <param name="result">不含外部資源的封閉 operation result。</param>
        public void CompleteLineId(OperationExecutionResult result) => _lineIdCompletion.SetResult(result);
    }
}
