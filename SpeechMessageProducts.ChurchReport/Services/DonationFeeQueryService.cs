// ============================================================================
// 檔案：ChurchReport/Services/DonationFeeQueryService.cs
// 目的：奉獻收費單查詢與畫面模型轉換。
//
// 保母教學：
// - 預設仍走舊 ToolUtility 路徑，確保現有環境不中斷。
// - 若注入 IPackage01FeeReadClient 且 DynamicsAccess:Package01FeeReadsEnabled=true，
//   則改走 no-SDK Package 1 受控操作。
// - 產品仍只得到 DedicationFee 畫面模型，不直接碰 CRM Entity（新路徑）。
// - 舊路徑暫時仍依賴 ToolUtility/Entity，這是遷移期兼容，不是最終態。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Models;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻收費單查詢與轉換服務。
    /// </summary>
    public sealed class DonationFeeQueryService
    {
        private readonly ToolUtilityClass _utility;
        private readonly IPackage01FeeReadClient? _package01FeeReadClient;
        private readonly ProductDynamicsOptions? _dynamicsAccess;
        private readonly bool _package01Enabled;
        private readonly LegacyToolUtilityDrainController? _legacyDrainController;

        /// <summary>
        /// 建立保留既有 legacy 行為的奉獻費用查詢服務。
        /// 此相容建構式不會自行建立 controller；正式 host 必須從 DI 注入唯一的 lifecycle owner，
        /// 避免手動建立第二份計數器而錯誤宣稱跨 host 或跨 Organization 的容量證明。
        /// </summary>
        /// <param name="utility">既有 ToolUtility 實例；遷移期仍由其執行 legacy CRM 存取。</param>
        public DonationFeeQueryService(ToolUtilityClass utility)
            : this(utility, package01FeeReadClient: null, dynamicsAccess: null, legacyDrainController: null)
        {
        }

        /// <summary>
        /// 建立費用查詢服務，依 deployment-owned Package01 flag 選擇既有 legacy 或已註冊 typed client。
        /// legacy controller 只在 caller 已由主 DI 取得唯一 host-owned 實例時才包住 fee ingress；它不會
        /// 改變 flag、不會建立 CRM/Gateway client，也不把同步 ToolUtility work 宣稱為可取消或全域排空。
        /// </summary>
        /// <param name="utility">遷移期的既有 ToolUtility；呼叫端仍負責其原有生命週期。</param>
        /// <param name="package01FeeReadClient">已由 process host 擁有 transport 的 typed client；可為 null。</param>
        /// <param name="dynamicsAccess">只讀 deployment-owned Dynamics 設定，不接受 request routing。</param>
        /// <param name="package01FeeReadsEnabled">僅當 client 存在時才允許 typed read path。</param>
        /// <param name="legacyDrainController">可選的 host-owned legacy ingress controller；不得手動 new。</param>
        public DonationFeeQueryService(
            ToolUtilityClass utility,
            IPackage01FeeReadClient? package01FeeReadClient,
            IOptions<ProductDynamicsOptions>? dynamicsAccess,
            bool package01FeeReadsEnabled = false,
            LegacyToolUtilityDrainController? legacyDrainController = null)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _package01FeeReadClient = package01FeeReadClient;
            _dynamicsAccess = dynamicsAccess?.Value;
            _package01Enabled = package01FeeReadsEnabled && package01FeeReadClient is not null;
            _legacyDrainController = legacyDrainController;
        }

        /// <summary>
        /// 取得此 service 是否已由 deployment-owned Package01 設定與 typed client 同時啟用。
        /// </summary>
        /// <remarks>
        /// 此值在建構時固定，沒有 setter、Session 寫入或 request-time 重選路由；它只用來讓
        /// controller 明確區分既有 legacy rollback 分支與 typed 分支，不能當作 CE enablement
        /// 或流量切換證據。
        /// </remarks>
        public bool IsPackage01FeeReadEnabled => _package01Enabled;

        /// <summary>
        /// 將 CRM 查出的奉獻收費單填入表單模型，並同步更新總金額。
        /// </summary>
        public async Task FillFeeListAsync(
            DonationPaymentFormModel model,
            Entity contact,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(contact);

            var fullName = model.FullName;
            var contactId = contact.Id;

            if (_package01Enabled)
            {
                await FillFeeListViaPackage01Async(model, contactId, fullName, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            // 同步 legacy SDK call 不能觀察 cancellation；因此 lease 的作用僅是精確計量本受控 ingress。
            // shutdown/drain timeout 不可據此宣稱遠端 I/O 已停止，外部 non-overlap runbook 仍必須驗證
            // 全部 legacy coverage 與 durable topology。lease 以 await using 保證 mapping 或 CRM fault
            // 時也會在 finally 等價路徑釋放，不保留 request/contact/Entity 至 shared controller。
            await using var lease = await AcquireLegacyFeeLeaseAsync(cancellationToken).ConfigureAwait(false);

            // ---- 舊路徑（遷移期兼容）----
            EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
                fullName,
                contactId.ToString(),
                model.QueryStartDate,
                model.QueryEndDate);

            System.Diagnostics.Trace.WriteLine(
                $"[DEDQUERY-LEGACY] ContactId={contactId:D} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={feeEntities.Entities.Count}");

            model.TotalAmount = 0;
            model.DedicationFeeList = feeEntities.Entities
                .Select(MapFee)
                .ToList();

            foreach (var fee in model.DedicationFeeList)
            {
                model.TotalAmount += fee.Amount;
            }
        }

        /// <summary>
        /// 以 Package01 的受控「依聯絡人、不分日期」能力讀取奉獻稽核列。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 信任邊界：<paramref name="contactId"/> 只能在 controller 已完成伺服器端登入與角色
        /// 授權後傳入，且在此方法中僅作為 typed operation 的 locator。profile 與 workload subject
        /// 都由 deployment/service 固定，瀏覽器不能選取 endpoint、credential、connector、organization
        /// 或 contactName。
        /// </para>
        /// <para>
        /// 隔離與生命週期：每次呼叫只配置 request-local DTO 投影與陣列，不讀取 target CRM
        /// Entity、不接收 DonationPaymentFormModel、不寫入 Session/cache/static state，也沒有要由
        /// 此方法 Dispose 的外部資源。取消與 typed fault 原樣向上傳遞；不 fallback、不中斷重試。
        /// </para>
        /// </remarks>
        /// <param name="contactId">已授權 target 的 typed locator；空 GUID 一律拒絕。</param>
        /// <param name="cancellationToken">目前 HTTP request 的取消 token，原樣傳給 ProductClient。</param>
        /// <returns>新的 request-local 費用列與 checked 總額。</returns>
        /// <exception cref="InvalidOperationException">Package01 未啟用或缺少 deployment profile。</exception>
        /// <exception cref="ArgumentException">目標 contact ID 為空。</exception>
        /// <exception cref="OverflowException">總額超出付款顯示模型可支援的 Int32 範圍。</exception>
        public async Task<DonationFeeAuditReadResult> RetrieveFeeAuditByContactAsync(
            Guid contactId,
            CancellationToken cancellationToken = default)
        {
            if (contactId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty contact ID is required for fee audit reads.", nameof(contactId));
            }

            if (!_package01Enabled)
            {
                throw new InvalidOperationException("Package01 fee audit reads are not enabled.");
            }

            var profileAlias = _dynamicsAccess?.ProfileAlias;
            if (string.IsNullOrWhiteSpace(profileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess:ProfileAlias is required when Package01 fee reads are enabled.");
            }

            // Service identity 是固定 workload subject，絕不由 HTTP caller、Session 或 target contact 導出。
            const string workloadSubjectId = "church-report-service";
            var rows = await _package01FeeReadClient!
                .RetrieveDedicationFeesByContactAsync(
                    profileAlias,
                    workloadSubjectId,
                    contactId,
                    contactName: null,
                    cancellationToken)
                .ConfigureAwait(false);

            ArgumentNullException.ThrowIfNull(rows);

            var mappedFees = new List<DonationFeeAuditRow>(rows.Count);
            long totalAmount = 0;
            foreach (var row in rows)
            {
                ArgumentNullException.ThrowIfNull(row);
                var mappedFee = MapFeeAuditRow(row);
                mappedFees.Add(mappedFee);
                totalAmount = checked(totalAmount + mappedFee.Amount);
            }

            if (totalAmount > int.MaxValue || totalAmount < int.MinValue)
            {
                throw new OverflowException("The dedication fee total exceeds the supported model range.");
            }

            return new DonationFeeAuditReadResult(mappedFees, (int)totalAmount);
        }

        /// <summary>
        /// 只在 host 已注入 controller 時建立受控 lease；未注入時維持既有 compatibility path。
        /// intake 停止時會在任何 ToolUtility CRM 呼叫前 fail closed，避免 shutdown 與新的 legacy
        /// ingress 重疊。回傳的 lease 僅在目前 async stack 存活，不保存呼叫端資料。
        /// </summary>
        private async ValueTask<LegacyToolUtilityDrainController.LegacyToolUtilityDrainLease?>
            AcquireLegacyFeeLeaseAsync(CancellationToken cancellationToken)
        {
            if (_legacyDrainController is null)
            {
                return null;
            }

            var lease = await _legacyDrainController.AcquireAsync(
                    LegacyToolUtilityWorkload.Package01FeeRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                throw new InvalidOperationException("Legacy ToolUtility intake is stopped.");
            }

            return lease;
        }

        private async Task FillFeeListViaPackage01Async(
            DonationPaymentFormModel model,
            Guid contactId,
            string? fullName,
            CancellationToken cancellationToken)
        {
            var profileAlias = _dynamicsAccess?.ProfileAlias;
            if (string.IsNullOrWhiteSpace(profileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess:ProfileAlias is required when Package01 fee reads are enabled.");
            }

            // WorkloadSubjectId 使用產品部署識別，不是終端使用者 LINE/session。
            const string workloadSubjectId = "church-report-service";

            var rows = await _package01FeeReadClient!
                .RetrieveDedicationFeesByContactDateRangeAsync(
                    profileAlias,
                    workloadSubjectId,
                    contactId,
                    model.QueryStartDate,
                    model.QueryEndDate,
                    fullName,
                    cancellationToken)
                .ConfigureAwait(false);

            System.Diagnostics.Trace.WriteLine(
                $"[DEDQUERY-P01] ContactId={contactId:D} Start={model.QueryStartDate:yyyy-MM-dd} End={model.QueryEndDate:yyyy-MM-dd} Returned={rows.Count}");

            // DTO 投影可能因上游不完整資料而失敗。先在 request-local 區域集合完成所有映射與
            // 加總，最後才原子地更新既有表單模型；如此 cancellation、fault 或投影例外不會留下
            // 半成品總額／清單給同一個請求後續流程，更不會建立跨請求共享的暫存狀態。
            var mappedFees = rows.Select(MapFeeDto).ToList();
            long totalAmount = 0;
            foreach (var fee in mappedFees)
            {
                totalAmount += fee.Amount;
            }

            if (totalAmount > int.MaxValue || totalAmount < int.MinValue)
            {
                // 畫面模型以 Int32 表示總額；若上游資料總和超出既有契約，寧可 fail-closed
                // 並維持舊 model，也不能讓 unchecked 運算環繞為錯誤負數或靜默截斷。
                throw new OverflowException("The dedication fee total exceeds the supported model range.");
            }

            model.TotalAmount = (int)totalAmount;
            model.DedicationFeeList = mappedFees;
        }

        private static DedicationFee MapFeeDto(SpeechMessage.Dynamics.ProductClient.Models.FeeRecordDto dto)
        {
            var amount = dto.Amount;
            if (amount < int.MinValue) amount = int.MinValue;
            if (amount > int.MaxValue) amount = int.MaxValue;

            return new DedicationFee
            {
                DedicationDate = (dto.CreatedOn ?? DateTimeOffset.MinValue).LocalDateTime,
                PayDate = (dto.PayDate ?? DateTimeOffset.MinValue).LocalDateTime,
                Amount = Convert.ToInt32(amount),
                PayWay = !string.IsNullOrWhiteSpace(dto.PayWayLabel)
                    ? dto.PayWayLabel!
                    : ConvertPayWay(dto.PayWayOption ?? -1),
                Category = !string.IsNullOrWhiteSpace(dto.CategoryLabel)
                    ? dto.CategoryLabel!
                    : "十一奉獻",
                Others = dto.Others ?? string.Empty,
                PaidPeriod = dto.PaidPeriod ?? string.Empty
            };
        }

        /// <summary>
        /// 將 ProductClient fee DTO 投影為不可變稽核列。
        /// </summary>
        /// <remarks>
        /// 此 mapping 不會回填或保留 <see cref="DonationPaymentFormModel"/>，也不產生 CRM
        /// Entity。所有字串與日期在本 request 的 local variables 中完成，回傳後只能讀取，
        /// 因而不能被另一個付款流程或使用者 request 修改。
        /// </remarks>
        /// <param name="dto">已由 typed client 回傳的單一 fee DTO。</param>
        /// <returns>僅含 JSON 稽核畫面核准欄位的不可變列。</returns>
        private static DonationFeeAuditRow MapFeeAuditRow(
            SpeechMessage.Dynamics.ProductClient.Models.FeeRecordDto dto)
        {
            var amount = dto.Amount;
            if (amount < int.MinValue || amount > int.MaxValue)
            {
                throw new OverflowException("A dedication fee amount exceeds the supported model range.");
            }

            return new DonationFeeAuditRow(
                !string.IsNullOrWhiteSpace(dto.CategoryLabel) ? dto.CategoryLabel : "一般奉獻",
                (dto.CreatedOn ?? DateTimeOffset.MinValue).LocalDateTime,
                (dto.PayDate ?? DateTimeOffset.MinValue).LocalDateTime,
                !string.IsNullOrWhiteSpace(dto.PayWayLabel)
                    ? dto.PayWayLabel
                    : ConvertPayWay(dto.PayWayOption ?? -1),
                Convert.ToInt32(amount),
                dto.PaidPeriod ?? string.Empty,
                dto.Others ?? string.Empty);
        }

        /// <summary>
        /// 將收費單清單投影成既有 AJAX endpoint 會序列化的匿名物件形狀。
        /// </summary>
        public static List<object> ToAjaxRows(IEnumerable<DedicationFee> fees)
        {
            return fees.Select(f => new
            {
                f.Category,
                f.DedicationDate,
                f.PayDate,
                f.PayWay,
                f.Amount,
                f.PaidPeriod,
                f.Others
            }).ToList<object>();
        }

        /// <summary>
        /// 將不可變 typed audit rows 轉為既有 AJAX JSON 欄位形狀。
        /// </summary>
        /// <remarks>
        /// 這個 overload 只枚舉目前 request 的 immutable rows，不存入 Session、cache 或 static
        /// state，也不把資料轉回付款表單或 CRM Entity；enumeration 結束後沒有需要釋放的資源。
        /// </remarks>
        /// <param name="fees">已授權 typed audit 讀取回傳的不可變列。</param>
        /// <returns>與既有畫面相容、但不暴露內部 DTO/Entity 的 JSON 物件列。</returns>
        public static List<object> ToAjaxRows(IEnumerable<DonationFeeAuditRow> fees)
        {
            ArgumentNullException.ThrowIfNull(fees);

            return fees.Select(f => new
            {
                f.Category,
                f.DedicationDate,
                f.PayDate,
                f.PayWay,
                f.Amount,
                f.PaidPeriod,
                f.Others
            }).ToList<object>();
        }

        /// <summary>
        /// 將 CRM new_pay_way OptionSet 值轉成 ChurchReport 畫面文字。
        /// </summary>
        public static string ConvertPayWay(int optionSetValue)
        {
            return optionSetValue switch
            {
                100000000 => "現金",
                100000001 => "信用卡",
                100000002 => "ATM轉帳",
                100000003 => "超商付款",
                100000005 => "LinePay",
                100000006 => "銀行轉帳",
                100000007 => "行動支付",
                100000008 => "銀聯卡",
                _ => "未知"
            };
        }

        private DedicationFee MapFee(Entity feeEntity)
        {
            var fee = new DedicationFee
            {
                DedicationDate = _utility.GetEntityDateTimeAttribute(feeEntity, "createdon").ToLocalTime(),
                PayDate = _utility.GetEntityDateTimeAttribute(feeEntity, "new_pay_date").ToLocalTime(),
                Amount = Convert.ToInt32(_utility.GetEntityMoneyAttribute(feeEntity, "new_fee_really_paid").Value),
                PayWay = ConvertPayWay(_utility.GetOptionSetAttribute(feeEntity, "new_pay_way")),
                Category = ConvertCategory(feeEntity),
                Others = _utility.GetEntityStringAttribute(feeEntity, "new_others"),
                PaidPeriod = _utility.GetEntityStringAttribute(feeEntity, "new_paid_period")
            };

            return fee;
        }

        /// <summary>
        /// 優先使用 CRM FormattedValues 的顯示文字；查不到時回到既有預設「十一奉獻」。
        /// </summary>
        public static string ConvertCategory(Entity feeEntity)
        {
            try
            {
                if (feeEntity.FormattedValues.Contains("new_category"))
                {
                    string displayText = feeEntity.FormattedValues["new_category"];
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        return displayText;
                    }
                }

                return "十一奉獻";
            }
            catch
            {
                return "十一奉獻";
            }
        }
    }
}
