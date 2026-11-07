/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P2-03 真實程序 stdin owner 的有限生命週期，防止輸入管線沒有關閉時，
 * 用無法取消的同步讀取或遺留 Task.Run 保留秘密、handle 與取消註冊。
 * 主要功能與流程：用實際 Windows 匿名管線注入無資料、caller取消及有限 UTF-8；
 * 另外以操作自有 MemoryStream 驗壞 UTF-8、容量與 Dispose 後拒絕，不讀任何真實秘密。
 * 資源擁有權與隔離：每案獨立 pipe／reader／stream／CTS，using 逆序釋放；有限等待最多兩秒，
 * 不啟動子程序或背景讀取，不改 Console.In／cwd／環境變數。測試資料全部公開合成。
 * 維護與驗證：決定性斷言真正 timeout／OCE、UTF-8完整還原、超量拒絕及 owner 關閉原stream；
 * 這些是本機輸入生命週期證據，不能視為 D365、欄位安全性或實機寫入驗收。
 */
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SpeechMessage.Dynamics.Deployment.Channels;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>使用真管線等待及合成 byte stream 驗產品 reader，所有外部資源由單案 using 擁有。</summary>
[Collection("Deployment working directory")]
public sealed class ChannelSecretInputTests
{
    /// <summary>管線存活但零資料時，reader 必須在上限內拋 timeout，沒有待 drain 的讀取 task。</summary>
    [Fact]
    public void StalledPipe_TimesOut_AndDisposesOwnedSource()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var source = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        var handle = source.SafePipeHandle;
        var watch = Stopwatch.StartNew();
        using (var reader = new ChannelSecretInput(source, handle, 3, TimeSpan.FromMilliseconds(40), default))
            Assert.Throws<TimeoutException>(() => reader.Read());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(handle.IsClosed);
    }

    /// <summary>caller 在管線等待中取消，必須傳回同 token 的 OCE；內部 deadline 不能冒充正常取消。</summary>
    [Fact]
    public void StalledPipe_CallerCancellation_InterruptsWait()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var source = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        using var reader = new ChannelSecretInput(source, source.SafePipeHandle, 3, TimeSpan.FromSeconds(2), cancellation.Token);
        var exception = Assert.Throws<OperationCanceledException>(() => reader.Read());
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    /// <summary>真管線包含繁中與代理對字元，來源關閉後讀到 EOF，不能把資料被拆包當成結束。</summary>
    [Fact]
    public void FinitePipe_DecodesStrictUtf8_AndEndsWhenWriterCloses()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var source = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        using var reader = new ChannelSecretInput(source, source.SafePipeHandle, 3, TimeSpan.FromSeconds(2), default);
        const string value = "合成-token🙂\n合成-secret\n";
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Dispose();
        Assert.Equal(value, reader.ReadToEnd());
    }

    /// <summary>已有限定來源仍必須拒絕不完整 UTF-8；不以 replacement 字元悄悄改變秘密。</summary>
    [Fact]
    public void MalformedUtf8_IsRejected()
    {
        using var source = new MemoryStream(new byte[] { 0xC3 });
        using var handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        using var reader = new ChannelSecretInput(source, handle, 1, TimeSpan.FromSeconds(2), default);
        Assert.Throws<DecoderFallbackException>(() => reader.ReadToEnd());
    }

    /// <summary>產品 reader 有獨立 byte 上限，即使上層錯誤呼叫 ReadToEnd 也不會讀無界資料。</summary>
    [Fact]
    public void OversizedBytes_AreRejected()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(new string('s', 17000)));
        using var handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        using var reader = new ChannelSecretInput(source, handle, 1, TimeSpan.FromSeconds(2), default);
        Assert.Throws<InvalidDataException>(() => reader.ReadToEnd());
    }

    /// <summary>Dispose 移除 reader／來源的使用權，重複關閉冪等但不能繼續讀取。</summary>
    [Fact]
    public void Dispose_ClosesSource_AndRejectsReads()
    {
        using var source = new MemoryStream();
        using var handle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        var reader = new ChannelSecretInput(source, handle, 1, TimeSpan.FromSeconds(2), default);
        reader.Dispose();
        reader.Dispose();
        Assert.False(source.CanRead);
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
    }

    /// <summary>暖機後連續128個真pipe owner都明確關閉，程序handle回到基線；不用GC掩蓋遺漏Dispose。</summary>
    [Fact]
    public void RepeatedPipeOwners_ReturnHandlesToBaselineWithoutGc()
    {
        for (var index = 0; index < 16; index++) ReadSyntheticPipe();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = process.HandleCount;
        for (var index = 0; index < 128; index++) ReadSyntheticPipe();
        process.Refresh();
        Assert.InRange(process.HandleCount - before, -8, 8);
    }

    /// <summary>一次完整pipe生命週期，只有公開合成字元；writer、reader與client來源全由using釋放。</summary>
    private static void ReadSyntheticPipe()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var source = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        using var reader = new ChannelSecretInput(source, source.SafePipeHandle, 3, TimeSpan.FromSeconds(2), default);
        writer.WriteByte((byte)'s');
        writer.Dispose();
        Assert.Equal("s", reader.ReadToEnd());
    }
}
