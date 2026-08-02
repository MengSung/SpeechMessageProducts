using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 保存官方 CRM Worker 初始允許的操作識別與不可變修訂版本。
/// 此清單是 Worker 端的 fail-closed allowlist；呼叫端無法透過 IPC 動態加入
/// 任意 SDK Execute、FetchXML、實體名稱或其他未審核操作。
/// </summary>
public static class OfficialWorkerOperations
{
    /// <summary>官方 SDK 身分健康檢查操作。</summary>
    public const string RuntimeHealthWhoAmI = "runtime.health.whoami";

    /// <summary>身分健康檢查目前的不可變操作修訂。</summary>
    public const string RuntimeHealthWhoAmIRevision = "operation-revision-0001";

    /// <summary>
    /// 建立每個 Worker session 專用的唯讀修訂對照表，避免跨 session 共用可變集合。
    /// </summary>
    /// <returns>以 ordinal 規則比較操作識別的唯讀對照表。</returns>
    public static IReadOnlyDictionary<string, string> CreateRevisionMap()
    {
        var revisions = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            [RuntimeHealthWhoAmI] = RuntimeHealthWhoAmIRevision
        };

        return new ReadOnlyDictionary<string, string>(revisions);
    }
}
