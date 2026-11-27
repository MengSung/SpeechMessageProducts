/*
檔案整體說明
目的與定位：共通層 F3 的固定產品憑證目錄，提供既有 Webhook secret 與 Core credential 契約；路由只索引，不取代租戶授權。
主要功能與流程：每次先讀不含秘密的 revision／modifiedon，再以單一刷新閘門建立最多十分鐘的完整候選。正式 ID、產品與 Channel 鍵全部驗證後才原子發布，任何來源失敗都不得回落舊憑證。
使用邊界與維護：組織與產品固定於 owner，不保留 SDK、User、Session 或 HttpContext。擁有的 key 必須複製並在停止排空後清零，快照中的字串只能清參照。借入來源及診斷由主機最後釋放；測試應驗證取消、競態、重複 ID 與回收基線。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.ChannelPool;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
namespace ToolUtilityNameSpace.Channels;

/// <summary>
/// 固定產品的有界目錄；同步 resolver 使用相同非同步資料路徑，不以 Task.Run 包裝 SDK。
/// 十分鐘 timer 只清除過期參照、不預抓資料；重建永遠由下一次操作 lazy 觸發。
/// </summary>
public sealed class DataverseChannelDirectory : IWebhookChannelSecretResolver, IChannelCredentialSource, IDisposable, IAsyncDisposable
{
    /// <summary>所有狀態、入場計數與發布世代的唯一保護鎖，不在持鎖期間等待 I/O。</summary>
    private readonly object state = new();
    /// <summary>只序列化候選重建及健康停用，不序列化正常 revision 讀取與 HTTP。</summary>
    private readonly SemaphoreSlim refresh = new(1, 1);
    /// <summary>唯一停止訊號；所有 admitted 操作均連結它，排空後才 Dispose。</summary>
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConditionalWeakTable<Exception, object> reported = new();
    /// <summary>依正式 Id 保存當前拒絕版本；容量不超過產品 Channel 數，換版／移除時清除。</summary>
    private readonly Dictionary<Guid, string> rejected = new();
    private readonly string product;
    private readonly Guid organizationId;
    private readonly int maximumChannels;
    private readonly IChurchClock clock;
    private readonly TimeProvider timeProvider;
    /// <summary>唯一有限到期 timer；不捕捉建立者 ExecutionContext，DisposeAsync 等待回呼排空。</summary>
    private readonly ITimer expiry;
    private IChannelDirectorySource? source;
    private IChannelDiagnostics? diagnostics;
    private Func<string, byte[], string>? decrypt;
    private byte[] key;
    private Snapshot? snapshot;
    private Task? closeTask;
    private bool closing;
    private int active;
    private int foreignReported;
    private long generation;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 固定產品與組織，複製精確六十四 bytes key；借入來源、時鐘與診斷由主機最後處置。
    /// 解密委派只能用本次複製的 key，不得保存輸入；未指定時使用既有 ChannelTokenProtector。
    /// </summary>
    public DataverseChannelDirectory(string product, Guid organizationId, IChannelDirectorySource source, byte[] key,
        IChurchClock clock, IChannelDiagnostics diagnostics, int maximumChannels = 256,
        Func<string, byte[], string>? decrypt = null, TimeProvider? timeProvider = null)
    {
        ChannelDirectoryValidation.Deployment(product, organizationId, maximumChannels);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (key.Length != 64) throw new ArgumentException("A 64-byte channel key is required.", nameof(key));
        if (source.OrganizationId != organizationId || !string.Equals(source.Product, product, StringComparison.Ordinal))
            throw new ArgumentException("Channel source belongs to another deployment.", nameof(source));
        this.product = product; this.organizationId = organizationId; this.maximumChannels = maximumChannels;
        this.source = source; this.clock = clock; this.diagnostics = diagnostics;
        this.decrypt = decrypt ?? ChannelTokenProtector.Unprotect;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.key = (byte[])key.Clone();
        try
        {
            if (ExecutionContext.IsFlowSuppressed()) expiry = CreateExpiryTimer();
            else { using (ExecutionContext.SuppressFlow()) expiry = CreateExpiryTimer(); }
        }
        catch { CryptographicOperations.ZeroMemory(this.key); throw; }
    }

    /// <summary>已驗證完整可服務鍵的分離唯讀副本，不暴露密文、可變字典或 Session 參照。</summary>
    public IReadOnlyList<string> ConfiguredChannelIds
    {
        get
        {
            using var operation = Begin(default);
            try
            {
                var current = GetSnapshotAsync(operation.Token).GetAwaiter().GetResult();
                return Array.AsReadOnly(current.Entries.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray());
            }
            catch (Exception exception) { Failure(exception, "ChannelDirectory.Read", operation); throw; }
        }
    }

    /// <summary>每次驗 revision 後解析當次 secret；未知路由無後備，外借字串禁止保存至下一次操作。</summary>
    public bool TryResolveSecret(string channelId, [NotNullWhen(true)] out string? secret)
    {
        using var operation = Begin(default);
        try
        {
            var entry = ResolveAsync(channelId, operation.Token).GetAwaiter().GetResult();
            secret = entry?.Secret;
            return secret != null;
        }
        catch (Exception exception) { secret = null; Failure(exception, "ChannelDirectory.ResolveSecret", operation); throw; }
    }

    /// <summary>回傳目前產品的不可變核心憑證；成功不代表 caller 已獲租戶授權，不可長期保留。</summary>
    public bool TryGetCredential(string channelKey, out LineChannelCredential? credential)
    {
        using var operation = Begin(default);
        try
        {
            var entry = ResolveAsync(channelKey, operation.Token).GetAwaiter().GetResult();
            credential = entry?.Credential;
            return credential != null;
        }
        catch (Exception exception) { credential = null; Failure(exception, "ChannelDirectory.ResolveCredential", operation); throw; }
    }

    /// <summary>啟動驗證完整候選；零筆可合法啟動，但絕不為未知 Channel 建立預設憑證。</summary>
    public void ValidateConfiguration() => _ = ConfiguredChannelIds;

    /// <summary>主機停止時先拒絕入場並取消所有操作，等待 HTTP／SDK 及 timer 回呼排空後清 key。</summary>
    public ValueTask DisposeAsync()
    {
        lock (state)
        {
            if (closeTask != null) return new ValueTask(closeTask);
            closing = true;
            snapshot = null;
            generation++;
            if (active == 0) drained.TrySetResult();
            closeTask = CloseCoreAsync();
            return new ValueTask(closeTask);
        }
    }

    /// <summary>同步主機相容關閉；禁止從 admitted 委派內呼叫，否則會等待自己的操作。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>供健康及傳輸建立總共十秒的單一 owner；最多一百二十八個並行操作，不建立無界等待佇列。</summary>
    internal Operation Begin(CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        lock (state)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (active >= 128) throw new InvalidOperationException("Channel directory operation capacity exceeded.");
            active++;
            try { return new Operation(this, caller, stop.Token, timeProvider); }
            catch { active--; throw; }
        }
    }

    /// <summary>取得純 UTC 時刻，只於健康完成時使用，不讓伺服器本地時間流入資料。</summary>
    internal DateTimeOffset UtcNow => clock.UtcNow.ToUniversalTime();

    /// <summary>取得操作內的已驗證 entry；非法或不在可服務集合內的路由只回 null。</summary>
    internal async Task<Entry?> ResolveAsync(string channelKey, CancellationToken cancellationToken)
    {
        if (!ChannelDirectoryValidation.Digits(channelKey, 20)) return null;
        var current = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        lock (state)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 另一操作已失效時不能把先前命中的憑證當成新解析結果。
            if (!ReferenceEquals(snapshot, current)) throw new InvalidOperationException("Channel snapshot invalidated during resolution.");
            return current.Entries.GetValueOrDefault(channelKey);
        }
    }

    /// <summary>只使觀察到的同一世代失效；舊 HTTP 不可清掉已發布的新世代。</summary>
    internal void Invalidate(long expectedGeneration)
    {
        lock (state)
        {
            if (snapshot?.Generation != expectedGeneration) return;
            snapshot = null; generation++;
        }
    }

    /// <summary>最終 401 封鎖相同正式 Id／版本，換版才恢復；去重狀態有產品容量上限。</summary>
    internal bool Reject(Entry entry)
    {
        lock (state)
        {
            if (rejected.TryGetValue(entry.Record.Id, out var version) && string.Equals(version, entry.Record.Revision, StringComparison.Ordinal)) return false;
            if (!rejected.ContainsKey(entry.Record.Id) && rejected.Count >= maximumChannels)
                throw new InvalidOperationException("Rejected channel capacity exceeded.");
            rejected[entry.Record.Id] = entry.Record.Revision;
            if (snapshot?.Generation == entry.Generation) { snapshot = null; generation++; }
            return true;
        }
    }

    /// <summary>健康 401 只停用相同正式版本；同版本並行拒絕最多一筆回寫及一次通知。</summary>
    internal async Task DisableAsync(Entry entry, ChannelHealthResult result, Operation operation)
    {
        await refresh.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            if (!Reject(entry)) return;
            var unauthorized = new HttpRequestException("Channel health authorization rejected.", null, HttpStatusCode.Unauthorized);
            try
            {
                await using var lease = await Source.OpenAsync(operation.Token).ConfigureAwait(false);
                await lease.WriteHealthAsync(entry.Record, result, true, operation.Token).ConfigureAwait(false);
            }
            catch (Exception failure) { throw new AggregateException("Channel invalidation could not be persisted.", unauthorized, failure); }
            Report(unauthorized, "ChannelHealth.Unauthorized", operation.Caller, operation);
        }
        finally { refresh.Release(); }
    }

    /// <summary>成功統計只回寫已解析的正式版本；回寫造成 modifiedon 異動後必須清除當前快照。</summary>
    internal async Task WriteHealthAsync(Entry entry, ChannelHealthResult result, CancellationToken cancellationToken)
    {
        await using (var lease = await Source.OpenAsync(cancellationToken).ConfigureAwait(false))
            await lease.WriteHealthAsync(entry.Record, result, false, cancellationToken).ConfigureAwait(false);
        Invalidate(entry.Generation);
    }

    /// <summary>真故障清除快照且保留原 Exception；未知路由是預期拒絕，不清快取也不告警。</summary>
    internal void Failure(Exception exception, string operationName, Operation operation)
    {
        if (exception is KeyNotFoundException) return;
        lock (state) { snapshot = null; generation++; }
        if (exception is OperationCanceledException && (operation.Caller.IsCancellationRequested || stop.IsCancellationRequested)) return;
        Report(exception, operationName, operation.Caller, operation);
    }

    /// <summary>同一原例外以弱鍵去重，不保留敏感本文；診斷故障只走固定 stderr，不遞迴或取代原失敗。</summary>
    internal void Report(Exception exception, string operationName, CancellationToken caller, Operation? operation = null)
    {
        if (operation != null && !operation.TryMarkReported()) return;
        IChannelDiagnostics? sink;
        lock (state)
        {
            if (reported.TryGetValue(exception, out _)) return;
            reported.Add(exception, new object());
            sink = diagnostics;
        }
        try { sink?.Report(exception, operationName, caller); }
        catch
        {
            try { Console.Error.WriteLine("[ChannelDirectory] DiagnosticsUnavailable"); }
            catch (System.IO.IOException) { /* stderr 不可用時仍保留產品原例外，不再嘗試遞迴通知。 */ }
        }
    }

    /// <summary>只於 admitted 操作讀取借用來源；主機不得在目錄排空之前 Dispose 來源或 manager。</summary>
    private IChannelDirectorySource Source => source ?? throw new ObjectDisposedException(nameof(DataverseChannelDirectory));

    /// <summary>
    /// 每次解析只借一個 SDK lease：先取不含秘密的版本；快取失配後在同租約內序列化刷新，
    /// 再讀完整候選並以刷新前後 metadata 驗證。任何不一致、取消或解密故障都不發布候選，
    /// 且失效期間沒有舊憑證回退；借入 lease 在此方法返回或失敗時確定歸還。
    /// </summary>
    private async Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var sourceLease = await Source.OpenAsync(cancellationToken).ConfigureAwait(false);
        var observed = Own(await sourceLease.ReadAsync(false, cancellationToken).ConfigureAwait(false));
        lock (state)
            if (snapshot is { } current && Fresh(current) && ChannelDirectoryValidation.Same(current.Metadata, observed))
                return current;

        await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 等待閘門時來源可能再旋轉；使用同一 lease 重讀版本，避免根據入閘前快照解密。
            observed = Own(await sourceLease.ReadAsync(false, cancellationToken).ConfigureAwait(false));
            lock (state)
                if (snapshot is { } current && Fresh(current) && ChannelDirectoryValidation.Same(current.Metadata, observed))
                    return current;

            var created = timeProvider.GetTimestamp();
            long epoch;
            lock (state)
            {
                snapshot = null;
                generation++;
                epoch = generation;
            }

            var rows = Own(await sourceLease.ReadAsync(true, cancellationToken).ConfigureAwait(false));
            if (!ChannelDirectoryValidation.Same(rows, observed))
                throw new InvalidOperationException("Channel snapshot changed before credential publication.");

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            PruneRejected(rows);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ChannelDirectoryValidation.Serviceable(row) || IsRejected(row)) continue;
                var token = Decrypt(row.TokenEncrypted);
                if (token.Length == 0 || token.Length > 2927) throw new InvalidOperationException("Invalid bounded channel token.");
                var secret = Decrypt(row.SecretEncrypted);
                if (secret.Length == 0 || secret.Length > 2927 || secret.Any(character => character <= 32 || character >= 127))
                    throw new InvalidOperationException("Invalid bounded channel secret.");
                entries.Add(row.ChannelKey, new Entry(row, new LineChannelCredential(token), token, secret, epoch));
            }

            var verified = Own(await sourceLease.ReadAsync(false, cancellationToken).ConfigureAwait(false));
            if (!ChannelDirectoryValidation.Same(rows, verified))
                throw new InvalidOperationException("Channel snapshot changed during credential loading.");
            var candidate = new Snapshot(rows, entries, created, epoch);
            lock (state)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (closing || generation != epoch || !Fresh(candidate))
                    throw new InvalidOperationException("Channel snapshot superseded before publication.");
                snapshot = candidate;
                expiry.Change(Lifetime - timeProvider.GetElapsedTime(created), Timeout.InfiniteTimeSpan);
                return candidate;
            }
        }
        finally { refresh.Release(); }
    }

    /// <summary>篩選及驗證本產品集合；同一 owner 對外產品最多產生一次固定安全告警。</summary>
    private ChannelDirectoryRecord[] Own(IReadOnlyList<ChannelDirectoryRecord> rows) =>
        ChannelDirectoryValidation.OwnRows(rows, product, maximumChannels, () =>
        {
            if (Interlocked.Exchange(ref foreignReported, 1) == 0)
                Report(new InvalidOperationException("Foreign product channel ignored."), "ChannelDirectory.ForeignProduct", default);
        });

    /// <summary>只保留目前產品現存 Id 的拒絕版本；每次候選全量驗證後清除已刪除或換版項目。</summary>
    private void PruneRejected(ChannelDirectoryRecord[] rows)
    {
        var versions = rows.ToDictionary(row => row.Id, row => row.Revision);
        lock (state)
            foreach (var id in rejected.Keys.ToArray())
                if (!versions.TryGetValue(id, out var version) || !string.Equals(version, rejected[id], StringComparison.Ordinal)) rejected.Remove(id);
    }

    /// <summary>只檢正式版本是否已確定拒絕，不以 Channel 顯示名或 caller 別名當索引。</summary>
    private bool IsRejected(ChannelDirectoryRecord row)
    {
        lock (state) return rejected.TryGetValue(row.Id, out var version) && string.Equals(version, row.Revision, StringComparison.Ordinal);
    }

    /// <summary>密文及明文都有限長；委派拿到的 key 是獨立複本，無論成功與否都清零。</summary>
    private string Decrypt(string? envelope)
    {
        if (string.IsNullOrEmpty(envelope) || envelope.Length > 4000) throw new InvalidOperationException("Invalid bounded channel ciphertext.");
        var copy = (byte[])key.Clone();
        try
        {
            var value = decrypt!(envelope, copy);
            if (value == null || value.Length > 2927) throw new InvalidOperationException("Invalid bounded channel plaintext.");
            return value;
        }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    /// <summary>TTL 以單調時間判斷，不讓主機時鐘倒退延長秘密快照生命週期。</summary>
    private bool Fresh(Snapshot value) => timeProvider.GetElapsedTime(value.Created) is var age && age >= TimeSpan.Zero && age < Lifetime;

    /// <summary>建立唯一沒有 request ExecutionContext 的到期 timer，未發布時保持停用。</summary>
    private ITimer CreateExpiryTimer() => timeProvider.CreateTimer(static owner => ((DataverseChannelDirectory)owner!).Expire(), this,
        Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>timer 只在到期後清除共享快照參照，沒有 SDK、告警、使用者回呼或背景刷新。</summary>
    private void Expire()
    {
        lock (state)
            if (snapshot is { } current && !Fresh(current)) { snapshot = null; generation++; }
    }

    /// <summary>排空所有 admitted 操作與 timer 回呼，再清除自有 key、候選、委派及借入來源參照。</summary>
    private async Task CloseCoreAsync()
    {
        // 確保 DisposeAsync 已離開 state lock 才執行取消回呼，避免同步 callback 重入死鎖。
        await Task.Yield();
        try
        {
            try { stop.Cancel(); }
            catch (AggregateException failure) { Report(failure, "ChannelDirectory.Stop", default); }
            await drained.Task.ConfigureAwait(false);
            await expiry.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (state)
            {
                snapshot = null; rejected.Clear(); reported.Clear();
                CryptographicOperations.ZeroMemory(key); key = Array.Empty<byte>();
                source = null; decrypt = null; diagnostics = null;
            }
            refresh.Dispose(); stop.Dispose();
        }
    }

    /// <summary>每個 operation 恰一次歸還入場計數，最後一個退出者喚醒停止 owner。</summary>
    private void Exit()
    {
        lock (state) { active--; if (closing && active == 0) drained.TrySetResult(); }
    }

    /// <summary>操作內不可變秘密，只在當前快照或有限在途操作持有；不能記錄或序列化。</summary>
    internal sealed class Entry(ChannelDirectoryRecord record, LineChannelCredential credential, string token, string secret, long generation)
    {
        internal ChannelDirectoryRecord Record { get; } = record;
        internal LineChannelCredential Credential { get; } = credential;
        internal string Token { get; } = token;
        internal string Secret { get; } = secret;
        internal long Generation { get; } = generation;
    }

    /// <summary>完整候選一次發布後不再修改；中繼資料及字典不對外暴露，沒有部分填入的可見狀態。</summary>
    private sealed record Snapshot(ChannelDirectoryRecord[] Metadata, Dictionary<string, Entry> Entries, long Created, long Generation);

    /// <summary>十秒 timeout／caller／shutdown token 的唯一 owner；每次傳輸可跨一次重解析但不得重置期限。</summary>
    internal sealed class Operation : IDisposable
    {
        private DataverseChannelDirectory? owner;
        private readonly CancellationTokenSource timeout;
        private readonly CancellationTokenSource linked;
        private int reported;
        /// <summary>一個有界操作最多產生一筆終端診斷，避免 401 失效與外層重拋重複告警。</summary>
        internal bool TryMarkReported() => Interlocked.Exchange(ref reported, 1) == 0;
        /// <summary>只有 Begin 入場成功後才建立；建立中失敗仍須釋放已取得的 timer。</summary>
        internal Operation(DataverseChannelDirectory owner, CancellationToken caller, CancellationToken shutdown, TimeProvider timeProvider)
        {
            this.owner = owner; Caller = caller;
            timeout = new CancellationTokenSource(OperationTimeout, timeProvider);
            try { linked = CancellationTokenSource.CreateLinkedTokenSource(caller, shutdown, timeout.Token); }
            catch { timeout.Dispose(); throw; }
        }
        /// <summary>只有 caller token 可分類正常使用者取消，不將內部 timeout 誤當正常取消。</summary>
        internal CancellationToken Caller { get; }
        /// <summary>整次操作總期限，所有 SDK、HTTP 及串流讀取都必須尊重。</summary>
        internal CancellationToken Token => linked.Token;
        /// <summary>先釋放連結及 timer，再退出 owner；不 Dispose caller 或 shutdown 來源。</summary>
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref owner, null);
            if (current == null) return;
            linked.Dispose(); timeout.Dispose(); current.Exit();
        }
    }
}
