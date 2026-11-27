/*
 * 檔案整體說明
 * 目的與定位：F8 稽核 sidecar 的獨立離線契約測試，保護欄位映射、秘密遮罩、有效操作者與租戶驗證、整秒時間精度的冪等重送，以及同步 SDK 的取消、失敗與租約排空。
 * 主要功能與流程：只以 Moq 取代外部 IOrganizationService 與租約，執行真正 DataverseAuditLog 及共用 executor；所有前後值、主鍵及錯誤訊息均為合成 canary，不讀 User Secrets 或連接實機。
 * 使用邊界與維護：每案獨有 AuditOwner，擁有暫存 ExceptionDiagnostics 並在 DisposeAsync 後刪除目錄；不共用主代理 fixture、可變狀態或網路連線。診斷只開檔案輸出，不啟動 LINE consumer。
 * 故障與決定性斷言：錯誤身分不得 Create；空回傳主鍵、SDK 故障與內部取消必須失敗並告警且歸還；呼叫端取消不得告警。阻塞替身用有限等待與 finally 解除閘門，驗證 SDK 真正退出前租約不釋放。
 * 證據限制：併行案例驗證候選不跨租戶，兩個獨立服務共用合成索引驗證事件重送、不同事件保留及衝突不覆寫；小數秒測試注入整秒且Unspecified Kind的Dataverse讀回，確認同一事件可冪等成功並歸還租約。本檔只模擬索引與SDK完整性錯誤，不宣稱真實資料庫索引Active、F8欄位實機精度、效能或實機結果。
 */
using System.Collections.Concurrent;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Access.Tests;

