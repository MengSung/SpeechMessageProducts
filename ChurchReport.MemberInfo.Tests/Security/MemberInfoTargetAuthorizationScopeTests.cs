using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using ChurchReport.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

/// <summary>
/// 驗證 MemberInfo target authorization 只能建立在已驗證的 P7 subject scope 與
/// server-owned evidence 之上。測試不寫入 Session、cache、CRM、feature gate 或
/// shared state；每個案例都使用獨立的 scalar/集合，並以 A/B 交錯與 failure matrix
/// 斷言跨使用者資料不能被重新發布。
/// </summary>
public sealed class MemberInfoTargetAuthorizationScopeTests
{
    /// <summary>
    /// Church-wide evidence 可以建立沒有名單集合的不可變 scope；這不表示 login kind
    /// 或 browser locator 自己具有 Church 授權。
    /// </summary>
    [Fact]
    public void TryCreate_with_complete_church_evidence_returns_church_scope()
    {
        var subject = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var requestScope = CreateRequestScope(subject);
        var evidence = MemberInfoTargetAuthorizationEvidence.Create(
            subject,
            MemberInfoTargetAccessMode.ChurchWide,
            Array.Empty<Guid>(),
            assignmentEvidenceComplete: true);

        var resolution = MemberInfoTargetAuthorizationScopeResolver.TryCreate(requestScope, evidence);

        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.None);
        resolution.Scope.Should().NotBeNull();
        resolution.Scope!.SubjectContactId.Should().Be(subject);
        resolution.Scope.AccessMode.Should().Be(MemberInfoTargetAccessMode.ChurchWide);
        resolution.Scope.VisibleListIds.Should().BeEmpty();
    }

    /// <summary>
    /// Shepherd evidence 即使目前沒有可見小組，也能產生空的 request-local allowlist；
    /// 「沒有小組」不是授權失敗，也不能退回 legacy ListManager 重新載入資料。
    /// </summary>
    [Fact]
    public void TryCreate_with_complete_empty_shepherd_evidence_returns_empty_allowlist()
    {
        var subject = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var resolution = MemberInfoTargetAuthorizationScopeResolver.TryCreate(
            CreateRequestScope(subject),
            MemberInfoTargetAuthorizationEvidence.Create(
                subject,
                MemberInfoTargetAccessMode.AssignedLists,
                Array.Empty<Guid>(),
                assignmentEvidenceComplete: true));

        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.None);
        resolution.Scope!.VisibleListIds.Should().BeEmpty();
    }

    /// <summary>
    /// Shepherd list IDs 必須去重、驗證、限制數量並 defensive-copy。原始輸入集合
    /// 在 scope 建立後改變，不得改變已發布的 authorization snapshot。
    /// </summary>
    [Fact]
    public void TryCreate_copies_and_bounds_shepherd_list_ids()
    {
        var subject = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var listA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var listB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var source = new List<Guid> { listA, listB };
        var evidence = MemberInfoTargetAuthorizationEvidence.Create(
            subject,
            MemberInfoTargetAccessMode.AssignedLists,
            source,
            assignmentEvidenceComplete: true);

        source.Clear();
        var resolution = MemberInfoTargetAuthorizationScopeResolver.TryCreate(
            CreateRequestScope(subject),
            evidence);

        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.None);
        resolution.Scope!.VisibleListIds.Should().Equal(listA, listB);
        resolution.Scope.VisibleListIds.Should().BeAssignableTo<IReadOnlyList<Guid>>();
    }

    /// <summary>
    /// 將另一個 subject 的 evidence 交給目前 request 必須固定拒絕，避免 A 借用 B 的
    /// authorization allowlist。
    /// </summary>
    [Fact]
    public void TryCreate_with_subject_mismatch_fails_closed()
    {
        var requestSubject = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var evidenceSubject = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var resolution = MemberInfoTargetAuthorizationScopeResolver.TryCreate(
            CreateRequestScope(requestSubject),
            MemberInfoTargetAuthorizationEvidence.Create(
                evidenceSubject,
                MemberInfoTargetAccessMode.AssignedLists,
                new[] { Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc") },
                assignmentEvidenceComplete: true));

        resolution.Scope.Should().BeNull();
        resolution.Failure.Should().Be(MemberInfoTargetAuthorizationFailure.SubjectMismatch);
    }

    /// <summary>
    /// 缺少 request scope、source evidence 或完整 assignment evidence 時，固定分類拒絕，
    /// 不應透過 Session、legacy manager 或另一個 request 補資料。
    /// </summary>
    [Fact]
    public void TryCreate_with_missing_or_incomplete_source_fails_closed()
    {
        var subject = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var completeScope = CreateRequestScope(subject);

        MemberInfoTargetAuthorizationScopeResolver.TryCreate(null, null)
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.MissingRequestScope);

        MemberInfoTargetAuthorizationScopeResolver.TryCreate(completeScope, null)
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.SourceUnavailable);

        MemberInfoTargetAuthorizationScopeResolver.TryCreate(
                completeScope,
                MemberInfoTargetAuthorizationEvidence.Create(
                    subject,
                    MemberInfoTargetAccessMode.AssignedLists,
                    Array.Empty<Guid>(),
                    assignmentEvidenceComplete: false))
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.IncompleteAssignmentEvidence);
    }

    /// <summary>
    /// Church scope 不得攜帶 Shepherd list IDs；空白、空 GUID、重複或超過固定上限的
    /// target 都拒絕，避免以錯誤或無界集合擴大資料範圍。
    /// </summary>
    [Fact]
    public void TryCreate_with_invalid_or_ambiguous_targets_fails_closed()
    {
        var subject = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var requestScope = CreateRequestScope(subject);
        var listId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        MemberInfoTargetAuthorizationScopeResolver.TryCreate(
                requestScope,
                MemberInfoTargetAuthorizationEvidence.Create(
                    subject,
                    MemberInfoTargetAccessMode.ChurchWide,
                    new[] { listId },
                    assignmentEvidenceComplete: true))
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.InvalidOrDuplicateTarget);

        MemberInfoTargetAuthorizationScopeResolver.TryCreate(
                requestScope,
                MemberInfoTargetAuthorizationEvidence.Create(
                    subject,
                    MemberInfoTargetAccessMode.AssignedLists,
                    new[] { Guid.Empty },
                    assignmentEvidenceComplete: true))
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.InvalidOrDuplicateTarget);

        MemberInfoTargetAuthorizationScopeResolver.TryCreate(
                requestScope,
                MemberInfoTargetAuthorizationEvidence.Create(
                    subject,
                    MemberInfoTargetAccessMode.AssignedLists,
                    new[] { listId, listId },
                    assignmentEvidenceComplete: true))
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.InvalidOrDuplicateTarget);

        var tooMany = Enumerable.Range(1, 513)
            .Select(index => new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
            .ToArray();
        MemberInfoTargetAuthorizationScopeResolver.TryCreate(
                requestScope,
                MemberInfoTargetAuthorizationEvidence.Create(
                    subject,
                    MemberInfoTargetAccessMode.AssignedLists,
                    tooMany,
                    assignmentEvidenceComplete: true))
            .Failure.Should().Be(MemberInfoTargetAuthorizationFailure.InvalidOrDuplicateTarget);
    }

    /// <summary>
    /// A/B request 交錯時，每個 scope 只發布自身 subject 與 list IDs；resolver 不得有
    /// static mutable state、cache、principal、Session、credential 或 CRM entity 欄位。
    /// </summary>
    [Fact]
    public void TryCreate_interleaved_subjects_never_cross_publish_target_state()
    {
        var subjectA = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var subjectB = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var listA = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var listB = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var evidenceA = MemberInfoTargetAuthorizationEvidence.Create(
            subjectA,
            MemberInfoTargetAccessMode.AssignedLists,
            new[] { listA },
            assignmentEvidenceComplete: true);
        var evidenceB = MemberInfoTargetAuthorizationEvidence.Create(
            subjectB,
            MemberInfoTargetAccessMode.AssignedLists,
            new[] { listB },
            assignmentEvidenceComplete: true);

        var resolutions = Enumerable.Range(0, 64)
            .Select(index => index % 2 == 0
                ? MemberInfoTargetAuthorizationScopeResolver.TryCreate(CreateRequestScope(subjectA), evidenceA)
                : MemberInfoTargetAuthorizationScopeResolver.TryCreate(CreateRequestScope(subjectB), evidenceB))
            .ToArray();

        resolutions.Where((_, index) => index % 2 == 0).Should().OnlyContain(result =>
            result.Failure == MemberInfoTargetAuthorizationFailure.None &&
            result.Scope!.SubjectContactId == subjectA &&
            result.Scope.VisibleListIds.SequenceEqual(new[] { listA }));
        resolutions.Where((_, index) => index % 2 != 0).Should().OnlyContain(result =>
            result.Failure == MemberInfoTargetAuthorizationFailure.None &&
            result.Scope!.SubjectContactId == subjectB &&
            result.Scope.VisibleListIds.SequenceEqual(new[] { listB }));
    }

    /// <summary>
    /// 公開 contract 只接受既有 request scope 與 server-owned evidence，不接 browser locator、
    /// owner、profile、connector、credential、HttpContext 或 cancellation registration；所有
    /// published state 均為 immutable scalar/collection。
    /// </summary>
    [Fact]
    public void Public_contract_has_no_request_or_credential_state()
    {
        typeof(MemberInfoTargetAuthorizationScopeResolver)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "TryCreate")
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Equal(typeof(P7GatewayRequestScope), typeof(MemberInfoTargetAuthorizationEvidence));

        typeof(MemberInfoTargetAuthorizationScopeResolver)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsLiteral)
            .Should().BeEmpty();

        typeof(MemberInfoTargetAuthorizationScope)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Should().Equal("AccessMode", "SubjectContactId", "VisibleListIds");
    }

    /// <summary>
    /// 驗證 evidence 只能由 ChurchReport assembly 內的 server-owned provider 建立；若將
    /// factory 公開給 controller、browser adapter 或其他 consumer，任何呼叫端都能偽造
    /// 「完整」assignment evidence，破壞 subject 已驗證但 target scope 未驗證的信任邊界。
    /// 此測試只檢查 public surface，不執行 I/O、Session、CRM 或共享狀態操作。
    /// </summary>
    [Fact]
    public void Evidence_factory_is_not_publicly_callable()
    {
        typeof(MemberInfoTargetAuthorizationEvidence)
            .GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull();
    }

    private static P7GatewayRequestScope CreateRequestScope(Guid subjectContactId)
    {
        var contactId = subjectContactId.ToString("D");
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, contactId),
                    new Claim(LoginClaimsFactory.ContactIdClaim, contactId),
                    new Claim(LoginClaimsFactory.LoginTypeClaim, "ACCOUNT")
                },
                CookieAuthenticationDefaults.AuthenticationScheme));

        return P7GatewayRequestScopeResolver.TryCreate(principal).Scope!;
    }
}
