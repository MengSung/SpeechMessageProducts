/*
檔案整體說明
目的與定位：保護 P5-07 好牧人 Host 課程 QR SDK adapter 的授權與提交契約，
經 ServiceCollection.AddGoodShepherdLineWebhook 啟用 Lessons:Qr 並解析正式私有 store，
不以替身 publication store 取代產品行為，也不啟動 Host 或背景 worker。
主要功能與流程：只在 IOrganizationService 邊界注入合成課程、contact 與 account
關聯，實際呼叫 Authorize／Commit，驗證正式身分鏈、兩筆 RowVersion CAS、
一次 ExecuteTransactionRequest 及完整 31／32 欄位；錯教會、缺版本須在寫入前拒絕，
交易故障須保留同一例外且不重試。合成 SDK 僅記錄請求，沒有模擬資料庫交易，
因此本檔不能證明 D365 原子性、併發衝突處理或實機部署已通過。
資源與隔離：每個 DI scope 獨占一個合成 SDK；容器為唯一 Dispose owner。
A／B 同時存在但使用不同 SDK 與正式 ID，測試結束清空 Entity、請求與故障引用，
以存活計數回到零驗證釋放。測試無 Session、憑證、Secrets、HTTP、SQL 或檔案 I/O；
固定 invalid 網域及暫存根目錄只作啟動設定純值，不建立目錄、不解析 hosted service。
驗證方式：只執行本類別篩選；斷言真 adapter 所產生的讀取與交易內容，不把記錄器
收到交易請求誤稱為遠端資料已寫入。未預期 SDK 方法立即失敗，避免偷偷走無條件 Update。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.Webhooks;

/// <summary>
/// 透過正式 DI 組合入口測試私有 D365 adapter；只替換遠端傳輸，
/// 每案例的容器與 SDK 都有明確測試區塊 owner，不共用可變租戶資料。
/// </summary>
public sealed class LessonQrD365PublicationTests
{
    /// <summary>
    /// 以兩種既有 QR 張數驗證實際授權鏈與完整交易。
    /// 決定性斷言包含 contact guard、兩筆原始版本 CAS、每個 URL 及零額外寫入；
    /// scope 離開後 SDK 必須釋放，不能將此離線請求驗證視為資料庫原子性實測。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteredStore_AuthorizesOwnershipAndSubmitsOneCompleteCasTransaction(bool includePoll)
    {
        var owner = new SdkOwner();
        using var configuration = CreateConfiguration();
        using var provider = CreateProvider(configuration, owner);
        using (var scope = provider.CreateScope())
        {
            var sdk = Assert.IsType<RecordingSdk>(scope.ServiceProvider.GetRequiredService<IOrganizationService>());
            var store = scope.ServiceProvider.GetRequiredService<ILessonQrPublicationStore>();
            Assert.Equal("LessonQrD365Store", store.GetType().Name);
            var commit = store.Authorize(sdk.ChurchId, sdk.Lesson.Id, CancellationToken.None);
            Assert.Equal(new[] { "new_disciple_lessons", "contact" }, sdk.Reads.Select(read => read.Table));
            Assert.Equal(sdk.Lesson.Id, sdk.Reads[0].Id);
            Assert.Equal(sdk.Contact.Id, sdk.Reads[1].Id);
            Assert.Equal(new[] { "new_contact_new_disciple_lessons" }, sdk.Reads[0].Columns);
            Assert.Equal(new[] { "parentcustomerid" }, sdk.Reads[1].Columns);
            Assert.Empty(sdk.Transactions);

            // 模擬來源物件後續變動；commit 必須保存授權當下版本，不能借用可變 Entity。
            sdk.Lesson.RowVersion = "303";
            sdk.Contact.RowVersion = "404";
            var urls = CreateUrls(sdk.Lesson.Id, includePoll);
            commit.Commit(urls, CancellationToken.None);
            AssertTransaction(sdk, urls);
        }
        Assert.Equal(0, owner.Active);
        Assert.Equal(1, owner.Disposed);
    }

    /// <summary>
    /// 在合成來源注入錯教會或課程／聯絡人缺版本，真 Authorize 必須失敗，
    /// 不能產生 commit 或送出交易。不同讀取深度也確認失敗後沒有繼續取得其他資料；
    /// 每次失敗仍由 scope 釋放 SDK，使存活數回到零。
    /// </summary>
    [Theory]
    [InlineData("wrong-church", 2)]
    [InlineData("lesson-version", 1)]
    [InlineData("contact-version", 2)]
    public void RegisteredStore_RejectsWrongChurchOrMissingRowVersionBeforeWrite(string fault, int reads)
    {
        var owner = new SdkOwner();
        using var configuration = CreateConfiguration();
        using var provider = CreateProvider(configuration, owner);
        using (var scope = provider.CreateScope())
        {
            var sdk = Assert.IsType<RecordingSdk>(scope.ServiceProvider.GetRequiredService<IOrganizationService>());
            if (fault == "wrong-church")
                sdk.Contact["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
            else if (fault == "lesson-version")
                sdk.Lesson.RowVersion = null;
            else
                sdk.Contact.RowVersion = null;

            var store = scope.ServiceProvider.GetRequiredService<ILessonQrPublicationStore>();
            Assert.Throws<InvalidOperationException>(() =>
                store.Authorize(sdk.ChurchId, sdk.Lesson.Id, CancellationToken.None));
            Assert.Equal(reads, sdk.Reads.Count);
            Assert.Empty(sdk.Transactions);
        }
        Assert.Equal(0, owner.Active);
        Assert.Equal(1, owner.Disposed);
    }

    /// <summary>
    /// 在 SDK Execute 入場注入同一個故障物件，驗證真 Commit 不包裝、不吞例外、
    /// 不內部重試，且送出前已組好兩筆 CAS 及完整 URL。記錄器不模擬寫入或 rollback；
    /// 只證明產品使用單一交易請求，並在失敗後仍由 scope Dispose 排空本地資源。
    /// </summary>
    [Fact]
    public void RegisteredStore_PropagatesSameTransactionFailureWithoutRetry()
    {
        var owner = new SdkOwner();
        using var configuration = CreateConfiguration();
        using var provider = CreateProvider(configuration, owner);
        using (var scope = provider.CreateScope())
        {
            var sdk = Assert.IsType<RecordingSdk>(scope.ServiceProvider.GetRequiredService<IOrganizationService>());
            var failure = new InvalidOperationException("Synthetic SDK transaction failure.");
            sdk.ExecuteFailure = failure;
            var store = scope.ServiceProvider.GetRequiredService<ILessonQrPublicationStore>();
            var commit = store.Authorize(sdk.ChurchId, sdk.Lesson.Id, CancellationToken.None);
            var urls = CreateUrls(sdk.Lesson.Id, true);

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                commit.Commit(urls, CancellationToken.None)));
            AssertTransaction(sdk, urls);
        }
        Assert.Equal(0, owner.Active);
        Assert.Equal(1, owner.Disposed);
    }

    /// <summary>
    /// 同一容器同時開啟 A／B scope，各自讀取不同教會、contact 與課程，
    /// 交錯授權及提交後核對交易仍只含各自身分、版本及 URL。
    /// 拒絕跨 scope 課程讀取，再逐一 Dispose，確認另一 SDK 仍可使用且最終存活數為零。
    /// </summary>
    [Fact]
    public void RegisteredStore_UsesDistinctScopedSdksAndDrainsBothOwners()
    {
        var owner = new SdkOwner();
        using var configuration = CreateConfiguration();
        using var provider = CreateProvider(configuration, owner);
        using (var scopeA = provider.CreateScope())
        {
            var sdkA = Assert.IsType<RecordingSdk>(scopeA.ServiceProvider.GetRequiredService<IOrganizationService>());
            var storeA = scopeA.ServiceProvider.GetRequiredService<ILessonQrPublicationStore>();
            var urlsA = CreateUrls(sdkA.Lesson.Id, false);
            var commitA = storeA.Authorize(sdkA.ChurchId, sdkA.Lesson.Id, CancellationToken.None);
            using (var scopeB = provider.CreateScope())
            {
                var sdkB = Assert.IsType<RecordingSdk>(scopeB.ServiceProvider.GetRequiredService<IOrganizationService>());
                var storeB = scopeB.ServiceProvider.GetRequiredService<ILessonQrPublicationStore>();
                Assert.NotSame(sdkA, sdkB);
                Assert.NotSame(storeA, storeB);
                Assert.NotEqual(sdkA.ChurchId, sdkB.ChurchId);
                Assert.NotEqual(sdkA.Contact.Id, sdkB.Contact.Id);
                Assert.NotEqual(sdkA.Lesson.Id, sdkB.Lesson.Id);
                Assert.Equal(2, owner.Active);
                var commitB = storeB.Authorize(sdkB.ChurchId, sdkB.Lesson.Id, CancellationToken.None);
                var urlsB = CreateUrls(sdkB.Lesson.Id, true);
                commitB.Commit(urlsB, CancellationToken.None);
                commitA.Commit(urlsA, CancellationToken.None);
                AssertTransaction(sdkA, urlsA);
                AssertTransaction(sdkB, urlsB);
                Assert.Throws<InvalidOperationException>(() =>
                    storeB.Authorize(sdkA.ChurchId, sdkA.Lesson.Id, CancellationToken.None));
                Assert.Single(sdkB.Transactions);
            }
            Assert.Equal(1, owner.Active);
            Assert.Equal(1, owner.Disposed);
            Assert.NotNull(storeA.Authorize(sdkA.ChurchId, sdkA.Lesson.Id, CancellationToken.None));
        }
        Assert.Equal(0, owner.Active);
        Assert.Equal(2, owner.Disposed);
    }

    /// <summary>
    /// 僅從記憶體建立明確啟用 QR 的部署設定；不加 Secrets、環境或 reload provider。
    /// 圖片路徑與網址僅供建構不可變選項，本測試不解析 publisher、不建立檔案。
    /// </summary>
    private static ConfigurationRoot CreateConfiguration() =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lessons:Qr:Enabled"] = "true",
            ["Lessons:Qr:StorageDirectory"] = Path.GetTempPath(),
            ["Lessons:Qr:PublicBaseUrl"] = "https://qr.invalid/images/",
            ["Lessons:Qr:Churches:11111111-1111-1111-1111-111111111111:RegularPrefix"] = "https://qr.invalid/?QrCodeId=",
            ["Lessons:Qr:Churches:11111111-1111-1111-1111-111111111111:PollPrefix"] = "https://poll.invalid/?QrCodeId="
        }).Build();

    /// <summary>
    /// 正式 AddLineWebhook 註冊 private adapter，SDK factory 由每個 scope 擁有；
    /// 不解析 hosted services 或啟動 consumer，因此沒有遠端連線、隱藏 task 或 timer。
    /// </summary>
    private static ServiceProvider CreateProvider(IConfiguration configuration, SdkOwner owner)
    {
        var services = new ServiceCollection();
        services.AddScoped<IOrganizationService>(_ => owner.Open());
        services.AddGoodShepherdLineWebhook(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// 獨立列出既有十五堂簽到／簽退、報名與選填問卷欄位，不重用產品 plan，
    /// 避免產品若遺漏或改錯欄位，測試也以同一錯誤產生期望值而偽通過。
    /// </summary>
    private static IReadOnlyDictionary<string, string> CreateUrls(Guid lessonId, bool includePoll)
    {
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index <= 15; index++)
        {
            urls.Add($"new_{index}_signon_url", $"https://qr.invalid/{lessonId:D}/{index}-on.png");
            urls.Add($"new_{index}_signoff_url", $"https://qr.invalid/{lessonId:D}/{index}-off.png");
        }
        urls.Add("new_enroll_ursl", $"https://qr.invalid/{lessonId:D}/enroll.png");
        if (includePoll) urls.Add("new_poll_url", $"https://qr.invalid/{lessonId:D}/poll.png");
        return urls;
    }

    /// <summary>
    /// 核對一次完整交易的順序、表名、正式 ID、RowVersion 及每筆 CAS；
    /// contact 只能重申同一 account，課程只能帶完整 URL 集合，不能夾帶其他 scope 欄位。
    /// </summary>
    private static void AssertTransaction(RecordingSdk sdk, IReadOnlyDictionary<string, string> urls)
    {
        var transaction = Assert.Single(sdk.Transactions);
        Assert.False(transaction.ReturnResponses);
        Assert.Equal(2, transaction.Requests.Count);
        var guard = Assert.IsType<UpdateRequest>(transaction.Requests[0]);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, guard.ConcurrencyBehavior);
        Assert.Equal("contact", guard.Target.LogicalName);
        Assert.Equal(sdk.Contact.Id, guard.Target.Id);
        Assert.Equal("202", guard.Target.RowVersion);
        var ownership = Assert.IsType<EntityReference>(Assert.Single(guard.Target.Attributes).Value);
        Assert.True(guard.Target.Contains("parentcustomerid"));
        Assert.Equal("account", ownership.LogicalName);
        Assert.Equal(sdk.ChurchId, ownership.Id);
        Assert.Empty(ownership.KeyAttributes);
        var update = Assert.IsType<UpdateRequest>(transaction.Requests[1]);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
        Assert.Equal("new_disciple_lessons", update.Target.LogicalName);
        Assert.Equal(sdk.Lesson.Id, update.Target.Id);
        Assert.Equal("101", update.Target.RowVersion);
        Assert.Equal(urls.Count, update.Target.Attributes.Count);
        foreach (var (field, value) in urls)
            Assert.Equal(value, update.Target.GetAttributeValue<string>(field));
    }

    /// <summary>
    /// 單一案例的合成 SDK 計數 owner，只保留整數而不保留業務資料或已釋放服務；
    /// 工廠交付的 SDK 由 DI scope 唯一釋放，測試以存活數觀測確定性清理。
    /// </summary>
    private sealed class SdkOwner
    {
        /// <summary>尚未 Dispose 的 SDK 數，僅由本測試同步執行緒更新。</summary>
        public int Active { get; private set; }
        /// <summary>已完成清理的 SDK 數，用來識別漏釋放或重複釋放。</summary>
        public int Disposed { get; private set; }

        /// <summary>為新 scope 建立獨立資料與資源記錄，不將 SDK 存入 owner。</summary>
        public RecordingSdk Open()
        {
            Active++;
            return new RecordingSdk(this);
        }

        /// <summary>SDK 清空本地引用後通知 owner；不處理或保存任何租戶資料。</summary>
        public void Release()
        {
            Active--;
            Disposed++;
        }
    }

    /// <summary>
    /// 只支援精確主鍵 Retrieve 及單一交易 Execute 的離線 SDK 記錄器。
    /// 每實例資料只存活於一個 scope；不模擬持久寫入、交易原子性或資料庫 CAS 判定，
    /// 故障只在 Execute 注入，所有其他寫入入口拒絕，Dispose 清空記錄與來源。
    /// </summary>
    private sealed class RecordingSdk : IOrganizationService, IDisposable
    {
        private readonly SdkOwner _owner;
        private bool _disposed;

        /// <summary>建立單一 scope 的正式合成身分鏈及不相同版本，不連接任何服務。</summary>
        public RecordingSdk(SdkOwner owner)
        {
            _owner = owner;
            ChurchId = Guid.NewGuid();
            Contact = new Entity("contact", Guid.NewGuid()) { RowVersion = "202" };
            Contact["parentcustomerid"] = new EntityReference("account", ChurchId);
            Lesson = new Entity("new_disciple_lessons", Guid.NewGuid()) { RowVersion = "101" };
            Lesson["new_contact_new_disciple_lessons"] = new EntityReference("contact", Contact.Id);
        }

        /// <summary>本 SDK 所屬教會，其他 scope 永遠使用不同 ID。</summary>
        public Guid ChurchId { get; }
        /// <summary>只供本案例注入版本與關聯故障的合成課程。</summary>
        public Entity Lesson { get; }
        /// <summary>只供本案例注入版本與教會故障的合成聯絡人。</summary>
        public Entity Contact { get; }
        /// <summary>最多本案例數次讀取的純值記錄；Dispose 時清空。</summary>
        public List<(string Table, Guid Id, string[] Columns)> Reads { get; } = new();
        /// <summary>本案例送出的交易請求，保留到斷言完成後由 Dispose 清空。</summary>
        public List<ExecuteTransactionRequest> Transactions { get; } = new();
        /// <summary>選填的同一故障物件，不包裝且在 Dispose 釋放引用。</summary>
        public Exception? ExecuteFailure { get; set; }

        /// <summary>
        /// 只回傳目前 SDK 的精確正式 ID，跨 scope 或未預期資料立即拒絕。
        /// 保存 ColumnSet 純值快照供斷言，不保留呼叫端可變查詢物件。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Reads.Add((entityName, id, columnSet.Columns.ToArray()));
            if (entityName == Lesson.LogicalName && id == Lesson.Id) return Lesson;
            if (entityName == Contact.LogicalName && id == Contact.Id) return Contact;
            throw new InvalidOperationException("Synthetic SDK scope does not own this identity.");
        }

        /// <summary>
        /// 記錄真 adapter 組出的完整交易，必要時拋同一故障；不實作遠端寫入，
        /// 也不虛構合成回應具備資料庫提交或 rollback 保證。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Transactions.Add(Assert.IsType<ExecuteTransactionRequest>(request));
            if (ExecuteFailure is not null) throw ExecuteFailure;
            return new ExecuteTransactionResponse();
        }

        /// <summary>拒絕額外 Create，避免產品跳過既有課程的單一交易契約。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>拒絕無條件 Update，避免測試漏掉版本 guard 被繞過。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本測試不授權刪除正式資料，誤入立即失敗。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本契約只允許精確主鍵讀取，不允許廣泛或跨 scope 查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>不允許交易之外的關聯變更，誤入立即失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>不允許解除關聯，避免移除正式歸屬以繞過授權。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        /// <summary>
        /// 由 DI scope 同步且冪等釋放；沒有背景工作須等待，先清除 Entity、
        /// 交易與故障引用，再更新存活數，防止計數先歸零但資料仍被記錄器保留。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Lesson.Attributes.Clear();
            Contact.Attributes.Clear();
            Reads.Clear();
            Transactions.Clear();
            ExecuteFailure = null;
            _owner.Release();
        }
    }
}
