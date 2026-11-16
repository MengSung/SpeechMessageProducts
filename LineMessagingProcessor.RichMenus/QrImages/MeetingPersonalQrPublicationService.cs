/*
 * 檔案整體說明
 * 目的與定位：P5-09 真正圖片消費與發布，借用每筆 scope 的 SDK 授權／交易接縫，
 * 沿用既有 renderer、descriptor 及儲存選項，不建立第二個 QrImageGenerate consumer。
 * 主要流程：聚會先展開完整 URL 並驗證各欄最多五百字元，部署錯誤在 SDK／檔案 I/O 前拒絕；
 * 再讀權威歸屬與版本。個人一張以 PNG bytes 原子建立
 * annotation，聚會逐張寫暫存、flush 後原子搬移完整組，最後同交易 CAS 回寫三十八欄。
 * 資源與隔離：每次最多持有一個 512KiB PNG stream；每次 await 消費後立即 Dispose，
 * 不遺棄工作。所有路徑只由設定根與正式 GUID、固定欄位構成，不使用姓名或 HTTP。
 * 固定來源 lease 最多等三十秒；staging 唯一 owner 持有獨占 handle，finally 清理。
 * 正式檔案每教會／meeting／版本最多三十八張、19MiB，重送逐檔比對而不覆寫；
 * 個人不寫磁碟。部署者負責專屬目錄 ACL、靜態映射及正式記錄刪除時的圖片保留政策。
 * 失敗與驗證：CAS 失敗不發布 URL；檔案與 D365 不是分散式交易，已搬移但尚未被
 * URL 引用的固定有界圖片在交易不確定時保留供重送核對，絕不刪除可能已被引用的檔案。
 * 前綴／PNG 編碼器改變若導致同版本衝突，必須停用 worker、備份並核對既有 URL，
 * 由部署者協調遷移或另立內容版本；本版本固定為一，禁止自動刪除／覆寫來假裝重送成功。
 * 取消入場與複製傳遞原 token；同步 SDK 完整返回才釋放 scope。錯誤向 worker 告警
 * 邊界傳播；清理錯誤另交共用入口、不覆蓋主要錯誤。離線故障測試不替代實機驗收。
 */
using System.Collections.ObjectModel;
using SpeechMessage.Hosting.Outbound;

namespace LineMessagingProcessor.RichMenus.QrImages;

/// <summary>按正式教會 ID 提供不可變部署快照；未設定必須拒絕而非 fallback。</summary>
public interface IMeetingPersonalQrOptionsProvider
{
    /// <summary>取得目前工作的前綴，不保留 caller 或跨工作可變狀態。</summary>
    MeetingPersonalQrOptions GetOptions(Guid churchId);
}

/// <summary>區分 schema2 與 Lesson 的儲存設定，避免 DI 的相同型別互相覆蓋。</summary>
/// <param name="Storage">已驗證的部署固定根與公開 URL；不接受工作或 HTTP 路徑。</param>
public sealed record MeetingPersonalQrStorageOptions(LessonQrStorageOptions Storage);

/// <summary>scoped SDK 邊界；權威歸屬及全部 guard 必須在產圖前完成。</summary>
public interface IMeetingPersonalQrPublicationStore
{
    /// <summary>回傳操作局部版本快照；跨教會、缺來源或交易容量不足立即拒絕。</summary>
    IMeetingPersonalQrCommit Authorize(Guid churchId, MeetingPersonalQrPayload payload, CancellationToken token);
}

/// <summary>只活到本筆工作返回的交易候選，不能快取或跨 scope 使用。</summary>
public interface IMeetingPersonalQrCommit
{
    /// <summary>個人的權威 fullname；meeting 為空，不接受 payload 姓名。</summary>
    string FullName { get; }
    /// <summary>所有版本 CAS 與完整 URL／附件建立同交易；不作無條件更新。</summary>
    void Commit(IReadOnlyDictionary<string, string> urls, byte[]? personalPng, CancellationToken token);
}

