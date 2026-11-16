/*
 * 檔案整體說明
 * 目的與定位：保護 P5-09 會議／個人 QR 的 Core 雙宿主入列契約，不在沙箱繪圖。
 * 主要流程：以合成 SDK 呼叫真正服務及 D365OutboundEnqueueStore，驗五欄 schema=2、
 * kind 整數 meeting=0／personal=1（事件 purpose 仍用字串，不擴張共用純數值契約）、
 * 真待辦 ID、資料庫替代鍵衝突讀回、來源歸屬、完整分頁、故障與並行 A／B 隔離。
 * SDK 只模擬正式 ID、讀取及原子唯一鍵；不是實機 D365、圖片發布或跨程序驗收證據。
 * 測試的固定事件向量由獨立 Python 長度標記／SHA256／Guid little-endian 編碼取得，
 * 不以產品函式生成期望。相同姓名不同正式 ID 必須保留，重複出席 ID 必須整輪拒絕。
 * 使用邊界與維護：各案 using 擁有合成 SDK 並清空來源、待辦、callback 與故障參照；
 * 並行測試等待全部工作排空才 Dispose，不留 timer、訂閱、連線或跨測試靜態狀態。
 * active 計數證明同步 SDK 離場，借用者不得 Dispose SDK；取消源也由測試確定釋放。
 * 所有來源皆為合成值，不讀 token、秘密、舊敏感原始碼或外部服務。
 */
using System.ServiceModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.QrCodes;

/// <summary>以實際產品協調流程及合成 SDK 保護 QR 入列與失敗前零發布邊界。</summary>
public sealed class QrImageTaskTests
{
    /// <summary>核定具名測試；移除入列、改變五欄或以來源 ID 冒充待辦 ID 都必須失敗。</summary>
    [Theory]
    [InlineData(false, 0, 38, "5c082079-6246-8f58-ae62-e31499fc25bb")]
    [InlineData(true, 1, 1, "2fd533a1-6bed-8d50-b420-a365e728a16f")]
    public void QrCodes_ImageNeeded_EnqueuesTaskOnce(bool personal, int kind, int imageCount, string source)
    {
        using var sdk = new Sdk();
        var first = Enqueue(sdk, personal);
        Assert.Equal(first, Enqueue(sdk, personal));
        var row = Assert.Single(sdk.Tasks).Value;
        Assert.Equal(row.Id, Assert.Single(first));
        Assert.NotEqual(sdk.RecordId, row.Id);
        Assert.Equal(2, sdk.Creates);
        Assert.Equal(1, sdk.Conflicts);
        Assert.Equal("new_outboundtask", row.LogicalName);
        Assert.Equal(100000001, row.GetAttributeValue<OptionSetValue>("new_type").Value);
        Assert.Equal(sdk.ChurchId, row.GetAttributeValue<EntityReference>("new_accountid").Id);
        Assert.Equal(personal ? sdk.RecordId : (Guid?)null, row.GetAttributeValue<EntityReference>("new_contactid")?.Id);
        var payload = JObject.Parse(row.GetAttributeValue<string>("new_payload"));
        Assert.Equal(5, payload.Count);
        Assert.Equal(2, (int)payload["schema"]!);
        Assert.Equal(JTokenType.Integer, payload["kind"]!.Type);
        Assert.Equal(kind, (int)payload["kind"]!);
        Assert.Equal(sdk.RecordId, (Guid)payload["recordId"]!);
        Assert.Equal(1, (int)payload["contentVersion"]!);
        Assert.Equal(imageCount, (int)payload["imageCount"]!);
        Assert.Equal(sdk.ChurchId.ToString("N") + "-100000001-" + Guid.Parse(source).ToString("N"),
            row.GetAttributeValue<string>("new_idempotencykey"));
        Assert.Throws<NotSupportedException>(() => ((IList<Guid>)first)[0] = Guid.NewGuid());
        Assert.Equal(0, sdk.Active);
        Assert.Equal(0, sdk.Disposals);
    }

