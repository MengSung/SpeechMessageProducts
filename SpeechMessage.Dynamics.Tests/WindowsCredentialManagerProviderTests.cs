using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Worker-local Windows Credential Manager 邊界只建立 <see cref="SecureString"/>，
/// 並在成功、格式失敗與重複釋放路徑確定清除 unmanaged credential blob 與 native handle。
/// </summary>
public sealed class WindowsCredentialManagerProviderTests
{
    /// <summary>
    /// 證明有效 credential 會被投影成唯讀 SecureString，且 provider 返回前已清零並釋放原生緩衝區。
    /// </summary>
    [Fact]
    public void Read_creates_a_read_only_secure_password_and_cleans_native_ownership()
    {
        using var native = FakeWindowsCredentialNativeApi.Success(
            "SPEECHMESSAGE\\svc_dynamics",
            "test-only-password");
        var provider = new WindowsCredentialManagerProvider(native);

        using var credential = provider.Read("dynamics-sunnyvalechback-service");

        credential.UserName.Should().Be("svc_dynamics");
        credential.Domain.Should().Be("SPEECHMESSAGE");
        credential.Password.IsReadOnly().Should().BeTrue();
        ReadSecureString(credential.Password).Should().Be("test-only-password");
        native.ZeroMemoryCallCount.Should().Be(1);
        native.FreeCredentialCallCount.Should().Be(1);
        native.BlobWasZeroWhenFreed.Should().BeTrue();
    }

    /// <summary>
    /// 證明不合法的 UTF-16 blob 不會繞過清理；即使沒有建立可回傳的 credential，原生記憶體仍必須歸零並釋放。
    /// </summary>
    [Fact]
    public void Read_cleans_native_ownership_when_the_blob_shape_is_invalid()
    {
        using var native = FakeWindowsCredentialNativeApi.InvalidOddBlob(
            "SPEECHMESSAGE\\svc_dynamics");
        var provider = new WindowsCredentialManagerProvider(native);

        var action = () => provider.Read("dynamics-sunnyvalechback-service");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The Windows credential reference is unavailable.");
        native.ZeroMemoryCallCount.Should().Be(1);
        native.FreeCredentialCallCount.Should().Be(1);
        native.BlobWasZeroWhenFreed.Should().BeTrue();
    }

    /// <summary>
    /// 證明不存在的 reference fail closed，且不會假裝擁有或釋放未取得的 native handle。
    /// </summary>
    [Fact]
    public void Read_fails_closed_without_cleanup_calls_when_the_reference_is_missing()
    {
        using var native = FakeWindowsCredentialNativeApi.Missing();
        var provider = new WindowsCredentialManagerProvider(native);

        var action = () => provider.Read("dynamics-missing-service");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The Windows credential reference is unavailable.");
        native.ZeroMemoryCallCount.Should().Be(0);
        native.FreeCredentialCallCount.Should().Be(0);
    }

    /// <summary>
    /// 證明 credential 的 managed secret owner 可重複 Dispose 而不洩漏或重複釋放，
    /// 第一次釋放後 SecureString 即不可再讀取。
    /// </summary>
    [Fact]
    public void Credential_disposal_is_idempotent_and_retires_the_secure_password()
    {
        using var native = FakeWindowsCredentialNativeApi.Success(
            "svc_dynamics@speechmessage.com.tw",
            "test-only-password");
        var provider = new WindowsCredentialManagerProvider(native);
        var credential = provider.Read("dynamics-sunnyvalechback-service");

        credential.Dispose();
        credential.Dispose();

        credential.UserName.Should().Be("svc_dynamics@speechmessage.com.tw");
        credential.Domain.Should().BeEmpty();
        var action = () => _ = credential.Password.Length;
        action.Should().Throw<ObjectDisposedException>();
    }

    private static string ReadSecureString(SecureString secureString)
    {
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            return Marshal.PtrToStringUni(pointer, secureString.Length) ?? string.Empty;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            }
        }
    }

    private sealed class FakeWindowsCredentialNativeApi : IWindowsCredentialNativeApi, IDisposable
    {
        private readonly bool _available;
        private readonly string _userName;
        private IntPtr _credentialPointer;
        private IntPtr _blobPointer;
        private readonly int _blobSize;
        private bool _disposed;

        private FakeWindowsCredentialNativeApi(
            bool available,
            string userName,
            byte[] blob)
        {
            _available = available;
            _userName = userName;
            _blobSize = blob.Length;
            if (!available)
            {
                return;
            }

            _credentialPointer = Marshal.AllocHGlobal(1);
            _blobPointer = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, _blobPointer, blob.Length);
        }

        public int ZeroMemoryCallCount { get; private set; }

        public int FreeCredentialCallCount { get; private set; }

        public bool BlobWasZeroWhenFreed { get; private set; }

        public static FakeWindowsCredentialNativeApi Success(
            string userName,
            string password)
        {
            return new FakeWindowsCredentialNativeApi(
                true,
                userName,
                Encoding.Unicode.GetBytes(password));
        }

        public static FakeWindowsCredentialNativeApi InvalidOddBlob(string userName)
        {
            return new FakeWindowsCredentialNativeApi(
                true,
                userName,
                new byte[] { 1, 2, 3 });
        }

        public static FakeWindowsCredentialNativeApi Missing()
        {
            return new FakeWindowsCredentialNativeApi(false, string.Empty, Array.Empty<byte>());
        }

        public bool TryReadGeneric(
            string targetName,
            out WindowsCredentialNativeRecord credential)
        {
            if (!_available)
            {
                credential = default;
                return false;
            }

            credential = new WindowsCredentialNativeRecord(
                _credentialPointer,
                _userName,
                _blobPointer,
                _blobSize);
            return true;
        }

        public void ZeroMemory(IntPtr address, int byteCount)
        {
            ZeroMemoryCallCount++;
            for (var index = 0; index < byteCount; index++)
            {
                Marshal.WriteByte(address, index, 0);
            }
        }

        public void FreeCredential(IntPtr credentialPointer)
        {
            FreeCredentialCallCount++;
            BlobWasZeroWhenFreed = true;
            for (var index = 0; index < _blobSize; index++)
            {
                if (Marshal.ReadByte(_blobPointer, index) != 0)
                {
                    BlobWasZeroWhenFreed = false;
                    break;
                }
            }

            ReleaseNativeMemory();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseNativeMemory();
        }

        private void ReleaseNativeMemory()
        {
            if (_blobPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_blobPointer);
                _blobPointer = IntPtr.Zero;
            }

            if (_credentialPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_credentialPointer);
                _credentialPointer = IntPtr.Zero;
            }
        }
    }
}
