/*
 * 檔案整體說明
 *
 * 目的與定位：將 P5 自訂活動所需的平台執行識別擷取成不可變純值，供
 * ActivityShellBase 的 internal 規則入口消費，避免規則保存可變 IWorkflowContext。
 * 這些平台識別用來標示來源 scope，不取代規則自己的組織／業務授權驗證。
 *
 * 主要功能與流程：外殼在本次執行取得 IWorkflowContext 後建立本物件，
 * 複製訊息、主要記錄、事件、組織、執行與起始使用者，以及深度與平台執行模式。
 * 只保存 GUID、整數與不可變字串；不保存 context、參數集合、映像或 SDK service。
 * 本型別目前只定義共用平台識別；正式活動的其他 InArgument 仍須由該模組
 * 定義明確的單次評估及純值傳遞契約，不能把 WF context 塞入本快照。
 *
 * 使用邊界與維護：本物件僅保留至當次同步規則結束，不放入 static、快取、
 * Session、佇列或背景工作；缺少 context 立即拒絕，平台空字串欄位正規化為空字串。
 * 本檔不擁有連線、計時器、取消註冊或可處置資源，不另行授權、查詢或釋放 SDK。
 * 離線測試以同一活動 A／B／A 執行的輸入識別驗證快照不沿用前次 scope。
 */
using System;
using Microsoft.Xrm.Sdk.Workflow;

namespace SpeechMessage.Dynamics.Workflows.Infrastructure;

/// <summary>
/// 單次活動規則的不可變平台識別快照；沒有指向平台可變集合或服務的參照。
/// 使用者與組織識別必須由平台 extension 取得，不能以活動參數冒充權限。
/// </summary>
internal sealed class ActivityInput
{
    /// <summary>立即複製平台純值，建構完成後不再讀取或持有借用的 workflow context。</summary>
    /// <param name="context">本次執行由平台提供的活動 context；不得跨執行保存。</param>
    public ActivityInput(IWorkflowContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        MessageName = context.MessageName ?? string.Empty;
        PrimaryEntityName = context.PrimaryEntityName ?? string.Empty;
        PrimaryEntityId = context.PrimaryEntityId;
        CorrelationId = context.CorrelationId;
        OrganizationId = context.OrganizationId;
        OrganizationName = context.OrganizationName ?? string.Empty;
        UserId = context.UserId;
        InitiatingUserId = context.InitiatingUserId;
        Depth = context.Depth;
        Mode = context.Mode;
    }

    /// <summary>平台訊息名稱；防遞迴仍由外殼核定政策判斷，規則不能改寫本值。</summary>
    public string MessageName { get; }
    /// <summary>主要記錄的實體邏輯名稱，僅表示平台來源，不代表已通過業務授權。</summary>
    public string PrimaryEntityName { get; }
    /// <summary>平台主要記錄識別；正式資料列必須使用其真實資料庫識別而非顯示文字。</summary>
    public Guid PrimaryEntityId { get; }
    /// <summary>本次活動的關聯事件識別，用於安全診斷及業務拒絕文字。</summary>
    public Guid CorrelationId { get; }
    /// <summary>本次平台組織識別；規則須另檢查業務資料是否屬於該來源邊界。</summary>
    public Guid OrganizationId { get; }
    /// <summary>平台組織唯一名稱，只供受控路由，不能單獨作為組織授權依據。</summary>
    public string OrganizationName { get; }
    /// <summary>本次借用 SDK service 所用的執行使用者，不接受活動輸入覆寫。</summary>
    public Guid UserId { get; }
    /// <summary>啟動本次平台操作的使用者；不得以此值擴大執行使用者的資料權限。</summary>
    public Guid InitiatingUserId { get; }
    /// <summary>平台遞迴深度，只在本次快照使用，不累計成活動實例狀態。</summary>
    public int Depth { get; }
    /// <summary>平台同步／非同步模式，與 ExecutionMode 的 Live／Shadow 選擇無關。</summary>
    public int Mode { get; }
}
