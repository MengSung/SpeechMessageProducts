// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureReconcilerTests.cs
// 用途：驗證 P7.2 Slice C 的純唯讀 reconciliation classifier 對已讀取 snapshot 與
//       記憶體中的 WhoAmI owner identity，只輸出固定、去識別化且永久 no-go 的分類。
//
// 安全與生命週期：
// 1. 本測試只建立合成 GUID 與 SDK-free snapshot；不建立連線、工作階段、快取、計時器、背景工作或
//    可釋放資源，因此不存在跨測試、跨使用者或跨租戶的 state retention。
// 2. classifier 只比較傳入的 immutable-like 純值，不能重建遺失的歷史 baseline，也不能授權重試、
//    cleanup 或任何外部 mutation；所有結果都是固定分類，不能帶入 identity 或原始資料。
// 3. 每個測試的 decisive assertions 同時保護固定 no-go 結果、read-only probe 標記與不可重試契約；
//    若未來實作將觀察結果誤判為足以執行，這些測試必須立即失敗。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 保護 Slice C 歷史 baseline 不存在時的純值分類契約。這些案例故意不模擬任何外部服務；它們證明
/// classifier 即使取得完整或不預期的目前 snapshot，也只能產生 sanitized no-go，不能把目前狀態誤當成
/// 可用於 write、rollback、cleanup 或自動 retry 的歷史證據。
/// </summary>
public sealed class P72Data8ListManagementFixtureReconcilerTests
{
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetListId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ObservedOwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WhoAmITargetOwnerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DifferentLeaderId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TargetLeaderId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset SundayStart = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 保護可讀取到各能力「看似合理」目前形狀時的 fail-closed 分類。故障模型是歷史 baseline 從未
    /// 被可靠保存；決定性 assertion 是即使 add/removal、small-group、owner 與 transfer 都符合各自
    /// 的預期前置形狀，結果仍固定為 no-go、baseline-unprovable、已執行 read-only probe 且不可重試。
    /// </summary>
    [Fact]
    public void Classify_normal_observed_shape_keeps_the_missing_historical_baseline_as_no_go()
    {
        var result = P72Data8ListManagementFixtureReconciler.Classify(
            addMembership: new P72MembershipSnapshot([]),
            removeMembership: new P72MembershipSnapshot([ContactId]),
            smallGroup: CreateSmallGroup(DifferentLeaderId),
            smallGroupExpected: CreateSmallGroup(TargetLeaderId),
            contactOwnerId: ObservedOwnerId,
            transferFixture: CreateTransferFixture(WhoAmITargetOwnerId),
            transfer: new P72TransferGraphSnapshot(
                SourceMembershipPresent: true,
                TargetMembershipPresent: false,
                PresentRecordId: null,
                PresentRecordMatches: false,
                PrimaryListId: SourceListId,
                OwnerId: ObservedOwnerId),
            whoAmITargetOwnerId: WhoAmITargetOwnerId);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("baseline-unprovable");
        result.ReadOnlyProbeExecuted.Should().BeTrue();
        result.SafeToRetry.Should().BeFalse();
        result.OwnerBinding.Should().Be("matches-service-identity");
        result.AddMembership.Should().Be("baseline-absent");
        result.RemoveMembership.Should().Be("baseline-present");
        result.SmallGroup.Should().Be("not-expected-baseline-unproven");
        result.ContactOwner.Should().Be("non-target-baseline-unproven");
        result.Transfer.Should().Be("baseline-shape-unproven");
    }

    /// <summary>
    /// 驗證已通過 Data8 WhoAmI 的非空 service-owner 識別值，會在任何 fixture store 讀取之前立即被投影為
    /// 去識別化的 <c>matches-service-identity</c> 分類。故障注入刻意不建立 store、連線或 CRM session；
    /// decisive assertion 是成功的 WhoAmI 不能因為後續 Retrieve/RetrieveMultiple 失敗而被覆寫成
    /// <c>unavailable</c>，讓 parent handoff 能分辨認證／WhoAmI 邊界與 fixture-read 邊界，同時不保留 GUID。
    /// </summary>
    [Fact]
    public void Classify_verified_owner_binding_before_fixture_store_reads()
    {
        var ownerBinding = P72Data8ListManagementFixtureReconciler
            .ClassifyVerifiedOwnerBinding(WhoAmITargetOwnerId);

        ownerBinding.Should().Be("matches-service-identity");
    }

    /// <summary>
    /// 注入每個已讀取 projection 的不預期但仍可比較形狀：add 已存在、remove 缺席、small-group 已
    /// 等於 expected、owner 已是 WhoAmI target，並且 transfer 已有 target membership。決定性
    /// assertion 是 classifier 只回傳對應固定分類，仍不將任何一種現況升格為可安全重試或 cleanup。
    /// </summary>
    [Fact]
    public void Classify_unexpected_observed_shape_remains_sanitized_and_no_go()
    {
        var result = P72Data8ListManagementFixtureReconciler.Classify(
            addMembership: new P72MembershipSnapshot([ContactId]),
            removeMembership: new P72MembershipSnapshot([]),
            smallGroup: CreateSmallGroup(TargetLeaderId),
            smallGroupExpected: CreateSmallGroup(TargetLeaderId),
            contactOwnerId: WhoAmITargetOwnerId,
            transferFixture: CreateTransferFixture(WhoAmITargetOwnerId),
            transfer: new P72TransferGraphSnapshot(
                SourceMembershipPresent: true,
                TargetMembershipPresent: true,
                PresentRecordId: null,
                PresentRecordMatches: false,
                PrimaryListId: TargetListId,
                OwnerId: WhoAmITargetOwnerId),
            whoAmITargetOwnerId: WhoAmITargetOwnerId);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("baseline-unprovable");
        result.ReadOnlyProbeExecuted.Should().BeTrue();
        result.SafeToRetry.Should().BeFalse();
        result.OwnerBinding.Should().Be("matches-service-identity");
        result.AddMembership.Should().Be("unexpected-present");
        result.RemoveMembership.Should().Be("unexpected-absent");
        result.SmallGroup.Should().Be("expected-baseline-unproven");
        result.ContactOwner.Should().Be("target-baseline-unproven");
        result.Transfer.Should().Be("unexpected-shape-unproven");
    }