/// <summary>以獨立合成服務與真實診斷 owner 驗證稽核寫入可觀察契約。</summary>
public sealed class AuditLogTests
{
    /// <summary>省略任一欄位映射、把 RecordId 當主鍵或未轉 UTC 都必須失敗。</summary>
    [Fact]
    public async Task Audit_RecordsBeforeAfterActor()
    {
        await using var owner = new AuditOwner();
        await owner.Log.WriteAsync(owner.Entry);
        Entity row = Assert.Single(owner.Created);
        Assert.Equal("new_auditentry", row.LogicalName);
        Assert.Equal(Guid.Empty, row.Id);
        Assert.Equal("Audit", row.GetAttributeValue<string>("new_name"));
        Assert.Equal("contact", row.GetAttributeValue<string>("new_entity"));
        Assert.Equal(owner.Entry.RecordId.ToString("D"), row.GetAttributeValue<string>("new_recordid"));
        Assert.Equal("name", row.GetAttributeValue<string>("new_field"));
        Assert.Equal("修改前", row.GetAttributeValue<string>("new_before"));
        Assert.Equal("修改後", row.GetAttributeValue<string>("new_after"));
        Assert.Equal(new EntityReference("contact", owner.Entry.ActorContactId), row["new_actorcontactid"]);
        Assert.Equal(new EntityReference("account", owner.Entry.TenantId), row["new_tenantid"]);
        Assert.Equal("audit-canary-001", row.GetAttributeValue<string>("new_correlationid"));
        Assert.Equal(new DateTime(2026, 11, 26, 1, 15, 30, DateTimeKind.Utc), row["new_at"]);
        Assert.Equal(DateTimeKind.Utc, row.GetAttributeValue<DateTime>("new_at").Kind);
        Assert.Equal(owner.Entry.EventId.ToString("D"), row.GetAttributeValue<string>("new_eventid"));
        Assert.Equal(11, row.Attributes.Count);
        Assert.Equal(1, owner.Acquired);
        Assert.Equal(1, owner.Released);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>刪除大小寫／分隔正規化或敏感詞遮罩，將使合成秘密流入送往 SDK 的候選。</summary>
    [Theory]
    [InlineData("token")]
    [InlineData("new_AccessToken")]
    [InlineData("new_t.o.k.e.n")]
    [InlineData("new_T-O_K.E:N")]
    [InlineData("client_SECRET")]
    [InlineData("new_s-e-c-r-e-t")]
    [InlineData("new_PASSWORD")]
    [InlineData("new_p.a.s.s.w.o.r.d")]
    [InlineData("new_Passwd")]
    [InlineData("new_pwd")]
    [InlineData("creditCard")]
    [InlineData("card_number")]
    [InlineData("new_c-a-r-d-n-o")]
    [InlineData("卡號")]
    [InlineData("信用_卡_號")]
    [InlineData("ＴＯＫＥＮ")]
    public async Task Audit_SensitiveFieldMasked(string field)
    {
        await using var owner = new AuditOwner();
        await owner.Log.WriteAsync(owner.Entry with { Field = field, Before = "syntheticBeforeCanary", After = "syntheticAfterCanary" });
        Entity row = Assert.Single(owner.Created);
        Assert.Equal("***", row["new_before"]);
        Assert.Equal("***", row["new_after"]);
        Assert.Equal(field, row["new_field"]);
    }

    /// <summary>只遮欄名不足以處理巢狀資料；混合、編碼與卡號候選都採整值遮罩，不解析或輸出其原文。</summary>
    [Theory]
    [InlineData("{\"outer\":{\"token\":\"syntheticCanary\"}}")]
    [InlineData("{\"outer\":{\"t\\u006fken\":\"syntheticCanary\"}}")]
    [InlineData("[{\"pAss-word\":\"syntheticCanary\"}]")]
    [InlineData("{\"ordinary\":\"plain\"}")]
    [InlineData("prefix {\"value\":\"syntheticCanary\"} suffix")]
    [InlineData("access_T-O-K-E-N syntheticCanary")]
    [InlineData("Bearer syntheticCanary")]
    [InlineData("password syntheticCanary")]
    [InlineData("s-e-c-r-e-t syntheticCanary")]
    [InlineData("卡-號 4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("４１１１　１１１１　１１１１　１１１１")]
    [InlineData("value=syntheticCanary")]
    [InlineData("https://example.invalid/?value=syntheticCanary")]
    [InlineData("%7B%22hidden%22%3A%22syntheticCanary%22%7D")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJ2YWx1ZSI6ImNhbmFyeSJ9.signature")]
    public async Task Audit_EmbeddedSensitiveOrStructuredValue_MaskedEntirely(string value)
    {
        await using var owner = new AuditOwner();
        await owner.Log.WriteAsync(owner.Entry with { Before = value, After = value });
        Entity row = Assert.Single(owner.Created);
        Assert.Equal("***", row["new_before"]);
        Assert.Equal("***", row["new_after"]);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>一般 null 與剛好 4000 字純文字可保存；敏感欄位即使 null 也不例外繞過固定遮罩。</summary>
    [Fact]
    public async Task Audit_NullAndMaximumPlainText_RespectStorageBoundary()
    {
        await using var owner = new AuditOwner();
        string text = new('文', 4000);
        await owner.Log.WriteAsync(owner.Entry with { Before = null, After = text });
        Entity row = Assert.Single(owner.Created);
        Assert.Null(row["new_before"]);
        Assert.Equal(text, row["new_after"]);
        await owner.Log.WriteAsync(owner.Entry with { Field = "token", Before = null, After = null });
        Assert.All(owner.Created.Skip(1), x =>
        {
            Assert.Equal("***", x["new_before"]);
            Assert.Equal("***", x["new_after"]);
        });
    }

    /// <summary>格式、正式主鍵及日期範圍必須在借用前拒絕，錯誤不回顯候選內容。</summary>
    [Theory]
    [InlineData("entity-empty")]
    [InlineData("entity-long")]
    [InlineData("entity-control")]
    [InlineData("field-empty")]
    [InlineData("field-long")]
    [InlineData("field-control")]
    [InlineData("record-empty")]
    [InlineData("event-empty")]
    [InlineData("contact-empty")]
    [InlineData("tenant-empty")]
    [InlineData("correlation-empty")]
    [InlineData("correlation-long")]
    [InlineData("correlation-control")]
    [InlineData("before-long")]
    [InlineData("after-long")]
    [InlineData("date-min")]
    [InlineData("date-utc-underflow")]
    public async Task Audit_InvalidInput_RejectedBeforeLease(string invalid)
    {
        await using var owner = new AuditOwner();
        AuditEntry entry = invalid switch
        {
            "entity-empty" => owner.Entry with { Entity = "" },
            "entity-long" => owner.Entry with { Entity = new string('a', 129) },
            "entity-control" => owner.Entry with { Entity = "bad\nsyntheticCanary" },
            "field-empty" => owner.Entry with { Field = "" },
            "field-long" => owner.Entry with { Field = new string('a', 129) },
            "field-control" => owner.Entry with { Field = "bad\nsyntheticCanary" },
            "record-empty" => owner.Entry with { RecordId = Guid.Empty },
            "event-empty" => owner.Entry with { EventId = Guid.Empty },
            "contact-empty" => owner.Entry with { ActorContactId = Guid.Empty },
            "tenant-empty" => owner.Entry with { TenantId = Guid.Empty },
            "correlation-empty" => owner.Entry with { CorrelationId = "" },
            "correlation-long" => owner.Entry with { CorrelationId = new string('a', 129) },
            "correlation-control" => owner.Entry with { CorrelationId = "bad\nsyntheticCanary" },
            "before-long" => owner.Entry with { Before = new string('a', 4001) },
            "after-long" => owner.Entry with { After = new string('a', 4001) },
            "date-min" => owner.Entry with { At = DateTimeOffset.MinValue },
            "date-utc-underflow" => owner.Entry with { At = new DateTimeOffset(1753, 1, 1, 0, 0, 0, TimeSpan.FromHours(8)) },
            _ => throw new InvalidOperationException("Unknown test case.")
        };
        ArgumentException failure = await Assert.ThrowsAnyAsync<ArgumentException>(() => owner.Log.WriteAsync(entry));
        Assert.DoesNotContain("syntheticCanary", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, owner.Acquired);
        Assert.Empty(owner.Created);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>偽造 GUID、停用主體、錯租戶或錯 choice 型別均不得寫入；必須確定歸還唯一租約。</summary>
    [Theory]
    [InlineData("inactive-contact")]
    [InlineData("missing-contact-state")]
    [InlineData("wrong-parent")]
    [InlineData("contact-parent")]
    [InlineData("wrong-contact-id")]
    [InlineData("missing-contact")]
    [InlineData("inactive-tenant")]
    [InlineData("missing-tenant-state")]
    [InlineData("missing-tenant-flag")]
    [InlineData("false-tenant")]
    [InlineData("boolean-tenant-flag")]
    [InlineData("wrong-tenant-id")]
    public async Task Audit_InvalidActor_NeverCreatesAndDrains(string invalid)
    {
        await using var owner = new AuditOwner();
        Entity contact = owner.Identities[("contact", owner.Entry.ActorContactId)];
        Entity tenant = owner.Identities[("account", owner.Entry.TenantId)];
        switch (invalid)
        {
            case "inactive-contact": contact["statecode"] = new OptionSetValue(1); break;
            case "missing-contact-state": contact.Attributes.Remove("statecode"); break;
            case "wrong-parent": contact["parentcustomerid"] = new EntityReference("account", Guid.NewGuid()); break;
            case "contact-parent": contact["parentcustomerid"] = new EntityReference("contact", owner.Entry.TenantId); break;
            case "wrong-contact-id": contact.Id = Guid.NewGuid(); break;
            case "missing-contact": owner.Identities.TryRemove(("contact", owner.Entry.ActorContactId), out _); break;
            case "inactive-tenant": tenant["statecode"] = new OptionSetValue(1); break;
            case "missing-tenant-state": tenant.Attributes.Remove("statecode"); break;
            case "missing-tenant-flag": tenant.Attributes.Remove("new_istenant"); break;
            case "false-tenant": tenant["new_istenant"] = new OptionSetValue(100000000); break;
            case "boolean-tenant-flag": tenant["new_istenant"] = true; break;
            case "wrong-tenant-id": tenant.Id = Guid.NewGuid(); break;
            default: throw new InvalidOperationException("Unknown test case.");
        }
        await Assert.ThrowsAnyAsync<Exception>(() => owner.Log.WriteAsync(owner.Entry));
        Assert.Empty(owner.Created);
        Assert.Equal(1, owner.Acquired);
        Assert.Equal(1, owner.Released);
        Assert.Equal(0, owner.Outstanding);
        Assert.DoesNotContain(owner.Entry.ActorContactId.ToString("D"), owner.DiagnosticText(), StringComparison.Ordinal);
    }

    /// <summary>預取消不得借用 SDK 或告警，防止取消操作仍寫入紀錄。</summary>
    [Fact]
    public async Task Audit_CallerCanceledBeforeAcquire_NoLeaseOrAlert()
    {
        await using var owner = new AuditOwner();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Task task = owner.Log.WriteAsync(owner.Entry, cancel.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Equal(0, owner.Acquired);
        Assert.Empty(owner.Created);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>SDK 進行中取消不能讓租約先歸還；以獨立受控 thread 而非產品背景工作注入阻塞。</summary>
    [Fact]
    public async Task Audit_CancelDuringSdk_WaitsForSdkBeforeLeaseReturn()
    {
        await using var owner = new AuditOwner();
        using var cancel = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.OnCreate = _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic gate timeout.");
            Assert.Equal(0, owner.Released);
            return Guid.NewGuid();
        };
        var worker = new Thread(() =>
        {
            try { owner.Log.WriteAsync(owner.Entry, cancel.Token).GetAwaiter().GetResult(); completion.SetResult(null); }
            catch (Exception failure) { completion.SetResult(failure); }
        }) { IsBackground = true };
        worker.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            cancel.Cancel();
            Assert.Equal(1, owner.Outstanding);
            Assert.Equal(0, owner.Released);
            Assert.Equal(0, owner.Faulted);
            Assert.False(completion.Task.IsCompleted);
            release.Set();
            Assert.IsAssignableFrom<OperationCanceledException>(await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        }
        Assert.Equal(1, owner.Released);
        Assert.Equal(1, owner.Faulted);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>傳輸真正失敗保留同一例外並記安全 metadata；不能讓診斷複製合成秘密。</summary>
    [Fact]
    public async Task Audit_SdkFailure_PreservesExceptionReportsAndDrains()
    {
        await using var owner = new AuditOwner();
        var failure = new InvalidOperationException("password=syntheticSdkCanary");
        owner.OnCreate = _ => throw failure;
        Exception actual = await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Log.WriteAsync(owner.Entry));
        Assert.Same(failure, actual);
        Assert.Equal(1, owner.Faulted);
        Assert.Equal(1, owner.Released);
        Assert.Equal(0, owner.Outstanding);
        string text = owner.DiagnosticText();
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("syntheticSdkCanary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("修改前", text, StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(Path.Combine(owner.DirectoryPath, "Exception.log")));
    }

    /// <summary>沒有 caller 取消的內部 OCE 必須是 faulted task 並告警，不能被錯當正常取消。</summary>
    [Fact]
    public async Task Audit_InternalCancellation_IsFaultedAndReported()
    {
        await using var owner = new AuditOwner();
        var failure = new OperationCanceledException("syntheticTransportCanary");
        owner.OnCreate = _ => throw failure;
        Task task = owner.Log.WriteAsync(owner.Entry);
        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() => task));
        Assert.True(task.IsFaulted);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal(1, owner.Faulted);
        Assert.Contains("OperationCanceledException", owner.DiagnosticText(), StringComparison.Ordinal);
        Assert.DoesNotContain("syntheticTransportCanary", owner.DiagnosticText(), StringComparison.Ordinal);
    }

    /// <summary>SDK 回空主鍵不是成功；錯誤必須經共用診斷並逐次歸還，不能重送製造重複稽核。</summary>
    [Fact]
    public async Task Audit_EmptyCreateId_IsReportedFailure()
    {
        await using var owner = new AuditOwner();
        owner.OnCreate = _ => Guid.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Log.WriteAsync(owner.Entry));
        Assert.Single(owner.Created);
        Assert.Equal(1, owner.Faulted);
        Assert.Equal(1, owner.Released);
        Assert.Equal(0, owner.Outstanding);
        Assert.Contains("InvalidOperationException", owner.DiagnosticText(), StringComparison.Ordinal);
    }

    /// <summary>反覆 A／B 租戶併行不得交叉覆寫候選；每次重新驗身分且所有 lease 回到零基線。</summary>
    [Fact]
    public async Task Audit_ConcurrentActors_DetachedCandidatesAndCompleteDrain()
    {
        await using var owner = new AuditOwner();
        AuditEntry second = owner.AddActor(Guid.NewGuid(), Guid.NewGuid()) with { Before = "乙前", After = "乙後" };
        Parallel.For(0, 128, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            AuditEntry entry = (i % 2 == 0 ? owner.Entry : second) with
            {
                RecordId = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                CorrelationId = "audit-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            owner.Log.WriteAsync(entry).GetAwaiter().GetResult();
        });
        Assert.Equal(128, owner.Created.Count);
        Assert.Equal(128, owner.Created.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(128, owner.Created.Select(x => x.GetAttributeValue<string>("new_recordid")).Distinct(StringComparer.Ordinal).Count());
        Assert.All(owner.Created, row =>
        {
            bool first = row.GetAttributeValue<EntityReference>("new_actorcontactid").Id == owner.Entry.ActorContactId;
            Assert.Equal(first ? owner.Entry.TenantId : second.TenantId, row.GetAttributeValue<EntityReference>("new_tenantid").Id);
            Assert.Equal(first ? "修改前" : "乙前", row["new_before"]);
            Assert.Equal(first ? "修改後" : "乙後", row["new_after"]);
        });
        Assert.Equal(128, owner.Acquired);
        Assert.Equal(128, owner.Released);
        Assert.Equal(0, owner.Faulted);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>兩個獨立服務共享合成唯一資料庫，32次同事件重送只保留一個正式ID；無應用鎖替代產品唯一鍵。</summary>
    [Fact]
    public async Task Audit_TwoInstances_RepeatedEventCreatesOneCanonicalRecord()
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        DataverseAuditLog other = owner.CreateLog();
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            (i % 2 == 0 ? owner.Log : other).WriteAsync(owner.Entry).GetAwaiter().GetResult());
        Entity row = Assert.Single(owner.Stored.Values);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(owner.Entry.EventId.ToString("D"), row["new_eventid"]);
        Assert.Equal(32, owner.Acquired);
        Assert.Equal(32, owner.Released);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal(0, owner.Faulted);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>不同事件即使其餘欄位完全相同都保留；相同事件在不同租戶也有獨立唯一鍵。</summary>
    [Fact]
    public async Task Audit_DifferentEventsOrTenants_PreserveLegitimateRows()
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        await owner.Log.WriteAsync(owner.Entry);
        await owner.Log.WriteAsync(owner.Entry with { EventId = Guid.NewGuid() });
        AuditEntry secondTenant = owner.AddActor(Guid.NewGuid(), Guid.NewGuid()) with { EventId = owner.Entry.EventId };
        await owner.Log.WriteAsync(secondTenant);
        Assert.Equal(3, owner.Stored.Count);
        Assert.Equal(3, owner.Stored.Values.Select(x => x.Id).Distinct().Count());
        Assert.Equal(0, owner.Outstanding);
    }

    /// <summary>已存在事件內容不同必須拒絕並告警，不能以Upsert改寫既有稽核證據。</summary>
    [Fact]
    public async Task Audit_RepeatedEventWithDifferentContent_RejectsWithoutMutation()
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        await owner.Log.WriteAsync(owner.Entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Log.WriteAsync(owner.Entry with { After = "其他內容" }));
        Assert.Equal("修改後", Assert.Single(owner.Stored.Values)["new_after"]);
        Assert.Equal(0, owner.Outstanding);
        Assert.Contains("InvalidOperationException", owner.DiagnosticText(), StringComparison.Ordinal);
        Assert.DoesNotContain("其他內容", owner.DiagnosticText(), StringComparison.Ordinal);
    }

    /// <summary>只接受兩種已知完整性衝突碼；精確讀回失敗或不完整時不能當作重送成功。</summary>
    [Theory]
    [InlineData("zero")]
    [InlineData("two")]
    [InlineData("more")]
    [InlineData("entity")]
    [InlineData("content-entity")]
    [InlineData("missing-event")]
    [InlineData("typed-event")]
    [InlineData("id")]
    [InlineData("tenant")]
    [InlineData("event")]
    [InlineData("record")]
    [InlineData("actor")]
    [InlineData("field")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("correlation")]
    [InlineData("at")]
    [InlineData("name")]
    public async Task Audit_InvalidConflictReadback_FailsClosedAndDrains(string invalid)
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        await owner.Log.WriteAsync(owner.Entry);
        Entity copy = AuditOwner.Copy(Assert.Single(owner.Stored.Values));
        EntityCollection result = new([copy]);
        switch (invalid)
        {
            case "zero": result.Entities.Clear(); break;
            case "two": result.Entities.Add(AuditOwner.Copy(copy)); break;
            case "more": result.MoreRecords = true; break;
            case "entity": copy.LogicalName = "contact"; break;
            case "content-entity": copy["new_entity"] = "account"; break;
            case "missing-event": copy.Attributes.Remove("new_eventid"); break;
            case "typed-event": copy["new_eventid"] = 1; break;
            case "id": copy.Id = Guid.Empty; break;
            case "tenant": copy["new_tenantid"] = new EntityReference("account", Guid.NewGuid()); break;
            case "event": copy["new_eventid"] = Guid.NewGuid().ToString("D"); break;
            case "record": copy["new_recordid"] = Guid.NewGuid().ToString("D"); break;
            case "actor": copy["new_actorcontactid"] = new EntityReference("account", owner.Entry.ActorContactId); break;
            case "field": copy["new_field"] = "other"; break;
            case "before": copy["new_before"] = "other"; break;
            case "after": copy["new_after"] = "other"; break;
            case "correlation": copy["new_correlationid"] = "other"; break;
            case "at": copy["new_at"] = owner.Entry.At.UtcDateTime.AddSeconds(1); break;
            case "name": copy["new_name"] = "other"; break;
            default: throw new InvalidOperationException("Unknown test case.");
        }
        owner.OnQuery = _ => result;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Log.WriteAsync(owner.Entry));
        Assert.Single(owner.Stored);
        Assert.Equal(2, owner.Acquired);
        Assert.Equal(2, owner.Released);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal(1, owner.Faulted);
    }

    /// <summary>非唯一性fault不能查回或重試；保留真正例外及一次租約釋放。</summary>
    [Fact]
    public async Task Audit_OtherFault_DoesNotReadBackOrRetry()
    {
        await using var owner = new AuditOwner();
        var failure = new FaultException<OrganizationServiceFault>(new() { ErrorCode = unchecked((int)0x80040216) });
        owner.OnCreate = _ => throw failure;
        Assert.Same(failure, await Assert.ThrowsAsync<FaultException<OrganizationServiceFault>>(() => owner.Log.WriteAsync(owner.Entry)));
        Assert.Single(owner.Created);
        Assert.Equal(0, owner.Queries);
        Assert.Equal(0, owner.Outstanding);
    }

    /// <summary>官方兩種完整性錯誤都必須精確核對；SDK讀回Unspecified仍表示UTC，不套本地時區。</summary>
    [Theory]
    [InlineData(unchecked((int)0x80040237))]
    [InlineData(unchecked((int)0x80060892))]
    public async Task Audit_KnownIntegrityFault_ReadsExactExistingEvent(int code)
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        await owner.Log.WriteAsync(owner.Entry);
        Entity row = AuditOwner.Copy(Assert.Single(owner.Stored.Values));
        row["new_at"] = DateTime.SpecifyKind(owner.Entry.At.UtcDateTime, DateTimeKind.Unspecified);
        owner.OnCreate = _ => throw new FaultException<OrganizationServiceFault>(new() { ErrorCode = code });
        owner.OnQuery = _ => new EntityCollection([row]);
        await owner.Log.WriteAsync(owner.Entry);
        Assert.Equal(1, owner.Queries);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>
    /// 注入1 tick、毫秒尾數及秒尾小數的事件時間，再把合成Dataverse既有列改成整秒Unspecified讀回，重送必須視為同一持久化事實。
    /// 先以唯一鍵Create，再注入明確duplicate fault與精確查回；斷言兩次候選都已向下量化為UTC整秒、唯一正式列未被覆寫、查回僅一次且租約全部排空。
    /// 本測試只證明離線精度相容契約，不宣稱F8的new_at欄位已在實機部署或已實機確認精度。
    /// </summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(1234567L)]
    [InlineData(9999999L)]
    public async Task Audit_SecondPrecisionReadback_RetryWithSubsecondEventTimeSucceeds(long fractionalTicks)
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        AuditEntry entry = owner.Entry with { At = owner.Entry.At.AddTicks(fractionalTicks) };
        DateTime utc = entry.At.UtcDateTime;
        DateTime expectedAt = new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        await owner.Log.WriteAsync(entry);
        Entity persisted = Assert.Single(owner.Stored.Values);
        // Dataverse SDK 對 UserLocal 時間可能以 Unspecified Kind 回傳；其 ticks 依 SDK 契約代表 UTC 值。
        persisted["new_at"] = DateTime.SpecifyKind(expectedAt, DateTimeKind.Unspecified);

        // 相同 tenant＋EventId 使第二次 Create 走唯一鍵衝突，再比較平台實際可表達的整秒時間。
        await owner.Log.WriteAsync(entry);

        Assert.Equal(2, owner.Created.Count);
        Assert.All(owner.Created, candidate =>
        {
            DateTime actualAt = candidate.GetAttributeValue<DateTime>("new_at");
            Assert.Equal(expectedAt, actualAt);
            Assert.Equal(DateTimeKind.Utc, actualAt.Kind);
        });
        Assert.Single(owner.Stored);
        Assert.Equal(1, owner.Queries);
        Assert.Equal(2, owner.Acquired);
        Assert.Equal(2, owner.Released);
        Assert.Equal(0, owner.Faulted);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal(string.Empty, owner.DiagnosticText());
    }

    /// <summary>讀回SDK故障不得覆寫或換鍵重送；原錯保持且租約排空，已提交列保持不變。</summary>
    [Fact]
    public async Task Audit_ConflictReadbackFailure_PreservesFaultAndDrains()
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        await owner.Log.WriteAsync(owner.Entry);
        var failure = new TimeoutException("syntheticReadbackCanary");
        owner.OnQuery = _ => throw failure;
        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => owner.Log.WriteAsync(owner.Entry)));
        Assert.Single(owner.Stored);
        Assert.Equal(2, owner.Created.Count);
        Assert.Equal(1, owner.Queries);
        Assert.Equal(0, owner.Outstanding);
        Assert.DoesNotContain("syntheticReadbackCanary", owner.DiagnosticText(), StringComparison.Ordinal);
    }

    /// <summary>讀回期間caller取消須等待同步SDK返回，隨後拒絕發布並唯一歸還，不告警正常取消。</summary>
    [Fact]
    public async Task Audit_CancelDuringConflictReadback_DrainsWithoutAlert()
    {
        await using var owner = new AuditOwner();
        using var cancel = new CancellationTokenSource();
        owner.EnableUniqueStore();
        await owner.Log.WriteAsync(owner.Entry);
        owner.OnQuery = _ =>
        {
            cancel.Cancel();
            Assert.Equal(1, owner.Outstanding);
            return new EntityCollection([AuditOwner.Copy(Assert.Single(owner.Stored.Values))]);
        };
        Task task = owner.Log.WriteAsync(owner.Entry, cancel.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Equal(0, owner.Outstanding);
        Assert.Equal("", owner.DiagnosticText());
    }

    /// <summary>Dataverse把空文字視為null；候選須先正規化，避免同事實讀回後因空值形狀不同被誤判衝突。</summary>
    [Fact]
    public async Task Audit_EmptyValues_AreCanonicalNullBeforePersistence()
    {
        await using var owner = new AuditOwner();
        owner.EnableUniqueStore();
        AuditEntry entry = owner.Entry with { Before = "", After = "" };
        await owner.Log.WriteAsync(entry);
        Entity row = Assert.Single(owner.Stored.Values);
        Assert.Null(row["new_before"]);
        Assert.Null(row["new_after"]);
        row.Attributes.Remove("new_before");
        row.Attributes.Remove("new_after");
        await owner.CreateLog().WriteAsync(entry);
        Assert.Single(owner.Stored);
        Assert.Equal(0, owner.Outstanding);
    }

    /// <summary>測試唯一資源 owner：短命 mock 租約與真實本地診斷，資料及回呼只保留到單案 Dispose。</summary>
    private sealed class AuditOwner : IAsyncDisposable
    {
        private int _acquired;
        private int _released;
        private int _faulted;
        private readonly ExceptionDiagnostics _diagnostics;
        private readonly Mock<IOrganizationService> _service = new(MockBehavior.Strict);

        /// <summary>每次 Create 真正收到的安全候選，僅為本案斷言保留，不是產品快取。</summary>
        internal ConcurrentQueue<Entity> Created { get; } = new();
        /// <summary>合成 contact／account 正式資料列；外部操作僅能透過 SDK 替身重新讀取。</summary>
        internal ConcurrentDictionary<(string Entity, Guid Id), Entity> Identities { get; } = new();
        /// <summary>注入單次 SDK 成功、故障或阻塞；不涉及外部資源。</summary>
        internal Func<Entity, Guid> OnCreate { get; set; } = _ => Guid.NewGuid();
        /// <summary>只有本案使用的合成資料庫，原子TryAdd模擬資料庫唯一鍵；清理由Dispose負責。</summary>
        internal ConcurrentDictionary<(Guid Tenant, string Event), Entity> Stored { get; } = new();
        /// <summary>合成精確查詢故障注入；預設空結果，不存取外部資料。</summary>
        internal Func<QueryExpression, EntityCollection> OnQuery { get; set; } = _ => new EntityCollection();
        private int _queries;
        /// <summary>查回次數供斷言非衝突錯誤沒有隱性查詢。</summary>
        internal int Queries => Volatile.Read(ref _queries);
        /// <summary>真實被測稽核服務，不與其他測試共享依賴。</summary>
        internal DataverseAuditLog Log { get; }
        /// <summary>主案例純值，全部為合成資料。</summary>
        internal AuditEntry Entry { get; }
        /// <summary>唯一診斷目錄，不含使用者輸入並於診斷排空後刪除。</summary>
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "SpeechMessageAudit-" + Guid.NewGuid().ToString("N"));
        /// <summary>已借用租約的原子計數。</summary>
        internal int Acquired => Volatile.Read(ref _acquired);
        /// <summary>已確定歸還租約的原子計數。</summary>
        internal int Released => Volatile.Read(ref _released);
        /// <summary>已被標記不可重用租約的原子計數。</summary>
        internal int Faulted => Volatile.Read(ref _faulted);
        /// <summary>已停止操作後必須歸零的租約數。</summary>
        internal int Outstanding => Acquired - Released;

        /// <summary>建立本案診斷及精確 SDK 邊界；只替代網路，實際驗證與清理由產品執行。</summary>
        internal AuditOwner()
        {
            _diagnostics = new ExceptionDiagnostics(DirectoryPath, outputOptions: new ExceptionOutputOptions(true, false));
            Entry = AddActor(Guid.NewGuid(), Guid.NewGuid());
            _service.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns((string entity, Guid id, ColumnSet columns) =>
                {
                    Assert.False(columns.AllColumns);
                    Assert.Equal(entity == "contact" ? ["parentcustomerid", "statecode"] : ["statecode", "new_istenant"], columns.Columns.ToArray());
                    return Identities.TryGetValue((entity, id), out Entity? row) ? row : null!;
                });
            _service.Setup(x => x.Create(It.IsAny<Entity>())).Returns((Entity entity) =>
            {
                Created.Enqueue(entity);
                return OnCreate(entity);
            });
            _service.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                Interlocked.Increment(ref _queries);
                return OnQuery(Assert.IsType<QueryExpression>(query));
            });
            Log = new DataverseAuditLog(Acquire, _diagnostics, "GoodShepherd2");
        }

        /// <summary>建立 active 正式關係與 choice 租戶旗標，不能把任意 GUID 當有效身分。</summary>
        internal AuditEntry AddActor(Guid contactId, Guid tenantId)
        {
            Identities[("contact", contactId)] = new Entity("contact", contactId)
            {
                ["parentcustomerid"] = new EntityReference("account", tenantId),
                ["statecode"] = new OptionSetValue(0)
            };
            Identities[("account", tenantId)] = new Entity("account", tenantId)
            {
                ["new_istenant"] = new OptionSetValue(100000001),
                ["statecode"] = new OptionSetValue(0)
            };
            return new AuditEntry("contact", Guid.NewGuid(), "name", "修改前", "修改後", contactId, tenantId,
                "audit-canary-001", new DateTimeOffset(2026, 11, 26, 9, 15, 30, TimeSpan.FromHours(8))) { EventId = Guid.NewGuid() };
        }

        /// <summary>建立依賴相同而無共享產品狀態的第二服務，讓跨實例測試不依賴本機產品鎖。</summary>
        internal DataverseAuditLog CreateLog() => new(Acquire, _diagnostics, "GoodShepherd2");

        /// <summary>複製純值與lookup，測試讀回不能改動已提交合成列。</summary>
        internal static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var item in row.Attributes)
                copy[item.Key] = item.Value is EntityReference reference ? new EntityReference(reference.LogicalName, reference.Id) : item.Value;
            return copy;
        }

        /// <summary>以完整租戶／事件tuple模擬索引；回讀驗證精確查詢及有界投影，無Update入口。</summary>
        internal void EnableUniqueStore()
        {
            OnCreate = candidate =>
            {
                Entity row = Copy(candidate);
                row.Id = Guid.NewGuid();
                var key = (row.GetAttributeValue<EntityReference>("new_tenantid").Id, row.GetAttributeValue<string>("new_eventid") ?? "");
                if (!Stored.TryAdd(key, row))
                    throw new FaultException<OrganizationServiceFault>(new() { ErrorCode = unchecked((int)0x80040237) });
                return row.Id;
            };
            OnQuery = query =>
            {
                Assert.Equal("new_auditentry", query.EntityName);
                Assert.Equal(2, query.TopCount);
                Assert.False(query.ColumnSet.AllColumns);
                Assert.Equal(11, query.ColumnSet.Columns.Count);
                Assert.Equal(LogicalOperator.And, query.Criteria.FilterOperator);
                Assert.Empty(query.LinkEntities);
                Assert.Equal(2, query.Criteria.Conditions.Count);
                var tenant = Assert.Single(query.Criteria.Conditions, x => x.AttributeName == "new_tenantid");
                var eventId = Assert.Single(query.Criteria.Conditions, x => x.AttributeName == "new_eventid");
                Assert.Equal(ConditionOperator.Equal, tenant.Operator);
                Assert.Equal(ConditionOperator.Equal, eventId.Operator);
                var key = ((Guid)Assert.Single(tenant.Values), (string)Assert.Single(eventId.Values));
                return Stored.TryGetValue(key, out Entity? row) ? new EntityCollection([Copy(row)]) : new EntityCollection();
            };
        }

        /// <summary>每操作建立獨立 mock 租約；標記及清理不能在 SDK 回呼結束前被呼叫。</summary>
        private IAccessDataverseLease Acquire(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _acquired);
            var lease = new Mock<IAccessDataverseLease>(MockBehavior.Strict);
            lease.SetupGet(x => x.Service).Returns(_service.Object);
            lease.Setup(x => x.MarkFaulted()).Callback(() => Interlocked.Increment(ref _faulted));
            lease.Setup(x => x.Dispose()).Callback(() => Interlocked.Increment(ref _released));
            return lease.Object;
        }

        /// <summary>只供斷言安全 metadata，不向測試輸出複製任何合成原始候選。</summary>
        internal string DiagnosticText()
        {
            string path = Path.Combine(DirectoryPath, "Exception.log");
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        /// <summary>先排空診斷，再清除依賴及檔案；finally 確保失敗案例亦不遺留暫存目錄。</summary>
        public async ValueTask DisposeAsync()
        {
            try { await _diagnostics.DisposeAsync(); }
            finally
            {
                _service.Invocations.Clear();
                Created.Clear();
                Identities.Clear();
                Stored.Clear();
                if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
