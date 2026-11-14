/*
 * 檔案整體說明
 *
 * 目的與定位：把一次 Plugin Execute 所需的純輸入從 Dataverse context 擷取成不可變
 * 操作值，隔離規則與可變的 service provider。此檔不保存 HttpContext、Session、
 * token、credential 或任何跨執行資料。
 *
 * 主要功能與流程：PluginShellBase 在深度檢查後建立 PluginInput；規則只消費
 * 訊息、主要實體、深度、階段、模式、Target、前後映像與組織／使用者識別。
 * Entity 在擷取與讀出時皆遞迴複製；Delete 的 EntityReference 正規化為只有身分
 * 與替代鍵的 Entity，避免遺失刪除目標或改動 SDK context 內的共用集合。
 *
 * 使用邊界與維護：此型別只活在單次 Execute；呼叫端不得把它放入 static、cache、
 * queue 或背景工作。複製委派 RecordingOrganizationService 的有界 SDK 複製器，
 * 未知可變型別或過深資料圖明確拒絕，不以保留原參照作為 fallback。
 * 本型別沒有網路、取消或資源 owner；測試驗證輸入不會跨案例保留。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>一次 Plugin 執行的不可變輸入快照。</summary>
internal sealed class PluginInput
{
    /// <summary>只由本次輸入持有的快照；getter 再複製，規則不能改動後續讀取結果。</summary>
    private readonly Entity? target;
    /// <summary>註冊名稱為 PreImage 的獨立快照，不保存 context 的集合參照。</summary>
    private readonly Entity? preImage;
    /// <summary>註冊名稱為 PostImage 的獨立快照，生命週期限於本次規則。</summary>
    private readonly Entity? postImage;

    /// <summary>建立一次執行的輸入快照。</summary>
    public PluginInput(IPluginExecutionContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        MessageName = context.MessageName ?? string.Empty;
        PrimaryEntityName = context.PrimaryEntityName ?? string.Empty;
        PrimaryEntityId = context.PrimaryEntityId;
        CorrelationId = context.CorrelationId;
        OrganizationId = context.OrganizationId;
        OrganizationName = context.OrganizationName ?? string.Empty;
        UserId = context.UserId;
        Depth = context.Depth;
        Stage = context.Stage;
        Mode = context.Mode;
        target = CopyTarget(context.InputParameters, "Target");
        preImage = CopyTarget(context.PreEntityImages, "PreImage");
        postImage = CopyTarget(context.PostEntityImages, "PostImage");
    }

    /// <summary>Dataverse 訊息名稱。</summary>
    public string MessageName { get; }
    /// <summary>主要實體邏輯名稱。</summary>
    public string PrimaryEntityName { get; }
    /// <summary>主要實體 ID。</summary>
    public Guid PrimaryEntityId { get; }
    /// <summary>本次執行的事件 ID。</summary>
    public Guid CorrelationId { get; }
    /// <summary>來源組織 ID。</summary>
    public Guid OrganizationId { get; }
    /// <summary>平台提供的組織唯一名稱；僅供路由，不能取代授權或組織 ID。</summary>
    public string OrganizationName { get; }
    /// <summary>執行使用者 ID。</summary>
    public Guid UserId { get; }
    /// <summary>本次平台遞迴深度，規則不得自行改寫以繞過外殼政策。</summary>
    public int Depth { get; }
    /// <summary>註冊執行階段，保留 SDK 數值以供規則檢查必要前置。</summary>
    public int Stage { get; }
    /// <summary>平台同步／非同步模式，與 Shadow／Live 設定模式不同。</summary>
    public int Mode { get; }
    /// <summary>每次讀取均為獨立 Entity；不存在時為 null，Delete 保留目標身分。</summary>
    public Entity? Target => Copy(target);
    /// <summary>每次讀取均複製 PreImage；缺少必要映像由所屬規則明確拒絕。</summary>
    public Entity? PreImage => Copy(preImage);
    /// <summary>每次讀取均複製 PostImage；不猜測或混用其他名稱的映像。</summary>
    public Entity? PostImage => Copy(postImage);

    /// <summary>從參數集合複製指定 Entity，不把集合本體暴露給規則。</summary>
    private static Entity? CopyTarget(ParameterCollection parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value)) return null;
        if (value is Entity entity) return Copy(entity);
        if (value is EntityReference reference)
        {
            var identity = new Entity(reference.LogicalName, reference.Id);
            foreach (var key in reference.KeyAttributes) identity.KeyAttributes.Add(key.Key, key.Value);
            return Copy(identity);
        }
        throw new NotSupportedException("PluginInput.UnsupportedTargetType");
    }

    /// <summary>從影像集合複製第一個指定 Entity。</summary>
    private static Entity? CopyTarget(EntityImageCollection images, string name)
    {
        return images.TryGetValue(name, out var entity) ? Copy(entity) : null;
    }

    /// <summary>重用影子服務的有界深層複製，避免兩套 SDK 值處理政策漂移。</summary>
    private static Entity? Copy(Entity? source) =>
        source is null ? null : RecordingOrganizationService.CloneEntity(source);
}