/// <summary>單筆 handler 僅借用 scoped 發布器；資源由 publisher 及 DI scope 確定性擁有。</summary>
/// <param name="publication">單筆 scope 發布器，完整 await 後才能關閉 DI scope。</param>
/// <param name="churchId">待辦教會，仍由正式來源重驗，不單憑輸入授權。</param>
/// <param name="payload">封閉五欄解析結果，只有固定版本與完整圖片數量。</param>
public sealed class MeetingPersonalQrHandler(MeetingPersonalQrPublicationService publication,
    Guid churchId, MeetingPersonalQrPayload payload) : IOutboundTaskHandler
{
    /// <summary>完整 await 單筆發布；錯誤不吞掉，由原有 worker 分類並通知。</summary>
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        publication.ExecuteAsync(churchId, payload, cancellationToken);
}

/// <summary>串起權威授權、純值候選、單 stream 產圖及原子資料庫提交。</summary>
public sealed class MeetingPersonalQrPublicationService
{
    private readonly IMeetingPersonalQrPublicationStore _store;
    private readonly IMeetingPersonalQrOptionsProvider _options;
    private readonly LessonQrStorageOptions _storage;
    private readonly ILessonQrImageRenderer _renderer;

    /// <summary>只借用 scoped 依賴；預設 renderer 無狀態，不持有跨教會資源。</summary>
    public MeetingPersonalQrPublicationService(IMeetingPersonalQrPublicationStore store,
        IMeetingPersonalQrOptionsProvider options, MeetingPersonalQrStorageOptions storage,
        ILessonQrImageRenderer? renderer = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storage = storage?.Storage ?? throw new ArgumentNullException(nameof(storage));
        _renderer = renderer ?? new QrImageRenderer();
    }

