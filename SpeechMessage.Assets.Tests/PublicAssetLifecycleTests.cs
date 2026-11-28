/*
檔案整體說明
目的與定位：F7儲存庫的故障、取消與owner停止契約，以受控來源Stream注入阻塞、原始IO故障與無界bytes，不讀真實檔案或使用者資料。
主要功能與流程：先確定來源進入讀取才取消或停止；兩個佔用者填滿容量後第三個立即拒絕，DisposeAsync不得先於已進行操作完成。來源超限只可讀到5MiB加一byte，所有失敗後候選檔為零且同一owner可恢復新操作。
使用邊界與維護：案內Task全部await排空，取消來源及借入Stream由測試唯一處置，store不能關閉借入來源；fixture在最後排空store後移除自己的GUID根。替身只保存本案合成PNG及完成訊號，無timer、註冊、連線或跨案全域狀態，不以離線測試代表VM或LINE成功。
*/
using Xunit;
using SixLabors.ImageSharp;
namespace SpeechMessage.Assets.Tests;

/// <summary>用決定性故障注入保護F7容量及清理，不使用sleep、GC或單純檔名存在作為成功依據。</summary>
public sealed class PublicAssetLifecycleTests
{
    /// <summary>來源已阻塞才取消；原始來源保持開啟、無候選發布，取消後同一owner能再次接受合法圖片。</summary>
    [Fact]
    public async Task PutLogo_CancellationLeavesNoFiles_AndCapacityReturns()
    {
        await using var fixture = new AssetFixture(); using var png = AssetFixture.Source(Color.Red);
        using var source = new ControlledImageStream(png.ToArray(), block: true);
        using var cancel = new CancellationTokenSource();
        var pending = fixture.Store.PutTenantLogoAsync(fixture.Scope, source, cancel.Token);
        await source.Started.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(source.WasDisposed); Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
        using var retry = AssetFixture.Source(Color.Blue);
        var asset = await fixture.Store.PutTenantLogoAsync(fixture.Scope, retry, default);
        Assert.True(File.Exists(fixture.File(asset)));
    }

    /// <summary>雙上傳滿載後第三個拒絕而非累積等待；停止必須等待兩者完成，拒絕停止後新呼叫且重複Dispose安全。</summary>
    [Fact]
    public async Task Store_BusyRejectsImmediately_AndDisposeDrainsOwnedOperations()
    {
        await using var fixture = new AssetFixture(); using var png = AssetFixture.Source(Color.Red);
        using var a = new ControlledImageStream(png.ToArray(), block: true); using var b = new ControlledImageStream(png.ToArray(), block: true);
        var first = fixture.Store.PutTenantLogoAsync(fixture.Scope, a, default);
        var second = fixture.Store.PutTenantLogoAsync(fixture.Scope, b, default);
        Task? disposal = null;
        try
        {
            await Task.WhenAll(a.Started, b.Started).WaitAsync(TimeSpan.FromSeconds(5));
            using var third = AssetFixture.Source(Color.Green);
            await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, third, default));
            disposal = fixture.Store.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, third, default));
        }
        finally
        {
            a.Release(); b.Release(); await Task.WhenAll(first, second);
            if (disposal is not null) await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(a.WasDisposed); Assert.False(b.WasDisposed);
        Assert.Equal(2, Directory.GetFiles(fixture.Root, "*.png", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>無界來源不能依賴Length或讀到EOF；最多讀到5MiB加一即拒絕且不建立正式圖片。</summary>
    [Fact]
    public async Task PutLogo_UnseekableSourceHasExactByteBound()
    {
        await using var fixture = new AssetFixture(); using var source = new ControlledImageStream([], endless: true);
        await Assert.ThrowsAsync<AssetRejectedException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default));
        Assert.Equal(5L * 1024 * 1024 + 1, source.BytesRead);
        Assert.False(source.WasDisposed); Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>真實I/O失敗須保留同一Exception供共用告警，不能包成預期拒絕或因清理覆蓋原錯；來源不被store處置。</summary>
    [Fact]
    public async Task PutLogo_SourceFaultPropagatesOriginalException_AndNoPartialPublication()
    {
        await using var fixture = new AssetFixture(); var failure = new IOException("synthetic-read-failure");
        using var source = new ControlledImageStream([], failure: failure);
        var actual = await Assert.ThrowsAsync<IOException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default));
        Assert.Same(failure, actual); Assert.False(source.WasDisposed);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>不帶caller取消的來源阻塞至固定30秒deadline，必須報TimeoutException且釋放容量，不能當成使用者正常取消。</summary>
    [Fact]
    public async Task PutLogo_InternalDeadlineIsAFailureAndReleasesCapacity()
    {
        await using var fixture = new AssetFixture(); using var png = AssetFixture.Source(Color.Red);
        using var source = new ControlledImageStream(png.ToArray(), block: true);
        await Assert.ThrowsAsync<TimeoutException>(() => fixture.Store.PutTenantLogoAsync(fixture.Scope, source, default));
        Assert.False(source.WasDisposed);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
        using var retry = AssetFixture.Source(Color.Blue);
        Assert.NotNull(await fixture.Store.PutTenantLogoAsync(fixture.Scope, retry, default));
    }
}

