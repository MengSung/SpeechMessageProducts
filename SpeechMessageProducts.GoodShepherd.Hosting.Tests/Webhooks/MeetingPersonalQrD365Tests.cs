/*
 * 檔案整體說明
 * 目的與定位：以真 AddGoodShepherdLineWebhook 組合根與 SDK 接縫驗證 P5-09 的權威歸屬、
 * 完整分頁、正式 ID 防重及同一 ExecuteTransaction 內的全部版本 guard。
 * 主要流程：合成 SDK 只在記憶體驗證 CAS 後一次套用候選；注入 meeting、出席、
 * contact 競爭及跨教會、缺頁、重複 ID、超限，斷言沒有部分 URL 或附件。
 * 個人依來源 deterministic annotation ID 重送，姓名來自 contact 且檔名不含姓名。
 * 使用邊界與維護：每案 Harness 唯一擁有容器與 scope；SDK 在 scope Dispose 清除
 * Entity／委派及交易引用，不建立 D365 連線、HTTP、SQL 或圖片目錄。
 * 驗證方式：這是離線交易模型及真 adapter 請求的驗證，不是 Dataverse 的交易、
 * optimistic concurrency 或線上 Plugin 行為實測；實機授權及部署另行驗收。
 */
using LineMessagingProcessor.AspNetCore.Webhooks;
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.Webhooks;

