/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義可保存與審閱的結構新增計畫；它承接純planner結果，讓apply重新核對同一組織與同一schema。
 * 主要功能：SchemaPlan保存版本、server組織及publisher正式ID、UTC時刻、schema hash、metadata fingerprint、完整有序操作、pending key及plan hash。
 * SchemaOperation亦含AddOptionValue的正式值／新label，null省略保持舊證據hash。
 * SchemaOperation只接受產品規劃的新增／納入元件動詞，包含正式表名、欄位或key名；新元件ID必須等SDK建立後取得，不可捏造。
 * 輸入輸出與依賴：SchemaPlanner建立並計算hash，SchemaFiles嚴格讀寫，SchemaDeploymentEngine在每次apply前重新讀server並驗整份計畫。
 * 安全與生命週期：計畫只有結構身份，不含資料列、token、密碼或SDK；每份由命令獨佔，不保存在跨使用者static或request狀態。
 * 重要限制：SHA-256用來偵測完整性與漂移，不是數位簽章，也不代表授權。pending key不是成功，任何操作也不能繞過VM隔離／備份前置。
 * 維護驗證：新增計畫欄位必須納入hash、strict JSON與ValidatePlan；保留竄改、組織ID／版本漂移及pending/failed key測試。
 */
using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>僅含結構身份的新增操作；資料庫ID在已存在時固定，新元件ID只能由SDK回應／重新讀取metadata取得。</summary>
/// <param name="Kind">固定白名單動詞。</param><param name="Entity">固定目標表。</param><param name="Name">正式欄位或key名稱。</param>
/// <param name="Id">既有metadata ID，新元件為空直到SDK建立。</param>
internal sealed record SchemaOperation(string Kind, string Entity, string Name, Guid Id)
{
    /// <summary>只在AddOptionValue存在的正式選項數值；null於舊操作省略以保留歷史hash。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? OptionValue { get; init; }
    /// <summary>新增選項的核定顯示名；不讀寫既有標籤，null不改舊操作canonical JSON。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OptionLabel { get; init; }
}

/// <summary>F0 精確盤點後保留的既有不可變 owner；不是寫入操作，也不准許建立同 owner 的新租戶表。</summary>
/// <param name="Entity">固定已盤點的實體邏輯名稱。</param>
/// <param name="MetadataId">同一測試組織已觀察到的正式 metadata ID。</param>
/// <param name="Ownership">必須與盤點一致的既有擁有權，不由呼叫端選擇。</param>
internal sealed record SchemaRetainedOwnership(string Entity, Guid MetadataId, string Ownership);

/// <summary>人工可審核的完整結構計畫；不含SDK、欄位值、帳密或其他組織權限，不能取代Host的寫入授權。</summary>
internal sealed record SchemaPlan
{
    /// <summary>只接受格式1，避免舊格式缺失hash或範圍仍被套用。</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>server正式組織ID，同alias另一個組織不可使用。</summary>
    public Guid OrganizationId { get; init; }
    /// <summary>已核對發行者的正式ID；新建時為空，由本次SDK回應取得，不能重新猜另一發行者。</summary>
    public Guid PublisherId { get; init; }
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
    /// <summary>
    /// 僅在精確 F0 組織／表身分全部吻合時列出既有 owner；同時參與計畫完整性雜湊與摘要。
    /// 舊組織沒有此沿用時省略 null，保留歷史計畫表示；其餘 audit／security／key 約束不受影響。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SchemaRetainedOwnership>? RetainedOwnership { get; init; }
    /// <summary>FS 分層檢視的完整計畫與現況指紋；舊清單省略 null，保留既有 canonical hash。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public UiViewPlan? Ui { get; init; }
    /// <summary>涵蓋本計畫全部有效欄位的雜湊，不依字串長度或檔名推論可信。</summary>
    public string Sha256 { get; init; } = "";
}
