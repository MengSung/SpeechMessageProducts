using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.RichMenus.Tests.Support;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Assignment;

/// <summary>
/// <see cref="LineRichMenuAssignmentWorkflow"/> 的行為測試。
///
/// 這組測試的重點不是測 LINE 官方 API 本身，而是鎖住共用工作流的資料流：
/// 1. 產品只用 menu key 表達想切到哪個 RichMenu。
/// 2. 共用層負責解析 richMenuId 並呼叫 processor。
/// 3. state store 只作為輔助紀錄，不可以讓解除綁定流程跳過 LINE unlink。
///
/// 這些規則會影響未來產品整合，所以測試名稱刻意寫得偏長，
/// 讓維護者看到失敗訊息時就知道是哪個業務邊界被破壞。
/// </summary>
public sealed class LineRichMenuAssignmentWorkflowTests
{
    [Fact]
    public async Task AssignAsync_links_user_to_cached_rich_menu_id()
    {
        var processor = new CapturingRichMenuProcessor();
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(processor, cache);

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeTrue();
        result.RichMenuId.Should().Be("rich-menu-001");
        processor.Calls.Should().Contain("link:U123:rich-menu-001");
    }

    [Fact]
    public async Task AssignAsync_returns_validation_failure_when_menu_key_is_unknown()
    {
        var workflow = new LineRichMenuAssignmentWorkflow(
            new CapturingRichMenuProcessor(),
            new InMemoryLineRichMenuIdCache());

        var result = await workflow.AssignAsync("U123", "missing");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ValidationFailed);
        result.ErrorCode.Should().Be("line-richmenu-menu-key-not-found");
    }

    [Fact]
    public async Task AssignAsync_resolves_online_rich_menu_when_cache_is_empty()
    {
        var definition = new LineRichMenuDefinition(
            "member-main",
            "member-main",
            RichMenuTestFactory.CreateMenu("member-main"),
            RichMenuTestFactory.CreatePngFactory());
        var processor = new CapturingRichMenuProcessor();
        var cache = new InMemoryLineRichMenuIdCache();
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore(),
            new StaticLineRichMenuCatalog(new[] { definition }));
        var versionedName = LineRichMenuFingerprint.BuildName(definition, RichMenuTestFactory.CreatePngBytes());
        processor.ExistingRichMenus.Add(RichMenuTestFactory.CreateMenu(versionedName).ToResponseRichMenu("rich-menu-online"));

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeTrue();
        result.RichMenuId.Should().Be("rich-menu-online");
        processor.Calls.Should().Contain("list");
        processor.Calls.Should().Contain("link:U123:rich-menu-online");
        cache.TryGet("member-main", out var cachedRichMenuId).Should().BeTrue();
        cachedRichMenuId.Should().Be("rich-menu-online");
    }

    [Fact]
    public async Task AssignOrThrowAsync_throws_standard_exception_when_assignment_fails()
    {
        var workflow = new LineRichMenuAssignmentWorkflow(
            new CapturingRichMenuProcessor(),
            new InMemoryLineRichMenuIdCache());

        var action = () => workflow.AssignOrThrowAsync("U123", "missing");

        var exception = await action.Should().ThrowAsync<LineRichMenuException>();
        exception.Which.AssignmentResult.Should().NotBeNull();
        exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ValidationFailed);
    }

    [Fact]
    public async Task UnassignAsync_calls_line_unlink_even_when_state_store_is_empty()
    {
        var processor = new CapturingRichMenuProcessor();
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            new InMemoryRichMenuStateStore());

        var result = await workflow.UnassignAsync("U123");

        result.Succeeded.Should().BeTrue();
        result.Changed.Should().BeTrue();
        result.PreviousMenuKey.Should().BeNull();
        processor.Calls.Should().Contain("unlink:U123");
    }

    [Fact]
    public async Task UnassignAsync_returns_previous_menu_key_and_removes_state_when_record_exists()
    {
        var processor = new CapturingRichMenuProcessor();
        var stateStore = new InMemoryRichMenuStateStore();
        await stateStore.SetAsync(new RichMenuUserState(
            "U123",
            "member-main",
            previousMenuKey: "guest-main",
            expiresAt: null,
            updatedAt: DateTimeOffset.UtcNow));
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            stateStore);

        var result = await workflow.UnassignAsync("U123");

        result.Succeeded.Should().BeTrue();
        result.Changed.Should().BeTrue();
        result.PreviousMenuKey.Should().Be("member-main");
        processor.Calls.Should().Contain("unlink:U123");
        var storedState = await stateStore.GetAsync("U123");
        storedState.Should().BeNull();
    }

    [Fact]
    public async Task AssignAsync_returns_provider_rejected_when_line_rejects_link_request()
    {
        var processor = new CapturingRichMenuProcessor
        {
            LinkException = new LineResponseException("invalid rich menu link")
        };
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore());

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderRejected);
        result.ErrorCode.Should().Be("line-richmenu-provider-rejected");
        result.ErrorMessage.Should().Be("invalid rich menu link");
    }

    [Fact]
    public async Task AssignAsync_returns_provider_unavailable_when_line_link_network_fails()
    {
        var processor = new CapturingRichMenuProcessor
        {
            LinkException = new HttpRequestException("network unavailable")
        };
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore());

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        result.ErrorCode.Should().Be("line-richmenu-provider-unavailable");
        result.ErrorMessage.Should().Be("network unavailable");
    }

    [Fact]
    public async Task AssignAsync_returns_provider_unavailable_when_online_rich_menu_lookup_network_fails()
    {
        var definition = new LineRichMenuDefinition(
            "member-main",
            "member-main",
            RichMenuTestFactory.CreateMenu("member-main"),
            RichMenuTestFactory.CreatePngFactory());
        var processor = new CapturingRichMenuProcessor
        {
            ListException = new HttpRequestException("list network unavailable")
        };
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            new InMemoryRichMenuStateStore(),
            new StaticLineRichMenuCatalog(new[] { definition }));

        var result = await workflow.AssignAsync("U123", "member-main");

        // cache miss 時 workflow 會先向 LINE 查詢線上 RichMenu 清單。
        // 這是 provider 邊界，所以網路錯誤應該變成標準 ProviderUnavailable 結果；
        // 但因為尚未解析到 richMenuId，也不能繼續 link 使用者。
        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        result.ErrorCode.Should().Be("line-richmenu-provider-unavailable");
        result.ErrorMessage.Should().Be("list network unavailable");
        processor.Calls.Should().NotContain(call => call.StartsWith("link:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssignAsync_returns_provider_timeout_when_line_link_times_out()
    {
        var processor = new CapturingRichMenuProcessor
        {
            LinkException = new TaskCanceledException("provider timeout")
        };
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore());

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        result.ErrorCode.Should().Be("line-richmenu-provider-timeout");
        result.ErrorMessage.Should().Be("provider timeout");
    }

    [Fact]
    public async Task AssignAsync_returns_provider_timeout_when_line_link_throws_timeout_exception()
    {
        var processor = new CapturingRichMenuProcessor
        {
            LinkException = new TimeoutException("provider hard timeout")
        };
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore());

        var result = await workflow.AssignAsync("U123", "member-main");

        // 部分 .NET / HTTP client 實作在逾時時會丟 TimeoutException，
        // 不是 TaskCanceledException。對產品呼叫端而言，兩者都代表 LINE provider
        // 當下無法完成請求，所以共用 workflow 統一轉成 provider timeout。
        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        result.ErrorCode.Should().Be("line-richmenu-provider-timeout");
        result.ErrorMessage.Should().Be("provider hard timeout");
    }

    [Fact]
    public async Task AssignAsync_does_not_swallow_unexpected_processor_exception()
    {
        var processor = new CapturingRichMenuProcessor
        {
            LinkException = new InvalidOperationException("processor bug")
        };
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore());

        var action = () => workflow.AssignAsync("U123", "member-main");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("processor bug");
    }

    [Fact]
    public async Task AssignAsync_does_not_report_provider_failure_when_state_store_set_fails()
    {
        var processor = new CapturingRichMenuProcessor();
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var stateStore = new ThrowingRichMenuStateStore(setException: new HttpRequestException("state store write failed"));
        var workflow = new LineRichMenuAssignmentWorkflow(processor, cache, stateStore);

        var action = () => workflow.AssignAsync("U123", "member-main");

        // 這個測試刻意讓 state store 丟出 HttpRequestException。
        //
        // 原因是外部 provider 斷線也常用 HttpRequestException 表示；
        // 如果 workflow 的 try/catch 範圍太大，就會把本機狀態寫入失敗誤判成 LINE provider failure。
        // 正確行為是：LINE link 已經送出，但本機狀態沒有寫成功，所以例外必須往外拋，
        // 讓呼叫端、測試或監控能看見資料一致性問題，而不是收到一個假的 ProviderUnavailable 結果。
        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("state store write failed");
        processor.Calls.Should().Contain("link:U123:rich-menu-001");
    }

    [Fact]
    public async Task AssignOrThrowAsync_throws_standard_exception_when_provider_link_fails()
    {
        var processor = new CapturingRichMenuProcessor
        {
            LinkException = new HttpRequestException("network unavailable")
        };
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore());

        var action = () => workflow.AssignOrThrowAsync("U123", "member-main");

        var exception = await action.Should().ThrowAsync<LineRichMenuException>();
        exception.Which.AssignmentResult.Should().NotBeNull();
        exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        exception.Which.AssignmentResult.ErrorCode.Should().Be("line-richmenu-provider-unavailable");
    }

    [Fact]
    public async Task UnassignAsync_returns_provider_rejected_when_line_rejects_unlink_request()
    {
        var processor = new CapturingRichMenuProcessor
        {
            UnlinkException = new LineResponseException("invalid rich menu unlink")
        };
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            new InMemoryRichMenuStateStore());

        var result = await workflow.UnassignAsync("U123");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderRejected);
        result.ErrorCode.Should().Be("line-richmenu-provider-rejected");
        result.ErrorMessage.Should().Be("invalid rich menu unlink");
    }

    [Fact]
    public async Task UnassignAsync_returns_provider_unavailable_when_line_unlink_times_out()
    {
        var processor = new CapturingRichMenuProcessor
        {
            UnlinkException = new TaskCanceledException("provider timeout")
        };
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            new InMemoryRichMenuStateStore());

        var result = await workflow.UnassignAsync("U123");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        result.ErrorCode.Should().Be("line-richmenu-provider-timeout");
        result.ErrorMessage.Should().Be("provider timeout");
    }

    [Fact]
    public async Task UnassignAsync_does_not_swallow_unexpected_processor_exception()
    {
        var processor = new CapturingRichMenuProcessor
        {
            UnlinkException = new InvalidOperationException("processor bug")
        };
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            new InMemoryRichMenuStateStore());

        var action = () => workflow.UnassignAsync("U123");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("processor bug");
    }

    [Fact]
    public async Task UnassignAsync_does_not_report_provider_failure_when_state_store_remove_fails()
    {
        var processor = new CapturingRichMenuProcessor();
        var stateStore = new ThrowingRichMenuStateStore(removeException: new HttpRequestException("state store remove failed"))
        {
            ExistingState = new RichMenuUserState(
                "U123",
                "member-main",
                previousMenuKey: null,
                expiresAt: null,
                updatedAt: DateTimeOffset.UtcNow)
        };
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            stateStore);

        var action = () => workflow.UnassignAsync("U123");

        // 與 AssignAsync 的 state-store 測試相同，這裡要證明 unlink provider 呼叫成功後，
        // 本機狀態刪除失敗不能被包裝成 LINE provider failure。
        //
        // 這對未來產品很重要：不同產品可能用不同的狀態儲存方式，
        // 但共用 RichMenu workflow 必須維持清楚資料流，讓「LINE 外部平台失敗」與
        // 「本機狀態儲存失敗」能被呼叫端分開診斷與補償。
        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("state store remove failed");
        processor.Calls.Should().Contain("unlink:U123");
    }

    [Fact]
    public async Task UnassignOrThrowAsync_throws_standard_exception_when_provider_unlink_fails()
    {
        var processor = new CapturingRichMenuProcessor
        {
            UnlinkException = new HttpRequestException("network unavailable")
        };
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            new InMemoryLineRichMenuIdCache(),
            new InMemoryRichMenuStateStore());

        var action = () => workflow.UnassignOrThrowAsync("U123");

        var exception = await action.Should().ThrowAsync<LineRichMenuException>();
        exception.Which.AssignmentResult.Should().NotBeNull();
        exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable);
        exception.Which.AssignmentResult.ErrorCode.Should().Be("line-richmenu-provider-unavailable");
    }

    private sealed class ThrowingRichMenuStateStore : IRichMenuStateStore
    {
        private readonly Exception? _setException;
        private readonly Exception? _removeException;

        public ThrowingRichMenuStateStore(Exception? setException = null, Exception? removeException = null)
        {
            _setException = setException;
            _removeException = removeException;
        }

        public RichMenuUserState? ExistingState { get; set; }

        public Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingState);
        }

        public Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default)
        {
            if (_setException != null)
            {
                throw _setException;
            }

            ExistingState = state;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default)
        {
            if (_removeException != null)
            {
                throw _removeException;
            }

            ExistingState = null;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RichMenuUserState> expiredStates = Array.Empty<RichMenuUserState>();
            return Task.FromResult(expiredStates);
        }
    }
}