    /// <summary>直接呼叫正式入口，不透過反射、測試版產品邏輯或條件編譯替代服務。</summary>
    private static IReadOnlyList<Guid> Enqueue(Sdk sdk, bool personal) => personal
        ? QrImageTaskService.EnqueuePersonal(sdk, sdk.RecordId)
        : QrImageTaskService.EnqueueMeeting(sdk, sdk.RecordId);

    /// <summary>權威 contact 缺教會、錯表、空鍵、替代鍵或錯來源身分都不得產生待辦。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-parent")]
    [InlineData("empty-parent")]
    [InlineData("alternate-parent")]
    [InlineData("wrong-id")]
    [InlineData("wrong-table")]
    [InlineData("wrong-primary")]
    [InlineData("null-source")]
    public void Personal_InvalidAuthoritativeSource_RejectsBeforeWrite(string fault)
    {
        using var sdk = new Sdk();
        var row = sdk.Sources[("contact", sdk.RecordId)];
        switch (fault)
        {
            case "missing": row.Attributes.Remove("parentcustomerid"); break;
            case "wrong-parent": row["parentcustomerid"] = new EntityReference("contact", sdk.ChurchId); break;
            case "empty-parent": row["parentcustomerid"] = new EntityReference("account", Guid.Empty); break;
            case "alternate-parent":
                row.GetAttributeValue<EntityReference>("parentcustomerid").KeyAttributes.Add("accountnumber", "synthetic");
                break;
            case "wrong-id": row.Id = Guid.NewGuid(); break;
            case "wrong-table": row.LogicalName = "account"; break;
            case "wrong-primary": row["contactid"] = Guid.NewGuid(); break;
            case "null-source": sdk.Read = (_, _) => null!; break;
            default: throw new InvalidOperationException();
        }
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, true));
        Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates); Assert.Equal(0, sdk.Active);
    }

    /// <summary>會議本體讀回錯誤必須在分頁前拒絕，不能用出席反推一個不存在的來源。</summary>
    [Fact]
    public void Meeting_WrongSource_RejectsBeforeAttendance()
    {
        using var sdk = new Sdk();
        sdk.Sources[("new_meeting_statistics", sdk.RecordId)].Id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, false));
        Assert.Equal(0, sdk.PageReads); Assert.Equal(0, sdk.Creates);
    }

    /// <summary>缺正式關聯、重複列、多教會及零出席為核定拒絕差異，不猜測教會或發布前頁。</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("wrong-table")]
    [InlineData("empty-id")]
    [InlineData("wrong-primary")]
    [InlineData("missing-contact")]
    [InlineData("alternate-contact")]
    [InlineData("wrong-contact-table")]
    [InlineData("missing-meeting")]
    [InlineData("wrong-meeting")]
    [InlineData("alternate-meeting")]
    [InlineData("multiple-churches")]
    [InlineData("missing-church")]
    public void Meeting_InvalidAttendance_RejectsWholeCandidate(string fault)
    {
        using var sdk = new Sdk();
        var row = sdk.Attendance();
        var rows = new EntityCollection(new[] { row });
        switch (fault)
        {
            case "empty": rows.Entities.Clear(); break;
            case "duplicate": rows.Entities.Add(row); break;
            case "wrong-table": row.LogicalName = "contact"; break;
            case "empty-id": row.Id = Guid.Empty; break;
            case "wrong-primary": row["new_present_recordid"] = Guid.NewGuid(); break;
            case "missing-contact": row.Attributes.Remove("new_contact_new_present_record"); break;
            case "alternate-contact":
                row.GetAttributeValue<EntityReference>("new_contact_new_present_record").KeyAttributes.Add("name", "synthetic");
                break;
            case "wrong-contact-table": row["new_contact_new_present_record"] = new EntityReference("account", sdk.RecordId); break;
            case "missing-meeting": row.Attributes.Remove("new_meeting_statistics_new_present_re"); break;
            case "wrong-meeting": row["new_meeting_statistics_new_present_re"] = new EntityReference("new_meeting_statistics", Guid.NewGuid()); break;
            case "alternate-meeting":
                row.GetAttributeValue<EntityReference>("new_meeting_statistics_new_present_re").KeyAttributes.Add("name", "synthetic");
                break;
            case "multiple-churches":
                var other = Guid.NewGuid();
                sdk.Sources.Add(("contact", other), new Entity("contact", other)
                    { ["parentcustomerid"] = new EntityReference("account", Guid.NewGuid()), ["fullname"] = "同名" });
                rows.Entities.Add(sdk.Attendance(contact: other));
                break;
            case "missing-church": sdk.Sources[("contact", sdk.RecordId)].Attributes.Remove("parentcustomerid"); break;
            default: throw new InvalidOperationException();
        }
        sdk.Pages = _ => rows;
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, false));
        Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates); Assert.Equal(0, sdk.Active);
    }

    /// <summary>同名不同正式會員及同會員不同出席列都有效；每頁 cookie 必須原樣送續頁。</summary>
    [Fact]
    public void Meeting_SameNameAndRepeatedContactAcrossPages_PreservesRows()
    {
        using var sdk = new Sdk();
        var other = Guid.NewGuid();
        sdk.Sources.Add(("contact", other), new Entity("contact", other)
            { ["parentcustomerid"] = new EntityReference("account", sdk.ChurchId), ["fullname"] = "同名" });
        sdk.Pages = query =>
        {
            if (query.PageInfo.PageNumber == 1)
            {
                Assert.Null(query.PageInfo.PagingCookie);
                return new EntityCollection(new[] { sdk.Attendance(), sdk.Attendance(contact: other) })
                    { MoreRecords = true, PagingCookie = "cookie-1" };
            }
            Assert.Equal(2, query.PageInfo.PageNumber); Assert.Equal("cookie-1", query.PageInfo.PagingCookie);
            return new EntityCollection(new[] { sdk.Attendance() });
        };
        Assert.Single(Enqueue(sdk, false)); Assert.Equal(2, sdk.PageReads); Assert.Single(sdk.Tasks);
    }

    /// <summary>缺 cookie、cookie 循環、跨頁重複、空續頁或超量頁都 fail closed，無首頁部分發布。</summary>
    [Theory]
    [InlineData("missing-cookie")]
    [InlineData("repeated-cookie")]
    [InlineData("large-cookie")]
    [InlineData("duplicate-next-page")]
    [InlineData("empty-next-page")]
    [InlineData("null-page")]
    [InlineData("oversized-page")]
    public void Meeting_InvalidPaging_NeverEnqueues(string fault)
    {
        using var sdk = new Sdk();
        var row = sdk.Attendance();
        sdk.Pages = query =>
        {
            if (fault == "null-page") return null!;
            if (fault == "oversized-page") return new EntityCollection(Enumerable.Range(0, 501).Select(_ => sdk.Attendance()).ToList());
            if (query.PageInfo.PageNumber == 1)
                return new EntityCollection(new[] { row })
                {
                    MoreRecords = true,
                    PagingCookie = fault == "missing-cookie" ? null : fault == "large-cookie" ? new string('x', 32769) : "same"
                };
            return new EntityCollection(fault == "empty-next-page" ? Array.Empty<Entity>() :
                new[] { fault == "duplicate-next-page" ? row : sdk.Attendance() })
                { MoreRecords = fault == "repeated-cookie", PagingCookie = "same" };
        };
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, false));
        Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates); Assert.InRange(sdk.PageReads, 1, 2);
    }

    /// <summary>剛好一百頁或一萬列可完整發布；宣告更多則拒絕，不進第 101 頁或漏掉第 10001 列。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Meeting_PagingBudgets_AreExact(bool rowLimit, bool overflow)
    {
        using var sdk = new Sdk();
        var lastPage = rowLimit ? 20 : 100;
        sdk.Pages = query => new EntityCollection(Enumerable.Range(0, rowLimit ? 500 : 1).Select(_ => sdk.Attendance()).ToList())
        {
            MoreRecords = query.PageInfo.PageNumber < lastPage || overflow,
            PagingCookie = "page-" + query.PageInfo.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (overflow)
        {
            Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, false)); Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates);
        }
        else Assert.Single(Enqueue(sdk, false));
        Assert.Equal(lastPage, sdk.PageReads); Assert.Equal(0, sdk.Active);
    }

    /// <summary>前頁未滿五百而後頁跨越一萬列時，仍按總正式 ID 數拒絕，不能只檢查頁數。</summary>
    [Fact]
    public void Meeting_PartialPageThenRowOverflow_RejectsBeforeWrite()
    {
        using var sdk = new Sdk();
        sdk.Pages = query => new EntityCollection(Enumerable.Range(0, query.PageInfo.PageNumber == 1 ? 499 : 500)
            .Select(_ => sdk.Attendance()).ToList())
        {
            MoreRecords = query.PageInfo.PageNumber < 21,
            PagingCookie = "page-" + query.PageInfo.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, false));
        Assert.Equal(21, sdk.PageReads); Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates);
    }

    /// <summary>
    /// 一萬個不同正式會友只各讀一次，最後一筆逾時則整組零發布且原例外不變；
    /// 合成吞吐測試只驗呼叫數／排空，不冒充遠端網路延遲或沙箱時限驗收。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Meeting_TenThousandUniqueContacts_IsBoundedAndLastReadTimeoutRejects(bool timeout)
    {
        using var sdk = new Sdk();
        var rows = new List<Entity>(10000);
        for (var index = 0; index < 10000; index++)
        {
            var contact = Guid.NewGuid();
            sdk.Sources.Add(("contact", contact), new Entity("contact", contact)
                { ["parentcustomerid"] = new EntityReference("account", sdk.ChurchId) });
            rows.Add(sdk.Attendance(contact: contact));
        }
        var reads = 0;
        var failure = new TimeoutException("Synthetic last contact timeout.");
        sdk.Read = (table, id) =>
        {
            if (table == "contact" && ++reads == 10000 && timeout) throw failure;
            return sdk.Sources[(table, id)];
        };
        sdk.Pages = query => new EntityCollection(rows.Skip((query.PageInfo.PageNumber - 1) * 500).Take(500).ToList())
        {
            MoreRecords = query.PageInfo.PageNumber < 20,
            PagingCookie = "page-" + query.PageInfo.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (timeout)
        {
            Assert.Same(failure, Assert.Throws<TimeoutException>(() => Enqueue(sdk, false)));
            Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates);
        }
        else Assert.Single(Enqueue(sdk, false));
        Assert.Equal(10000, reads); Assert.Equal(20, sdk.PageReads); Assert.Equal(0, sdk.Active);
    }

    /// <summary>第二頁另一教會即使帶停用標記也參與完整歸屬驗證，不加舊規則不存在的 statecode 過濾。</summary>
    [Fact]
    public void Meeting_CrossPageChurchConflict_RejectsEvenInactiveAttendance()
    {
        using var sdk = new Sdk();
        var other = Guid.NewGuid();
        sdk.Sources.Add(("contact", other), new Entity("contact", other)
            { ["parentcustomerid"] = new EntityReference("account", Guid.NewGuid()) });
        sdk.Pages = query =>
        {
            var row = sdk.Attendance(contact: query.PageInfo.PageNumber == 1 ? sdk.RecordId : other);
            row["statecode"] = new OptionSetValue(1);
            return new EntityCollection(new[] { row })
                { MoreRecords = query.PageInfo.PageNumber == 1, PagingCookie = "next" };
        };
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, false));
        Assert.Equal(2, sdk.PageReads); Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates);
    }

    /// <summary>不確定寫入已提交卻逾時時先保留原錯誤，明確重試才經唯一鍵取得同一正式 ID。</summary>
    [Fact]
    public void CommittedCreateThenTimeout_ExplicitRetryConvergesWithoutDuplicate()
    {
        using var sdk = new Sdk();
        var failure = new TimeoutException("Synthetic committed response loss.");
        sdk.AfterCommitFailure = failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => Enqueue(sdk, true)));
        Assert.Equal(1, sdk.Creates);
        var storedId = Assert.Single(sdk.Tasks).Value.Id;
        sdk.AfterCommitFailure = null;
        Assert.Equal(storedId, Assert.Single(Enqueue(sdk, true)));
        Assert.Single(sdk.Tasks); Assert.Equal(2, sdk.Creates); Assert.Equal(1, sdk.Conflicts);
        Assert.Equal(0, sdk.Active);
    }

    /// <summary>後頁及 Create 故障保留原 Exception；明確重試重新驗證來源並可成功，不留半個候選。</summary>
    [Fact]
    public void Failures_KeepOriginalExceptionAndPermitExplicitRetry()
    {
        using var sdk = new Sdk();
        var failure = new TimeoutException("Synthetic failure.");
        sdk.Pages = query => query.PageInfo.PageNumber == 1
            ? new EntityCollection(new[] { sdk.Attendance() }) { MoreRecords = true, PagingCookie = "next" }
            : throw failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => Enqueue(sdk, false)));
        Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates); Assert.Equal(0, sdk.Active);
        sdk.Pages = null; sdk.CreateFailure = failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => Enqueue(sdk, false)));
        Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Active);
        sdk.CreateFailure = null;
        Assert.Single(Enqueue(sdk, false));
        sdk.Read = (_, _) => throw failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => Enqueue(sdk, true)));
        Assert.Equal(0, sdk.Active);
    }

    /// <summary>已存在事件內容被改動時，真正 store 必須拒絕而非覆寫既有完成狀態。</summary>
    [Fact]
    public void ExistingEvent_ContentConflict_IsRejectedWithoutOverwrite()
    {
        using var sdk = new Sdk();
        Enqueue(sdk, true);
        var row = Assert.Single(sdk.Tasks).Value;
        row["new_payload"] = "{}";
        row["new_taskstatus"] = new OptionSetValue(100000002);
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, true));
        Assert.Equal("{}", row.GetAttributeValue<string>("new_payload"));
        Assert.Equal(100000002, row.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
        Assert.Single(sdk.Tasks);
    }

    /// <summary>跨次歸屬變更必須重新讀權威來源；A／B／A 分區各自穩定，缺歸屬不能沿用舊成功。</summary>
    [Fact]
    public void Personal_AuthoritativeScopeChanges_AreNeverCached()
    {
        using var sdk = new Sdk();
        var first = Assert.Single(Enqueue(sdk, true));
        var contact = sdk.Sources[("contact", sdk.RecordId)];
        contact["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        Assert.NotEqual(first, Assert.Single(Enqueue(sdk, true)));
        Assert.Equal(2, sdk.Tasks.Count);
        contact.Attributes.Remove("parentcustomerid");
        Assert.Throws<InvalidOperationException>(() => Enqueue(sdk, true));
        Assert.Equal(2, sdk.Creates);
        contact["parentcustomerid"] = new EntityReference("account", sdk.ChurchId);
        Assert.Equal(first, Assert.Single(Enqueue(sdk, true)));
        Assert.Equal(2, sdk.Tasks.Count);
    }

    /// <summary>保留回傳唯讀結果時，已釋放 owner 的 SDK 及來源列仍可回收，不能被靜態或命令滯留。</summary>
    [Fact]
    public void ReturnedIds_DoNotRetainSdkOrSourceEntities()
    {
        var (result, references) = ExecuteAndRelease();
        for (var attempt = 0; attempt < 3 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.NotEqual(Guid.Empty, Assert.Single(result));
        GC.KeepAlive(result);
    }

    /// <summary>獨立非內嵌 frame 結束 SDK using 後只留下結果與弱參照，避免 JIT 局部根造成假滯留。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IReadOnlyList<Guid> Result, WeakReference[] References) ExecuteAndRelease()
    {
        using var sdk = new Sdk();
        var row = sdk.Sources[("contact", sdk.RecordId)];
        var result = Enqueue(sdk, true);
        return (result, new[] { new WeakReference(sdk), new WeakReference(row) });
    }

    /// <summary>並行同事件經真 store 的唯一鍵衝突核對取得同 ID，A／B 相同來源不交叉共享教會或待辦。</summary>
    [Fact]
    public async Task Concurrent_ABA_UsesDatabaseWriterAndDrainsBeforeOwnerDisposal()
    {
        using var a = new Sdk();
        using var b = new Sdk();
        var calls = Enumerable.Range(0, 64).Select(i => Task.Run(() => Enqueue(i % 2 == 0 ? a : b, true))).ToArray();
        var results = await Task.WhenAll(calls);
        var aId = Assert.Single(a.Tasks).Value.Id;
        var bId = Assert.Single(b.Tasks).Value.Id;
        Assert.NotEqual(aId, bId);
        for (var i = 0; i < results.Length; i++) Assert.Equal(i % 2 == 0 ? aId : bId, Assert.Single(results[i]));
        Assert.Equal(aId, Assert.Single(Enqueue(a, true)));
        Assert.Equal(0, a.Active); Assert.Equal(0, b.Active);
        Assert.Equal(0, a.Disposals); Assert.Equal(0, b.Disposals);
    }

    /// <summary>規則先驗與預取消必須零 I/O；空待辦 ID 及儲存故障不能變成假成功。</summary>
    [Fact]
    public void Rule_ValidationCancellationAndFaults_DoNotPublishFakeResults()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new Store();
        var church = Guid.NewGuid(); var record = Guid.NewGuid();
        Assert.Throws<ArgumentNullException>(() => QrImageTaskRule.Enqueue(null!, church, record, true));
        Assert.Throws<ArgumentException>(() => QrImageTaskRule.Enqueue(store, Guid.Empty, record, true));
        Assert.Throws<ArgumentException>(() => QrImageTaskRule.Enqueue(store, church, Guid.Empty, true));
        Assert.Throws<OperationCanceledException>(() => QrImageTaskRule.Enqueue(store, church, record, true, cancellation.Token));
        Assert.Equal(0, store.Calls);
        Assert.Throws<InvalidOperationException>(() => QrImageTaskRule.Enqueue(store, church, record, true));
        var failure = new TimeoutException("Synthetic store failure.");
        store.Failure = failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => QrImageTaskRule.Enqueue(store, church, record, true)));
        Assert.Equal(2, store.Calls);
    }

    /// <summary>服務對空正式 ID 與空 SDK 在讀取前拒絕；格式有效本身不是授權證明。</summary>
    [Fact]
    public void Service_InvalidArguments_RejectBeforeSdk()
    {
        using var sdk = new Sdk();
        Assert.Throws<ArgumentNullException>(() => QrImageTaskService.EnqueuePersonal(null!, sdk.RecordId));
        Assert.Throws<ArgumentNullException>(() => QrImageTaskService.EnqueueMeeting(null!, sdk.RecordId));
        Assert.Throws<ArgumentException>(() => QrImageTaskService.EnqueuePersonal(sdk, Guid.Empty));
        Assert.Throws<ArgumentException>(() => QrImageTaskService.EnqueueMeeting(sdk, Guid.Empty));
        Assert.Empty(sdk.Tasks); Assert.Equal(0, sdk.Creates); Assert.Equal(0, sdk.PageReads);
    }

    /// <summary>單案純同步故障假件，不存命令、SDK 或取消註冊；生命期止於該測試返回。</summary>
    private sealed class Store : IOutboundEnqueueStore
    {
        /// <summary>用於斷言先驗拒絕沒有呼叫儲存。</summary>
        internal int Calls { get; private set; }
        /// <summary>由本測試擁有的原始例外，不向其他測試共用。</summary>
        internal Exception? Failure { get; set; }
        /// <summary>注入空正式 ID 或原始失敗；不宣稱提供資料庫唯一性。</summary>
        public Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (Failure is not null) throw Failure;
            return Guid.Empty;
        }
    }

    /// <summary>
    /// 單案 SDK 假件；只允許最小來源欄位與待辦 API，以鎖模擬資料庫唯一鍵而非產品記憶體防重。
    /// 不模擬授權提升或連線；來源及已儲存列的唯一 owner 是本測試 using。
    /// </summary>
    private sealed class Sdk : IOrganizationService, IDisposable
    {
        /// <summary>只在此假件內保護原子新增及讀回，Dispose 在所有測試工作排空後執行。</summary>
        private readonly object gate = new();
        /// <summary>當前同步 SDK 呼叫數，所有入口 finally 遞減。</summary>
        private int active;
        /// <summary>固定來源正式主鍵，會議與個人案例各自使用獨立 SDK。</summary>
        internal Guid RecordId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");
        /// <summary>操作專屬教會正式 ID；不以姓名或組織名稱決定。</summary>
        internal Guid ChurchId { get; } = Guid.NewGuid();
        /// <summary>單次合成權威來源，建構後由案例配置，不與產品共用。</summary>
        internal Dictionary<(string Table, Guid Id), Entity> Sources { get; } = new();
        /// <summary>唯一鍵儲存列；僅在全部並行工作排空後由斷言讀取及 Dispose 清空。</summary>
        internal Dictionary<string, Entity> Tasks { get; } = new(StringComparer.Ordinal);
        /// <summary>出席分頁故障注入；沒有預設之外的背景生產者。</summary>
        internal Func<QueryExpression, EntityCollection>? Pages { get; set; }
        /// <summary>讀來源故障或身分錯配注入，原例外必須由產品原樣傳出。</summary>
        internal Func<string, Guid, Entity>? Read { get; set; }
        /// <summary>建立故障注入，用於證明寫入失敗不能回假 ID。</summary>
        internal Exception? CreateFailure { get; set; }
        /// <summary>模擬提交成功但傳輸回應遺失；單次同步 owner 在 Dispose 清除例外參照。</summary>
        internal Exception? AfterCommitFailure { get; set; }
        /// <summary>合成 Create 總次數，與衝突數一起證明走真正資料庫 writer 路徑。</summary>
        internal int Creates { get; private set; }
        /// <summary>已知替代鍵衝突總數，由 D365 store 處理而非服務預查。</summary>
        internal int Conflicts { get; private set; }
        /// <summary>單輪頁查詢數，可決定性驗證上限不會進第 101 頁。</summary>
        internal int PageReads { get; private set; }
        /// <summary>外部 owner Dispose 次數；產品借用期間必須為零。</summary>
        internal int Disposals { get; private set; }
        /// <summary>排空基線，不以 GC 或延遲等待掩蓋未完成工作。</summary>
        internal int Active => Volatile.Read(ref active);

        /// <summary>建立最小有效會議及同 ID 個人來源；姓名僅為合成的無權威顯示值。</summary>
        internal Sdk()
        {
            Sources.Add(("new_meeting_statistics", RecordId), new Entity("new_meeting_statistics", RecordId));
            Sources.Add(("contact", RecordId), new Entity("contact", RecordId)
            {
                ["parentcustomerid"] = new EntityReference("account", ChurchId), ["fullname"] = "同名"
            });
        }

        /// <summary>產生獨立出席列，正式列 ID 與會員 ID 分離，方便測試同人多列與真正重複。</summary>
        internal Entity Attendance(Guid? id = null, Guid? contact = null) => new("new_present_record", id ?? Guid.NewGuid())
        {
            ["new_meeting_statistics_new_present_re"] = new EntityReference("new_meeting_statistics", RecordId),
            ["new_contact_new_present_record"] = new EntityReference("contact", contact ?? RecordId)
        };

        /// <summary>檢查最小權限投影，不讀全欄或 token；回傳來源由產品只讀借用。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Interlocked.Increment(ref active);
            try
            {
                Assert.False(columnSet.AllColumns);
                if (entityName == "contact") Assert.Equal("parentcustomerid", Assert.Single(columnSet.Columns));
                else Assert.Empty(columnSet.Columns);
                return Read is not null ? Read(entityName, id) : Sources[(entityName, id)];
            }
            finally { Interlocked.Decrement(ref active); }
        }

        /// <summary>核對查詢範圍、正式排序與續頁；衝突讀回只能使用精確唯一鍵。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Interlocked.Increment(ref active);
            try
            {
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.False(expression.ColumnSet.AllColumns);
                if (expression.EntityName == "new_outboundtask")
                {
                    Assert.Equal(2, expression.TopCount);
                    var condition = Assert.Single(expression.Criteria.Conditions);
                    Assert.Equal("new_idempotencykey", condition.AttributeName);
                    lock (gate) return new EntityCollection(new[] { Tasks[(string)Assert.Single(condition.Values)] });
                }
                Assert.Equal("new_present_record", expression.EntityName);
                Assert.Equal(3, expression.ColumnSet.Columns.Count);
                Assert.Contains("new_contact_new_present_record", expression.ColumnSet.Columns);
                Assert.Contains("new_meeting_statistics_new_present_re", expression.ColumnSet.Columns);
                Assert.Contains("new_present_recordid", expression.ColumnSet.Columns);
                var filter = Assert.Single(expression.Criteria.Conditions);
                Assert.Equal("new_meeting_statistics_new_present_re", filter.AttributeName);
                Assert.Equal(ConditionOperator.Equal, filter.Operator);
                Assert.Equal(RecordId, Assert.Single(filter.Values));
                var order = Assert.Single(expression.Orders);
                Assert.Equal("new_present_recordid", order.AttributeName);
                Assert.Equal(OrderType.Ascending, order.OrderType);
                Assert.InRange(expression.PageInfo.Count, 1, 500);
                PageReads++;
                return Pages is not null ? Pages(expression) : new EntityCollection(new[] { Attendance() });
            }
            finally { Interlocked.Decrement(ref active); }
        }

        /// <summary>以原子 Create 注入替代鍵衝突，產生與來源不同的真正儲存 ID；不覆寫舊狀態。</summary>
        public Guid Create(Entity entity)
        {
            Interlocked.Increment(ref active);
            try
            {
                lock (gate)
                {
                    Creates++;
                    if (CreateFailure is not null) throw CreateFailure;
                    Assert.Equal("new_outboundtask", entity.LogicalName);
                    var key = entity.GetAttributeValue<string>("new_idempotencykey");
                    if (Tasks.ContainsKey(key))
                    {
                        Conflicts++;
                        throw new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) });
                    }
                    entity.Id = Guid.NewGuid();
                    Tasks.Add(key, entity);
                    if (AfterCommitFailure is not null) throw AfterCommitFailure;
                    return entity.Id;
                }
            }
            finally { Interlocked.Decrement(ref active); }
        }

        /// <summary>本契約禁止更新來源或重設已完成待辦；若產品新增此副作用測試立即失敗。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本契約不執行未知 SDK 訊息或建立背景作業。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>QR 入列沒有刪除權，禁止擴大資料操作面。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>QR 入列不建立關聯，禁止以其他 SDK 寫入繞過唯一鍵。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        /// <summary>QR 入列不移除關聯，任何意外副作用須明確失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();

        /// <summary>owner 在斷言後釋放所有資料與 callback；必須先排空，不能靠 Dispose 中止飛行中工作。</summary>
        public void Dispose()
        {
            Assert.Equal(0, Active);
            Disposals++;
            Sources.Clear(); Tasks.Clear(); Pages = null; Read = null; CreateFailure = null; AfterCommitFailure = null;
        }
    }
}