/// <summary>保護正式組合根 schema2 adapter 的來源隔離、單交易與生命週期。</summary>
public sealed class MeetingPersonalQrD365Tests
{
    /// <summary>同一正式 meeting 同時綁兩個教會時必須整份設定拒絕，不留下部分 DI 註冊。</summary>
    [Fact]
    public void Registration_DuplicateMeetingBindingAcrossChurches_RejectsWholeSnapshot()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var meeting = Guid.NewGuid();
        var values = new Dictionary<string, string?>
        {
            ["QrCodes:Qr:StorageDirectory"] = Path.GetTempPath(),
            ["QrCodes:Qr:PublicBaseUrl"] = "https://images.invalid/"
        };
        foreach (var church in new[] { first, second })
        {
            values[$"QrCodes:Qr:Churches:{church:D}:MeetingPrefix"] = "https://qr.invalid/?QrCodeId=";
            values[$"QrCodes:Qr:Churches:{church:D}:PersonalPrefix"] = "https://personal.invalid/?QrCodeId=";
            values[$"QrCodes:Qr:Churches:{church:D}:MeetingIds:0"] = meeting.ToString("D");
        }
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddMeetingPersonalQrPublication(configuration));
        Assert.Empty(services);
    }

    /// <summary>兩種開關同時啟用仍只建立一個 QR worker；單獨新開關也能解析正式 store。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_UsesOneQrWorker(bool lessons)
    {
        using var host = new Harness(lessons);
        var descriptor = Assert.Single(host.QrWorkers);
        // 只解析 QR 的真 factory，其他 hosted services 需要本案未授權的 SQL／HTTP 依賴。
        using var worker = Assert.IsType<OutboundTaskWorker>(descriptor.ImplementationFactory!(host.Provider));
        Assert.NotNull(host.Store);
    }

    /// <summary>同名不同正式出席 ID 跨兩頁全部保留；三十八欄與所有關聯版本一次提交。</summary>
    [Fact]
    public void Meeting_FullPagingAndSameNames_ProducesAllGuardsAndCompleteUrls()
    {
        using var host = new Harness();
        host.Sdk.AddPresent(101);
        var payload = Parse("meeting", host.Sdk.Meeting.Id);
        host.Store.Authorize(host.Sdk.Church, payload, default).Commit(Urls(payload), null, default);
        Assert.Equal(2, host.Sdk.Pages);
        var transaction = Assert.Single(host.Sdk.Transactions);
        Assert.Equal(103, transaction.Requests.Count);
        Assert.All(transaction.Requests, r =>
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, Assert.IsType<UpdateRequest>(r).ConcurrencyBehavior));
        Assert.Equal(38, host.Sdk.Meeting.Attributes.Keys.Count(k => k != "name"));
        Assert.Equal(101, transaction.Requests.OfType<UpdateRequest>()
            .Count(r => r.Target.LogicalName == "new_present_record"));
    }

    /// <summary>每個來源版本各別競爭都必須阻止完整提交，不能只在先前驗過後無 guard 更新。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("present")]
    [InlineData("contact")]
    public void Meeting_RaceOnEverySource_RollsBackAllUrls(string changed)
    {
        using var host = new Harness();
        host.Sdk.AddPresent(2);
        var payload = Parse("meeting", host.Sdk.Meeting.Id);
        var commit = host.Store.Authorize(host.Sdk.Church, payload, default);
        host.Sdk.BeforeExecute = () =>
        {
            var row = changed switch
            {
                "meeting" => host.Sdk.Meeting,
                "present" => host.Sdk.Presents[1],
                _ => host.Sdk.Contact
            };
            row.RowVersion = "999";
        };
        Assert.Throws<InvalidOperationException>(() => commit.Commit(Urls(payload), null, default));
        Assert.Single(host.Sdk.Meeting.Attributes);
        Assert.Single(host.Sdk.Transactions);
    }

    /// <summary>空來源、多教會或精確重複 ID 一律拒絕；不以姓名去重掩蓋壞來源。</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("church")]
    [InlineData("duplicate")]
    [InlineData("cookie")]
    [InlineData("version")]
    public void Meeting_InvalidOwnershipOrIncompleteSource_FailsClosed(string fault)
    {
        using var host = new Harness();
        host.Sdk.AddPresent(fault == "cookie" ? 101 : 2);
        if (fault == "empty") host.Sdk.Presents.Clear();
        if (fault == "church")
        {
            var other = new Entity("contact", Guid.NewGuid()) { RowVersion = "3" };
            other["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
            host.Sdk.Rows.Add(other.Id, other);
            host.Sdk.Presents[1]["new_contact_new_present_record"] = other.ToEntityReference();
        }
        if (fault == "duplicate") host.Sdk.Presents[1] = host.Sdk.Presents[0];
        if (fault == "cookie") host.Sdk.MissingCookie = true;
        if (fault == "version") host.Sdk.Presents[1].RowVersion = null;
        Assert.Throws<InvalidOperationException>(() => host.Store.Authorize(host.Sdk.Church,
            Parse("meeting", host.Sdk.Meeting.Id), default));
        Assert.Empty(host.Sdk.Transactions);
    }

    /// <summary>完整查完後發現交易超容量必須明確拒絕；不能只取可放入交易的前幾筆。</summary>
    [Fact]
    public void Meeting_TransactionOverCapacity_RejectsWithoutTruncation()
    {
        using var host = new Harness();
        host.Sdk.AddPresent(1000);
        Assert.Throws<InvalidOperationException>(() => host.Store.Authorize(host.Sdk.Church,
            Parse("meeting", host.Sdk.Meeting.Id), default));
        Assert.Equal(10, host.Sdk.Pages);
        Assert.Empty(host.Sdk.Transactions);
    }

    /// <summary>超一百頁／一萬筆仍宣稱有更多時拒絕，不能把第一萬筆當成完整來源。</summary>
    [Fact]
    public void Meeting_QueryLimit_RejectsInsteadOfPublishingPrefix()
    {
        using var host = new Harness();
        host.Sdk.AddPresent(10001);
        Assert.Throws<InvalidOperationException>(() => host.Store.Authorize(host.Sdk.Church,
            Parse("meeting", host.Sdk.Meeting.Id), default));
        Assert.Equal(100, host.Sdk.Pages);
        Assert.Empty(host.Sdk.Transactions);
    }

    /// <summary>個人從 contact 讀姓名，附件固定 ID 且同來源重跑不增附件；關聯 CAS 同交易。</summary>
    [Fact]
    public void Personal_RetryUsesDeterministicAttachmentAndAuthoritativeName()
    {
        using var host = new Harness();
        var payload = Parse("personal", host.Sdk.Contact.Id);
        var png = Png();
        for (var i = 0; i < 2; i++)
        {
            var commit = host.Store.Authorize(host.Sdk.Church, payload, default);
            Assert.Equal("同名甲 @&?", commit.FullName);
            commit.Commit(new Dictionary<string, string>(), png, default);
        }
        var note = Assert.Single(host.Sdk.Rows.Values.Where(e => e.LogicalName == "annotation"));
        Assert.Equal(payload.AnnotationId, note.Id);
        Assert.Equal("同名甲 @&?QR Code", note.GetAttributeValue<string>("subject"));
        Assert.Equal("image/png", note.GetAttributeValue<string>("mimetype"));
        Assert.Equal($"{payload.RecordId:N}_QRCode.png", note.GetAttributeValue<string>("filename"));
        Assert.Equal(Convert.ToBase64String(png), note.GetAttributeValue<string>("documentbody"));
        Assert.Single(host.Sdk.Transactions.SelectMany(t => t.Requests).OfType<CreateRequest>());
        Assert.All(host.Sdk.Transactions, t => Assert.Contains(t.Requests, r =>
            r is UpdateRequest u && u.Target.Id == payload.RecordId &&
            u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches));
    }

    /// <summary>姓名／教會改動與附件 Create 競爭時接觸同一 contact guard，失敗不得留下附件。</summary>
    [Fact]
    public void Personal_OwnershipRace_DoesNotCreateAttachment()
    {
        using var host = new Harness();
        var payload = Parse("personal", host.Sdk.Contact.Id);
        var commit = host.Store.Authorize(host.Sdk.Church, payload, default);
        host.Sdk.BeforeExecute = () => host.Sdk.Contact.RowVersion = "99";
        Assert.Throws<InvalidOperationException>(() =>
            commit.Commit(new Dictionary<string, string>(), Png(), default));
        Assert.DoesNotContain(host.Sdk.Rows.Values, e => e.LogicalName == "annotation");
    }

    /// <summary>同來源兩個候選同時授權，較舊版本提交必須失敗；重讀後只核對既有附件不再 Create。</summary>
    [Fact]
    public void Personal_ConcurrentSameSourceAndRetry_KeepOneCanonicalAttachment()
    {
        using var host = new Harness();
        var payload = Parse("personal", host.Sdk.Contact.Id);
        var first = host.Store.Authorize(host.Sdk.Church, payload, default);
        var second = host.Store.Authorize(host.Sdk.Church, payload, default);
        first.Commit(new Dictionary<string, string>(), Png(), default);
        Assert.Throws<InvalidOperationException>(() => second.Commit(new Dictionary<string, string>(), Png(), default));
        host.Store.Authorize(host.Sdk.Church, payload, default)
            .Commit(new Dictionary<string, string>(), Png(), default);
        Assert.Single(host.Sdk.Rows.Values.Where(r => r.LogicalName == "annotation"));
    }

    /// <summary>同名不同正式 contact 必須各有附件，姓名不作防重鍵、檔名或資料列身分。</summary>
    [Fact]
    public void Personal_SameNameDifferentIds_PreserveBothAttachments()
    {
        using var host = new Harness();
        var other = new Entity("contact", Guid.NewGuid()) { RowVersion = "1" };
        other["fullname"] = host.Sdk.Contact["fullname"];
        other["parentcustomerid"] = new EntityReference("account", host.Sdk.Church);
        host.Sdk.Rows.Add(other.Id, other);
        foreach (var contactId in new[] { host.Sdk.Contact.Id, other.Id })
            host.Store.Authorize(host.Sdk.Church, Parse("personal", contactId), default)
                .Commit(new Dictionary<string, string>(), Png(), default);
        var notes = host.Sdk.Rows.Values.Where(r => r.LogicalName == "annotation").ToArray();
        Assert.Equal(2, notes.Length);
        Assert.NotEqual(notes[0].Id, notes[1].Id);
        Assert.Equal(notes[0]["subject"], notes[1]["subject"]);
        Assert.NotEqual(notes[0]["filename"], notes[1]["filename"]);
    }

    /// <summary>不完整 URL 或已取消提交在 Execute 前拒絕；來源仍保持未發布狀態。</summary>
    [Fact]
    public void Meeting_IncompleteCandidateOrCancelledCommit_DoesNotExecute()
    {
        using var host = new Harness();
        host.Sdk.AddPresent(2);
        var payload = Parse("meeting", host.Sdk.Meeting.Id);
        var commit = host.Store.Authorize(host.Sdk.Church, payload, default);
        Assert.Throws<InvalidOperationException>(() => commit.Commit(new Dictionary<string, string>(), null, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => commit.Commit(Urls(payload), null, cancellation.Token));
        Assert.Empty(host.Sdk.Transactions);
        Assert.Single(host.Sdk.Meeting.Attributes);
    }

    /// <summary>SDK 防線依 metadata 限制五百字；完整三十八欄中只要一欄超限就零 Execute。</summary>
    [Fact]
    public void Meeting_CommitUrlOverMetadataCapacity_DoesNotExecute()
    {
        using var host = new Harness();
        host.Sdk.AddPresent(2);
        var payload = Parse("meeting", host.Sdk.Meeting.Id);
        var commit = host.Store.Authorize(host.Sdk.Church, payload, default);
        var urls = new Dictionary<string, string>(Urls(payload));
        urls["new_sunday_first_qr_on_url"] = "https://images.invalid/" + new string('a', 480) + ".png";
        Assert.True(urls["new_sunday_first_qr_on_url"].Length > 500);
        Assert.Throws<InvalidOperationException>(() => commit.Commit(urls, null, default));
        Assert.Empty(host.Sdk.Transactions);
        Assert.Single(host.Sdk.Meeting.Attributes);
    }

    /// <summary>兩 scope 的 SDK 不相同；跨教會被拒絕，Dispose 後來源及交易引用清空。</summary>
    [Fact]
    public void Scopes_AreIsolatedAndDisposedAfterFailure()
    {
        var host = new Harness();
        var sdk = host.Sdk;
        using (var other = host.Provider.CreateScope())
        {
            var otherSdk = Assert.IsType<Sdk>(other.ServiceProvider.GetRequiredService<IOrganizationService>());
            Assert.NotSame(sdk, otherSdk);
            Assert.Throws<InvalidOperationException>(() => other.ServiceProvider
                .GetRequiredService<IMeetingPersonalQrPublicationStore>()
                .Authorize(sdk.Church, Parse("personal", otherSdk.Contact.Id), default));
        }
        host.Dispose();
        Assert.True(sdk.Disposed);
        Assert.Empty(sdk.Rows);
        Assert.Empty(sdk.Transactions);
    }

    /// <summary>每次建立封閉合成 payload，不直接建構產品內部候選。</summary>
    private static MeetingPersonalQrPayload Parse(string kind, Guid id) =>
        MeetingPersonalQrPayload.Parse($"{{\"schema\":2,\"kind\":{(kind == "meeting" ? 0 : 1)},\"recordId\":\"{id:D}\"," +
            $"\"contentVersion\":1,\"imageCount\":{(kind == "meeting" ? 38 : 1)}}}");

    /// <summary>合成已完成 PNG，不代表真 renderer 或遠端附件上限驗收。</summary>
    private static byte[] Png() => [137, 80, 78, 71, 13, 10, 26, 10, 1];

    /// <summary>建立完整欄位 URL；精確 codec 對照另由 RichMenus 候選測試保護。</summary>
    private static IReadOnlyDictionary<string, string> Urls(MeetingPersonalQrPayload payload) =>
        MeetingPersonalQrPlan.Create(payload, new("https://qr.invalid/?QrCodeId=",
            "https://personal.invalid/?QrCodeId="), "").ToDictionary(p => p.FieldName,
                p => "https://images.invalid/" + p.FieldName + ".png");

    /// <summary>容器、scope 及純記憶體組態唯一 owner，不啟動 Host／worker 或連線。</summary>
    private sealed class Harness : IDisposable
    {
        private readonly ConfigurationRoot _configuration;
        private readonly IServiceScope _scope;
        /// <summary>以新開關獨立啟用，選填舊開關驗證同 type worker 不重複。</summary>
        public Harness(bool lessons = false)
        {
            const string church = "11111111-1111-1111-1111-111111111111";
            _configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["QrCodes:Qr:Enabled"] = "true",
                    ["QrCodes:Qr:StorageDirectory"] = Path.GetTempPath(),
                    ["QrCodes:Qr:PublicBaseUrl"] = "https://images.invalid/",
                    [$"QrCodes:Qr:Churches:{church}:MeetingPrefix"] = "https://qr.invalid/?QrCodeId=",
                    [$"QrCodes:Qr:Churches:{church}:PersonalPrefix"] = "https://personal.invalid/?QrCodeId=",
                    ["Lessons:Qr:Enabled"] = lessons.ToString(),
                    ["Lessons:Qr:StorageDirectory"] = Path.GetTempPath(),
                    ["Lessons:Qr:PublicBaseUrl"] = "https://lessons.invalid/",
                    [$"Lessons:Qr:Churches:{church}:RegularPrefix"] = "https://qr.invalid/?QrCodeId=",
                    [$"Lessons:Qr:Churches:{church}:PollPrefix"] = "https://poll.invalid/?QrCodeId="
                }).Build();
            var services = new ServiceCollection().AddScoped<IOrganizationService>(_ => new Sdk());
            services.AddGoodShepherdLineWebhook(_configuration);
            QrWorkers = services.Where(d => d.ServiceType == typeof(IHostedService) &&
                d.ImplementationFactory is { } factory &&
                (factory.Method.Name.Contains("<RegisterLessonQr>", StringComparison.Ordinal) ||
                 factory.Method.Name.Contains("<RegisterMeetingPersonalQr>", StringComparison.Ordinal))).ToArray();
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = Provider.CreateScope();
            Sdk = (Sdk)_scope.ServiceProvider.GetRequiredService<IOrganizationService>();
            Store = _scope.ServiceProvider.GetRequiredService<IMeetingPersonalQrPublicationStore>();
        }
        /// <summary>唯一擁有本案 scoped SDK 的根容器，結束時確定性 Dispose。</summary>
        public ServiceProvider Provider { get; }
        /// <summary>原始組合根的 QR descriptors，僅解析這些以避免啟動無關 SQL owner。</summary>
        public IReadOnlyList<ServiceDescriptor> QrWorkers { get; }
        /// <summary>本案 scope 的合成資料庫，僅供故障注入與結果斷言。</summary>
        public Sdk Sdk { get; }
        /// <summary>由真組合根解析的 adapter，不使用測試 publication store。</summary>
        public IMeetingPersonalQrPublicationStore Store { get; }
        /// <summary>先 scope 排空 SDK，再容器與組態；沒有尚未 await 的工作。</summary>
        public void Dispose() { _scope.Dispose(); Provider.Dispose(); _configuration.Dispose(); }
    }

    /// <summary>單 scope 的合成交易模型；先驗所有版本與唯一 ID，再一次套用完整候選。</summary>
    private sealed class Sdk : IOrganizationService, IDisposable
    {
        /// <summary>本實例唯一教會 ID，其他 scope 的 ID 不得通過授權。</summary>
        public Guid Church { get; } = Guid.NewGuid();
        /// <summary>本案權威聯絡人；只保留到 SDK Dispose。</summary>
        public Entity Contact { get; }
        /// <summary>本案 meeting 來源；版本故障透過此物件注入。</summary>
        public Entity Meeting { get; }
        /// <summary>合成正式資料庫，只在所有版本驗證成功後才套用交易。</summary>
        public Dictionary<Guid, Entity> Rows { get; } = new();
        /// <summary>依頁序回傳的完整出席來源，允許注入重複正式 ID。</summary>
        public List<Entity> Presents { get; } = new();
        /// <summary>本案交易請求觀察值，Dispose 清除，不保留到下一案例。</summary>
        public List<ExecuteTransactionRequest> Transactions { get; } = new();
        /// <summary>在真正版本判定前變更來源，模擬讀取與提交之間的競爭。</summary>
        public Action? BeforeExecute;
        /// <summary>故障注入續頁卻缺 cookie，必須拒絕前頁發布。</summary>
        public bool MissingCookie;
        /// <summary>確定性清理完成標記，不以 GC 推論資源已釋放。</summary>
        public bool Disposed;
        /// <summary>實際查詢頁數，驗證完整讀取及上限拒絕。</summary>
        public int Pages;
        /// <summary>每個實例建立不同正式 ID，避免 scope 意外重用未被發現。</summary>
        public Sdk()
        {
            Contact = new Entity("contact", Guid.NewGuid()) { RowVersion = "1" };
            Contact["parentcustomerid"] = new EntityReference("account", Church);
            Contact["fullname"] = "同名甲 @&?";
            Meeting = new Entity("new_meeting_statistics", Guid.NewGuid()) { RowVersion = "1" };
            Meeting["name"] = "同名聚會";
            Rows.Add(Contact.Id, Contact);
            Rows.Add(Meeting.Id, Meeting);
        }
        /// <summary>不同出席正式 ID 全部保留；顯示姓名故意相同，不可按姓名合併。</summary>
        public void AddPresent(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var row = new Entity("new_present_record", Guid.NewGuid()) { RowVersion = "1" };
                row["new_name"] = "同名";
                row["new_meeting_statistics_new_present_re"] = Meeting.ToEntityReference();
                row["new_contact_new_present_record"] = Contact.ToEntityReference();
                Presents.Add(row);
                Rows.Add(row.Id, row);
            }
        }
        /// <summary>精確主鍵及 ColumnSet 投影；回傳 detached 候選而非可變資料庫物件。</summary>
        public Entity Retrieve(string name, Guid id, ColumnSet columns)
        {
            var source = Rows[id];
            Assert.Equal(name, source.LogicalName);
            return Copy(source, columns);
        }
        /// <summary>只支援本包兩種完整有界查詢，逐頁 cookie 與正式 lookup 由斷言保護。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            if (expression.EntityName == "annotation")
            {
                Assert.Contains(expression.Criteria.Conditions, c => c.AttributeName == "annotationid");
                var id = (Guid)expression.Criteria.Conditions.Single(c => c.AttributeName == "annotationid").Values[0];
                return new EntityCollection(Rows.TryGetValue(id, out var annotation)
                    ? new[] { Copy(annotation, expression.ColumnSet) } : Array.Empty<Entity>());
            }
            Assert.Equal("new_present_record", expression.EntityName);
            Assert.Contains(expression.Criteria.Conditions, c =>
                c.AttributeName == "new_meeting_statistics_new_present_re" && Equals(c.Values[0], Meeting.Id));
            Assert.Equal(100, expression.PageInfo.Count);
            var page = expression.PageInfo.PageNumber;
            Assert.Equal(++Pages, page);
            if (page > 1) Assert.Equal((page - 1).ToString(), expression.PageInfo.PagingCookie);
            var result = new EntityCollection(Presents.Skip((page - 1) * 100).Take(100)
                .Select(row => Copy(row, expression.ColumnSet)).ToList())
            {
                MoreRecords = Presents.Count > page * 100,
                PagingCookie = MissingCookie ? null : page.ToString()
            };
            return result;
        }
        /// <summary>驗所有 CAS／Create 無衝突後才套用；故障時保持所有產品欄位不變。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var transaction = Assert.IsType<ExecuteTransactionRequest>(request);
            Transactions.Add(transaction);
            BeforeExecute?.Invoke();
            foreach (var operation in transaction.Requests)
            {
                if (operation is UpdateRequest update)
                {
                    Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                    if (Rows[update.Target.Id].RowVersion != update.Target.RowVersion)
                        throw new InvalidOperationException("Injected version conflict.");
                }
                else if (operation is CreateRequest create && Rows.ContainsKey(create.Target.Id))
                    throw new InvalidOperationException("Injected duplicate primary key.");
            }
            foreach (var operation in transaction.Requests)
            {
                if (operation is UpdateRequest update)
                {
                    var row = Rows[update.Target.Id];
                    foreach (var (key, value) in update.Target.Attributes) row[key] = value;
                    row.RowVersion = (int.Parse(row.RowVersion) + 1).ToString();
                }
                else if (operation is CreateRequest create)
                {
                    var row = Copy(create.Target, new ColumnSet(true));
                    row.RowVersion = "1";
                    Rows.Add(row.Id, row);
                }
            }
            return new ExecuteTransactionResponse();
        }
        /// <summary>複製投影與 lookup，避免來源修改直接污染 adapter 已取得的候選。</summary>
        private static Entity Copy(Entity source, ColumnSet columns)
        {
            var result = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
            foreach (var (key, value) in source.Attributes)
                if (columns.AllColumns || columns.Columns.Contains(key))
                    result[key] = value is EntityReference reference
                        ? new EntityReference(reference.LogicalName, reference.Id) : value;
            return result;
        }
        /// <summary>直接寫入均禁止，只允許產品產生單一原子交易。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>禁止無條件 Update 繞過 CAS。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本功能不刪正式來源或附件。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>不允許交易外關聯變更。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>不允許解除正式歸屬。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>scope 唯一釋放者，清除個資、交易及委派後標示完成，不保留已關閉來源。</summary>
        public void Dispose()
        {
            Rows.Clear(); Presents.Clear(); Transactions.Clear();
            Contact.Attributes.Clear(); Meeting.Attributes.Clear();
            BeforeExecute = null; Disposed = true;
        }
    }
}
