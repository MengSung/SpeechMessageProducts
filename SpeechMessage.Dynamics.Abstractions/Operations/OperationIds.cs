// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs
// 目的：集中管理 Package 0 / Package 1 的 capabilityOperationId 字串常數。
//
// 保母教學：
// - capabilityOperationId 必須符合 ^[a-z0-9]+(\.[a-z0-9]+)+$
// - 不要在業務程式隨手拼字串，避免拼錯或出現連字號。
// - 這些 ID 必須與 phase0-organization-call-matrix.json 的 normalizedCallSites 對齊。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// Package 0（runtime）與 Package 1（fee reads）的正式操作 ID。
/// </summary>
public static class OperationIds
{
    // -------- Package 0: runtime foundation --------

    /// <summary>對應 ORG-CALL-00003：WhoAmI 健康檢查。</summary>
    public const string RuntimeHealthWhoAmI = "runtime.health.whoami";

    /// <summary>對應 ORG-CALL-00004：profile 連線/資源健康檢查。</summary>
    public const string RuntimePoolValidateConnection = "runtime.pool.validate.connection";

    /// <summary>對應 ORG-CALL-00040：option-set metadata 讀取。</summary>
    public const string MetadataOptionSetByAttribute = "metadata.optionset.retrieve.by.attribute";

    // -------- Package 1: fee reads --------

    /// <summary>對應 ORG-CALL-00005：依 contact 讀取 dedication fee。</summary>
    public const string FeeDedicationRetrieveByContact = "fee.dedication.retrieve.by.contact";

    /// <summary>對應 ORG-CALL-00006：依 contact + 日期區間讀取 dedication fee。</summary>
    public const string FeeDedicationRetrieveByContactDateRange = "fee.dedication.retrieve.by.contact.date.range";

    /// <summary>對應 ORG-CALL-00064：依 dedication booking + paid period 讀取 fee。</summary>
    public const string FeesRetrieveByDedicationPeriod = "fees.retrieve.by.dedication.period";

    /// <summary>對應 ORG-CALL-00066：fee editor 依 disciple lesson 載入。</summary>
    public const string FeesEditorLoadByDiscipleLesson = "fees.editor.load.by.disciplelesson";

    /// <summary>對應 ORG-CALL-00061：依 contact 讀取 stor lessons（fee 畫面支援）。</summary>
    public const string LessonsStorRetrieveByContact = "lessons.stor.retrieve.by.contact";

    /// <summary>對應 ORG-CALL-00062：依 disciple lesson 讀取 stor lessons。</summary>
    public const string LessonsStorRetrieveByDiscipleLesson = "lessons.stor.retrieve.by.disciplelesson";
}