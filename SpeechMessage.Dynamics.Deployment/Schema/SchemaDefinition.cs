/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義P1結構清單的純值資料契約，作為人工維護JSON、嚴格解析器與新增SDK引擎之間的唯一格式。
 * 主要功能：SchemaDefinition宣告格式版本、solution／publisher與new前綴；SchemaEntity描述既有或新實體；
 * SchemaField描述型別、長度、必填、安全、日期、lookup、phase與choice；SchemaKey保留正式索引身份與有序欄位。
 * 輸入輸出與依賴：SchemaFiles從JSON建立候選並完整驗證，SchemaPlanner比較server投影，SchemaDeploymentEngine依清單新增。
 * 此處建構record不會驗證授權或進行I/O，呼叫端不能以成功反序列化當成schema合法、組織授權或可安全寫入。
 * 隔離與生命週期：物件僅屬單次命令，不含SDK、連線、token、request或背景工作；集合在命令範圍獨佔，不作全域可變快取。
 * 重要限制：phase=1才參與P1寫入；未指定樂觀並行政策以null表示，不能誤用false要求改server唯讀metadata。
 * 維護驗證：新增欄位須同步嚴格JSON驗證、canonical hash、planner比對與SDK建立；SchemaDefinitionTests獨立對照附錄A，避免自我驗證。
 */
using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>單命令擁有的結構定義；只含受管metadata，沒有業務記錄、憑證或SDK參照。Load完整驗證後才交規劃器。</summary>
internal sealed record SchemaDefinition
{
    /// <summary>固定清單格式；未知版本在任何SDK呼叫前拒絕。</summary>
    public int SchemaVersion { get; init; }
    /// <summary>唯一未受管理solution名稱，不接受caller改成其他產品。</summary>
    public string Solution { get; init; } = "";
    /// <summary>首次建立solution且沒有相容發行者時使用的穩定publisher身份。</summary>
    public string Publisher { get; init; } = "";
    /// <summary>本產品固定new前綴，不修改既有publisher。</summary>
    public string Prefix { get; init; } = "";
    /// <summary>完整操作候選，最多32表；只在本命令存活，不跨命令快取或原地發布。</summary>
    public IReadOnlyList<SchemaEntity> Entities { get; init; } = Array.Empty<SchemaEntity>();
}

/// <summary>一個受管實體的純定義；IsNew才驗證owner/audit/concurrency，不強制改既有account/contact政策。</summary>
internal sealed record SchemaEntity
{
    /// <summary>資料庫正式邏輯名稱，不以顯示文字為身份。</summary>
    public string LogicalName { get; init; } = "";
    /// <summary>組織基礎語言使用的固定顯示名，來源為附錄A。</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>新實體複數顯示名，不讀取或包含教會資料。</summary>
    public string PluralName { get; init; } = "";
    /// <summary>只有四張新表可建立；既有account/contact不存在時明確拒絕。</summary>
    public bool IsNew { get; init; }
    /// <summary>新表唯一主名稱欄，由CreateEntity一併建立。</summary>
    public string PrimaryName { get; init; } = "";
    /// <summary>新表要求的稽核狀態；既有不相容時衝突，不自動UpdateEntity。</summary>
    public bool Audit { get; init; }
    /// <summary>有值才要求明確樂觀並行政策；SDK此回應欄唯讀，新自訂表使用server預設true並於建立後核對，不能假造setter。</summary>
    public bool? OptimisticConcurrency { get; init; }
    /// <summary>最多128欄的單次候選；phase=1才參與本階段寫入。</summary>
    public IReadOnlyList<SchemaField> Fields { get; init; } = Array.Empty<SchemaField>();
    /// <summary>最多八個正式alternate key，不以應用鎖取代資料庫唯一性。</summary>
    public IReadOnlyList<SchemaKey> Keys { get; init; } = Array.Empty<SchemaKey>();
}

/// <summary>欄位的明確型別與約束；只對已核定新增內容使用，不能把既有metadata轉型或降低安全限制。</summary>
internal sealed record SchemaField
{
    /// <summary>正式欄位邏輯名稱，必須new前綴且在同實體唯一。</summary>
    public string LogicalName { get; init; } = "";
    /// <summary>基礎語言固定label，不保存任何使用者提供文字。</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>固定string/memo/integer/choice/lookup/datetime型別，不接受任意SDK型別。</summary>
    public string Type { get; init; } = "";
    /// <summary>文字型別的明確最大長度；非文字必須為零。</summary>
    public int MaxLength { get; init; }
    /// <summary>建立欄位時的ApplicationRequired設定；不得宣稱可替代伺服器業務驗證。</summary>
    public bool Required { get; init; }
    /// <summary>敏感加密欄的欄位安全；本工具不新增角色或FieldSecurityProfile權限。</summary>
    public bool Secured { get; init; }
    /// <summary>lookup的唯一正式目標表，不能從列值或caller路由決定。</summary>
    public string Target { get; init; } = "";
    /// <summary>日期明確行為；本P1時間點只能UserLocal存UTC，不轉換既有資料。</summary>
    public string Behavior { get; init; } = "";
    /// <summary>日期顯示格式與行為共同驗證，不只比對AttributeType。</summary>
    public string Format { get; init; } = "";
    /// <summary>清單保留未來欄位的階段資訊，P1不得提前建立。</summary>
    public int Phase { get; init; } = 1;
    /// <summary>choice的明確數值／label候選，無重複數值，不使用位置當正式代碼。</summary>
    public IReadOnlyList<SchemaOption> Options { get; init; } = Array.Empty<SchemaOption>();
}

/// <summary>具有正式數值身份的選項，顯示文字相同也不合併不同數值。</summary>
/// <param name="Value">附錄核定選項數值。</param>
/// <param name="Label">附錄核定基礎顯示文字。</param>
internal sealed record SchemaOption(int Value, string Label);

/// <summary>一個正式索引鍵的名稱與有序欄位；建立後必須確認Active，不把非同步建置受理當驗收成功。</summary>
internal sealed record SchemaKey
{
    /// <summary>固定key邏輯名稱，不接受任意名稱覆蓋另一索引。</summary>
    public string LogicalName { get; init; } = "";
    /// <summary>唯一鍵欄位順序，皆須在同表P1欄位存在；不得以臨時request token代替正式欄位。</summary>
    public IReadOnlyList<string> Attributes { get; init; } = Array.Empty<string>();
}
