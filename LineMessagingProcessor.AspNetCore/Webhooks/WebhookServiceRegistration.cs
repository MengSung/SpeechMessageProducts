/*
檔案整體說明
目的與定位：提供共用 Webhook 契約註冊與真正 Host 啟動前的 fail-closed 守門，只驗證必要依賴，不安裝任何假正式服務。
主要功能與流程：從部署 section 建立獨立 WebhookOptions，再以 HostedLifecycleService.StartingAsync 在所有服務 StartAsync 之前驗證三依賴可在 scope 內解析；Host 平行啟動也不能提前接收 HTTP。只保存 ScopeFactory，不捕獲 scoped verifier、SQL 連線或 queue。
使用邊界與維護：服務集合僅組合根單寫；同入口註冊重複拒絕，避免設定衝突。探測 scope 在 StartAsync 返回前釋放，缺任一依賴就固定錯誤拒絕，容器仍由 Host Dispose。正式空 secret／SQL／queue 初始化政策由後續包實作；本守門不宣稱遠端健康。
課程 QR：Lessons:Qr:Enabled 明確啟用時建立獨立 QR worker，僅認領 QrImageGenerate；
不覆蓋付款 worker。每次 store 操作及每筆圖片工作使用獨立 DI scope 的授權 SDK，
課程與聯絡人版本 guard 在同一 D365 交易提交完整 URL。圖片由 QR publisher 管理。
版本不符、缺歸屬或缺前綴 fail closed；同步 SDK 完整排空後才釋放 scope，
真正失敗交既有 OutboundTaskWorker 共用告警。部署 SDK 必須有有限 I/O 逾時；
聯絡人 no-op CAS 仍可能觸發 Update Plugin，實機驗收須核對註冊及 optimistic concurrency。
*/
using LineMessagingProcessor.RichMenus.QrImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Outbound;
using CoreOutbound = ToolUtilityNameSpace.Outbound;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>只註冊不可變選項及啟動守門；正式 verifier／deduplicator／queue 必須由後續包明確提供。</summary>
public static partial class WebhookServiceRegistration
{
    /// <summary>從部署設定嚴格讀取 legacy 布林及 channel；不訂閱 reload、不建立隱藏容器或預設服務。</summary>
    /// <param name="services">尚未 Build 的單一 Host 服務集合。</param>
    /// <param name="configuration">Host 擁有的受信任組態，只借用於建立快照。</param>
    /// <returns>原集合；缺正式依賴於 Host.StartAsync 拒絕，避免接收未受保護的 HTTP。</returns>
    public static IServiceCollection AddLineWebhook(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(d => d.ServiceType == typeof(WebhookOptions)))
            throw new InvalidOperationException("Webhook registration must have a single owner.");
        var flag = configuration.GetSection("LineWebhook:EnableLegacyCallback");
        var channel = configuration.GetSection("LineWebhook:LegacyChannelId");
        var enabled = false;
        if (flag.GetChildren().Any() || (flag.Value is not null && !bool.TryParse(flag.Value, out enabled)) || channel.GetChildren().Any())
            throw new InvalidOperationException("Invalid webhook startup configuration.");
        var options = new WebhookOptions(enabled, channel.Value);
        // 三個具名編譯期接縫各有獨立檔案 owner；本包沒有實作，呼叫會被編譯器移除。
        // 不是反射探索或 no-op 正式服務；因此未完成任何後續包時仍由 startup probe 拒絕。
        RegisterSignature(services, configuration);
        RegisterDeduplication(services, configuration);
        RegisterQueue(services, configuration);
        RegisterDispatch(services, configuration);
        RegisterLessonQr(services, configuration);
        services.AddRouting();
        services.AddSingleton(options);
        services.AddHostedService<WebhookStartupRegistration>();
        return services;
    }

    /// <summary>P3-03 在 WebhookSignatureRegistration.cs 實作此 partial method，只註冊正式驗簽及啟動設定守門，不改 Program。</summary>
    static partial void RegisterSignature(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-04 在 WebhookDeduplicationRegistration.cs 實作此 partial method，獨立註冊 SQL owner 及有界清理，不增加 A.8 欄位。</summary>
    static partial void RegisterDeduplication(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-05 在 WebhookQueueRegistration.cs 實作此 partial method，註冊有界 queue／背景 drain owner，不改其他包檔案。</summary>
    static partial void RegisterQueue(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-06 註冊真 scoped SDK event dispatcher、範圍驗證及具名 HTTP adapter。</summary>
    static partial void RegisterDispatch(IServiceCollection services, IConfiguration configuration);

    /// <summary>明確啟用才註冊 QR 專用 worker；無效開關拒絕，不替代付款的介面註冊。</summary>
    private static void RegisterLessonQr(IServiceCollection services, IConfiguration configuration)
    {
        if (RegisterMeetingPersonalQr(services, configuration)) return;
        var flag = configuration.GetSection("Lessons:Qr:Enabled");
        if (flag.GetChildren().Any() ||
            (flag.Value is not null && !bool.TryParse(flag.Value, out _)))
            throw new InvalidOperationException("Invalid lesson QR enabled configuration.");
        if (!bool.TryParse(flag.Value, out var enabled) || !enabled) return;
        services.AddLessonQrPublication(configuration);
        services.AddScoped<ILessonQrPublicationStore, LessonQrD365Store>();
        services.AddSingleton<IHostedService>(provider =>
            new OutboundTaskWorker(
                new LessonQrWorkStore(provider.GetRequiredService<IServiceScopeFactory>()),
                provider.GetRequiredService<LessonQrBundleHostFactory>()));
    }

    /// <summary>每次 store 操作借用獨立 scope 的 SDK，只允許 QR 類型，不捕獲產品或教會狀態。</summary>
    private sealed class LessonQrWorkStore(IServiceScopeFactory scopes) : IOutboundWorkStore
    {
        /// <summary>在 SDK 查詢端限制類型，回傳有界 detached 候選。</summary>
        public IReadOnlyList<OutboundWorkItem> ReadCandidates(DateTime now, int maximum, CancellationToken token)
        {
            using var scope = scopes.CreateScope();
            using var store = Open(scope.ServiceProvider);
            return store.ReadCandidates(now, maximum, token).Select(ToWork).ToArray();
        }

        /// <summary>CAS 認領在同一短 scope 排空，不保存 SDK 到其他 store 操作。</summary>
        public OutboundWorkItem? TryClaim(OutboundWorkItem item, DateTime now, CancellationToken token)
        {
            using var scope = scopes.CreateScope();
            using var store = Open(scope.ServiceProvider);
            var claim = store.TryClaim(ToRecord(item), now, token);
            return claim is null ? null : ToWork(claim);
        }

        /// <summary>依本次 owner 版本結算，過期 owner 不得覆寫新 owner。</summary>
        public bool TrySettle(OutboundWorkItem item, OutboundWorkStatus status, int attempts,
            DateTime now, DateTime? next, string? error, CancellationToken token)
        {
            using var scope = scopes.CreateScope();
            using var store = Open(scope.ServiceProvider);
            return store.TrySettle(ToRecord(item), (CoreOutbound.OutboundTaskStatus)status,
                attempts, now, next, error, token);
        }

        /// <summary>Core store 只借用當次 scope SDK，型別白名單不可由待辦改寫。</summary>
        private static CoreOutbound.D365OutboundTaskStore Open(IServiceProvider provider) =>
            new(provider.GetRequiredService<IOrganizationService>(),
                new[] { CoreOutbound.OutboundTaskType.QrImageGenerate });

        /// <summary>Core 驗證後的正式列轉為 Hosting 純值，不帶 Entity 或 SDK。</summary>
        private static OutboundWorkItem ToWork(CoreOutbound.OutboundTaskRecord row) =>
            new(row.Id, row.RowVersion, row.ChurchId, row.ContactId, (OutboundWorkType)row.Type,
                row.PayloadJson, row.IdempotencyKey, (OutboundWorkStatus)row.Status,
                row.Attempts, row.NextAttemptOn, row.ClaimedOn);

        /// <summary>只接受 QR 工作並經 Core constructor 重驗值及邊界。</summary>
        private static CoreOutbound.OutboundTaskRecord ToRecord(OutboundWorkItem row)
        {
            if (row.Type != OutboundWorkType.QrImageGenerate)
                throw new InvalidOperationException("Invalid lesson QR work type.");
            return new(row.Id, row.RowVersion, row.ChurchId, row.ContactId,
                CoreOutbound.OutboundTaskType.QrImageGenerate, row.PayloadJson, row.IdempotencyKey,
                (CoreOutbound.OutboundTaskStatus)row.Status, row.Attempts, row.NextAttemptOn, row.ClaimedOn);
        }
    }

    /// <summary>單筆工作 SDK 授權邊界，只保存 scope 擁有的 SDK，不存入 singleton。</summary>
    private sealed class LessonQrD365Store(IOrganizationService sdk) : ILessonQrPublicationStore
    {
        /// <summary>正式課程→主要聯絡人→教會，缺版本或錯配在檔案 I/O 前拒絕。</summary>
        public ILessonQrPublicationCommit Authorize(Guid churchId, Guid lessonId, CancellationToken token)
        {
            if (churchId == Guid.Empty || lessonId == Guid.Empty)
                throw new InvalidOperationException("Invalid lesson QR identity.");
            var lesson = Read("new_disciple_lessons", lessonId, "new_contact_new_disciple_lessons", token);
            var contactId = Lookup(lesson, "new_contact_new_disciple_lessons", "contact");
            var contact = Read("contact", contactId, "parentcustomerid", token);
            if (Lookup(contact, "parentcustomerid", "account") != churchId)
                throw new InvalidOperationException("Lesson QR church authorization failed.");
            return new LessonQrD365Commit(sdk, churchId, lesson.Id, lesson.RowVersion,
                contact.Id, contact.RowVersion);
        }

        /// <summary>精確查正式 ID；沒有版本不得退回無條件 Update。</summary>
        private Entity Read(string table, Guid id, string column, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var row = sdk.Retrieve(table, id, new ColumnSet(column));
            return row is not null && row.Id == id && row.LogicalName == table &&
                !string.IsNullOrWhiteSpace(row.RowVersion) ? row :
                throw new InvalidOperationException("Invalid lesson QR source identity or version.");
        }

        /// <summary>lookup 必須具備正式非空 ID 及正確表名，替代鍵及顯示名稱不是授權。</summary>
        private static Guid Lookup(Entity row, string column, string table) =>
            row.Attributes.TryGetValue(column, out var value) && value is EntityReference reference &&
            reference.Id != Guid.Empty && reference.LogicalName == table && reference.KeyAttributes.Count == 0
                ? reference.Id : throw new InvalidOperationException("Invalid lesson QR source ownership.");
    }

    /// <summary>單次授權版本的原子提交；只活到本次 publisher 返回，不跨工作重用。</summary>
    private sealed class LessonQrD365Commit(IOrganizationService sdk, Guid churchId,
        Guid lessonId, string lessonVersion, Guid contactId, string contactVersion) : ILessonQrPublicationCommit
    {
        /// <summary>聯絡人歸屬 CAS 及課程 URL CAS 同交易；任一失敗整組回復，不吞 SDK 例外。</summary>
        public void Commit(IReadOnlyDictionary<string, string> urls, CancellationToken token)
        {
            if (urls.Count is not (31 or 32))
                throw new InvalidOperationException("Invalid lesson QR URL count.");
            var expected = LessonQrBundlePlan.Create(lessonId, 1, urls.Count == 32,
                new LessonQrBundleOptions("https://validation.invalid/?QrCodeId=", "https://validation.invalid/?QrCodeId="));
            var lesson = new Entity("new_disciple_lessons", lessonId) { RowVersion = lessonVersion };
            foreach (var descriptor in expected.Images)
            {
                if (!urls.TryGetValue(descriptor.FieldName, out var url) || url.Length > 2000 ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("Invalid lesson QR URL candidate.");
                lesson[descriptor.FieldName] = url;
            }
            var contact = new Entity("contact", contactId) { RowVersion = contactVersion };
            contact["parentcustomerid"] = new EntityReference("account", churchId);
            var requests = new OrganizationRequestCollection
            {
                new UpdateRequest { Target = contact, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches },
                new UpdateRequest { Target = lesson, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }
            };
            token.ThrowIfCancellationRequested();
            sdk.Execute(new ExecuteTransactionRequest { Requests = requests, ReturnResponses = false });
        }
    }

    /// <summary>啟動的短生命週期依賴探測器；scope 自行釋放，從不將 request service 提升成 singleton。</summary>
    private sealed class WebhookStartupRegistration(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
    {
        /// <summary>三個必填介面都能解析才允許開始 HTTP；缺任何一個直接拋固定失敗，不讀遠端或輸出憑證。</summary>
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<IWebhookSignatureVerifier>() is null ||
                scope.ServiceProvider.GetService<IWebhookEventDeduplicator>() is null ||
                scope.ServiceProvider.GetService<IWebhookEventQueue>() is null)
                throw new InvalidOperationException("Webhook requires signature, deduplication and queue implementations.");
        }

        /// <summary>必要驗證已在 StartingAsync 完成，這裡不可再延後檢查，否則平行啟動會產生接收空窗。</summary>
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>不保存啟動後狀態或訂閱；真正 queue 的 consumer 由後續包擁有。</summary>
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>探測 scope 已確定釋放，不提早停止其他 owner 的在途 HTTP 或 queue。</summary>
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>本探測器不保存業務資源，停止無需背景工作；真正 queue drain 由 queue owner 執行。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>停止後無剩餘 task、timer 或資料集合，不新增跨 Host 可變狀態。</summary>
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
