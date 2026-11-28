/*
檔案整體說明
目的與定位：F7公開Logo的檔案系統實作，只接受伺服器授權後的固定產品與租戶GUID，沒有HTTP、Session、credential或資料庫依賴。Logo明確公開，不能用於私人附件。
主要功能與流程：最多借讀5MiB加一byte，僅識別PNG／JPEG，先檢查8192邊長及40M像素，再解碼第一影格、校正方向與中心裁切640方形；無metadata的PNG經至多兩輪無損壓縮仍超過300KiB即拒絕。SHA與64bit隨機值組成新檔名，同目錄CreateNew候選、flush與關閉後才不覆寫搬移並返回URL。
使用邊界與維護：每次輸入、像素、輸出及候選檔只有該次操作擁有，完成或故障皆確定釋放／清理，不重用跨租戶buffer或像素池。唯一store owner最多接兩個操作，滿載立即拒絕；每次30秒合作式逾時，借入Stream必須遵守取消且始終由caller處置。DisposeAsync停止接單，等待既有工作清理，再釋放私有allocator；沒有背景task或無界等待列。
信任與失敗處理：部署根與每層目錄都拒絕reparse；專用owner及限制其他寫入者的ACL仍是前提，不能抵抗惡意本機管理員在檢查後替換路徑。無效圖片是固定拒絕，真正IO例外保留交呼叫端共用告警；清理再失敗時保留原錯與清理錯。離線測試驗證原子發布、取消、隔離及排空，不代表公開網域或LINE已通。
*/
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SpeechMessage.Assets;

/// <summary>單一Host擁有的公開圖片發布器；只保留固定部署設定與容量計數，不保留租戶或圖片快取。</summary>
public sealed class FileSystemPublicAssetStore : IPublicAssetStore, IAsyncDisposable
{
    private const int MaximumSourceBytes = 5 * 1024 * 1024;
    private const int MaximumPngBytes = 300 * 1024;
    private readonly object _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _root;
    private readonly Uri _publicBase;
    private readonly Configuration _configuration;
    private int _active;
    private bool _stopping;

