using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 只在呼叫期間操作 caller 獨占 Entity 的無狀態工具，不保存 Entity、logger、憑證或任何租戶資料。
    /// 不建立 I/O、cache、背景工作或 disposable，因此沒有可跨請求殘留的資源或資料。
    /// </summary>
    /// <remarks>
    /// Entity 不是執行緒安全集合；同一 Entity 的並行讀寫須由其 owner 協調。
    /// 保留舊 InvalidOperationException 包裝契約及真實 InnerException，固定外層訊息避免回顯輸入。
    /// 這與 SDK 轉接器的原樣上拋不同，是既有公開 API 的刻意相容，不在 Core 執行最終告警；
    /// host 必須把真實例外交共用入口，禁止輸出原 Message、ToString 或 Entity 內容。
    /// </remarks>
    public class EntityAttributeUtilityService : IEntityAttributeUtilityService
    {
        /// <summary>建立不需要主機診斷依賴的純工具；實例不擁有外部資源，可供不同操作共用。</summary>
        public EntityAttributeUtilityService()
        {
        }

        /// <summary>保留舊建構簽章與 null 拒絕；不保存或處置未使用的記錄物件，避免延長 request 生命。</summary>
        /// <param name="logger">只為來源相容保留的非 null 物件，不能作為租戶或授權依據。</param>
        public EntityAttributeUtilityService(object logger)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 讀取原值或一層 AliasedValue 的文字；缺欄位、null 值及空欄位名稱依舊回空字串。
        /// 沿用值物件原有 ToString 文化語義，不在資料抽取時改成其他格式或設定全域文化。
        /// </summary>
        /// <param name="targetEntity">本次操作已授權且由 caller 獨占的 Entity，不保存其參照。</param>
        /// <param name="attributeName">既有 logical name；null 或空字串視為未指定。</param>
        /// <returns>原值文字或合法缺值的空字串。</returns>
        /// <exception cref="InvalidOperationException">Entity 無效或值轉字串失敗，InnerException 保留真實故障。</exception>
        public string GetAttributeValue(Entity targetEntity, string? attributeName)
        {
            try
            {
#if NETFRAMEWORK
                if (targetEntity == null) throw new ArgumentNullException(nameof(targetEntity));
#else
                ArgumentNullException.ThrowIfNull(targetEntity);
#endif
                if (string.IsNullOrEmpty(attributeName) || !targetEntity.Contains(attributeName)) return string.Empty;
                var value = targetEntity[attributeName];
                return value is AliasedValue alias ? alias.Value?.ToString() ?? string.Empty : value?.ToString() ?? string.Empty;
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The entity attribute could not be read.", failure);
            }
        }

        /// <summary>
        /// 就地移除指定屬性，缺欄位是合法 no-op；不更換 Entity、不清除其他欄位，也不呼叫 SDK。
        /// 移除代表不提交該屬性，與明確設 null 的清值語義不同。
        /// </summary>
        /// <param name="entity">caller 獨占的 Entity；ref 只保留舊簽章，實例不會被替換。</param>
        /// <param name="propertyName">非空、非全空白欄位名稱。</param>
        /// <exception cref="InvalidOperationException">輸入無效或欄位操作失敗，保留原始 InnerException。</exception>
        public void RemoveAttribute(ref Entity entity, string propertyName)
        {
            try
            {
                RequireMutationInput(entity, propertyName);
                entity.Attributes.Remove(propertyName);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The entity attribute could not be removed.", failure);
            }
        }

        /// <summary>
        /// 明確保存值為 null 的欄位，即使原本沒有該欄也會新增，供 SDK 更新表達清除值。
        /// 僅修改指定欄位，caller 持有完整候選並負責後續授權、提交與發布。
        /// </summary>
        /// <param name="entity">本次操作獨占 Entity，不保留或替換其參照。</param>
        /// <param name="propertyName">非空、非全空白欄位名稱。</param>
        /// <exception cref="InvalidOperationException">輸入無效或欄位操作失敗，保留原始 InnerException。</exception>
        public void SetEntityAttributeToNull(ref Entity entity, string propertyName)
        {
            try
            {
                RequireMutationInput(entity, propertyName);
                entity.Attributes[propertyName] = null;
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The entity attribute could not be cleared.", failure);
            }
        }

        /// <summary>保留舊 mutation 引數例外及參數名，固定訊息不包含 entity／欄位值，不構成授權。</summary>
        private static void RequireMutationInput(Entity entity, string propertyName)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentNullException(nameof(propertyName));
        }
    }
}
