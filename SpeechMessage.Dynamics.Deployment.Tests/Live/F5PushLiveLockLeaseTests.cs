/*
檔案整體說明
目的與定位：保護 F5 Live 入口的 D365／LINE 雙鎖前置及讀取 handle 生命週期，全部案例都是離線故障測試。
主要功能與流程：以每案唯一暫存目錄的合成 owner 文字測試錯線、錯包、缺鎖、重複欄位、大小及編碼拒絕，
再以作業系統的獨占開檔驗證成功時不可替換、部分取得失敗立即排空、重複 Dispose 安全且不改寫或刪除鎖。
信任與隔離：不讀 ProgramData 的真實鎖、User Secrets、D365 或 LINE；不同案例只操作自身正式暫存路徑，
測試不以租約取代服務端組織／產品核對，亦不能用通過宣稱 F5 實機推播或 FS 寫入已獲准。
資源與失敗：Fixture 唯一擁有暫存目錄，所有 stream 以 using 關閉後才刪自己的檔案；沒有背景 task 或共享可變狀態。
決定性斷言：持租約期間獨占開檔必須失敗，成功或失敗排空後均可獨占重開，兩個原始檔案位元組保持相同。
*/
using System;
using System.IO;
using System.Text;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>只消費區域暫存依賴的 F5 雙鎖契約，不帶 Live3P 分類，故一般守門一定執行。</summary>
[Trait("Category", "Offline")]
[Trait("WP", "F5")]
public sealed class F5PushLiveLockLeaseTests
{
    /// <summary>合成 round-trip 時間只驗語法，不冒充實際持鎖時間或建立新的鎖過期政策。</summary>
    private const string Start = "start=2026-11-28T20:00:00.0000000+08:00";

    /// <summary>模擬精確 owner 各種錯配，斷言全部拒絕且不依 substring 或重複欄位放行。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lane=B wp=F5")]
    [InlineData("lane=A wp=F3")]
    [InlineData("lane=A wp=F5x")]
    [InlineData("lane=AA wp=F5")]
    [InlineData("lane=A lane=A wp=F5")]
    [InlineData("lane=A wp=F5 wp=F5")]
    [InlineData("lane=A wp=F5 extra=1")]
    [InlineData("lane=A wp=F5 start=invalid")]
    [InlineData("lane=A wp=F5 start=2026-11-28T20:00:00.0000000+08:00 start=2026-11-28T20:00:00.0000000+08:00")]
    public void Owner_MissingWrongOrAmbiguous_Refused(string? text)
    {
        // 其他 owner 故障保留正確時間，避免案例因缺時間先拒絕而未保護被測的精確 owner 契約。
        if (text is not null && !text.Contains("start=", StringComparison.Ordinal)) text += " " + Start;
        Assert.Throws<InvalidDataException>(() => F5PushLiveLockLease.RequireOwner(text));
    }

    /// <summary>精確三個 token 可交換順序；時間必須存在且符合 02 指定的 round-trip 文字契約。</summary>
    [Theory]
    [InlineData("lane=A wp=F5 start=2026-11-28T20:00:00.0000000+08:00")]
    [InlineData("wp=F5\r\nlane=A start=2026-11-28T20:00:00.0000000+08:00\r\n")]
    public void Owner_ExactTokens_Accepted(string text) => F5PushLiveLockLease.RequireOwner(text);

    /// <summary>即使線與包精確，缺少鎖建立時間亦須拒絕，不能把 F12 的可省略時間契約套到本入口。</summary>
    [Fact]
    public void Owner_StartMissing_Refused() =>
        Assert.Throws<InvalidDataException>(() => F5PushLiveLockLease.RequireOwner("lane=A wp=F5"));

    /// <summary>限制純文字解析大小，不能因無限空白略過前置的有界輸入契約。</summary>
    [Fact]
    public void Owner_Oversized_Refused() =>
        Assert.Throws<InvalidDataException>(() => F5PushLiveLockLease.RequireOwner(new string(' ', 4097) + "lane=A wp=F5"));

