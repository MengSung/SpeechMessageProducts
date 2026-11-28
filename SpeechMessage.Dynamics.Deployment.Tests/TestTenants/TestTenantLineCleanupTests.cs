/*
檔案整體說明
目的與定位：保護 F12 測試租戶 LINE 清理的精確 alias 前綴、伺服器宮格身份與借入資源生命週期，不將其他租戶的宮格或使用者綁定當成清理對象。
主要功能與流程：以 DispatchProxy 合成 LINE 用戶端及記憶體速率預算，注入重複／空白身份、共用宮格、配額拒絕、查詢失敗、取消及刪除中斷；斷言拒絕前零副作用、逐宮格取得預算、取消後實際操作已排空，以及保存計畫可在 alias 消失後繼續依明確 menu ID 清理。
依賴與輸出：僅使用合成 alias、menu ID 與使用者代號；不建立 HTTP、SQL、SDK、通知或真實 Channel 連線，不使用 token 或真實個資。事件清單只存在單一測試 owner 的記憶體中，測試完成即可回收。
使用邊界與維護：閘門由各測試建立並在 finally 釋放，清理 Task 必須等待真完成；借入 client 與 budget 不得由產品 helper Dispose。測試替身沒有背景工作、timer、訂閱或外部資源，計畫快照測試另確認不暴露可變集合。
失敗處理與驗證：只執行 TestTenantLineCleanupTests 的離線案例；這些結果不代表實機清理、跨程序 Channel 寫入鎖或 CLI 計畫持久化已整合。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using SpeechMessage.Scheduling;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>以純合成依賴檢查清理權限邊界、部分失敗續跑與沒有遺留工作的取消契約。</summary>
public sealed class TestTenantLineCleanupTests
{
    /// <summary>混合兩個租戶及相似前綴，確認只有精確代碼前綴的 alias、宮格與綁定被刪除。</summary>
    [Fact]
    public async Task Cleanup_OnlyTouchesTenantAliasPrefix()
    {
        var (client, state) = SyntheticLineClient.Create(
            ("c202-main", "menu-other"), ("c201-z", "menu-z"),
            ("c201-a", "menu-a"), ("c2010-main", "menu-similar"));
        state.Bindings.Add("synthetic-user", "menu-a");
        using var budget = new SyntheticBudget();

        await NewCleanup(client, budget).CleanupAsync("c201", ["synthetic-user"], default);

        Assert.Equal(new List<string> { "c202-main", "c2010-main" }, state.Aliases.Select(row => row.RichMenuAliasId));
        Assert.Equal(new List<string> { "menu-other", "menu-similar" }, state.Menus.Order(StringComparer.Ordinal));
        Assert.Empty(state.Bindings);
        Assert.Equal(new List<string> { "user-unlink:synthetic-user", "alias-delete:c201-a", "menu-delete:menu-a",
            "alias-delete:c201-z", "menu-delete:menu-z" }, state.Mutations);
        Assert.Equal(new List<string> { "GoodShepherd2/T01/richmenu-delete", "GoodShepherd2/T01/richmenu-delete" }, budget.Calls);
        Assert.False(state.Disposed);
        Assert.False(budget.Disposed);
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>外部 alias 指向目標宮格時，完整預檢必須在解除、刪除或消耗預算之前拒絕。</summary>
    [Fact]
    public async Task Cleanup_MenuSharedByForeignAlias_RejectsWithoutSideEffects()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "shared"), ("c202-main", "shared"));
        using var budget = new SyntheticBudget();
        await Assert.ThrowsAsync<InvalidDataException>(() => NewCleanup(client, budget).CleanupAsync("c201", [], default));
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
    }

    /// <summary>即使重複身份位於非目標租戶，仍必須拒絕整個不可信 alias 清單，不能去重掩蓋衝突。</summary>
    [Fact]
    public async Task Cleanup_DuplicateAlias_RejectsWithoutSideEffects()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"),
            ("c202-main", "first"), ("c202-main", "second"));
        using var budget = new SyntheticBudget();
        await Assert.ThrowsAsync<InvalidDataException>(() => NewCleanup(client, budget).CleanupAsync("c201", [], default));
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
    }

    /// <summary>空白 alias 或 menu ID 不是穩定身份；注入任何一種缺漏都必須在零副作用下拒絕。</summary>
    [Theory]
    [InlineData("", "menu")]
    [InlineData(" ", "menu")]
    [InlineData("c201-main", "")]
    [InlineData("c201-main", " ")]
    public async Task Cleanup_MissingStableIdentity_Rejects(string alias, string menu)
    {
        var (client, state) = SyntheticLineClient.Create((alias, menu));
        using var budget = new SyntheticBudget();
        await Assert.ThrowsAsync<InvalidDataException>(() => NewCleanup(client, budget).CleanupAsync("c201", [], default));
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
    }

    /// <summary>一千筆為可接受上限；第一千零一筆必須拒絕，避免無界保留或只驗證局部清單。</summary>
    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    public async Task Cleanup_AliasListBound_IsEnforced(int count, bool reject)
    {
        var rows = Enumerable.Range(0, count).Select(index =>
            ("c202-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), "other-menu")).ToArray();
        var (client, state) = SyntheticLineClient.Create(rows);
        using var budget = new SyntheticBudget();
        var cleanup = NewCleanup(client, budget);
        if (reject)
            await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.CleanupAsync("c201", [], default));
        else
            await cleanup.CleanupAsync("c201", [], default);
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
    }

    /// <summary>使用者已綁定其他租戶宮格時，清理本租戶仍須保留該綁定。</summary>
    [Fact]
    public async Task Cleanup_NonTargetUserBinding_IsPreserved()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"), ("c202-main", "other"));
        state.Bindings.Add("synthetic-user", "other");
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", ["synthetic-user"], default);
        Assert.Equal("other", state.Bindings["synthetic-user"]);
        Assert.DoesNotContain(state.Mutations, operation => operation.StartsWith("user-unlink:", StringComparison.Ordinal));
    }

    /// <summary>預檢後使用者切換租戶，解除前的即時查詢必須看到新綁定並保留。</summary>
    [Fact]
    public async Task Cleanup_UserBindingChangesAfterPreflight_IsPreserved()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"), ("c202-main", "other"));
        state.Bindings.Add("synthetic-user", "target");
        var reads = 0;
        state.BeforeCall = operation =>
        {
            if (operation == "user-read:synthetic-user" && ++reads == 2)
                state.Bindings["synthetic-user"] = "other";
            return Task.CompletedTask;
        };
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", ["synthetic-user"], default);
        Assert.Equal("other", state.Bindings["synthetic-user"]);
        Assert.DoesNotContain("user-unlink:synthetic-user", state.Mutations);
    }

    /// <summary>使用者查詢不支援、回傳空身份或後續使用者查詢失敗時，任何遠端副作用都不得先發生。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_UserLookupCannotBeVerified_FailsClosed(bool missingIdentity)
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        state.Bindings.Add("synthetic-a", "target");
        state.Bindings.Add("synthetic-z", "");
        var failure = new NotSupportedException("Synthetic lookup unavailable.");
        state.BeforeCall = operation => operation == "user-read:synthetic-z" && !missingIdentity
            ? Task.FromException(failure) : Task.CompletedTask;
        using var budget = new SyntheticBudget();
        if (missingIdentity)
            await Assert.ThrowsAsync<InvalidDataException>(() => NewCleanup(client, budget)
                .CleanupAsync("c201", ["synthetic-z", "synthetic-a"], default));
        else
            Assert.Same(failure, await Assert.ThrowsAsync<NotSupportedException>(() => NewCleanup(client, budget)
                .CleanupAsync("c201", ["synthetic-z", "synthetic-a"], default)));
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>僅明確的 HTTP 404 表示使用者未綁定，允許繼續清理但不送解除要求。</summary>
    [Fact]
    public async Task Cleanup_UserWithoutDirectBinding_DoesNotUnlink()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", ["synthetic-missing"], default);
        Assert.DoesNotContain("user-unlink:synthetic-missing", state.Mutations);
        Assert.Empty(state.Menus);
    }

    /// <summary>預算拒絕必須拋固定型別與固定訊息供 CLI 續跑，不能刪宮格或先移除其 alias。</summary>
    [Fact]
    public async Task Cleanup_BudgetExhausted_DoesNotDeleteMenu()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget { Allowed = false };
        var error = await Assert.ThrowsAsync<TestTenantLineCleanupBudgetExceededException>(() =>
            NewCleanup(client, budget).CleanupAsync("c201", [], default));
        Assert.Equal("Test tenant LINE cleanup rate budget exhausted.", error.Message);
        Assert.Empty(state.Mutations);
        Assert.Single(state.Aliases);
        Assert.Contains("target", state.Menus);
        Assert.Single(budget.Calls);
    }

    /// <summary>同租戶多個合法 alias 可指向同一 menu，全部 alias 保留身份而宮格刪除只消耗一次預算。</summary>
    [Fact]
    public async Task Cleanup_MultipleTargetAliasesToOneMenu_UsesOneBudget()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-z", "target"), ("c201-a", "target"));
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", [], default);
        Assert.Equal(new List<string> { "alias-delete:c201-a", "alias-delete:c201-z", "menu-delete:target" }, state.Mutations);
        Assert.Single(budget.Calls);
    }

    /// <summary>alias 成功刪除但 menu 失敗後，新 helper 必須靠原計畫的確切 ID 續跑，不靠名稱猜身分。</summary>
    [Fact]
    public async Task Cleanup_SavedPlan_ResumesAfterAliasesWereDeleted()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"), ("c202-main", "other"));
        using var budget = new SyntheticBudget();
        var cleanup = NewCleanup(client, budget);
        var plan = await cleanup.CreatePlanAsync("c201", default);
        var failure = new IOException("Synthetic provider failure.");
        state.BeforeCall = operation => operation == "menu-delete:target"
            ? Task.FromException(failure) : Task.CompletedTask;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => cleanup.CleanupAsync(plan, [], default)));
        Assert.DoesNotContain(state.Aliases, row => row.RichMenuAliasId == "c201-main");
        Assert.Contains("target", state.Menus);

        state.BeforeCall = null;
        await NewCleanup(client, budget).CleanupAsync(plan, [], default);

        Assert.Equal("other", Assert.Single(state.Menus));
        Assert.Single(state.Mutations, operation => operation == "alias-delete:c201-main");
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>保存計畫後若出現外租戶共用、alias 轉指或新目標 alias，必須重新驗證後零副作用拒絕。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Cleanup_SavedPlanRevalidation_RejectsScopeDrift(int change)
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        var cleanup = NewCleanup(client, budget);
        var plan = await cleanup.CreatePlanAsync("c201", default);
        if (change == 0) state.Aliases.Add(new RichMenuAlias { RichMenuAliasId = "c202-main", RichMenuId = "target" });
        if (change == 1) state.Aliases[0].RichMenuId = "replacement";
        if (change == 2) state.Aliases.Add(new RichMenuAlias { RichMenuAliasId = "c201-new", RichMenuId = "new-menu" });
        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.CleanupAsync(plan, [], default));
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
    }

    /// <summary>計畫綁定產品及 Channel；即使 alias 字面相同也不能套用至別的隔離範圍。</summary>
    [Theory]
    [InlineData("GoodButler", "T01")]
    [InlineData("GoodShepherd2", "T02")]
    public async Task Cleanup_PlanCannotCrossProductOrChannel(string product, string channel)
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        var plan = await NewCleanup(client, budget).CreatePlanAsync("c201", default);
        state.Calls.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TestTenantLineCleanup(client, budget, product, channel).CleanupAsync(plan, [], default));
        Assert.Empty(state.Calls);
        Assert.Empty(budget.Calls);
    }

    /// <summary>沒有保存計畫時，無 alias 的宮格缺少租戶身分證據；不得擅自刪除孤立宮格。</summary>
    [Fact]
    public async Task Cleanup_WithoutSavedPlan_DoesNotGuessOrphanOwnership()
    {
        var (client, state) = SyntheticLineClient.Create();
        state.Menus.Add("orphan-with-tenant-like-name");
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", [], default);
        Assert.Single(state.Menus);
        Assert.Empty(state.Mutations);
    }

    /// <summary>計畫複製 caller 候選且唯讀發布；可信持久化 owner 可重建相同身份，在 alias 消失後安全續跑。</summary>
    [Fact]
    public async Task Cleanup_RestoredImmutablePlan_RetainsOriginalMenuIdentity()
    {
        var rows = new List<KeyValuePair<string, string>> { new("c201-main", "target") };
        var original = new TestTenantLineCleanup.Plan("GoodShepherd2", "T01", "c201", rows);
        rows[0] = new("c202-main", "other");
        var item = Assert.Single(original.Aliases);
        Assert.Equal("c201-main", item.Key);
        Assert.Equal("target", item.Value);
        var collection = Assert.IsAssignableFrom<IList<KeyValuePair<string, string>>>(original.Aliases);
        Assert.Throws<NotSupportedException>(() => collection[0] = rows[0]);
        var restored = new TestTenantLineCleanup.Plan(original.Product, original.ChannelKey, original.Code, original.Aliases);
        var (client, state) = SyntheticLineClient.Create(("c202-main", "other"));
        state.Menus.Add("target");
        using var budget = new SyntheticBudget();

        await NewCleanup(client, budget).CleanupAsync(restored, [], default);

        Assert.Equal("other", Assert.Single(state.Menus));
        Assert.Equal("menu-delete:target", Assert.Single(state.Mutations));
    }

    /// <summary>恢復資料仍須逐筆驗證前綴及唯一身份，不能用計畫構造器繞過 alias 清單契約。</summary>
    [Theory]
    [InlineData("c202-other", "menu", false)]
    [InlineData("", "menu", false)]
    [InlineData("c201-main", "", false)]
    [InlineData("c201-main", "menu", true)]
    public void Cleanup_PlanRestore_RejectsInvalidIdentity(string alias, string menu, bool duplicate)
    {
        var rows = new List<KeyValuePair<string, string>> { new(alias, menu) };
        if (duplicate) rows.Add(new(alias, menu));
        Assert.Throws<InvalidDataException>(() => new TestTenantLineCleanup.Plan("GoodShepherd2", "T01", "c201", rows));
    }

    /// <summary>上次實際刪除成功但回應遺失時，保存計畫可把明確 menu 404 視為已完成，不擴張清理範圍。</summary>
    [Fact]
    public async Task Cleanup_AlreadyDeletedPlannedMenu_IsIdempotent()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        var cleanup = NewCleanup(client, budget);
        var plan = await cleanup.CreatePlanAsync("c201", default);
        state.Aliases.Clear();
        state.Menus.Clear();

        await cleanup.CleanupAsync(plan, [], default);

        Assert.Contains("menu-delete:target", state.Calls);
        Assert.Empty(state.Mutations);
        Assert.Single(budget.Calls);
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>alias 在查詢後已被同一清理 owner 移除時，精確 404 不應阻止依原計畫刪除其宮格。</summary>
    [Fact]
    public async Task Cleanup_AliasAlreadyMissing_ContinuesWithPlannedMenu()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        state.BeforeCall = operation =>
        {
            if (operation == "alias-delete:c201-main") state.Aliases.Clear();
            return Task.CompletedTask;
        };
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", [], default);
        Assert.Empty(state.Menus);
        Assert.Equal("menu-delete:target", Assert.Single(state.Mutations));
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>非 404 的 SDK 故障必須保留真實例外及零副作用；禁止把權限錯誤當成使用者未綁定。</summary>
    [Fact]
    public async Task Cleanup_UserLookupForbidden_PreservesFailureWithoutMutation()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        var failure = new LineResponseException("Synthetic forbidden.") { StatusCode = HttpStatusCode.Forbidden };
        state.BeforeCall = operation => operation == "user-read:synthetic-user"
            ? Task.FromException(failure) : Task.CompletedTask;
        using var budget = new SyntheticBudget();
        Assert.Same(failure, await Assert.ThrowsAsync<LineResponseException>(() => NewCleanup(client, budget)
            .CleanupAsync("c201", ["synthetic-user"], default)));
        Assert.Empty(state.Mutations);
        Assert.Empty(budget.Calls);
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>預算等待期間出現外租戶 alias 共用時，預算後重驗必須在 alias／menu 刪除之前拒絕。</summary>
    [Fact]
    public async Task Cleanup_ForeignAliasAppearsDuringBudgetWait_RejectsBeforeDeletion()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget
        {
            BeforeAcquire = () =>
            {
                state.Aliases.Add(new RichMenuAlias { RichMenuAliasId = "c202-main", RichMenuId = "target" });
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => NewCleanup(client, budget).CleanupAsync("c201", [], default));
        Assert.Empty(state.Mutations);
        Assert.Contains("target", state.Menus);
    }

    /// <summary>已取消呼叫不能開始遠端查詢，借入依賴亦不能被關閉。</summary>
    [Fact]
    public async Task Cleanup_CancelledBeforeStart_LeavesNoOperations()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewCleanup(client, budget).CleanupAsync("c201", [], cancellation.Token));
        Assert.Empty(state.Calls);
        Assert.Equal(0, state.ActiveOperations);
        Assert.False(state.Disposed);
        Assert.False(budget.Disposed);
    }

    /// <summary>在讀取、解除或刪除的不可取消 SDK 呼叫中取消，Task 必須等到閘門釋放後才結束且不發起下一操作。</summary>
    [Theory]
    [InlineData("aliases-read")]
    [InlineData("user-read:synthetic-user")]
    [InlineData("user-unlink:synthetic-user")]
    [InlineData("alias-delete:c201-main")]
    [InlineData("menu-delete:target")]
    public async Task Cleanup_Cancellation_DrainsActualSdkOperation(string pausedOperation)
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        state.Bindings.Add("synthetic-user", "target");
        using var budget = new SyntheticBudget();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.BeforeCall = async operation =>
        {
            if (operation != pausedOperation) return;
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
        };
        var pending = NewCleanup(client, budget).CleanupAsync("c201", ["synthetic-user"], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, state.ActiveOperations);
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.Equal(0, state.ActiveOperations);
        Assert.Equal(pausedOperation, state.Calls[^1]);
        Assert.False(state.Disposed);
    }

    /// <summary>預算依賴忽略取消時，也須等待其真正完成後停止；不得遺留背景預算工作。</summary>
    [Fact]
    public async Task Cleanup_Cancellation_DrainsBudgetOperation()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        budget.BeforeAcquire = async () => { entered.SetResult(); await release.Task.ConfigureAwait(false); };
        var pending = NewCleanup(client, budget).CleanupAsync("c201", [], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, budget.ActiveOperations);
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        Assert.Empty(state.Mutations);
        Assert.Equal(0, budget.ActiveOperations);
        Assert.Equal(0, state.ActiveOperations);
    }

    /// <summary>進入非同步查詢前複製 caller 的使用者集合，之後 caller 改值不能擴張解除範圍。</summary>
    [Fact]
    public async Task Cleanup_CallerMutation_DoesNotChangeUserSnapshot()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        state.Bindings.Add("synthetic-original", "target");
        state.Bindings.Add("synthetic-replacement", "target");
        string[] users = ["synthetic-original"];
        state.BeforeCall = operation =>
        {
            if (operation == "aliases-read") users[0] = "synthetic-replacement";
            return Task.CompletedTask;
        };
        using var budget = new SyntheticBudget();
        await NewCleanup(client, budget).CleanupAsync("c201", users, default);
        Assert.Contains("user-unlink:synthetic-original", state.Mutations);
        Assert.DoesNotContain("user-unlink:synthetic-replacement", state.Mutations);
    }

    /// <summary>輸入身份重複須在第一個 I/O 前拒絕，不能以去重方式掩蓋 caller 的不完整清理集合。</summary>
    [Fact]
    public async Task Cleanup_DuplicateUsers_RejectsBeforeIo()
    {
        var (client, state) = SyntheticLineClient.Create(("c201-main", "target"));
        using var budget = new SyntheticBudget();
        await Assert.ThrowsAsync<InvalidDataException>(() => NewCleanup(client, budget)
            .CleanupAsync("c201", ["synthetic-user", "synthetic-user"], default));
        Assert.Empty(state.Calls);
    }

    /// <summary>每案建立同一固定產品及 Channel 的 helper，不把測試替身的所有權移交給產品程式。</summary>
    private static TestTenantLineCleanup NewCleanup(ILineMessagingClient client, IChannelRateBudget budget) =>
        new(client, budget, "GoodShepherd2", "T01");

    /// <summary>只有記憶體事件與可控閘門的預算替身；測試 owner Dispose 僅標記所有權，不建立真實 SQL。</summary>
    private sealed class SyntheticBudget : IChannelRateBudget, IDisposable
    {
        /// <summary>依呼叫順序保存完整預算隔離鍵，以驗證沒有借用別的產品或 Channel 額度。</summary>
        internal List<string> Calls { get; } = [];
        /// <summary>控制合成配額拒絕，不使用 sleep 或真實時鐘。</summary>
        internal bool Allowed { get; init; } = true;
        /// <summary>測試 owner 的 Dispose 標記，用來確認產品不關閉借入依賴。</summary>
        internal bool Disposed { get; private set; }
        /// <summary>當前尚未排空的預算呼叫數；每次呼叫在 finally 回到基線。</summary>
        internal int ActiveOperations { get; private set; }
        /// <summary>由當次測試持有的單次預算閘門，忽略取消以重現不可遺棄依賴。</summary>
        internal Func<Task>? BeforeAcquire { get; set; }

        /// <summary>記錄預算隔離鍵，等待故障閘門真完成，再回傳合成額度；不排入任何背景工作。</summary>
        public async Task<bool> TryAcquireAsync(string product, string channelKey, string operation, CancellationToken ct)
        {
            Calls.Add(product + "/" + channelKey + "/" + operation);
            ActiveOperations++;
            try
            {
                if (BeforeAcquire is not null) await BeforeAcquire().ConfigureAwait(false);
                return Allowed;
            }
            finally { ActiveOperations--; }
        }

        /// <summary>僅由測試 owner 關閉記憶體替身；不接觸產品借入的其他資源。</summary>
        public void Dispose() { Disposed = true; GC.SuppressFinalize(this); }
    }

    /// <summary>只支援清理所需五個 API 的純合成代理，未宣告的呼叫一律失敗，避免測試意外外連。</summary>
    public class SyntheticLineClient : DispatchProxy, IDisposable
    {
        /// <summary>代理產生器所需的無參數建構子；本身不啟動連線或背景工作。</summary>
        public SyntheticLineClient() { }
        /// <summary>單一測試擁有的遠端 alias 合成來源，允許故障測試注入重複身份。</summary>
        internal List<RichMenuAlias> Aliases { get; } = [];
        /// <summary>合成宮格存在集合，以 provider ID 判斷刪除，不用名稱推論租戶。</summary>
        internal HashSet<string> Menus { get; } = new(StringComparer.Ordinal);
        /// <summary>合成使用者目前綁定，由當次測試獨占，不跨測試共用。</summary>
        internal Dictionary<string, string> Bindings { get; } = new(StringComparer.Ordinal);
        /// <summary>完整呼叫事件順序，供取消案例判斷最後一個已排空操作。</summary>
        internal List<string> Calls { get; } = [];
        /// <summary>只記錄已成功執行的合成副作用，避免把失敗嘗試誤當遠端變更。</summary>
        internal List<string> Mutations { get; } = [];
        /// <summary>尚未結束的合成 SDK 呼叫數；所有分支均在 finally 歸零。</summary>
        internal int ActiveOperations { get; private set; }
        /// <summary>由測試 owner 管理的借入資源 Dispose 記號。</summary>
        internal bool Disposed { get; private set; }
        /// <summary>測試可注入查詢失敗、狀態切換或有界等待閘門，不使用真實 API。</summary>
        internal Func<string, Task>? BeforeCall { get; set; }

        /// <summary>建立介面代理及其獨立狀態，所有 alias 與 menu 都由合成輸入產生。</summary>
        internal static (ILineMessagingClient Client, SyntheticLineClient State) Create(params (string Alias, string Menu)[] rows)
        {
            var client = Create<ILineMessagingClient, SyntheticLineClient>();
            var state = (SyntheticLineClient)client;
            foreach (var row in rows)
            {
                state.Aliases.Add(new RichMenuAlias { RichMenuAliasId = row.Alias, RichMenuId = row.Menu });
                state.Menus.Add(row.Menu);
            }
            return (client, state);
        }

        /// <summary>分派唯一允許的五種 SDK 操作；未知方法直接拒絕，沒有真實 client fallback。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var id = args is { Length: > 0 } ? (string)args[0]! : "";
            return targetMethod?.Name switch
            {
                nameof(ILineMessagingClient.GetRichMenuAliasListAsync) => RunAsync("aliases-read", () =>
                    new RichMenuAliasList { Aliases = Aliases.Select(row => new RichMenuAlias
                        { RichMenuAliasId = row.RichMenuAliasId, RichMenuId = row.RichMenuId }).ToList() }),
                nameof(ILineMessagingClient.GetRichMenuIdOfUserAsync) => RunAsync("user-read:" + id, () =>
                    Bindings.TryGetValue(id, out var menu) ? menu : throw NotFound()),
                nameof(ILineMessagingClient.DeleteRichMenuAliasAsync) => RunAsync("alias-delete:" + id, () =>
                    Mutate("alias-delete:" + id, () => Aliases.RemoveAll(row => row.RichMenuAliasId == id) > 0)),
                nameof(ILineMessagingClient.DeleteRichMenuAsync) => RunAsync("menu-delete:" + id, () =>
                    Mutate("menu-delete:" + id, () => Menus.Remove(id))),
                nameof(ILineMessagingClient.UnLinkRichMenuFromUserAsync) => RunAsync("user-unlink:" + id, () =>
                    Mutate("user-unlink:" + id, () => Bindings.Remove(id))),
                _ => throw new NotSupportedException("Unexpected synthetic LINE operation.")
            };
        }

        /// <summary>操作成功才記錄副作用；不存在的穩定 ID 以 SDK 的精確 404 型別模擬。</summary>
        private bool Mutate(string operation, Func<bool> mutation)
        {
            if (!mutation()) throw NotFound();
            Mutations.Add(operation);
            return true;
        }

        /// <summary>完整等待注入閘門後執行合成操作，在成功、取消與故障三種情況都排空活動計數。</summary>
        private async Task<T> RunAsync<T>(string operation, Func<T> action)
        {
            Calls.Add(operation);
            ActiveOperations++;
            try
            {
                if (BeforeCall is not null) await BeforeCall(operation).ConfigureAwait(false);
                return action();
            }
            finally { ActiveOperations--; }
        }

        /// <summary>產生不含原始回應本文的合成 404，供不存在綁定或重複清理的精確分類使用。</summary>
        private static LineResponseException NotFound() => new("Synthetic missing resource.") { StatusCode = HttpStatusCode.NotFound };

        /// <summary>代理不持有外部資源；此標記僅用來驗證產品未奪取測試 owner 的生命週期責任。</summary>
        public void Dispose() { Disposed = true; GC.SuppressFinalize(this); }
    }
}
