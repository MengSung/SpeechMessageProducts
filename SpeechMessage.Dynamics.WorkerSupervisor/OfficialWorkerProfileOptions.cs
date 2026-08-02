namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 定義單一 official Worker profile generation 的部署擁有、init-only bootstrap snapshot。
/// <see cref="OfficialWorkerProfileExecutor"/> 只為此 generation 建立一個 process、named pipe、
/// stdout/stderr reader graph 與一個容量為一的 operation gate；因此每個 Worker 同時最多一個在途作業，
/// 並在多次依序作業間重複使用同一個 worker-local <c>CrmServiceClient</c>，不提供可調高的 per-worker concurrency。
/// </summary>
/// <remarks>
/// 此型別只含非機密、不可由 request 改寫的識別與有限門檻，不含 CRM endpoint、Credential、Token、Cookie、
/// connection string、caller identity 或 product Session。Executor 在 hash 驗證、Pipe 或 Process ownership
/// 開始前先驗證 Worker version、三個 identifier、絕對路徑、64 位十六進位 SHA-256、所有 timeout 與 recycle
/// options；任一值不合法即 fail closed。作業 timeout／caller cancellation 無法中斷已進入 worker 內的同步
/// CRM SDK 呼叫，所以該 generation 會停止 admission，並由有限 drain deadline 內的 graceful drain／forced
/// process termination 回收 WCF channel、handle、reader task 與 process memory。
/// </remarks>
public sealed class OfficialWorkerProfileOptions
{
    /// <summary>
    /// 取得 server-owned profile alias。值必須是 1 至 128 字元的安全 identifier，
    /// 只用於綁定已授權 routing；不得包含 CRM host、Credential、Session 或 caller 提供的動態路由值。
    /// </summary>
    public required string ProfileAlias { get; init; }

    /// <summary>
    /// 取得不可變 profile generation ID。此值同時綁定 bootstrap、READY handshake 與每個 Worker request，
    /// 防止 active／draining generation、不同 Credential 或不同 package graph 共用同一 process/client state。
    /// </summary>
    public required string ProfileGenerationId { get; init; }

    /// <summary>
    /// 取得此 generation 唯一允許的 CE worker version；它決定 executable kind 與 READY CE version，
    /// CE 8.2／9.1 不得在同一 process 或 SDK assembly graph 內混用。
    /// </summary>
    public required OfficialWorkerVersion WorkerVersion { get; init; }

    /// <summary>
    /// 取得部署審核後的絕對 worker executable path。驗證不會搜尋替代路徑或 fallback executable；
    /// 路徑不完整時在 Process allocation 前拒絕整個 generation。
    /// </summary>
    public required string WorkerExecutablePath { get; init; }

    /// <summary>
    /// 取得 executable 的 64 位十六進位 SHA-256。Executor 在建立 Pipe／Process 前以固定時間比較實際 hash；
    /// mismatch 直接 fail closed，不會改寫設定、換版或啟動未驗證程式。
    /// </summary>
    public required string WorkerExecutableSha256 { get; init; }

    /// <summary>
    /// 取得不可變 package-lock ID。值會進入非機密 bootstrap 與 nonce-bound READY 驗證，
    /// 確保執行中的 Worker 與部署批准的 Microsoft SDK dependency graph 完全一致。
    /// </summary>
    public required string PackageLockId { get; init; }

    /// <summary>
    /// 取得 process 啟動後等待 named-pipe connection 與完整 READY frame 的有限期限。
    /// 值必須大於零且不超過十分鐘；超時時 generation 從未發布，Executor 會要求強制退休並保留唯一 cleanup owner。
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 取得單一 request/response IPC round trip 的有限期限。Executor 的容量一 gate 讓此期限只套用於一個
    /// 在途 operation；超時或 caller cancellation 會停止後續 admission 並標記 forced termination。
    /// 此值不能直接取消已進入同步 CRM SDK 的呼叫，Process exit 才是其最終資源清理邊界。
    /// </summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 取得整個 deterministic cleanup attempt 的有限期限，涵蓋停止 admission、取得 operation gate、
    /// graceful drain、必要時 Kill、確認 process exit、關閉 Pipe／reader 及等待所有 Execute entrant 離開。
    /// 值必須大於零且不超過十分鐘；若期限內無法證明清理完成，Executor 保留資源 reference 並允許後續
    /// serialized Dispose 重試，不能把未知 process/handle 狀態誤報為已釋放。
    /// </summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 取得此 generation 唯一擁有的 immutable recycle 門檻。Age、完整作業數、Private Bytes、Working Set
    /// 與連續完整 timeout 都必須是有限正值並受部署硬上限約束；達門檻後只停止下一次 admission，
    /// 由 Supervisor 以 replace-and-drain／forced termination 回收 process，而不在原地清空 SDK static state。
    /// 設為 null 或門檻建構失敗會在 Worker 發布前使設定 fail closed，且不建立背景 timer、cache 或額外 process。
    /// </summary>
    public OfficialWorkerRecyclePolicyOptions RecyclePolicyOptions { get; init; } = new();
}
