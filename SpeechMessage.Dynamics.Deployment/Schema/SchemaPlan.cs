using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>僅含結構身份的新增操作；資料庫ID在已存在時固定，新元件ID只能由SDK回應／重新讀取metadata取得。</summary>
/// <param name="Kind">固定白名單動詞。</param><param name="Entity">固定目標表。</param><param name="Name">正式欄位或key名稱。</param>
/// <param name="Id">既有metadata ID，新元件為空直到SDK建立。</param>
internal sealed record SchemaOperation(string Kind, string Entity, string Name, Guid Id);

/// <summary>人工可審核的完整結構計畫；不含SDK、欄位值、帳密或其他組織權限，不能取代Host的寫入授權。</summary>
internal sealed record SchemaPlan
{
    /// <summary>只接受格式1，避免舊格式缺失hash或範圍仍被套用。</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>server正式組織ID，同alias另一個組織不可使用。</summary>
    public Guid OrganizationId { get; init; }
    /// <summary>server uniqueName，必須唯一白名單。</summary>
    public string Organization { get; init; } = "";
    /// <summary>明確UTC審核時刻，由Host時鐘供應，不使用CurrentCulture。</summary>
    public DateTimeOffset CreatedUtc { get; init; }
    /// <summary>完整metadata fingerprint；漂移必須重新審核plan。</summary>
    public string Fingerprint { get; init; } = "";
    /// <summary>已辨識定義的SHA-256，schema變更必須重新plan。</summary>
    public string SchemaHash { get; init; } = "";
    /// <summary>固定新增操作的完整有序候選，驗全部衝突後才發布。</summary>
    public IReadOnlyList<SchemaOperation> Operations { get; init; } = Array.Empty<SchemaOperation>();
    /// <summary>已存在但尚未Active的key，verify不能以零操作當完整成功。</summary>
    public IReadOnlyList<string> PendingKeys { get; init; } = Array.Empty<string>();
    /// <summary>涵蓋本計畫全部有效欄位的雜湊，不依字串長度或檔名推論可信。</summary>
    public string Sha256 { get; init; } = "";
}