    /// <summary>成功期間兩把鎖都不可獨占開啟；Dispose 後可重開且檔案原樣保留，證明租約不擁有刪鎖權。</summary>
    [Fact]
    public void Lease_HoldsBothHandles_AndDisposesWithoutChangingLocks()
    {
        using var fixture = new LockFixture();
        var original = File.ReadAllBytes(fixture.D365Path);
        using (var lease = F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory))
        {
            LockFixture.RequireHeld(fixture.D365Path);
            LockFixture.RequireHeld(fixture.LinePath);
            lease.Dispose();
            lease.Dispose();
        }
        LockFixture.RequireReleased(fixture.D365Path);
        LockFixture.RequireReleased(fixture.LinePath);
        Assert.Equal(original, File.ReadAllBytes(fixture.D365Path));
        Assert.Equal(original, File.ReadAllBytes(fixture.LinePath));
    }

    /// <summary>第二鎖錯 owner 時，第一鎖必須在例外返回前關閉；對方 SQL 鎖不參與核對或持有。</summary>
    [Fact]
    public void Lease_SecondOwnerWrong_DrainsFirstHandle()
    {
        using var fixture = new LockFixture();
        File.WriteAllText(fixture.LinePath, "lane=B wp=F5 " + Start, new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() => F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory));
        LockFixture.RequireReleased(fixture.D365Path);
        LockFixture.RequireReleased(fixture.LinePath);
    }

    /// <summary>第二把新鎖缺少時必須拒絕並釋放第一把，不能把單把 D365 鎖當成推播完整授權。</summary>
    [Fact]
    public void Lease_MissingLineLock_RefusedAndDrained()
    {
        using var fixture = new LockFixture();
        File.Delete(fixture.LinePath);
        Assert.Throws<FileNotFoundException>(() => F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory));
        LockFixture.RequireReleased(fixture.D365Path);
    }

    /// <summary>過渡期舊單鎖尚存在時仍視同 D365 被持有；即使新鎖也齊全，亦不能跳過過渡互斥規則。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lease_LegacyLockPresent_Refused(bool keepNewLocks)
    {
        using var fixture = new LockFixture();
        if (!keepNewLocks)
        {
            File.Delete(fixture.D365Path);
            File.Delete(fixture.LinePath);
        }
        File.WriteAllText(Path.Combine(fixture.Directory, "test-env.lock"), "lane=A wp=F5", new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() => F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory));
        if (keepNewLocks)
        {
            LockFixture.RequireReleased(fixture.D365Path);
            LockFixture.RequireReleased(fixture.LinePath);
        }
    }

    /// <summary>空檔及大於四 KiB 的第二鎖在讀本文之前拒絕，避免 unbounded ReadToEnd 並證明故障排空。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Lease_InvalidByteLength_RefusedAndDrained(int length)
    {
        using var fixture = new LockFixture();
        File.WriteAllBytes(fixture.LinePath, new byte[length]);
        Assert.Throws<InvalidDataException>(() => F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory));
        LockFixture.RequireReleased(fixture.D365Path);
        LockFixture.RequireReleased(fixture.LinePath);
    }

    /// <summary>壞 UTF-8 不以 replacement 字元默認修補；保留原始解碼故障且先關閉所有已開啟的 handle。</summary>
    [Fact]
    public void Lease_InvalidUtf8_RefusedAndDrained()
    {
        using var fixture = new LockFixture();
        File.WriteAllBytes(fixture.LinePath, new byte[] { 0xff, 0xfe });
        Assert.Throws<DecoderFallbackException>(() => F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory));
        LockFixture.RequireReleased(fixture.D365Path);
        LockFixture.RequireReleased(fixture.LinePath);
    }

    /// <summary>保留 PowerShell 舊版 UTF-8 BOM 相容，但完全不需要 SQL 鎖，避免擴大 F5 的資源序列。</summary>
    [Fact]
    public void Lease_Utf8BomAccepted_WithoutSqlLock()
    {
        using var fixture = new LockFixture();
        File.WriteAllText(fixture.LinePath, "lane=A wp=F5 " + Start, new UTF8Encoding(true));
        using var lease = F5PushLiveLockLease.AcquireFromDirectory(fixture.Directory);
        LockFixture.RequireHeld(fixture.D365Path);
        LockFixture.RequireHeld(fixture.LinePath);
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "test-env-sql.lock")));
    }

    /// <summary>每案唯一擁有者；路徑由測試自己產生，只在所有租約已排空後精確刪除該目錄。</summary>
    private sealed class LockFixture : IDisposable
    {
        /// <summary>新建兩個合成鎖，不接觸外部鎖；建構完成後只由此 fixture 負責刪除。</summary>
        internal LockFixture()
        {
            Directory = Path.Combine(Path.GetTempPath(), "f5-lock-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            try
            {
                File.WriteAllText(D365Path, "lane=A wp=F5 " + Start + "\r\n", new UTF8Encoding(false));
                File.WriteAllText(LinePath, "lane=A wp=F5 " + Start + "\r\n", new UTF8Encoding(false));
            }
            catch
            {
                System.IO.Directory.Delete(Directory, recursive: true);
                throw;
            }
        }

        /// <summary>本案唯一的絕對目錄，不接受使用者路徑或其他測試的目錄。</summary>
        internal string Directory { get; }
        /// <summary>先取得的 D365／IIS 資源鎖。</summary>
        internal string D365Path => Path.Combine(Directory, "test-env-d365.lock");
        /// <summary>後取得的 LINE 資源鎖，不包含 SQL。</summary>
        internal string LinePath => Path.Combine(Directory, "test-env-line.lock");

        /// <summary>由 OS 獨占開檔驗證被測租約仍持有讀取 handle，不依賴實作計數器。</summary>
        internal static void RequireHeld(string path) =>
            Assert.Throws<IOException>(() => { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });

        /// <summary>獨占開檔必須立即成功，證明例外與重複 Dispose 均不留下 handle。</summary>
        internal static void RequireReleased(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(stream.CanWrite);
        }

        /// <summary>只刪本案建立的目錄，若仍有 handle 則讓清理故障直接使測試失敗。</summary>
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
