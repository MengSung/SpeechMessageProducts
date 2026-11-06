using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>完整只讀metadata投影；只有定義與正式ID，不含業務列、SDK物件、設定本文或連線，最長隨單次命令存活。</summary>
internal sealed record SchemaSnapshot
{
    /// <summary>由服務端取得的正式組織ID，跨組織plan不可互用。</summary>
    public Guid OrganizationId { get; init; }
    /// <summary>服務端uniqueName，須與唯一白名單一致。</summary>
    public string Organization { get; init; } = "";
    /// <summary>服務端版本，只作定義環境證據，不使用本機SDK版本冒充。</summary>
    public string Version { get; init; } = "";
    /// <summary>真實組織基礎LCID，新增label必須使用此值，不捏造其他已啟用語系。</summary>
    public int LanguageCode { get; init; }
    /// <summary>完整有界實體／屬性投影，讀取成功前不得發布。</summary>
    public IReadOnlyList<SchemaEntityState> Entities { get; init; } = Array.Empty<SchemaEntityState>();
    /// <summary>只含本產品唯一solution定義，不匯出其他產品內容。</summary>
    public IReadOnlyList<SchemaSolutionState> Solutions { get; init; } = Array.Empty<SchemaSolutionState>();
    /// <summary>可核對的publisher身份與prefix，沒有帳號或憑證。</summary>
    public IReadOnlyList<SchemaPublisherState> Publishers { get; init; } = Array.Empty<SchemaPublisherState>();
    /// <summary>只含本solution的元件正式ID與類別，不讀元件業務內容。</summary>
    public IReadOnlyList<SchemaComponentState> Components { get; init; } = Array.Empty<SchemaComponentState>();
    /// <summary>訊息篩選器的純身份與目標表定義，不保存SDK Entity。</summary>
    public IReadOnlyList<SchemaFilterState> Filters { get; init; } = Array.Empty<SchemaFilterState>();
    /// <summary>既有步驟只匯出身份與執行設定，不讀secure/unsecure configuration。</summary>
    public IReadOnlyList<SchemaStepState> Steps { get; init; } = Array.Empty<SchemaStepState>();
}

/// <summary>實體純投影；nullable政策表示server未提供，不得默認為符合false。</summary>
/// <param name="Id">正式metadata ID。</param><param name="LogicalName">資料庫身份。</param>
/// <param name="PrimaryId">主鍵欄。</param><param name="PrimaryName">主名稱欄。</param>
/// <param name="Ownership">真實擁有權。</param><param name="Audit">稽核政策，未知為null。</param>
/// <param name="OptimisticConcurrency">樂觀並行政策，未知為null。</param><param name="Managed">受管理標記。</param>
/// <param name="Fields">脫離SDK的欄位。</param><param name="Keys">脫離SDK的替代索引鍵。</param>
internal sealed record SchemaEntityState(Guid Id, string LogicalName, string PrimaryId, string PrimaryName,
    string Ownership, bool? Audit, bool? OptimisticConcurrency, bool? Managed,
    IReadOnlyList<SchemaFieldState> Fields, IReadOnlyList<SchemaKeyState> Keys);

/// <summary>單一屬性的功能約束投影；不含記錄值或label翻譯本文，缺值不當成預設false。</summary>
/// <param name="Id">正式metadata ID。</param><param name="LogicalName">欄位身份。</param><param name="Type">明確型別。</param>
/// <param name="MaxLength">文字長度。</param><param name="Required">SDK required level。</param><param name="Secured">欄位安全。</param>
/// <param name="Targets">lookup目標。</param><param name="Behavior">日期行為。</param><param name="Format">日期格式。</param>
/// <param name="Options">正式選項數值，與顯示名無關。</param><param name="Managed">受管理標記。</param>
internal sealed record SchemaFieldState(Guid Id, string LogicalName, string Type, int? MaxLength, string Required,
    bool? Secured, IReadOnlyList<string> Targets, string Behavior, string Format, IReadOnlyList<int> Options, bool? Managed);

/// <summary>正式alternate key與建置狀態；Active以外不能verify為成功。</summary>
/// <param name="Id">metadata ID。</param><param name="LogicalName">正式key身份。</param>
/// <param name="Attributes">有序欄位。</param><param name="Status">SDK index狀態。</param>
internal sealed record SchemaKeyState(Guid Id, string LogicalName, IReadOnlyList<string> Attributes, string Status);

/// <summary>只保留solution必要metadata，不含任何資料庫或套件內容。</summary>
/// <param name="Id">正式solution ID。</param><param name="Name">唯一名稱。</param><param name="Managed">是否受管理。</param><param name="PublisherId">發行者正式ID。</param>
internal sealed record SchemaSolutionState(Guid Id, string Name, bool Managed, Guid PublisherId);
/// <summary>發行者純身份；不修改既有prefix或權限。</summary>
/// <param name="Id">正式ID。</param><param name="Name">唯一名稱。</param><param name="Prefix">實際前綴。</param>
internal sealed record SchemaPublisherState(Guid Id, string Name, string Prefix);
/// <summary>一個solutioncomponent正式列；同type/object重複屬衝突，不可靜默去重。</summary>
/// <param name="Id">正式列ID。</param><param name="Type">SDK componenttype。</param><param name="ObjectId">實際metadata ID。</param>
internal sealed record SchemaComponentState(Guid Id, int Type, Guid ObjectId);
/// <summary>訊息篩選器純定義，沒有任何訊息本文。</summary>
/// <param name="Id">正式filter ID。</param><param name="MessageId">SDK message ID。</param><param name="Primary">主要邏輯表。</param><param name="Secondary">次要邏輯表。</param>
internal sealed record SchemaFilterState(Guid Id, Guid MessageId, string Primary, string Secondary);
/// <summary>步驟純定義，刻意不含secure/unsecure config、trace或任何個資。</summary>
/// <param name="Id">正式step ID。</param><param name="MessageId">message。</param><param name="FilterId">filter。</param>
/// <param name="HandlerId">正式handler。</param><param name="Stage">執行階段。</param><param name="Mode">同步或非同步。</param>
/// <param name="Rank">執行次序。</param><param name="State">啟用狀態。</param><param name="FilteringAttributes">欄位清單定義。</param>
internal sealed record SchemaStepState(Guid Id, Guid MessageId, Guid FilterId, Guid HandlerId,
    int Stage, int Mode, int Rank, int State, string FilteringAttributes);
