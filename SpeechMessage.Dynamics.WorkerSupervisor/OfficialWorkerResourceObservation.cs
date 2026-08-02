namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 一次 official Worker process 資源讀取的 allocation-free 純量快照。
/// 此值不保存 <see cref="System.Diagnostics.Process"/>、Handle、Timer、Task、Profile 或 caller reference；
/// supervisor 可把讀取失敗、OS 回傳負值或不可信的大值原樣交給 Policy，由 Policy fail closed。
/// </summary>
public readonly record struct OfficialWorkerResourceObservation
{
    /// <summary>
    /// 單次觀測可接受的一 PiB 絕對上限。這不是允許 Worker 使用的部署門檻；它只排除
    /// OS/API 錯誤、符號轉換或算術 overflow 形狀，實際部署門檻永遠更低且由 options 限制。
    /// </summary>
    public const long MaximumSupportedObservedBytes = 1L << 50;

    /// <summary>
    /// 建立原始資源觀測。建構式刻意不拋例外，讓 hot-path caller 能把 unreadable／invalid
    /// 狀態以純量傳入；唯一信任決策由 <see cref="OfficialWorkerRecyclePolicy"/> 統一執行。
    /// </summary>
    /// <param name="isReadable">兩個 process counter 是否由同一次可信讀取完整取得。</param>
    /// <param name="privateBytes">OS 回報的 Private Bytes；負值或過大值由 Policy 拒絕。</param>
    /// <param name="workingSetBytes">OS 回報的 Working Set；負值或過大值由 Policy 拒絕。</param>
    public OfficialWorkerResourceObservation(
        bool isReadable,
        long privateBytes,
        long workingSetBytes)
    {
        IsReadable = isReadable;
        PrivateBytes = privateBytes;
        WorkingSetBytes = workingSetBytes;
    }

    /// <summary>取得兩個 counter 是否都已成功且完整讀取。</summary>
    public bool IsReadable { get; }

    /// <summary>取得本次原始 Private Bytes 純量。</summary>
    public long PrivateBytes { get; }

    /// <summary>取得本次原始 Working Set 純量。</summary>
    public long WorkingSetBytes { get; }

    /// <summary>取得不含例外或 OS 診斷文字的固定 unreadable 值。</summary>
    public static OfficialWorkerResourceObservation Unreadable => default;

    /// <summary>
    /// 僅供 Policy 判斷本次觀測是否完整且位於可信數值範圍；不配置集合或例外，
    /// 也不把無效值當成零而錯誤通過資源門檻。
    /// </summary>
    internal bool IsValid =>
        IsReadable &&
        PrivateBytes is >= 0 and <= MaximumSupportedObservedBytes &&
        WorkingSetBytes is >= 0 and <= MaximumSupportedObservedBytes;
}
