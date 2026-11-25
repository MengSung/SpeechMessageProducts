/*
檔案整體說明
目的與定位：承載好牧人產品專屬的課程 QR worker 註冊與 Dataverse 授權 adapter；這些規則依賴 GoodShepherd.Core，不屬於共用 Webhook pipeline。
主要流程：由產品組合根先處理 meeting/personal QR 的啟用組合，再嚴格讀取 Lessons:Qr:Enabled；明確啟用時註冊課程 QR publisher、scoped D365 store 與唯一 QrImageGenerate worker。worker 對每次 store 操作建立短 scope，並只接受該 QR 待辦類型。發布時重新核對 lesson→contact→church 正式主鍵與 RowVersion，最後以同一筆 Dataverse transaction 提交 contact 歸屬 guard 及完整 URL 候選。
資源與隔離：singleton worker/store 僅借用 IServiceScopeFactory；每次同步 SDK 操作以 using 關閉 scope，SDK 不提升至 singleton。授權與 commit 候選只存於單筆工作，scope 關閉後不保留 Entity、SDK、token 或租戶狀態。取消在查詢及交易前檢查；同步 SDK 呼叫由部署 transport timeout 限制，worker drain 後才 Dispose scope。
失敗與驗證：錯誤設定、缺欄位、錯教會、無效版本或不完整 URL 均 fail closed，不執行部分發布；SDK 例外原樣交給既有 worker 診斷。離線測試驗證授權與交易請求，不宣稱 Dataverse 實機原子性或 Plugin 行為已驗收。
*/
using SpeechMessageProducts.GoodShepherd.Hosting.QrImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Outbound;
using CoreOutbound = ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;

/// <summary>好牧人課程 QR 產品註冊與其 scoped Dataverse adapter。</summary>
public static partial class GoodShepherdWebhookRegistration
{
    /// <summary>
    /// 只有部署明確啟用課程 QR 且未由 meeting QR 註冊建立相同 worker 時，
    /// 才加入課程 publisher、scoped store 與唯一背景 worker。
    /// </summary>
    /// <param name="services">由 GoodShepherdWebhookRegistration 擁有的 Host 服務集合。</param>
    /// <param name="configuration">Host 擁有的部署設定；本方法只讀取 QR 啟動快照。</param>
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

    /// <summary>
    /// 將有界 QR 工作 store 介接至共用 outbound worker。
    /// 每個方法只在同步 SDK 操作期間建立 scope，回傳的工作資料為 detached 純值。
    /// </summary>
    /// <param name="scopes">Host 擁有的 scope factory；store 不保留任何 scoped SDK。</param>
    private sealed class LessonQrWorkStore(IServiceScopeFactory scopes) : IOutboundWorkStore
    {
        /// <summary>在 SDK 查詢端限制課程 QR 類型，並回傳 detached 候選快照。</summary>
        /// <param name="now">worker 本輪使用的 UTC 時間。</param>
        /// <param name="maximum">呼叫端要求的候選上限，由共用 store 驗證與套用。</param>
        /// <param name="token">本次同步 SDK 查詢的取消訊號。</param>
        /// <returns>本次查詢取得的不可變工作值集合。</returns>
        public IReadOnlyList<OutboundWorkItem> ReadCandidates(DateTime now, int maximum, CancellationToken token)
        {
            using var scope = scopes.CreateScope();
            using var store = Open(scope.ServiceProvider);
            return store.ReadCandidates(now, maximum, token).Select(ToWork).ToArray();
        }

        /// <summary>以短 scope 執行課程 QR 工作的 compare-and-swap 認領。</summary>
        /// <param name="item">候選工作純值；其類型會在轉換前再次核對。</param>
        /// <param name="now">本次認領的 UTC 時間。</param>
        /// <param name="token">本次同步 SDK 操作的取消訊號。</param>
        /// <returns>成功認領的 detached 工作，或表示競爭失敗的 null。</returns>
        public OutboundWorkItem? TryClaim(OutboundWorkItem item, DateTime now, CancellationToken token)
        {
            using var scope = scopes.CreateScope();
            using var store = Open(scope.ServiceProvider);
            var claim = store.TryClaim(ToRecord(item), now, token);
            return claim is null ? null : ToWork(claim);
        }

        /// <summary>以本次工作版本結算；逾期 owner 不得覆寫後續認領結果。</summary>
        /// <param name="item">已認領的工作版本。</param>
        /// <param name="status">worker 計算出的結算狀態。</param>
        /// <param name="attempts">已完成的嘗試次數。</param>
        /// <param name="now">本次結算 UTC 時間。</param>
        /// <param name="next">若可重試，下一次執行時間。</param>
        /// <param name="error">共用 worker 定義的安全錯誤分類。</param>
        /// <param name="token">本次同步 SDK 操作的取消訊號。</param>
        /// <returns>只有仍持有該版本時才回傳 true。</returns>
        public bool TrySettle(OutboundWorkItem item, OutboundWorkStatus status, int attempts,
            DateTime now, DateTime? next, string? error, CancellationToken token)
        {
            using var scope = scopes.CreateScope();
            using var store = Open(scope.ServiceProvider);
            return store.TrySettle(ToRecord(item), (CoreOutbound.OutboundTaskStatus)status,
                attempts, now, next, error, token);
        }