    /// <summary>驗證固定本機根與HTTPS基底，建立私有無像素池設定；沒有輸入時不建立租戶目錄。</summary>
    /// <param name="options">受信任組合根的固定部署設定，不能來自request。</param>
    public FileSystemPublicAssetStore(AssetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);
        ArgumentNullException.ThrowIfNull(options.PublicBaseUrl);
        if (!Path.IsPathFullyQualified(options.RootPath) || options.RootPath.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("A dedicated absolute local root is required.", nameof(options));
        var url = options.PublicBaseUrl;
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0 ||
            url.Query.Length != 0 || url.Fragment.Length != 0 || !url.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("An HTTPS public base without credentials, query or fragment is required.", nameof(options));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        _publicBase = url;
        RejectReparseAncestors(_root);
        _configuration = new Configuration(new PngConfigurationModule(), new JpegConfigurationModule())
        {
            MaxDegreeOfParallelism = 2,
            MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
            { MaximumPoolSizeMegabytes = 0, AllocationLimitMegabytes = 256 })
        };
    }

    /// <summary>借讀來源並完整發布；滿載無等待列，caller取消及內部逾時都等待清理後才結束。</summary>
    /// <param name="scope">已授權且使用固定產品及非空租戶GUID的scope，並非授權證明。</param>
    /// <param name="image">caller唯一擁有且遵守取消的PNG／JPEG來源，本方法不關閉。</param>
    /// <param name="ct">caller明確取消；內部逾時仍是真正失敗，不能冒充caller取消。</param>
    /// <returns>已flush與關閉後發布的新PNG網址與最終SHA，不包含任何資源參照。</returns>
    public async Task<PublicAsset> PutTenantLogoAsync(AssetScope scope, Stream image, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(image);
        if (scope.Product is not ("GoodShepherd2" or "GoodButler" or "GoodGuild") || scope.TenantId == Guid.Empty)
            throw new ArgumentException("A recognized product and tenant identity are required.", nameof(scope));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_active == 2) throw new AssetRejectedException("busy");
            _active++;
        }
        try
        {
            // 完成計數必須在linked registration與timer釋放之後才遞減，否則DisposeAsync可能早於真正資源排空返回。
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            byte[]? png = null;
            try
            {
                png = await NormalizeAsync(image, timeout.Token).ConfigureAwait(false);
                return await PublishAsync(scope, png, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException("Public asset processing exceeded its bounded time limit.");
            }
            finally { if (png is not null) CryptographicOperations.ZeroMemory(png); }
        }
        finally
        {
            lock (_gate) { _active--; CompleteDrain(); }
        }
    }

    /// <summary>停止接單後等待所有已接受操作完成清理；可同時及重複呼叫，不處置caller來源。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate) { _stopping = true; CompleteDrain(); return new ValueTask(_drained.Task); }
    }

    /// <summary>僅在gate內且停止後最後一個操作離開時釋放allocator；完成訊號不會在鎖內同步執行外部continuation。</summary>
    private void CompleteDrain()
    {
        if (!_stopping || _active != 0 || _drained.Task.IsCompleted) return;
        try { _configuration.MemoryAllocator.ReleaseRetainedResources(); _drained.TrySetResult(); }
        catch (Exception error) { _drained.TrySetException(error); }
    }

    /// <summary>讀入有界且只屬於本次的buffer；先識別尺寸再配置像素，僅圖片格式故障轉為預期拒絕。</summary>
    private async Task<byte[]> NormalizeAsync(Stream source, CancellationToken ct)
    {
        using var input = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var decoding = false;
        try
        {
            while (true)
            {
                var allowance = (int)Math.Min(buffer.Length, MaximumSourceBytes + 1L - input.Length);
                var count = await source.ReadAsync(buffer.AsMemory(0, allowance), ct).ConfigureAwait(false);
                if (count == 0) break;
                await input.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                if (input.Length > MaximumSourceBytes) throw new AssetRejectedException("source-too-large");
            }
            input.Position = 0;
            decoding = true;
            var identify = new DecoderOptions { Configuration = _configuration, SkipMetadata = true, MaxFrames = 1 };
            var info = await Image.IdentifyAsync(identify, input, ct).ConfigureAwait(false);
            if (info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 40_000_000)
                throw new AssetRejectedException("dimensions");
            input.Position = 0;
            // PNG壓縮文字／ICC不參與Logo輸出，解碼直接略過以免多個小chunk膨脹並保留大量metadata。
            // JPEG的APP metadata不壓縮且受5MiB來源限制，只在本次解碼借用EXIF方向，編碼全部剝除；不進行ICC色域轉換。
            var decoder = new DecoderOptions { Configuration = _configuration, MaxFrames = 1,
                SkipMetadata = info.Metadata.DecodedImageFormat == PngFormat.Instance };
            using var image = await Image.LoadAsync<Rgba32>(decoder, input, ct).ConfigureAwait(false);
            image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
            { Size = new Size(640, 640), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }));
            ct.ThrowIfCancellationRequested();
            using var output = new MemoryStream();
            try
            {
                await EncodeAsync(image, output, PngCompressionLevel.DefaultCompression, ct).ConfigureAwait(false);
                if (output.Length > MaximumPngBytes)
                {
                    output.SetLength(0); output.Position = 0;
                    await EncodeAsync(image, output, PngCompressionLevel.BestCompression, ct).ConfigureAwait(false);
                }
                if (output.Length > MaximumPngBytes) throw new AssetRejectedException("png-too-large");
                return output.ToArray();
            }
            finally { CryptographicOperations.ZeroMemory(output.GetBuffer()); }
        }
        catch (UnknownImageFormatException) when (decoding) { throw new AssetRejectedException("format"); }
        catch (ImageFormatException) when (decoding) { throw new AssetRejectedException("invalid-image"); }
        catch (EndOfStreamException) when (decoding) { throw new AssetRejectedException("invalid-image"); }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(input.GetBuffer());
        }
    }

    /// <summary>固定RGBA八位元無損PNG，排除附加metadata及文字區塊；不量化、不保存編碼器狀態於owner。</summary>
    private static Task EncodeAsync(Image<Rgba32> image, Stream output, PngCompressionLevel level, CancellationToken ct) =>
        image.SaveAsync(output, new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8,
            CompressionLevel = level, SkipMetadata = true, ChunkFilter = PngChunkFilter.ExcludeAll
        }, ct);

    /// <summary>同目錄寫入獨占候選並flush，最後不覆寫搬移才發布；出錯只刪自己已建立的候選，清理失敗保留雙錯。</summary>
    private async Task<PublicAsset> PublishAsync(AssetScope scope, byte[] png, CancellationToken ct)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(png));
        var name = sha[..16] + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)) + ".png";
        var relative = scope.Product + "/" + scope.TenantId.ToString("N") + "/" + name;
        var publicUrl = new Uri(_publicBase, relative);
        if (publicUrl.AbsoluteUri.Length > 2000) throw new AssetRejectedException("url-too-long");
        var directory = Path.Combine(_root, scope.Product, scope.TenantId.ToString("N"));
        RejectReparseAncestors(directory);
        Directory.CreateDirectory(directory);
        RejectReparseAncestors(directory);
        var candidate = Path.Combine(directory, ".upload-" + Guid.NewGuid().ToString("N") + ".tmp");
        var ownsCandidate = false;
        try
        {
            await using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                ownsCandidate = true;
                await file.WriteAsync(png, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            RejectReparseAncestors(directory);
            File.Move(candidate, Path.Combine(directory, name), overwrite: false);
            ownsCandidate = false;
            return new PublicAsset(publicUrl, sha);
        }
        catch (Exception original)
        {
            if (ownsCandidate)
            {
                try { File.Delete(candidate); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                { throw new AggregateException("Asset publication and cleanup failed.", original, cleanup); }
            }
            throw;
        }
    }

    /// <summary>逐層檢查現存祖先，拒絕檔案／junction／symbolic link；缺少目錄仍由後續受信任owner建立，不替代ACL隔離。</summary>
    private static void RejectReparseAncestors(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            try
            {
                var attributes = File.GetAttributes(current.FullName);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                    throw new IOException("The asset directory boundary is not a regular directory.");
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
        }
    }
}
