// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/FeeEditorLessonAccessResolverTests.cs
// 目的：保護費用編輯器唯讀端點的 server-derived 課程快照授權邊界。
//
// 測試邊界：每個案例只建立短生命週期的記憶體 lesson snapshot 與 Guid，不會啟動 MVC、
// 讀取 Session、建立 CRM／Gateway client、查詢快取或排程背景工作。故障注入覆蓋未載入、
// 登入不符、無效 ID、重複 ID 與 snapshot 外 locator；決定性斷言是只有完整、目前登入者
// 的伺服器快照可以授權目標，瀏覽器輸入不會成為權限來源。
// ============================================================================

using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 <see cref="FeeEditorLessonAccessResolver"/> 不會把瀏覽器 locator、舊登入或模糊的
/// lesson cache 當成授權資料。解析器只接受已由 controller 驗證為目前登入 scope 的 snapshot，
/// 以固定且有界的純記憶體檢查建立 request-local allowlist；它不持有 session、DTO、cache 或
/// 外部資源，所以每個測試結束後沒有需回收的 connection、timer 或 background work。
/// </summary>
public sealed class FeeEditorLessonAccessResolverTests
{
    /// <summary>
    /// 完整 server lesson snapshot 僅有一個有效 target 時，解析器必須授權該 target 並產生新的
    /// request-local allowlist。故障注入在後段會由其他案例處理；本案例保護正常授權不需要 CRM
    /// 補查，且 response 權限可由目前 request 的純 scalar snapshot 重新建立。
    /// </summary>
    [Fact]
    public void Current_loaded_server_snapshot_authorizes_its_unique_target()
    {
        var target = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var lessons = new[]
        {
            new Lesson { DiscipleLessonsId = target.ToString("D"), DiscipleLessonsName = "伺服器課程" }
        };

        FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(
                lessonListIsLoadedForCurrentLogin: true,
                serverLessons: lessons,
                out var authorizedLessonIds)
            .Should().BeTrue();
        authorizedLessonIds.Should().ContainSingle().Which.Should().Be(target);
    }

    /// <summary>
    /// 未載入、空白、無效、重複或不屬於目前登入者的 snapshot 都必須拒絕。這些故障代表
    /// FeeList 可能是舊 session、尚未建立或不可判斷的資料；決定性斷言是 helper 不會挑選任何
    /// lesson、嘗試修正資料或查 CRM，避免 IDOR 與跨登入 cache reuse。
    /// </summary>
    [Fact]
    public void Missing_or_ambiguous_server_snapshot_is_denied_without_selecting_a_lesson()
    {
        var duplicate = "22222222-2222-2222-2222-222222222222";
        var valid = new[] { new Lesson { DiscipleLessonsId = duplicate } };
        var invalid = new[] { new Lesson { DiscipleLessonsId = "not-a-guid" } };
        var duplicated = new[]
        {
            new Lesson { DiscipleLessonsId = duplicate },
            new Lesson { DiscipleLessonsId = duplicate }
        };

        FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(false, valid, out var notLoaded)
            .Should().BeFalse();
        notLoaded.Should().BeEmpty();

        FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(true, null, out var missing)
            .Should().BeFalse();
        missing.Should().BeEmpty();

        FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(true, invalid, out var invalidIds)
            .Should().BeFalse();
        invalidIds.Should().BeEmpty();

        FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(true, duplicated, out var duplicateIds)
            .Should().BeFalse();
        duplicateIds.Should().BeEmpty();
    }

    /// <summary>
    /// browser target 只能在 controller 完成 server snapshot 驗證後，才與已授權 allowlist 比對。
    /// 測試同時注入 snapshot 外的有效 Guid，證明 GUID 格式正確本身不會取得課程費用讀取權。
    /// 資料結構皆為測試私有且不含 session 或 CRM Entity，因此不會遮蔽跨 request 保留問題。
    /// </summary>
    [Fact]
    public void Browser_locator_must_match_the_server_authorized_allowlist()
    {
        var target = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var different = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var lessons = new[] { new Lesson { DiscipleLessonsId = target.ToString("D") } };

        FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(true, lessons, out var authorized)
            .Should().BeTrue();

        FeeEditorLessonAccessResolver.IsAuthorizedTarget(authorized, target).Should().BeTrue();
        FeeEditorLessonAccessResolver.IsAuthorizedTarget(authorized, different).Should().BeFalse();
    }
}
