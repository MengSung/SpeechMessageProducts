/*
檔案整體說明
目的與定位：F4每產品、Channel、宮格操作與UTC小時的SQL速率預算，確保多程序仍各只允許100次建立或刪除。
主要功能與流程：先驗固定產品與有界識別，再以完整四段鍵SHA定位專用hash及五秒SQL鎖。單一交易更新計數與兩小時期限；第101次回false，工作內記scoped下一UTC小時，dispatcher在handler返回後重排，不消耗錯誤重試。
使用邊界與維護：不呼叫LINE、不載入token、不把Channel識別當授權。每次只存整數及完整原始鍵以拒絕碰撞；連線、分散鎖及交易由using確定釋放。無記憶體預算快取或AsyncLocal，scope及停止owner限制所有存活操作。
*/
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Scheduling.Hangfire;

/// <summary>單次scoped預算介面，只借用本Host SQL與本次延期狀態。</summary>
internal sealed class SqlChannelRateBudget(HangfireSchedulingRuntime runtime, ScheduledJobExecution execution) : IChannelRateBudget
{
    /// <summary>各操作每UTC小時原子100次；false時handler須立即停止外部操作並返回。</summary>
    public async Task<bool> TryAcquireAsync(string product, string channelKey, string operation, CancellationToken ct)
    {
        if (!string.Equals(product, runtime.Options.Product, StringComparison.Ordinal))
            throw new ArgumentException("The rate budget belongs to another deployment.", nameof(product));
        JobKey.RequireIdentifier(channelKey, nameof(channelKey));
        if (operation is not "richmenu-create" and not "richmenu-delete")
            throw new ArgumentException("An approved rich menu operation is required.", nameof(operation));
        ct.ThrowIfCancellationRequested();
        // 工作內已有dispatcher的owner lease，停止發布後仍可完成同一次合法清理／計數。
        using var owner = execution.IsExecuting ? null : await runtime.EnterOperationAsync(ct).ConfigureAwait(false);
        var now = runtime.Clock.UtcNow.ToUniversalTime();
        var hour = now.Ticks / TimeSpan.TicksPerHour;
        var identity = $"{product}:{channelKey}:{operation}:{hour.ToString(CultureInfo.InvariantCulture)}";
        var address = "budget:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using var connection = runtime.Storage.GetConnection();
        using var held = connection.AcquireDistributedLock(address, TimeSpan.FromSeconds(5));
        ct.ThrowIfCancellationRequested();
        var values = connection.GetAllEntriesFromHash(address);
        var used = 0;
        if (values is not null && (values.Count != 2 || values.GetValueOrDefault("key") != identity ||
            !int.TryParse(values.GetValueOrDefault("used"), NumberStyles.None, CultureInfo.InvariantCulture, out used) ||
            used < 0 || used > 100))
            throw new InvalidOperationException("Stored rate budget conflicts with the complete scope.");
        if (used == 100)
        {
            if (execution.IsExecuting)
                execution.DeferredUntil = new DateTimeOffset((hour + 1) * TimeSpan.TicksPerHour, TimeSpan.Zero);
            return false;
        }
        HangfireJobScheduler.Write(connection, address, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = identity, ["used"] = (used + 1).ToString(CultureInfo.InvariantCulture)
        }, TimeSpan.FromHours(2));
        return true;
    }
}