    /// <summary>所有授權與候選驗證在圖片 I/O 前完成，已確認提交不以後到取消偽裝失敗。</summary>
    public async Task ExecuteAsync(Guid churchId, MeetingPersonalQrPayload payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (churchId == Guid.Empty) throw new ArgumentException("QR church identity is required.");
        var options = _options.GetOptions(churchId);
        if (payload.Kind == "meeting")
            options.RequireMeeting(payload.RecordId);
        var meetingPlan = payload.Kind == "meeting" ? MeetingPersonalQrPlan.Create(payload, options, "") : null;
        var urls = meetingPlan is null ? null : CreateMeetingUrls(churchId, payload, meetingPlan);
        var commit = _store.Authorize(churchId, payload, token);
        var plan = meetingPlan ?? MeetingPersonalQrPlan.Create(payload, options, commit.FullName);
        if (payload.Kind == "personal")
        {
            await using var png = await RenderAsync(plan[0], token).ConfigureAwait(false);
            var bytes = new byte[checked((int)png.Length)];
            await png.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            commit.Commit(new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()), bytes, token);
            return;
        }
        await PublishMeetingAsync(churchId, payload, plan, urls!, commit, token).ConfigureAwait(false);
    }

    /// <summary>完整展開固定三十八 URL；依 metadata 五百字元上限在 SDK、renderer 與磁碟前拒絕。</summary>
    private IReadOnlyDictionary<string, string> CreateMeetingUrls(Guid churchId, MeetingPersonalQrPayload payload,
        IReadOnlyList<LessonQrImageDescriptor> plan)
    {
        var relative = $"{churchId:N}/meeting/{payload.RecordId:N}/v1-38";
        var urls = plan.ToDictionary(d => d.FieldName,
            d => _storage.PublicBaseUrl + relative + "/" + d.FieldName + ".png", StringComparer.Ordinal);
        if (urls.Values.Any(url => url.Length > 500))
            throw new ArgumentException("Meeting QR public URL exceeds metadata capacity.");
        return new ReadOnlyDictionary<string, string>(urls);
    }

    /// <summary>逐張持有 PNG；不保留整組 streams，任何失敗都不能提交部分欄位。</summary>
    private async Task PublishMeetingAsync(Guid churchId, MeetingPersonalQrPayload payload,
        IReadOnlyList<LessonQrImageDescriptor> plan, IReadOnlyDictionary<string, string> urls,
        IMeetingPersonalQrCommit commit, CancellationToken token)
    {
        var parent = Path.Combine(_storage.DirectoryPath, churchId.ToString("N"), "meeting", payload.RecordId.ToString("N"));
        Directory.CreateDirectory(parent);
        await using var lease = await AcquireLeaseAsync(Path.Combine(parent, ".v1-38.lock"), token).ConfigureAwait(false);
        var temporary = Path.Combine(parent, ".staging-v1-38");
        var final = Path.Combine(parent, "v1-38");
        try
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            Directory.CreateDirectory(temporary);
            foreach (var descriptor in plan)
            {
                token.ThrowIfCancellationRequested();
                await using var png = await RenderAsync(descriptor, token).ConfigureAwait(false);
                await using var file = new FileStream(Path.Combine(temporary, descriptor.FieldName + ".png"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                await png.CopyToAsync(file, 81920, token).ConfigureAwait(false);
                file.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(final))
                await VerifyExistingAsync(temporary, final, plan, token).ConfigureAwait(false);
            else
                Directory.Move(temporary, final);
            token.ThrowIfCancellationRequested();
            commit.Commit(urls, null, token);
        }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            catch (Exception cleanup)
            {
                // 不覆蓋原始失敗，也不把已確認成功的 DB commit 偽裝成未提交。
                // 留下的 staging 仍為固定一組，下次取得同來源 lease 時才能回收。
                ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(cleanup,
                    "MeetingPersonalQrPublicationService.Cleanup", CancellationToken.None);
            }
        }
    }

    /// <summary>驗證可讀可定位、有界 PNG 標頭；成功才移交 owner，失敗自行釋放。</summary>
    private async Task<Stream> RenderAsync(LessonQrImageDescriptor descriptor, CancellationToken token)
    {
        var png = await _renderer.RenderAsync(descriptor.Content, token).ConfigureAwait(false);
        try
        {
            if (png is null || !png.CanRead || !png.CanSeek ||
                png.Length is < 8 or > QrImageRenderer.MaximumPngBytes)
                throw new InvalidDataException("Invalid QR PNG stream.");
            png.Position = 0;
            var signature = new byte[8];
            await png.ReadExactlyAsync(signature, token).ConfigureAwait(false);
            if (!signature.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("Invalid QR PNG signature.");
            png.Position = 0;
            return png;
        }
        catch
        {
            if (png is not null) await png.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>固定檔案 lease 有界三十秒，避免刪檔換 inode；只有持有者清除 staging。</summary>
    private static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
                { await Task.Delay(25, deadline.Token).ConfigureAwait(false); }
            }
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("QR publication lease timed out.", error); }
    }

    /// <summary>重送只能逐位元組比對不可變完整組，最多讀三十九個名稱且每檔最多 512KiB。</summary>
    private static async Task VerifyExistingAsync(string temporary, string final,
        IReadOnlyList<LessonQrImageDescriptor> plan, CancellationToken token)
    {
        if (Directory.EnumerateFileSystemEntries(final).Take(39).Count() != plan.Count)
            throw new InvalidOperationException("QR publication identity conflicts.");
        var left = new byte[81920];
        var right = new byte[81920];
        foreach (var descriptor in plan)
        {
            await using var existing = File.OpenRead(Path.Combine(final, descriptor.FieldName + ".png"));
            await using var candidate = File.OpenRead(Path.Combine(temporary, descriptor.FieldName + ".png"));
            if (existing.Length != candidate.Length || existing.Length > QrImageRenderer.MaximumPngBytes)
                throw new InvalidOperationException("QR publication content conflicts.");
            int read;
            while ((read = await existing.ReadAsync(left, token).ConfigureAwait(false)) > 0)
            {
                await candidate.ReadExactlyAsync(right.AsMemory(0, read), token).ConfigureAwait(false);
                if (!left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read)))
                    throw new InvalidOperationException("QR publication content conflicts.");
            }
        }
    }
}
