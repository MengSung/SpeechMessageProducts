/*
 * 檔案整體說明
 * 目的與定位：P5-07 真實課程 QR 發布器，將完整 PNG 候選存入部署者管理的圖片目錄，
 * 再以 Dataverse 交易一次回寫全部 URL。不是假上傳器，不使用 Session 或 LINE token。
 * 主要流程：重建設定式候選並比對每個欄位與內容，讀取正式課程→主要聯絡人→教會，
 * 確認授權及 RowVersion，再寫入同磁碟暫存目錄、flush、原子搬移完整圖片組。
 * 最後在同一交易內以聯絡人及課程的版本條件更新，任一競爭使全部 URL 回寫回復。
 * 資源與隔離：SDK 由單筆 DI scope 擁有，本服務只借用；stream 由 handler 擁有。
 * 暫存檔由持有固定 lease 檔獨占 handle 的呼叫唯一擁有，finally 關閉並刪除。程序中斷
 * 由 OS 釋放 handle，下次同事件重送先清除固定 staging，不累積隨機孤兒目錄。
 * 每事件只有一個固定 lease 檔與一組暫存圖片；lease 最多等三十秒，取消可提早離開。
 * lease 只保護檔案 staging 所有權，D365 唯一性仍由資料庫鍵與 CAS 負責。
 * 正式圖片是持久產品資料，依教會、
 * 課程與固定內容版本分區；目前來源只支援版本一，每課程最多兩組共 63 張、31.5 MiB。
 * 正式組不可覆寫；重送逐位元組核對，衝突拒絕，不用程序鎖替代跨程序唯一性。
 * 交易傳輸結果不明時不得刪除可能已被引用的圖片；同一路徑重試不增加檔案數量。
 * 部署者須提供專屬目錄與 HTTPS 靜態映射、檔案 ACL 及課程刪除時的持久資料清除。
 * 取消在 I/O 入場前及檔案複製傳遞；同步 SDK 入場後完整排空，不遺棄連線或 stream；
 * SDK 連線逾時由既有組合根設定。原失敗交 worker 共用告警；清理失敗另交共用入口，
 * 不覆蓋原交易結果或取消。無法刪除的 staging 仍限固定一組，後續重送再回收。
 * 不另建背景工作。PNG 編碼器或前綴改變造成同版本內容衝突時 fail closed，
 * 必須由部署者明確遷移／建立新內容版本，不允許重送靜默覆寫既有圖片。
 * 驗證：以真檔案系統、合成授權／提交接縫檢查完整候選、競爭、取消、重送及暫存清理；
 * 真正 SDK 交易在 Host 適配器，這些儲存測試沒有執行該適配器，不能代表其交易
 * 故障注入測試、D365 或靜態網址的實機驗收。
 */
namespace LineMessagingProcessor.RichMenus.QrImages;

/// <summary>Host 的真實 SDK 邊界；授權讀取及最後交易都必須借用同一工作 scope。</summary>
public interface ILessonQrPublicationStore
{
    /// <summary>讀取正式課程及聯絡人歸屬與版本；不符教會時在圖片 I/O 前拒絕。</summary>
    ILessonQrPublicationCommit Authorize(Guid churchId, Guid lessonId, CancellationToken token);
}

/// <summary>單次授權版本的交易候選；不得跨工作或跨教會快取。</summary>
public interface ILessonQrPublicationCommit
{
    /// <summary>以授權讀取的版本在同一交易回寫全部欄位；競爭或失敗全部回復。</summary>
    void Commit(IReadOnlyDictionary<string, string> urls, CancellationToken token);
}

/// <summary>部署者提供的固定儲存位置；路徑不接受待辦或 HTTP 輸入。</summary>
public sealed class LessonQrStorageOptions
{
    /// <summary>驗證專屬絕對目錄及無查詢、無憑證的 HTTPS 公開目錄。</summary>
    public LessonQrStorageOptions(string directory, string publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) ||
            !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || publicBaseUrl.Length > 1000)
            throw new ArgumentException("Invalid lesson QR storage configuration.");
        DirectoryPath = Path.GetFullPath(directory);
        PublicBaseUrl = uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    /// <summary>Host 獨占、部署者管理的圖片根目錄。</summary>
    public string DirectoryPath { get; }
    /// <summary>與根目錄對應的固定公開 HTTPS 目錄。</summary>
    public string PublicBaseUrl { get; }
}

/// <summary>單筆 scope 的真實圖片持久化及 D365 原子 URL 提交實作。</summary>
public sealed class LessonQrBundlePublicationService : ILessonQrBundlePublicationService
{
    private readonly ILessonQrPublicationStore _store;
    private readonly ILessonQrBundleOptionsProvider _options;
    private readonly LessonQrStorageOptions _storage;