/// <summary>本案借入來源的受控替身；只在ReadAsync注入故障／阻塞，私有MemoryStream由測試Dispose唯一釋放。</summary>
internal sealed class ControlledImageStream : Stream
{
    private readonly MemoryStream _source;
    private readonly bool _block;
    private readonly bool _endless;
    private readonly Exception? _failure;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _entered;
    /// <summary>複製本案合成來源；block只用完成訊號，不建立task worker、timer或取消註冊。</summary>
    internal ControlledImageStream(byte[] bytes, bool block = false, Exception? failure = null, bool endless = false)
    { _source = new MemoryStream(bytes, writable: false); _block = block; _failure = failure; _endless = endless; }
    /// <summary>來源確實進入讀取的訊號，測試可在此後注入取消，避免時序猜測。</summary>
    internal Task Started => _started.Task;
    /// <summary>僅由來源讀取累積，證明上游沒有無界緩衝；不將讀取內容保存到其他案例。</summary>
    internal long BytesRead { get; private set; }
    /// <summary>記錄唯一Dispose是否被store錯誤提前呼叫，測試自己仍在finally釋放。</summary>
    internal bool WasDisposed { get; private set; }
    /// <summary>解除本案讀取阻塞，測試必須先await進行中呼叫，再Dispose來源。</summary>
    internal void Release() => _released.TrySetResult();
    /// <summary>受控串流只支援非同步讀取；所有模式保留caller取消且不留下未await工作。</summary>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(WasDisposed, this);
        if (!_entered) { _entered = true; _started.TrySetResult(); if (_block) await _released.Task.WaitAsync(cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested();
        if (_failure is not null) throw _failure;
        int read;
        if (_endless) { buffer.Span.Clear(); read = buffer.Length; }
        else read = await _source.ReadAsync(buffer, cancellationToken);
        BytesRead += read; return read;
    }
    /// <summary>測試唯一清理入口；釋放私有MemoryStream並使重複Dispose安全，不擁有上傳Task。</summary>
    protected override void Dispose(bool disposing)
    { if (disposing && !WasDisposed) { WasDisposed = true; _released.TrySetResult(); _source.Dispose(); } base.Dispose(disposing); }
    /// <summary>尚未由測試關閉時可讀；不能將這個屬性視為存在任意長度資料的承諾。</summary>
    public override bool CanRead => !WasDisposed;
    /// <summary>刻意不支援seek，逼上游採取有界緩衝而非信任不可靠Length。</summary>
    public override bool CanSeek => false;
    /// <summary>來源僅供上傳借讀，任何寫入都屬測試或產品錯誤。</summary>
    public override bool CanWrite => false;
    /// <summary>不提供不可信來源長度；上游必須自行數byte。</summary>
    public override long Length => throw new NotSupportedException();
    /// <summary>不支援位置存取，禁止繞過ReadAsync的故障注入。</summary>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <summary>沒有待寫資料，Flush不建立額外資源。</summary>
    public override void Flush() { }
    /// <summary>禁止同步借讀以確保caller取消能沿真實非同步路徑傳遞。</summary>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <summary>不可seek，不將本案合成資料誤當任意可存取檔案。</summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <summary>唯讀來源不允許修改容量或資料。</summary>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <summary>唯讀來源不允許寫入，生命週期只由測試Dispose管理。</summary>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