    /// <summary>
    /// 注入缺失 snapshot 與空 WhoAmI identity，模擬唯讀 probe 無法安全投影任一能力的情況。決定性
    /// assertion 是每個 closed category 都回傳 unavailable，且不因資料不足而拋出、保留可變 state
    /// 或把未知狀態轉換成 retry／cleanup 授權。
    /// </summary>
    [Fact]
    public void Classify_unavailable_observations_returns_only_unavailable_categories()
    {
        var result = P72Data8ListManagementFixtureReconciler.Classify(
            addMembership: null,
            removeMembership: null,
            smallGroup: null,
            smallGroupExpected: null,
            contactOwnerId: null,
            transferFixture: null,
            transfer: null,
            whoAmITargetOwnerId: null);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("baseline-unprovable");
        result.ReadOnlyProbeExecuted.Should().BeTrue();
        result.SafeToRetry.Should().BeFalse();
        result.OwnerBinding.Should().Be("unavailable");
        result.AddMembership.Should().Be("unavailable");
        result.RemoveMembership.Should().Be("unavailable");
        result.SmallGroup.Should().Be("unavailable");
        result.ContactOwner.Should().Be("unavailable");
        result.Transfer.Should().Be("unavailable");
    }

    /// <summary>
    /// 注入 fixture 暫存 target owner 與本次 WhoAmI target 不同的 identity。這個故障模型防止未來
    /// 實作為了讓非空 owner 通過而接受任意 user；決定性 assertion 是 transfer 必須保持 unavailable，
    /// 其餘永久 no-go 欄位也不得轉成 retry 或 cleanup 授權。
    /// </summary>
    [Fact]
    public void Classify_rejects_a_transfer_fixture_target_owner_that_differs_from_who_am_i()
    {
        var result = P72Data8ListManagementFixtureReconciler.Classify(
            addMembership: new P72MembershipSnapshot([]),
            removeMembership: new P72MembershipSnapshot([ContactId]),
            smallGroup: CreateSmallGroup(DifferentLeaderId),
            smallGroupExpected: CreateSmallGroup(TargetLeaderId),
            contactOwnerId: ObservedOwnerId,
            transferFixture: CreateTransferFixture(ObservedOwnerId),
            transfer: new P72TransferGraphSnapshot(
                SourceMembershipPresent: true,
                TargetMembershipPresent: false,
                PresentRecordId: null,
                PresentRecordMatches: false,
                PrimaryListId: SourceListId,
                OwnerId: ObservedOwnerId),
            whoAmITargetOwnerId: WhoAmITargetOwnerId);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("baseline-unprovable");
        result.ReadOnlyProbeExecuted.Should().BeTrue();
        result.SafeToRetry.Should().BeFalse();
        result.OwnerBinding.Should().Be("matches-service-identity");
        result.Transfer.Should().Be("unavailable");
    }

    /// <summary>
    /// 建立只有 race-leader 不同的固定 six-field snapshot。這保留 small-group snapshot 的封閉欄位
    /// 順序，讓測試精確驗證 classifier 的值比較，而非引入任意欄位 map 或外部 graph。
    /// </summary>
    /// <param name="raceLeaderId">此合成 snapshot 的 race-leader identity。</param>
    /// <returns>只含測試本機純值的 small-group projection。</returns>
    private static P72SmallGroupFixedFieldsSnapshot CreateSmallGroup(Guid raceLeaderId)
        => new(
            AreaLeaderId: ContactId,
            AreaName: "test-area",
            RaceLeaderId: raceLeaderId,
            CoAreaLeaderId: null,
            CoRaceLeaderId: null,
            ViceFamilyLeaderId: null);

    /// <summary>
    /// 建立固定 UTC Sunday 的 transfer descriptor。descriptor 只在本測試 stack 內存在，且不含
    /// endpoint、credential、服務連線或任何可釋放 resource；verified target owner 只在這個測試
    /// stack 與 classifier 呼叫的純值參數中比對，避免把 service identity 偽裝成持久 fixture baseline。
    /// </summary>
    /// <param name="verifiedTargetOwnerId">同次 WhoAmI 唯讀 probe 已驗證的非空 service identity。</param>
    /// <returns>可供純 transfer-shape 比較的 task-local descriptor。</returns>
    private static P72TransferFixture CreateTransferFixture(Guid verifiedTargetOwnerId)
        => new(
            ContactId,
            SourceListId,
            TargetListId,
            SundayStart,
            TargetOwnerId: verifiedTargetOwnerId);
}
