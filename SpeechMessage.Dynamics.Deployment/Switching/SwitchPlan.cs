/*
檔案整體說明
目的與定位：P7切換計畫與單次現況候選的純資料契約，清楚分開可審核plan與本機回復用XAML／設定快照。
主要流程：reader建立有界完整SwitchSnapshot，planner只發布不含秘密及XAML的SwitchPlan；executor以原候選回復。
範圍與隔離：所有資料只屬一次已授權命令，沒有SDK或credential，集合由生產端唯讀包裝，不跨組織快取。
設定只允受管理模板與固定TokenKey佔位，真secure內容永不讀取；原workflow字串只能留在本機switch-log。
資源與驗證：本檔沒有Dispose資源，不授權任何遠端操作；SwitchPlanTests驗順序、舊新互斥與KeepLegacy結果。
*/
using System;
using System.Collections.Generic;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>DLL就緒比對，不包含DLL內容或執行個體。</summary>
/// <param name="Name">受管組件簡名。</param><param name="LocalSha256">本機內容摘要。</param><param name="RemoteSha256">遠端內容摘要；空表示未部署。</param>
internal sealed record SwitchAssembly(string Name, string LocalSha256, string RemoteSha256);

/// <summary>單次新步驟候選，configuration已驗證為受管理的非秘密設定。</summary>
/// <param name="Id">正式步驟ID。</param><param name="Registration">已驗清單項目。</param><param name="Enabled">當下啟用狀態。</param>
/// <param name="Configuration">核定範本的mode/options，不含任意原始設定。</param><param name="SecureId">正式secure lookup，只記ID不讀值。</param>
internal sealed record SwitchStep(Guid Id, StepRegistration Registration, bool Enabled, string Configuration, Guid? SecureId);

/// <summary>新活動及由已驗PE產出的完整組件識別，不執行該活動。</summary>
/// <param name="Registration">核定活動。</param><param name="QualifiedAssembly">含版本與簽章的組件識別。</param>
internal sealed record SwitchActivity(ActivityRegistration Registration, string QualifiedAssembly);

/// <summary>舊步驟metadata，只能以清單正式ID及parent識別對照，不依名稱猜測。</summary>
/// <param name="Id">正式ID。</param><param name="Assembly">組件簡名。</param><param name="Type">完整型別。</param><param name="Enabled">原狀態。</param>
internal sealed record SwitchLegacyStep(Guid Id, string Assembly, string Type, bool Enabled);

/// <summary>流程單次原況；XAML只留命令／本機回復紀錄，不放進plan。</summary>
/// <param name="Id">正式流程ID。</param><param name="State">0草稿、1啟用、2暫停。</param><param name="Xaml">完整原字串，回復逐位元組保留。</param>
internal sealed record SwitchWorkflow(Guid Id, int State, string Xaml);

/// <summary>完整取得才發布的單次候選；呼叫端不得在計畫或執行中修改任何集合。</summary>
/// <param name="Organization">核定組織純值。</param><param name="ServerOrganizationId">WhoAmI回報的正式組織ID。</param>
/// <param name="ManifestSha256">清單檔案摘要。</param><param name="CatalogSha256">組織清單摘要。</param><param name="Fingerprint">所有受管／舊項目與流程原況摘要。</param>
/// <param name="Assemblies">DLL就緒清單。</param><param name="Steps">新步驟。</param><param name="Activities">新活動。</param>
/// <param name="LegacySteps">舊步驟。</param><param name="Workflows">引用舊活動的流程。</param><param name="LegacyActivities">全部舊活動，包含未移植者。</param>
internal sealed record SwitchSnapshot(OrganizationDefinition Organization, Guid ServerOrganizationId, string ManifestSha256,
    string CatalogSha256, string Fingerprint, IReadOnlyList<SwitchAssembly> Assemblies, IReadOnlyList<SwitchStep> Steps,
    IReadOnlyList<SwitchActivity> Activities, IReadOnlyList<SwitchLegacyStep> LegacySteps, IReadOnlyList<SwitchWorkflow> Workflows,
    IReadOnlyList<WorkflowActivityReference> LegacyActivities)
{
    /// <summary>包含沒有步驟／活動的全部舊組件，避免核定書把空殼組件當已移除。</summary>
    public IReadOnlyList<string> RegisteredLegacyAssemblies { get; init; } = Array.Empty<string>();
}

/// <summary>固定操作純值；KeepLegacy與Manual無寫入效力，原因只能是固定安全metadata。</summary>
/// <param name="Kind">七種核定動詞之一。</param><param name="Id">正式步驟或流程ID。</param><param name="Module">安全模組代號。</param>
/// <param name="Enabled">目標啟用旗標或流程原啟用旗標。</param><param name="Configuration">受管非秘密設定。</param>
/// <param name="SecureConfiguration">空白或TokenKey佔位。</param><param name="Replacements">活動精確對照，不含XAML。</param><param name="Reason">保留或人工步驟的固定原因。</param>
internal sealed record SwitchOperation(string Kind, Guid Id, string Module, bool Enabled, string Configuration,
    string SecureConfiguration, IReadOnlyList<WorkflowActivityReplacement> Replacements, string Reason);

/// <summary>可審核計畫不包含真金鑰、SDK或原始XAML；SHA欄位為內部完整性，另以檔案SHA供使用者核定。</summary>
/// <param name="SchemaVersion">唯一格式1。</param><param name="Organization">canonical名稱。</param><param name="ServerOrganizationId">正式server ID。</param>
/// <param name="Kind">test或production。</param><param name="FromState">catalog起點。</param><param name="TargetState">off/shadow/live。</param>
/// <param name="Fingerprint">原況摘要。</param><param name="ManifestSha256">來源清單摘要。</param><param name="CatalogSha256">來源catalog摘要。</param>
/// <param name="Assemblies">兩DLL摘要。</param><param name="Operations">固定順序操作。</param><param name="Dormant">不切項目及提案原因。</param>
/// <param name="LegacyAssemblies">仍登錄舊組件簡名。</param><param name="Sha256">不含自身的完整性摘要。</param>
internal sealed record SwitchPlan(int SchemaVersion, string Organization, Guid ServerOrganizationId, string Kind, string FromState,
    string TargetState, string Fingerprint, string ManifestSha256, string CatalogSha256, IReadOnlyList<SwitchAssembly> Assemblies,
    IReadOnlyList<SwitchOperation> Operations, IReadOnlyList<string> Dormant, IReadOnlyList<string> LegacyAssemblies, string Sha256);
