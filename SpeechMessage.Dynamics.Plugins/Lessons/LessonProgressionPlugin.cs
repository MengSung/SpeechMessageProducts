/*
 * 檔案整體說明
 * 目的與定位：P5-07設定式進階Plugin入口，重用PluginShellBase的例外、深度及
 * Shadow生命週期。註冊實例只保存固定模式，不保存context或教會ID；profile
 * 由Core以本人正式account重新讀取，不接受註冊GUID或名稱猜測。
 * 主要功能與流程：每次Execute建立短生命週期Invocation處理同步Create／Update；
 * Core完成授權與五值投影後，Live發布到current／parent／grandparent，Shadow
 * 只記錄CRM意圖且不改SharedVariables。學籍卡觸碰由本次使用者SDK完成，
 * 無system service、額外權限或猜測的直接學分彙總。
 * 使用邊界與維護：Depth>1僅略過Update。每層配置獨立純值容器，同鍵已有值
 * 保留原五值僅換標記，來源ID衝突則拒絕以防跨事件污染。最多三層並驗證組織；
 * 無static可變資料、cache、timer或背景工作。平台擁有context與SDK，外殼不
 * Dispose；Invocation只活於同步Execute，基底finally排空及Dispose影子owner。
 * 故障原樣傳遞，CRM同步交易負責回滾；離線測試保護正常學籍卡通知、本人
 * 關聯、設定、Shadow及重用排空，不把通知成功當作未知下游彙總的實機驗收。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;

namespace SpeechMessage.Dynamics.Plugins.Lessons;

/// <summary>可重用入口只持有模式，每次建立獨立外殼以免context跨呼叫保留。</summary>
public sealed class LessonProgressionPlugin : IPlugin
{
    /// <summary>由驗證模式產生的固定JSON，不保存secure內容或使用者資料。</summary>
    private readonly string configuration;

    /// <summary>只接受mode；教會及profile必須由正式account取得，不允許step覆寫。</summary>
    public LessonProgressionPlugin(string unsecureConfig, string secureConfig)
    {
        var parsed = new StepConfiguration(unsecureConfig, secureConfig);
        if (parsed.Options.Count != 0 || !string.IsNullOrEmpty(parsed.SecureConfig))
            throw StepConfiguration.InvalidConfigurationException();
        configuration = parsed.Mode == PluginExecutionMode.Shadow ? "{\"mode\":\"Shadow\"}" : "{\"mode\":\"Live\"}";
    }

    /// <summary>借用本次context交給獨立外殼，返回後不保留任何本次執行資料。</summary>
    public void Execute(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null) throw new ArgumentNullException(nameof(serviceProvider));
        var context = serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext
            ?? throw new InvalidPluginExecutionException("Lesson progression context is missing.");
        new Invocation(configuration, context).Execute(serviceProvider);
    }

    /// <summary>唯一持有context的當次owner，不能快取或從Execute逸出。</summary>
    private sealed class Invocation : PluginShellBase
    {
        /// <summary>平台借入的同步context，只活於外層的一次Execute。</summary>
        private readonly IPluginExecutionContext context;

        /// <summary>沿用共用外殼及舊Update深度政策，不取得額外SDK權限。</summary>
        internal Invocation(string configuration, IPluginExecutionContext context)
            : base(configuration, string.Empty, new DepthPolicy(1, DepthException.Update)) => this.context = context;

        /// <summary>只接受同步PostOperation，Shadow使用相同適配器但不發布跨步驟狀態。</summary>
        internal override void ExecuteRule(PluginInput input, PluginServices services)
        {
            if (input.PrimaryEntityName != "new_stor_lessons" || input.MessageName is not ("Create" or "Update")) return;
            if (input.Stage != 40 || input.Mode != 0)
                throw new InvalidPluginExecutionException("Lesson progression requires synchronous PostOperation.");
            LessonProgressionRecords.Run(services.Organization, input.PrimaryEntityId,
                input.MessageName == "Create", projection =>
                {
                    if (!services.IsShadow) Publish(projection);
                });
        }

        /// <summary>
        /// 先驗證最多三層再發布，保留舊同鍵僅換標記行為但不共用ArrayList。
        /// 既有payload只能屬同一來源事件，型別或來源衝突在CRM寫入前拒絕。
        /// </summary>
        private void Publish(LessonProgressionRollCardProjection projection)
        {
            var candidates = new List<PublicationCandidate>(3);
            IPluginExecutionContext? current = context;
            for (var i = 0; i < 3 && current is not null; i++, current = current.ParentContext)
            {
                if (current.OrganizationId != context.OrganizationId)
                    throw new InvalidPluginExecutionException("Lesson progression context organization mismatch.");
                var values = new ArrayList
                {
                    LessonProgressionRollCardProjection.Marker, projection.SourceId, projection.Category,
                    projection.AchievedCredit, projection.EstimatedCredit
                };
                if (current.SharedVariables.TryGetValue(LessonProgressionRollCardProjection.SharedVariableKey, out var existing))
                {
                    if (existing is not ArrayList old || old.Count != 5 ||
                        old[1] is not Guid id || id != projection.SourceId || old[2] is not string text ||
                        text.Length > 2000 || old[3] is not double achieved || double.IsNaN(achieved) ||
                        double.IsInfinity(achieved) || old[4] is not double estimated ||
                        double.IsNaN(estimated) || double.IsInfinity(estimated))
                        throw new InvalidPluginExecutionException("Lesson progression shared event conflict.");
                    values = new ArrayList { LessonProgressionRollCardProjection.Marker, id, text, achieved, estimated };
                }
                candidates.Add(new PublicationCandidate(current, values));
            }
            foreach (var candidate in candidates)
                candidate.Context.SharedVariables[LessonProgressionRollCardProjection.SharedVariableKey] = candidate.Values;
        }

        /// <summary>
        /// 單次 Publish 的待發布候選，明確型別取代沙箱禁用的 ValueTuple。
        /// 僅持有最多三層內的一個借用 context 與該層獨有容器；不從同步呼叫逸出，
        /// 方法返回即釋放候選集合的參照，平台仍擁有 context 與已發布的 SharedVariables。
        /// </summary>
        private sealed class PublicationCandidate
        {
            /// <summary>接收本次已驗證候選，不複製或共用其他層的可變容器。</summary>
            internal PublicationCandidate(IPluginExecutionContext context, ArrayList values)
            {
                Context = context;
                Values = values;
            }

            /// <summary>本次同步 pipeline 借用的目標 context，不由本候選 Dispose。</summary>
            internal IPluginExecutionContext Context { get; }

            /// <summary>只發布給本層的獨立五值容器，不含 SDK 服務或 credential。</summary>
            internal ArrayList Values { get; }
        }
    }
}
