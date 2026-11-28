/*
檔案整體說明
目的與定位：F5 c121／c124 實機入口的有界寫前清冊接縫，保存本輪正式範圍以便故障後精確讀回，不是產品發送紀錄。
主要功能與流程：固定外部根以 CreateNew 開一個本輪 JSONL，任何操作返回前都由唯一 FileStream 完成 Write 與 Flush(true)；
只綁 c121／c124 正式租戶，canonical 操作與批次鍵由 runId 衍生，旗標候選及正式發送結果按已綁 scope 核對後保存。
輸入與信任邊界：純值僅含正式租戶／紀錄 ID、numeric Channel、固定操作鍵與旗標候選；不接受 LINE userId、訊息或秘密。
Channel 的 T01／T02 身分、SDK 組織授權及本輪 marker 真實所有權仍由完整實機入口負責，清冊本身不是遠端授權。
資源與隔離：每個 journal 唯一擁有一個 sink、最多兩筆租戶與兩筆結果，局部 gate 串行 append／Dispose，
最多二百五十六筆、二百五十六 KiB，每筆最多四 KiB；沒有 timer、網路、全域快取或背景工作。
工廠失敗關閉 sink；成功後 owner 在所有作業實際排空後 Dispose，清除局部參照但不刪檔，證據保留到明確核銷。
失敗與驗證：任何寫入、flush 或容量故障後永久拒絕後續操作，保留原例外及部分檔案，不能把未知提交當回滾；
錯 scope 在 append 前拒絕且不污染既有證據。離線真檔案、合成 sink、故障、並行與容量案例先 RED 再 GREEN，
只證明本接縫，不代表完整 Live 入口、真正磁碟硬體耐久性、D365、LINE 或手機驗收。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LineMessagingProcessor.Core.Gateway;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>清冊只允許明列的生命週期事件，不接收原始例外、任意訊息或外部文字。</summary>
internal enum F5LiveAction
{
    /// <summary>在呼叫 F12 建立前保存，建立結果未知仍保留本輪及 F12 清冊。</summary>
    CreateStarted = 1,
    /// <summary>正式 scope 已綁定，準備透過唯一 Gateway 發送。</summary>
    SendStarted = 2,
    /// <summary>c124 旗標 CAS 已依原 helper 的正式資料讀回。</summary>
    FlagReadBack = 3,
    /// <summary>準備清理前仍須依完整協調者驗證已無未知提交。</summary>
    CleanupStarted = 4,
    /// <summary>本輪正式 ID 清理及原基線均已由協調者讀回。</summary>
    CleanupComplete = 5
}

/// <summary>綁定後的純值範圍，鍵可重算但不能取代正式資料庫 ID 或服務端授權。</summary>
/// <param name="Code">固定 c121 或 c124。</param>
/// <param name="TenantId">F12 寫入並確認的正式租戶主鍵。</param>
/// <param name="ChannelKey">F12 配發後由實機入口核對 T01／T02 的數字鍵。</param>
/// <param name="CorrelationId">只由本輪 GUID 與固定代碼衍生的操作鍵。</param>
/// <param name="BatchKey">本輪接受或政策拒絕的 canonical 批次鍵。</param>
internal sealed record F5LiveJournalTenant(string Code, Guid TenantId, string ChannelKey, string CorrelationId, string BatchKey);

/// <summary>同流程離線故障接縫；Append 返回必須已完成落盤，唯一 journal owner 負責 Dispose。</summary>
internal interface IF5LiveJournalSink : IDisposable
{
    /// <summary>將一筆已受上限約束的 UTF-8 JSONL 寫入並同步 flush；不能保存借入的 Span。</summary>
    void Append(ReadOnlySpan<byte> line);
}

/// <summary>單輪同步寫前清冊 owner；永不自動刪除清冊或把它作為繞過資料庫驗證的授權。</summary>
internal sealed class F5PushLiveJournal : IDisposable
{
    private readonly object gate = new();
    private readonly Guid runId;
    private readonly int maximumRecords;
    private readonly int maximumBytes;
    private readonly Dictionary<string, F5LiveJournalTenant> tenants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SendBatchSummary> results = new(StringComparer.Ordinal);
    private IF5LiveJournalSink? sink;
    private int records;
    private int bytes;
    private bool faulted;

    /// <summary>工廠驗證輸入後移交唯一 sink；建構本身不呼叫外部 I/O，header 由工廠先保存。</summary>
    private F5PushLiveJournal(Guid runId, IF5LiveJournalSink sink, int maximumRecords, int maximumBytes)
    {
        this.runId = runId;
        this.sink = sink;
        this.maximumRecords = maximumRecords;
        this.maximumBytes = maximumBytes;
    }

    /// <summary>正式入口只使用機器外部固定根，不接受使用者、租戶或路由指定證據路徑。</summary>
    internal static F5PushLiveJournal Create(Guid runId) => CreateAt(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage", "artifacts", "f5-live"), runId);

    /// <summary>
    /// 離線可指定本案外部目錄；所有既有父路徑拒絕 link，禁止目前 repo 內的路徑。
    /// 檔名只由非空 runId 衍生，CreateNew 不覆寫舊證據；工廠失敗仍保留已建立的空檔或部分檔。
    /// </summary>
    internal static F5PushLiveJournal CreateAt(string directory, Guid runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (runId == Guid.Empty || !Path.IsPathFullyQualified(directory)) throw Rejected();
        var root = Path.GetFullPath(directory);
        var repository = Path.GetFullPath(DeploymentCli.FindRepositoryRoot());
        if (root.Equals(repository, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Rejected();
        SchemaDeploymentCli.EnsureNoLinks(root);
        Directory.CreateDirectory(root);
        SchemaDeploymentCli.EnsureNoLinks(root);
        var path = Path.Combine(root, runId.ToString("N") + ".jsonl");
        SchemaDeploymentCli.EnsureNoLinks(path);
        return CreateForTesting(runId, new FileSink(path));
    }

    /// <summary>
    /// 同流程注入合成 sink 與較小測試容量；接管 sink 後任何初始化失敗都關閉，且保留原始與清理故障。
    /// header 已 append 並確認 flush 才回傳 owner，不能把尚未存在的復原清冊交給後續 SDK。
    /// </summary>
    internal static F5PushLiveJournal CreateForTesting(Guid runId, IF5LiveJournalSink sink,
        int maximumRecords = 256, int maximumBytes = 262144)
    {
        ArgumentNullException.ThrowIfNull(sink);
        try
        {
            if (runId == Guid.Empty || maximumRecords is < 1 or > 256 || maximumBytes is < 256 or > 262144) throw Rejected();
            var journal = new F5PushLiveJournal(runId, sink, maximumRecords, maximumBytes);
            journal.Append(new { version = 1, kind = "run", runId, organization = "goodshepherd20", product = "GoodShepherd2", utc = DateTimeOffset.UtcNow });
            return journal;
        }
        catch (Exception original)
        {
            try { sink.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("F5 journal initialization and cleanup failed.", original, cleanup); }
            throw;
        }
    }

    /// <summary>
    /// 只保存兩個固定代碼與不同正式 ID；相同完整綁定可冪等，不同內容不覆寫。
    /// 輸入格式驗證不等於 T01／T02 授權，實機入口必須先對配發後的正式 Channel 做該項核對。
    /// </summary>
    internal F5LiveJournalTenant BindTenant(string code, Guid tenantId, string channelKey)
    {
        lock (gate)
        {
            _ = Ready();
            RequireCode(code);
            if (tenantId == Guid.Empty || string.IsNullOrEmpty(channelKey) || channelKey.Length > 20) throw Rejected();
            foreach (var value in channelKey) if (value is < '0' or > '9') throw Rejected();
            var correlation = "f5-" + runId.ToString("N") + "-" + code;
            var candidate = new F5LiveJournalTenant(code, tenantId, channelKey, correlation,
                SendBatchKeys.Create("GoodShepherd2", channelKey, tenantId, correlation, code == "c121" ? 0 : -1));
            if (tenants.TryGetValue(code, out var existing))
                return existing == candidate ? existing : throw Rejected();
            foreach (var row in tenants.Values) if (row.TenantId == tenantId) throw Rejected();
            Append(new { kind = "tenant", scope = candidate, utc = DateTimeOffset.UtcNow });
            tenants.Add(code, candidate);
            return candidate;
        }
    }

    /// <summary>c124 CAS 前的同步保存委派；只有相同 run marker、正式綁定 ID、不同旗標及數字版本可落盤。</summary>
    internal void SaveFlag(F5LiveFlagWrite write)
    {
        lock (gate)
        {
            _ = Ready();
            ArgumentNullException.ThrowIfNull(write);
            var tenant = Bound("c124");
            if (write.Code != "c124" || write.TenantId != tenant.TenantId || write.Marker != runId.ToString("N") ||
                write.Previous == write.Requested || string.IsNullOrEmpty(write.RowVersion) || write.RowVersion.Length > 64) throw Rejected();
            foreach (var value in write.RowVersion) if (value is < '0' or > '9') throw Rejected();
            Append(new { kind = "flag-before", candidate = write, utc = DateTimeOffset.UtcNow });
        }
    }

    /// <summary>只確認單人正式接受或政策拒絕，完整相同結果可冪等；跨代碼同 ID 或結果改變一律拒絕。</summary>
    internal void ConfirmSend(string code, SendBatchSummary result)
    {
        lock (gate)
        {
            _ = Ready();
            ArgumentNullException.ThrowIfNull(result);
            _ = Bound(code);
            if (result.RecordId == Guid.Empty || result.RecipientCount != 1 ||
                result.Outcome != (code == "c121" ? SendRecordOutcome.Accepted : SendRecordOutcome.Rejected)) throw Rejected();
            if (results.TryGetValue(code, out var prior))
            {
                if (prior != result) throw Rejected();
                return;
            }
            foreach (var priorResult in results.Values) if (priorResult.RecordId == result.RecordId) throw Rejected();
            Append(new { kind = "send-confirmed", code, result.RecordId, result.RecipientCount, outcome = (int)result.Outcome, utc = DateTimeOffset.UtcNow });
            results.Add(code, result);
        }
    }

    /// <summary>保存封閉 enum 階段；只有 CreateStarted 可早於綁定，階段紀錄不取代真正 SDK 或清理讀回證據。</summary>
    internal void RecordAction(string code, F5LiveAction action)
    {
        lock (gate)
        {
            _ = Ready();
            RequireCode(code);
            if (action is < F5LiveAction.CreateStarted or > F5LiveAction.CleanupComplete) throw Rejected();
            if (action != F5LiveAction.CreateStarted) _ = Bound(code);
            Append(new { kind = "action", code, action = (int)action, utc = DateTimeOffset.UtcNow });
        }
    }

    /// <summary>固定代碼與綁定缺一就拒絕，不能用 caller 提供的 tenantId 重新推導或覆寫原 scope。</summary>
    private F5LiveJournalTenant Bound(string code)
    {
        RequireCode(code);
        return tenants.TryGetValue(code, out var value) ? value : throw Rejected();
    }

    /// <summary>封閉代碼清單不接收他線或任意租戶名稱。</summary>
    private static void RequireCode(string code)
    {
        if (code is not ("c121" or "c124")) throw Rejected();
    }

    /// <summary>關閉優先於故障判斷；故障清冊只能由外部明確核銷，不能再次使用或當成成功繼續操作。</summary>
    private IF5LiveJournalSink Ready()
    {
        ObjectDisposedException.ThrowIf(sink is null, this);
        if (faulted) throw new InvalidOperationException("F5 journal is faulted; retained evidence requires reconciliation.");
        return sink;
    }

    /// <summary>
    /// 只有持 gate 的方法或尚未發布的工廠呼叫；typed 候選最大四 KiB，整檔與筆數都在 I/O 前限額。
    /// 每筆含 CRLF，sink 返回代表 flush 已確認；計數僅在返回後增加，任何失敗永久關閉後續操作。
    /// </summary>
    private void Append<T>(T candidate)
    {
        var writer = Ready();
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(candidate);
            if (json.Length > 4094 || records >= maximumRecords || json.Length + 2 > maximumBytes - bytes) throw Rejected();
            var line = new byte[json.Length + 2];
            json.CopyTo(line, 0);
            line[^2] = (byte)'\r';
            line[^1] = (byte)'\n';
            writer.Append(line);
            records++;
            bytes += line.Length;
        }
        catch { faulted = true; throw; }
    }

    /// <summary>固定錯誤不附原始輸入、檔案內容、秘密或正式 ID。</summary>
    private static InvalidDataException Rejected() => new("F5 live journal scope, identity or capacity is invalid.");

    /// <summary>等待局部 append 返回後只釋放一次 sink，清空兩個小快照且保留檔案證據，不重新 flush 或刪檔。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            var owner = sink;
            if (owner is null) return;
            sink = null;
            tenants.Clear();
            results.Clear();
            owner.Dispose();
        }
    }

    /// <summary>唯一真實檔案 owner；CreateNew／WriteThrough，禁止外部寫入或替換，證據只允許其他讀者。</summary>
    private sealed class FileSink : IF5LiveJournalSink
    {
        private readonly FileStream stream;
        /// <summary>路徑已由工廠限制；建構成功才持有一個 FileStream，不覆寫既有內容。</summary>
        internal FileSink(string path) => stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough);
        /// <summary>單筆寫入後同步 Flush(true)，任何故障由 journal 保留且不再授權下一操作。</summary>
        public void Append(ReadOnlySpan<byte> line) { stream.Write(line); stream.Flush(flushToDisk: true); }
        /// <summary>只關閉自己建立的 handle，絕不刪證據或其父目錄。</summary>
        public void Dispose() => stream.Dispose();
    }
}