        /// <summary>只借用目前 scope 的 SDK，Core 查詢 allowlist 固定為課程 QR 類型。</summary>
        private static CoreOutbound.D365OutboundTaskStore Open(IServiceProvider provider) =>
            new(provider.GetRequiredService<IOrganizationService>(),
                new[] { CoreOutbound.OutboundTaskType.QrImageGenerate });

        /// <summary>將 Core 已驗證的列轉成不含 Entity／SDK 的 Hosting 純值。</summary>
        private static OutboundWorkItem ToWork(CoreOutbound.OutboundTaskRecord row) =>
            new(row.Id, row.RowVersion, row.ChurchId, row.ContactId, (OutboundWorkType)row.Type,
                row.PayloadJson, row.IdempotencyKey, (OutboundWorkStatus)row.Status,
                row.Attempts, row.NextAttemptOn, row.ClaimedOn);

        /// <summary>僅接受 QR 工作類型，並以 Core record constructor 重驗正式欄位。</summary>
        private static CoreOutbound.OutboundTaskRecord ToRecord(OutboundWorkItem row)
        {
            if (row.Type != OutboundWorkType.QrImageGenerate)
                throw new InvalidOperationException("Invalid lesson QR work type.");
            return new(row.Id, row.RowVersion, row.ChurchId, row.ContactId,
                CoreOutbound.OutboundTaskType.QrImageGenerate, row.PayloadJson, row.IdempotencyKey,
                (CoreOutbound.OutboundTaskStatus)row.Status, row.Attempts, row.NextAttemptOn, row.ClaimedOn);
        }
    }

    /// <summary>
    /// 單筆課程 QR 工作的 Dataverse 授權與提交 adapter；只借用所屬 async scope 的 SDK，
    /// 不建立連線或把 SDK 保存在 singleton。
    /// </summary>
    /// <param name="sdk">目前工作 scope 擁有的 Dataverse SDK。</param>
    private sealed class LessonQrD365Store(IOrganizationService sdk) : ILessonQrPublicationStore
    {
        /// <summary>核對 lesson→contact→church 權威鏈及版本，未授權時不建立 commit 候選。</summary>
        /// <param name="churchId">共用工作列所屬教會 ID，必須與正式 contact 歸屬一致。</param>
        /// <param name="lessonId">待發布課程的正式資料列 ID。</param>
        /// <param name="token">每筆工作的取消訊號，在每次同步 SDK 查詢前檢查。</param>
        /// <returns>僅包含本次授權版本的短生命期 commit owner。</returns>
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

        /// <summary>以精確正式 ID 讀最小欄位投影；缺少 RowVersion 時拒絕無條件寫入。</summary>
        private Entity Read(string table, Guid id, string column, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var row = sdk.Retrieve(table, id, new ColumnSet(column));
            return row is not null && row.Id == id && row.LogicalName == table &&
                !string.IsNullOrWhiteSpace(row.RowVersion) ? row :
                throw new InvalidOperationException("Invalid lesson QR source identity or version.");
        }

        /// <summary>lookup 必須是指定表名的正式非空主鍵；替代鍵與顯示值不能成為授權依據。</summary>
        private static Guid Lookup(Entity row, string column, string table) =>
            row.Attributes.TryGetValue(column, out var value) && value is EntityReference reference &&
            reference.Id != Guid.Empty && reference.LogicalName == table && reference.KeyAttributes.Count == 0
                ? reference.Id : throw new InvalidOperationException("Invalid lesson QR source ownership.");
    }

    /// <summary>保存單次授權的 RowVersion 與 SDK 借用參照，僅存活於該工作 scope。</summary>
    /// <param name="sdk">由同一工作 scope 擁有的 Dataverse SDK。</param>
    /// <param name="churchId">已授權的教會正式 ID。</param>
    /// <param name="lessonId">已授權課程正式 ID。</param>
    /// <param name="lessonVersion">授權讀取時的課程 RowVersion。</param>
    /// <param name="contactId">已授權聯絡人正式 ID。</param>
    /// <param name="contactVersion">授權讀取時的聯絡人 RowVersion。</param>
    private sealed class LessonQrD365Commit(IOrganizationService sdk, Guid churchId,
        Guid lessonId, string lessonVersion, Guid contactId, string contactVersion) : ILessonQrPublicationCommit
    {
        /// <summary>以同一交易提交完整 QR URL 與 contact 歸屬 guard；拒絕不完整或無效候選。</summary>
        /// <param name="urls">publisher 建立的完整欄位候選；欄位集合須與正式 QR plan 相同。</param>
        /// <param name="token">單筆工作取消訊號；交易送出前會再次檢查。</param>
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
}