    /// <summary>借用同工作 scope 的儲存邊界與設定；不承接 SDK 或 PNG 的 Dispose 責任。</summary>
    public LessonQrBundlePublicationService(ILessonQrPublicationStore store,
        ILessonQrBundleOptionsProvider options, LessonQrStorageOptions storage)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// 拒絕不完整候選與未授權教會，完整儲存後才送出單一交易；確認提交後不以後到取消
    /// 偽裝失敗。正式圖片保留供讀取與安全重送，暫存目錄永遠由本呼叫清理。
    /// </summary>
    public async Task PublishAndCommitAsync(Guid churchId, Guid lessonId, int contentVersion,
        IReadOnlyList<LessonQrRenderedImage> images, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (churchId == Guid.Empty || lessonId == Guid.Empty || contentVersion != 1 ||
            images is null || images.Count is not (31 or 32))
            throw new ArgumentException("Invalid lesson QR publication candidate.");
        var plan = LessonQrBundlePlan.Create(lessonId, contentVersion, images.Count == 32,
            _options.GetOptions(churchId));
        for (var index = 0; index < plan.Count; index++)
        {
            var image = images[index];
            if (image is null || image.Descriptor != plan.Images[index] ||
                image.Png is null || !image.Png.CanRead || !image.Png.CanSeek ||
                image.Png.Length is < 1 or > QrImageRenderer.MaximumPngBytes)
                throw new ArgumentException("Invalid lesson QR publication candidate.");
        }

        var commit = _store.Authorize(churchId, lessonId, cancellationToken);

        var relative = $"{churchId:N}/{lessonId:N}/v1-{images.Count}";
        var parent = Path.Combine(_storage.DirectoryPath, churchId.ToString("N"), lessonId.ToString("N"));
        Directory.CreateDirectory(parent);
        var final = Path.Combine(parent, $"v1-{images.Count}");
        await using var lease = await AcquireLeaseAsync(Path.Combine(parent, $".v1-{images.Count}.lock"),
            cancellationToken).ConfigureAwait(false);
        var temporary = Path.Combine(parent, $".staging-v1-{images.Count}");
        // 只有持有同事件獨占 handle 才能清除前一程序中斷留下的 staging。
        if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        Directory.CreateDirectory(temporary);
        Exception? originalFailure = null;
        try
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                image.Png.Position = 0;
                await using var file = new FileStream(Path.Combine(temporary, FileName(image)),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                await image.Png.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try { Directory.Move(temporary, final); }
            catch (IOException) when (Directory.Exists(final))
            {
                // 另一程序已發布時，只能核對不可變內容，不得刪除或覆寫其圖片。
                await VerifyExistingAsync(final, images, cancellationToken).ConfigureAwait(false);
            }

            var urls = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var image in images)
                urls.Add(image.Descriptor.FieldName, _storage.PublicBaseUrl + relative + "/" + FileName(image));
            cancellationToken.ThrowIfCancellationRequested();
            commit.Commit(new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(urls),
                cancellationToken);
        }
        catch (Exception error)
        {
            originalFailure = error;
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            }
            catch (Exception cleanupError) when (originalFailure is not null)
            {
                // 保留原始逾時／取消的型別及 worker 分類；清理本身仍是真正失敗，
                // 必須交既有告警入口，不能只記字串或靜默吞掉。
                ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(cleanupError,
                    "LessonQrBundlePublicationService.Cleanup", CancellationToken.None);
            }
        }
    }

    /// <summary>欄位已逐項比對固定 plan，因此不能以 caller 路徑逃出儲存目錄。</summary>
    private static string FileName(LessonQrRenderedImage image) => image.Descriptor.FieldName + ".png";

    /// <summary>
    /// 有界等待同事件檔案 lease，Share.None 防止跨程序共同清除 staging。
    /// lease 檔保留固定名稱，避免刪檔後另一 owner 開出不同 inode 的競爭。
    /// </summary>
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
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Lesson QR staging lease timed out.", error);
        }
    }

    /// <summary>有界逐檔比對已發布內容；相同身分不同內容拒絕，避免重送改寫其他工作的圖片。</summary>
    private static async Task VerifyExistingAsync(string directory,
        IReadOnlyList<LessonQrRenderedImage> images, CancellationToken token)
    {
        // 最多讀取 33 個名稱就拒絕異常目錄，不對受污染目錄執行無界掃描。
        if (Directory.EnumerateFileSystemEntries(directory).Take(33).Count() != images.Count)
            throw new InvalidOperationException("Lesson QR publication identity conflicts.");
        foreach (var image in images)
        {
            await using var existing = new FileStream(Path.Combine(directory, FileName(image)),
                FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (existing.Length != image.Png.Length)
                throw new InvalidOperationException("Lesson QR publication content conflicts.");
            image.Png.Position = 0;
            var left = new byte[81920];
            var right = new byte[81920];
            int count;
            while ((count = await existing.ReadAsync(left, token).ConfigureAwait(false)) != 0)
            {
                await image.Png.ReadExactlyAsync(right.AsMemory(0, count), token).ConfigureAwait(false);
                if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count)))
                    throw new InvalidOperationException("Lesson QR publication content conflicts.");
            }
        }
    }
}
