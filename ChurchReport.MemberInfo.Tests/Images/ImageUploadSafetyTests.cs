/*
 * 檔案整體說明
 *
 * 目的與定位：以真正控制器影像路徑驗證內容白名單、解碼前尺寸拒絕與釋放責任。 僅以記憶體假 SDK 隔離網路；診斷 owner 只寫測試暫存目錄，不建立任何 LINE sender。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageUploadSafetyTests、CountedNonSeekableStream、TrackedImageFile、TrackedStream。
 * - 四條呼叫路徑都要拒絕偽裝 JPG 的 TIFF，不能把未驗證內容存進 CRM。
 * - 現有宣告白名單保持不變，實際 BMP 不能因宣告 JPG 而繞過內容辨識。
 * - WebP 曾被通用解碼器接受；本核定刻意改為拒絕，不能再保存原始檔案。
 * - PNG 只有合法標頭與結尾，沒有像素資料；回應必須先報尺寸而非嘗試像素解碼。 兩個單邊邊界與總面積邊界各自獨立，避免只檢查其中一項而產生假綠燈。
 * 測試案例入口包含 Upload_TiffFile_RejectedBeforeDecode、Upload_UnsupportedFormat_Rejected、
 * Upload_DisguisedWebpAsJpg_Rejected、Upload_OversizedDimensions_RejectedBeforeFullDecode、
 * Upload_CorruptFile_ReturnsExpectedRejection、Upload_ExactDimensionLimits_AreNotRejectedAsOversized、
 * Upload_NonSeekableOversizedStream_IsBoundedAndBorrowed、Upload_AnimatedGif_UsesFirstFrameForEveryRoute；
 * 其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using ChurchReport.Controllers;
using ChurchReport.Services.Images;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Images;

/// <summary>
/// 以真正控制器影像路徑驗證內容白名單、解碼前尺寸拒絕與釋放責任。
/// 僅以記憶體假 SDK 隔離網路；診斷 owner 只寫測試暫存目錄，不建立任何 LINE sender。
/// </summary>
[Collection("ExceptionReporting")]
public sealed class ImageUploadSafetyTests
{
    /// <summary>四條呼叫路徑都要拒絕偽裝 JPG 的 TIFF，不能把未驗證內容存進 CRM。</summary>
    [Theory]
    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
    public async Task Upload_TiffFile_RejectedBeforeDecode(string route) =>
        await AssertRejected(route, ImageFixture.CreateSample("tiff"), "格式");

    /// <summary>現有宣告白名單保持不變，實際 BMP 不能因宣告 JPG 而繞過內容辨識。</summary>
    [Theory]
    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
    public async Task Upload_UnsupportedFormat_Rejected(string route) =>
        await AssertRejected(route, ImageFixture.CreateSample("bmp"), "格式");

    /// <summary>WebP 曾被通用解碼器接受；本核定刻意改為拒絕，不能再保存原始檔案。</summary>
    [Theory]
    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
    public async Task Upload_DisguisedWebpAsJpg_Rejected(string route) =>
        await AssertRejected(route, ImageFixture.CreateSample("webp"), "格式");

    /// <summary>
    /// PNG 只有合法標頭與結尾，沒有像素資料；回應必須先報尺寸而非嘗試像素解碼。
    /// 兩個單邊邊界與總面積邊界各自獨立，避免只檢查其中一項而產生假綠燈。
    /// </summary>
    [Theory]
    [InlineData("member", 8193, 1)][InlineData("personal", 1, 8193)]
    [InlineData("new-attached", 8000, 5001)][InlineData("new-direct", 8000, 5001)]
    public async Task Upload_OversizedDimensions_RejectedBeforeFullDecode(string route, int width, int height) =>
        await AssertRejected(route, PngHeader(width, height), "尺寸");

    /// <summary>不是影像的內容必須走固定輸入拒絕，不寫入資料、不把原始內容或解析錯誤外送。</summary>
    [Theory]
    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
    public async Task Upload_CorruptFile_ReturnsExpectedRejection(string route) =>
        await AssertRejected(route, new byte[] { 1, 2, 3, 4, 5 }, null);

    /// <summary>
    /// 允許格式的合法標頭但缺少像素資料，必須以損壞圖檔拒絕；
    /// 上限本身仍允許，不能誤把等於 8192 或等於 40,000,000 判成尺寸超限。
    /// </summary>
    [Theory]
    [InlineData(8192, 1)][InlineData(1, 8192)][InlineData(8000, 5000)]
    public void Upload_ExactDimensionLimits_AreNotRejectedAsOversized(int width, int height)
    {
        using var input = new MemoryStream(PngHeader(width, height));
        var rejection = Assert.Throws<ImageInputRejectedException>(() =>
        {
            using var image = ImageInputValidator.Load(input);
        });
        Assert.Contains("損壞", rejection.Message);
        Assert.DoesNotContain("尺寸", rejection.Message);
        Assert.True(input.CanRead);
    }

    /// <summary>
    /// 非 seekable 串流不能因 Length 不可用而無界暫存。同步與非同步都只讀 5 MiB 加一，
    /// 拒絕後釋放內部副本但不釋放借用來源；來源最後仍由本案例 using 擁有。
    /// </summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Upload_NonSeekableOversizedStream_IsBoundedAndBorrowed(bool asynchronous)
    {
        using var input = new CountedNonSeekableStream(new byte[6 * 1024 * 1024]);
        ImageInputRejectedException rejection;
        if (asynchronous)
            rejection = await Assert.ThrowsAsync<ImageInputRejectedException>(async () =>
            {
                using var image = await ImageInputValidator.LoadAsync(input);
            });
        else
            rejection = Assert.Throws<ImageInputRejectedException>(() =>
            {
                using var image = ImageInputValidator.Load(input);
            });
        Assert.Contains("5MB", rejection.Message);
        Assert.Equal(5 * 1024 * 1024 + 1, input.ReadBytes);
        Assert.True(input.CanRead);
    }

    /// <summary>動態 GIF 輸出仍以第一影格為準，禁止多影格造成配置放大或改用第二人的影像。</summary>
    [Fact]
    public async Task Upload_AnimatedGif_UsesFirstFrameForEveryRoute()
    {
        using var source = new Image<Rgb24>(96, 64, new Rgb24(240, 20, 20));
        using var second = new Image<Rgb24>(96, 64, new Rgb24(20, 20, 240));
        source.Frames.AddFrame(second.Frames.RootFrame);
        using var output = new MemoryStream();
        source.Save(output, new GifEncoder());
        foreach (var route in new[] { "member", "personal", "new-attached", "new-direct" })
        {
            var result = await ImageFixture.NormalizeThroughExistingPath(route, output.ToArray());
            using var decoded = Image.Load<Rgb24>(result);
            var pixel = decoded[decoded.Width / 2, decoded.Height / 2];
            Assert.InRange(pixel.R, 220, 255);
            Assert.InRange(pixel.B, 0, 45);
        }
    }

    /// <summary>
    /// 重複跑實際六個解碼位置及拒絕分支，所有輸入串流立即歸零；
    /// 強制排空後不得新增 ImageSharp 未 Dispose 的配置。事件只計數、不保存配置堆疊，
    /// finally 解除 static 訂閱，避免測試自身延長圖檔或案例生命週期。
    /// </summary>
    [Fact]
    public async Task Upload_Soak_DrainsImageResourcesWithoutFinalizerLeaks()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var leaks = 0;
        void OnLeak(string _) => Interlocked.Increment(ref leaks);
        MemoryDiagnostics.UndisposedAllocation += OnLeak;
        try
        {
            var bytes = ImageFixture.CreateSample("png");
            for (var round = 0; round < 25; round++)
            {
                foreach (var route in new[] { "member", "personal", "new-attached", "new-direct" })
                {
                    var file = new TrackedImageFile(bytes);
                    var attempt = await ImageFixture.AttemptUpload(route, bytes, suppliedFile: file);
                    Assert.True(attempt.Success);
                    Assert.NotNull(attempt.Bytes);
                    Assert.Equal(0, file.ActiveStreams);
                }
                foreach (var route in new[] { "member-crop", "member-fit", "personal-crop" })
                    Assert.NotEmpty(ImageFixture.Thumbnail(route, bytes, 80));
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.Equal(0, Volatile.Read(ref leaks));
            Assert.Equal(baseline, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }
        finally
        {
            MemoryDiagnostics.UndisposedAllocation -= OnLeak;
        }
    }

    /// <summary>資料庫既有圖片無法以允許解碼器處理時，三個縮圖維持回原始陣列且不拋錯。</summary>
    [Theory]
    [InlineData("member-crop", "webp")][InlineData("member-fit", "tiff")][InlineData("personal-crop", "bmp")]
    public void Thumbnail_DisallowedFormat_KeepsExistingFallback(string route, string format)
    {
        var original = ImageFixture.CreateSample(format);
        Assert.Same(original, ImageFixture.Thumbnail(route, original, 80));
    }

    /// <summary>
    /// 檢查編譯後真正呼叫，而非 grep 原始碼；六個解碼入口都必須呼叫共用 validator。
    /// 同時禁止控制器自行再呼叫 Image.Load，避免只有掛名呼叫檢查卻繞回全格式解碼器。
    /// </summary>
    [Fact]
    public void Upload_AllCallSites_UseSharedValidator()
    {
        var assembly = typeof(MemberInfoController).Assembly;
        var validator = assembly.GetType("ChurchReport.Services.Images.ImageInputValidator");
        Assert.NotNull(validator);
        foreach (var (type, name) in new[]
        {
            (typeof(MemberInfoController), "NormalizeUploadedImage"),
            (typeof(MemberInfoController), "CreateThumbnailIfNeeded"),
            (typeof(MemberInfoController), "CreateFitThumbnail"),
            (typeof(PersonalController), "UploadContactImage"),
            (typeof(PersonalController), "CreateThumbnailIfNeeded"),
            (typeof(NewPersonController), "TryUploadNewPersonImageAsync")
        })
        {
            var root = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var calls = CalledMethods(ExecutableBody(root)).ToArray();
            Assert.Contains(calls, method => method.DeclaringType == validator);
            Assert.DoesNotContain(calls, method => method.DeclaringType?.Namespace == "SixLabors.ImageSharp"
                && method.DeclaringType.Name == "Image" && method.Name.StartsWith("Load", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 合法、拒絕與非 seekable 上傳都必須關閉自己開啟的 stream；同時跑 A/B 合成輸入，
    /// 防止 static 保存當次圖檔或重用別人的輸出。樣本釋放後計數必須回到零。
    /// </summary>
    [Theory]
    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
    public async Task Upload_ConcurrentInputs_DrainStreamsAndKeepResultsIsolated(string route)
    {
        var valid = ImageFixture.CreateSample("png");
        var invalid = ImageFixture.CreateSample("webp");
        var tasks = Enumerable.Range(0, 12).Select(async index =>
        {
            var allowed = index % 2 == 0;
            var bytes = allowed ? valid : invalid;
            var file = new TrackedImageFile(bytes, seekable: index % 3 != 0);
            var attempt = await ImageFixture.AttemptUpload(route, bytes, suppliedFile: file);
            Assert.Equal(allowed, attempt.Success);
            if (allowed) Assert.NotNull(attempt.Bytes); else Assert.Null(attempt.Bytes);
            Assert.Equal(0, file.ActiveStreams);
            Assert.True(file.OpenCount > 0);
        });
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// SDK 故障是非預期失敗；公開 action 要回原有失敗 schema 並經共用 owner 留一筆診斷。
    /// 注入的敏感假訊息不可進 JSON 或日誌；owner、registration 與暫存目錄確定性清理。
    /// </summary>
    [Theory]
    [InlineData("personal")][InlineData("new-direct")]
    public async Task Upload_UnexpectedFailure_ReportsOriginalExceptionWithoutPrivateMessage(string route)
    {
        var directory = Path.Combine(Path.GetTempPath(), "speechmessage-image-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                // 只啟動記憶體假 sender，避免把「未啟動 consumer」狀態混成第二筆失敗事件。
                diagnostics.StartNotifications((_, _) => Task.CompletedTask);
                using var registration = ExceptionReporting.Attach(diagnostics);
                var file = new TrackedImageFile(ImageFixture.CreateSample("jpeg"));
                var attempt = await ImageFixture.AttemptUpload(route, file.Bytes,
                    new InvalidOperationException("synthetic-private-value"), file);
                Assert.False(attempt.Success);
                Assert.Null(attempt.Bytes);
                Assert.DoesNotContain("synthetic-private-value", attempt.Message ?? string.Empty);
                Assert.Equal(0, file.ActiveStreams);
            }
            var record = Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
            Assert.Contains("System.InvalidOperationException", record);
            Assert.DoesNotContain("synthetic-private-value", record);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>使用真實診斷 owner 證明預期拒絕不誤報，並觀察 action／sink 確實沒有輸出可寫入的照片。</summary>
    private static async Task AssertRejected(string route, byte[] bytes, string? messagePart)
    {
        var directory = Path.Combine(Path.GetTempPath(), "speechmessage-image-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                // 只啟動記憶體假 sender，避免把「未啟動 consumer」狀態混成第二筆失敗事件。
                diagnostics.StartNotifications((_, _) => Task.CompletedTask);
                using var registration = ExceptionReporting.Attach(diagnostics);
                var file = new TrackedImageFile(bytes);
                var attempt = await ImageFixture.AttemptUpload(route, bytes, suppliedFile: file);
                Assert.False(attempt.Success);
                Assert.Null(attempt.Bytes);
                Assert.NotEmpty(attempt.Message ?? string.Empty);
                if (messagePart != null) Assert.Contains(messagePart, attempt.Message!);
                Assert.Equal(0, file.ActiveStreams);
            }
            Assert.False(File.Exists(Path.Combine(directory, "Exception.log"))
                && new FileInfo(Path.Combine(directory, "Exception.log")).Length > 0);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>合成 PNG IHDR 與 IEND，不配置任何大像素陣列；CRC 正確但刻意沒有可完整解碼的 IDAT。</summary>
    internal static byte[] PngHeader(int width, int height)
    {
        var bytes = Convert.FromHexString("89504E470D0A1A0A0000000D4948445200000000000000000802000000000000000000000049454E44AE426082");
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        uint crc = 0xffffffff;
        foreach (var value in bytes.AsSpan(12, 17))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ~crc);
        return bytes;
    }

    /// <summary>async 方法真正執行的內容在狀態機 MoveNext，不能只檢查建立 task 的外殼。</summary>
    private static MethodInfo ExecutableBody(MethodInfo method) =>
        method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? method;

    /// <summary>依 CLR opcode 的 operand 長度解析 IL，避免把常數位元組誤認為 call token。</summary>
    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opcode => (ushort)opcode.Value);
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var position = 0; position < il.Length;)
        {
            ushort value = il[position++];
            if (value == 0xfe) value = (ushort)(0xfe00 | il[position++]);
            var operand = opcodes[value].OperandType;
            if (operand == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, position))!;
            position += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
                _ => 4
            };
        }
    }
}

/// <summary>只提供有界測試內容的不可 seek 串流；讀取計數以實際 bytes 為準，不提供偽造長度。</summary>
internal sealed class CountedNonSeekableStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    /// <summary>不暴露可重設位置能力，強迫產品走有界副本。</summary>
    public override bool CanSeek => false;
    /// <summary>由底層實際讀取位置計數，不由待測程式的常數推導結果。</summary>
    internal long ReadBytes => base.Position;
    /// <summary>模擬網路來源不支援長度。</summary>
    public override long Length => throw new NotSupportedException();
    /// <summary>模擬網路來源不可重設位置。</summary>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
}

/// <summary>每次 OpenReadStream 交出不同的單一 owner 串流，借用原始假資料但不保存使用者狀態。</summary>
internal sealed class TrackedImageFile(byte[] bytes, bool seekable = true) : IFormFile
{
    /// <summary>測試自有的合成資料，沒有真實照片或個資。</summary>
    internal byte[] Bytes => bytes;
    /// <summary>歸零代表所有產品開啟的串流已確定釋放。</summary>
    internal int ActiveStreams { get; private set; }
    /// <summary>確保測試確實讀取影像而非在無關的前置條件失敗。</summary>
    internal int OpenCount { get; private set; }
    /// <summary>刻意使用允許的宣告型態來驗證實際內容。</summary>
    public string ContentType => "image/jpeg";
    /// <summary>不讀取真實 HTTP 標頭。</summary>
    public string ContentDisposition => string.Empty;
    /// <summary>只有本測試物件擁有的空標頭。</summary>
    public IHeaderDictionary Headers { get; } = new HeaderDictionary();
    /// <summary>合成內容的實際長度。</summary>
    public long Length => bytes.Length;
    /// <summary>固定上傳欄位名稱。</summary>
    public string Name => "imageFile";
    /// <summary>所有實際格式都偽裝成 JPG，不能以宣告代替驗證。</summary>
    public string FileName => "synthetic.jpg";
    /// <summary>釋放由呼叫端負責；計數僅存在本測試實例，不進 static。</summary>
    public Stream OpenReadStream()
    {
        OpenCount++; ActiveStreams++;
        return new TrackedStream(bytes, seekable, () => ActiveStreams--);
    }
    /// <summary>僅為 IFormFile 相容入口，同樣確定性釋放來源。</summary>
    public void CopyTo(Stream target) { using var stream = OpenReadStream(); stream.CopyTo(target); }
    /// <summary>非同步相容入口不建立背景工作，取消由呼叫端傳入。</summary>
    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
    {
        using var stream = OpenReadStream();
        await stream.CopyToAsync(target, cancellationToken);
    }

    /// <summary>借用測試陣列並記錄 Dispose；非 seekable 故障注入驗證產品有界暫存後再辨識。</summary>
    private sealed class TrackedStream(byte[] content, bool canSeek, Action closed) : MemoryStream(content, writable: false)
    {
        private bool disposed;
        /// <summary>故意模擬無法重設位置的表單串流。</summary>
        public override bool CanSeek => canSeek;
        /// <summary>不容許以 Position 繞過 CanSeek=false 的宣告。</summary>
        public override long Position
        {
            get => canSeek ? base.Position : throw new NotSupportedException();
            set { if (!canSeek) throw new NotSupportedException(); base.Position = value; }
        }
        /// <summary>回報恰一次釋放；底層陣列由測試案例擁有。</summary>
        protected override void Dispose(bool disposing)
        {
            if (!disposed) { disposed = true; closed(); }
            base.Dispose(disposing);
        }
    }
}
