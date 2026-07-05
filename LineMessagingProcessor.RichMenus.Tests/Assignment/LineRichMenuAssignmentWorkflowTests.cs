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
    /// <summary>
    /// 快取已有 richMenuId 時，指派流程應直接用快取值綁定使用者。
    ///
    /// 這是最常見的產品路徑：provisioning 已經把 menu key 與 provider richMenuId
    /// 建立好對照，assignment workflow 只負責把使用者連到既有 RichMenu，
    /// 不應重新建立或掃描線上選單。
    /// </summary>
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

    /// <summary>
    /// menu key 不存在時應回傳標準驗證失敗，而不是呼叫 LINE provider。
    ///
    /// 產品端只知道穩定的 menu key；如果這個 key 沒有被 catalog 或 cache 解析，
    /// 代表本機設定錯誤，不能把錯誤包裝成 LINE 平台失敗。
    /// </summary>
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

    /// <summary>
    /// 快取未命中時，workflow 應能用 catalog fingerprint 從線上 RichMenu 清單復原 richMenuId。
    ///
    /// 這保護重啟後的冷快取情境：LINE 上已存在相同版本選單時，不需要重新建立，
    /// 只要找回 provider id、寫回快取並繼續完成使用者綁定。
    /// </summary>
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

    /// <summary>
    /// AssignOrThrowAsync 在指派失敗時應丟出共用例外，並保留原始 assignment result。
    ///
    /// 呼叫端若採用 throw-based API，仍需要從例外取回狀態碼與錯誤碼，
    /// 才能在產品層做一致的錯誤記錄或使用者提示。
    /// </summary>
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

    /// <summary>
    /// 即使本機 state store 沒有紀錄，解除綁定仍必須呼叫 LINE unlink。
    ///
    /// state store 只是輔助追蹤上一個 menu key；LINE 端才是使用者目前實際綁定狀態。
    /// 若本機狀態遺失就跳過 unlink，使用者會繼續留在舊 RichMenu。
    /// </summary>
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

    /// <summary>
    /// state store 有紀錄時，解除綁定結果應帶回前一個 menu key 並清除本機狀態。
    ///
    /// 這讓呼叫端可得知解除前的產品選單來源，同時確保暫時性 RichMenu 狀態不會殘留，
    /// 影響後續 sweep 或重新指派判斷。
    /// </summary>
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

    /// <summary>
    /// LINE 回覆明確拒絕 link 時，workflow 應轉成 ProviderRejected。
    ///
    /// 這類錯誤通常代表 richMenuId 無效、使用者不可綁定或 LINE 端驗證失敗；
    /// 與網路斷線不同，呼叫端應能從標準狀態看出 provider 已處理但拒絕請求。
    /// </summary>
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

    /// <summary>
    /// LINE link 發生一般 HTTP/network 失敗時，workflow 應轉成 ProviderUnavailable。
    ///
    /// 這保護產品端不必直接理解 HttpRequestException，
    /// 只要依照共用 RichMenu 狀態碼決定重試、補償或告警。
    /// </summary>
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

    /// <summary>
    /// 線上 RichMenu 查詢失敗時，不應繼續嘗試 link 使用者。
    ///
    /// 快取未命中代表 workflow 尚未知道 provider richMenuId；若 list 呼叫失敗，
    /// 後續 link 沒有可靠 id 可用，因此必須停在 ProviderUnavailable。
    /// </summary>
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

    /// <summary>
    /// TaskCanceledException 型態的 provider 逾時應回報為標準 timeout 錯誤碼。
    ///
    /// 不同 HTTP client 可能以取消例外表示逾時；產品端不應因此看到不一致的錯誤分類。
    /// </summary>
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

    /// <summary>
    /// TimeoutException 型態的 provider 逾時也應回報為標準 timeout 錯誤碼。
    ///
    /// 這與 TaskCanceledException 測試互補，確保 workflow 的 provider 邊界能涵蓋常見逾時型態。
    /// </summary>
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

    /// <summary>
    /// processor 內部程式錯誤不應被 workflow 吞掉或誤包裝成 provider 失敗。
    ///
    /// InvalidOperationException 代表測試假物件模擬的程式缺陷；
    /// 若被轉成標準 ProviderUnavailable，會掩蓋真正需要修程式的問題。
    /// </summary>
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

    /// <summary>
    /// 本機 state store 寫入失敗不應被誤判為 LINE provider failure。
    ///
    /// 測試刻意使用 HttpRequestException，確認 workflow 的 try/catch 邊界只包住 provider 呼叫，
    /// 避免本機一致性問題被回報成外部平台暫時不可用。
    /// </summary>
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

    /// <summary>
    /// throw-based 指派 API 在 provider link 失敗時，應把標準 assignment result 放進例外。
    ///
    /// 產品端若選擇用例外控制流程，仍能讀取 ProviderUnavailable 與錯誤碼，
    /// 不需要重新解析底層 HttpRequestException。
    /// </summary>
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

    /// <summary>
    /// LINE 明確拒絕 unlink 時，解除綁定流程應轉成 ProviderRejected。
    ///
    /// 這讓呼叫端能分辨「LINE 已拒絕」與「LINE 無法連線」兩種補償策略。
    /// </summary>
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

    /// <summary>
    /// LINE unlink 逾時時，解除綁定流程應回報 provider timeout。
    ///
    /// 使用者實際是否解除成功在逾時時不可確定，因此 workflow 保留 provider unavailable 分類，
    /// 交由呼叫端決定是否重試或稍後查詢狀態。
    /// </summary>
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

    /// <summary>
    /// unlink processor 的非預期程式例外不應被 workflow 包裝成 provider failure。
    ///
    /// 這與 assign 的保護對稱，避免共用流程把內部 bug 偽裝成 LINE 平台問題。
    /// </summary>
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

    /// <summary>
    /// 本機 state store 移除失敗不應被誤判為 LINE unlink 失敗。
    ///
    /// LINE unlink 已送出後，若本機狀態清除失敗，呼叫端需要看到真正的儲存層錯誤，
    /// 才能安排補償或資料修復。
    /// </summary>
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

    /// <summary>
    /// throw-based 解除綁定 API 在 provider unlink 失敗時，應保留標準 assignment result。
    ///
    /// 這確保非同步背景流程或產品控制器即使用 OrThrow 版本，
    /// 仍能用同一組 RichMenu 狀態碼做診斷。
    /// </summary>
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

    /// <summary>
    /// 可注入寫入或移除例外的 state store 假物件。
    ///
    /// 它用來精準測試 workflow 的錯誤邊界：provider 例外應被標準化，
    /// 但本機狀態儲存失敗必須原樣往外拋，避免兩種不同責任域混在一起。
    /// </summary>
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
