/*
 * 檔案整體說明
 * 目的與定位：F8 稽核 sidecar 的獨立離線契約測試，保護欄位映射、秘密遮罩、有效操作者與租戶驗證，以及同步 SDK 的取消、失敗與租約排空。
 * 主要功能與流程：只以 Moq 取代外部 IOrganizationService 與租約，執行真正 DataverseAuditLog 及共用 executor；所有前後值、主鍵及錯誤訊息均為合成 canary，不讀 User Secrets 或連接實機。
 * 使用邊界與維護：每案獨有 AuditOwner，擁有暫存 ExceptionDiagnostics 並在 DisposeAsync 後刪除目錄；不共用主代理 fixture、可變狀態或網路連線。診斷只開檔案輸出，不啟動 LINE consumer。
 * 故障與決定性斷言：錯誤身分不得 Create；空回傳主鍵、SDK 故障與內部取消必須失敗並告警且歸還；呼叫端取消不得告警。阻塞替身用有限等待與 finally 解除閘門，驗證 SDK 真正退出前租約不釋放。
 * 證據限制：併行案例驗證同一服務不混用候選、實體及租戶，並以取得／釋放／fault 計數證明操作結束回到零；不宣稱真實資料庫交易、效能或實機結果。
 */
using System.Collections.Concurrent;
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
        Assert.Equal(10, row.Attributes.Count);
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
                "audit-canary-001", new DateTimeOffset(2026, 11, 26, 9, 15, 30, TimeSpan.FromHours(8)));
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
                if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
