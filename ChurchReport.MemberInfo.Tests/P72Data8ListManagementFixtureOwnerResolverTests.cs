// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureOwnerResolverTests.cs
// 用途：驗證 P7.2 Slice C 實機 fixture 的 CRM owner 僅能由已驗證的 Data8 WhoAmI 身分推導。
//
// 此測試不建立 Data8 client、不讀取 Credential Manager，也不連線或變更 Dynamics CE。它以短生命期
// executor fake 注入已投影的封閉 OperationExecutionResult，確認 live evidence 只接受 crm91/CE 9.1
// 的 WhoAmI branch，並把唯一的非空 UserId 留在單一測試呼叫範圍。這避免 Windows 帳號、descriptor
// 任意 GUID 或跨測試 mutable state 成為 CRM Assign/transfer 的目標。
//
// 資源生命週期：每個 fake 只持有一個 immutable result 與最後一個 request，沒有 thread、timer、
// subscription、stream、credential、token 或 session。測試結束後由 GC 回收，不可能跨測試或使用者
// 保留可變身分資料。
// ============================================================================

using FluentAssertions;
using System.Runtime.Versioning;
using SpeechMessage.Dynamics.Abstractions.Operations;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 Slice C 的 Data8 WhoAmI owner resolver。
/// 此類別保護「CRM 寫入目標只能等於同一個已驗證 Data8 profile 的 service identity」契約；任何失敗、
/// 非 WhoAmI branch、版本或組織不符、或不完整 GUID 都必須在 bridge dispatch 前回傳空值並 fail closed。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class P72Data8ListManagementFixtureOwnerResolverTests
{
    /// <summary>
    /// 保護契約：完整且與 crm91 profile 組織相符的 WhoAmI response，才能提供 owner assignment 與
    /// transfer 共用的單一 target owner。故障注入使用 recording executor，以確認 request 不帶參數或
    /// idempotency key；決定性斷言是解析出的 UserId 與 executor request 的三個固定 scalar。
    /// </summary>
    [Fact]
    public async Task Resolver_accepts_verified_data8_whoami_and_uses_closed_request_shape()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var businessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var organizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var executor = new RecordingExecutor(CreateWhoAmIResult(
            OperationIds.RuntimeHealthWhoAmI,
            "9.1",
            OperationResponseKind.WhoAmI,
            userId,
            businessUnitId,
            organizationId));

        var resolved = await LivePackage02Data8ListManagementEvidenceTests
            .ResolveFixtureTargetOwnerIdAsync(executor, organizationId);

        resolved.Should().Be(userId);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.ProfileAlias.Should().Be("sunnyvalechback");
        executor.LastRequest.CapabilityOperationId.Should().Be(OperationIds.RuntimeHealthWhoAmI);
        executor.LastRequest.WorkloadSubjectId.Should().Be("p7.2-list-management-fixture-owner");
        executor.LastRequest.Parameters.Should().BeEmpty();
        executor.LastRequest.IdempotencyKey.Should().BeNull();
    }

    /// <summary>
    /// 保護契約：未成功、缺少資料、錯誤 operation/CE version/response kind、空 UserId 或組織不符的
    /// response 都不得推導 CRM owner。故障注入逐一覆蓋這些會混淆 profile、connector 或 identity 的
    /// 分支；決定性斷言是 null，讓 live test 在任何 mutation 前輸出 fixture-precondition-failed。
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidWhoAmIResults))]
    public async Task Resolver_rejects_unverified_or_incomplete_whoami(OperationExecutionResult result)
    {
        var expectedOrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var executor = new RecordingExecutor(result);

        var resolved = await LivePackage02Data8ListManagementEvidenceTests
            .ResolveFixtureTargetOwnerIdAsync(executor, expectedOrganizationId);

        resolved.Should().BeNull();
        executor.LastRequest.Should().NotBeNull();
    }

    /// <summary>
    /// 建立所有必須 fail closed 的封閉 result branch。資料不包含 endpoint、credential、token、cookie
    /// 或真人資料；GUID 僅為測試常數，確保每一條負向分支都能由純記憶體測試重現。
    /// </summary>
    public static IEnumerable<object[]> InvalidWhoAmIResults()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var businessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var organizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        yield return [OperationExecutionResult.Failure("test-failure", "test failure")];
        yield return [OperationExecutionResult.Success(null)];
        yield return [CreateWhoAmIResult("wrong.operation", "9.1", OperationResponseKind.WhoAmI, userId, businessUnitId, organizationId)];
        yield return [CreateWhoAmIResult(OperationIds.RuntimeHealthWhoAmI, "8.2", OperationResponseKind.WhoAmI, userId, businessUnitId, organizationId)];
        yield return [OperationExecutionResult.Success(new OperationResponseData(
            OperationIds.RuntimeHealthWhoAmI,
            "9.1",
            OperationResponseKind.Unsupported))];
        yield return [CreateWhoAmIResult(OperationIds.RuntimeHealthWhoAmI, "9.1", OperationResponseKind.WhoAmI, null, businessUnitId, organizationId)];
        yield return [CreateWhoAmIResult(
            OperationIds.RuntimeHealthWhoAmI,
            "9.1",
            OperationResponseKind.WhoAmI,
            userId,
            businessUnitId,
            Guid.Parse("44444444-4444-4444-4444-444444444444"))];
    }

    /// <summary>
    /// 依需求建立不可變 WhoAmI result，不持有任何 CRM transport 或 session。response kind 不是
    /// WhoAmI 時，刻意建立無 branch 的 Unsupported envelope，讓 resolver 必須檢查 discriminator 而非
    /// 只相信 operation ID 或成功旗標。
    /// </summary>
    private static OperationExecutionResult CreateWhoAmIResult(
        string operationId,
        string ceVersion,
        OperationResponseKind responseKind,
        Guid? userId,
        Guid? businessUnitId,
        Guid? organizationId)
        => responseKind == OperationResponseKind.WhoAmI
            ? OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
                operationId,
                ceVersion,
                new WhoAmIResponseData
                {
                    UserId = userId,
                    BusinessUnitId = businessUnitId,
                    OrganizationId = organizationId
                }))
            : OperationExecutionResult.Success(new OperationResponseData(operationId, ceVersion, responseKind));

    /// <summary>
    /// 記錄 resolver 唯一 outbound executor request 的受控 fake。它不保存 caller cancellation token，
    /// 不建立背景工作，並將 request reference 限制在目前 test instance；用於證明 owner resolver 不會把
    /// descriptor、endpoint 或 credential 混入 Data8 request。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly OperationExecutionResult _result;

        /// <summary>建立回傳固定封閉 result 的 fake，不接受 null result。</summary>
        /// <param name="result">目前測試要注入的 immutable executor 結果。</param>
        public RecordingExecutor(OperationExecutionResult result)
            => _result = result ?? throw new ArgumentNullException(nameof(result));

        /// <summary>本 instance 觀察到的最後一個 request；不會跨 instance 或測試保存。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 立即回傳固定 result，且只記錄目前呼叫的 request 以供斷言。cancel token 不會被保存或註冊，
        /// 因此 fake 沒有需要釋放的 cancellation registration 或非同步資源。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request ?? throw new ArgumentNullException(nameof(request));
            return Task.FromResult(_result);
        }
    }
}
