"""驗證一次 ChurchReport 重現所產生的 Dataverse Trace 是否滿足全部生命週期不變量。

用法：
    python .trellis/scripts/verify_trace_invariants.py [trace 目錄]

預設目錄為 D:\\除錯追蹤（appsettings.Development.json 的 DiagnosticsTrace:Directory）。

每一條不變量都會印出實際數字，而不是只印通過與否；任何一條失敗時行程以 1 結束。
這些不變量對應 docs/architecture/dataverse-gateway-v1.md 的核心契約，以及
.trellis/tasks/08-22-churchreport-trace-findings-remediation 的 F4 驗收標準。
"""
import collections
import json
import os
import sys

DEFAULT_DIR = "D:\\除錯追蹤"


def load(path):
    """讀入 JSONL，回傳 (事件清單, 解析失敗行數)。以唯讀串流開啟，不修改原始 trace。"""
    records, bad = [], 0
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                records.append(json.loads(line))
            except ValueError:
                bad += 1
    return records, bad


class Report:
    """收集逐條不變量結果；任何一條失敗即整體失敗。"""

    def __init__(self):
        self.failed = 0
        self.passed = 0

    def check(self, name, ok, detail):
        mark = "PASS" if ok else "FAIL"
        if ok:
            self.passed += 1
        else:
            self.failed += 1
        print(f"[{mark}] {name}: {detail}")

    def note(self, name, detail):
        print(f"[INFO] {name}: {detail}")


def main():
    base = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DIR
    jsonl = os.path.join(base, "dataverse-trace.jsonl")
    tracelog = os.path.join(base, "Trace.log")

    if not os.path.exists(jsonl):
        print(f"找不到 {jsonl}；請先執行一次重現。")
        return 2

    records, bad = load(jsonl)
    report = Report()
    report.check("JSONL 解析", bad == 0, f"{len(records)} 筆事件，解析失敗 {bad} 行")

    events = collections.Counter(r.get("ev") for r in records)
    report.note("事件分佈", ", ".join(f"{k}={v}" for k, v in sorted(events.items())))

    # ---- F4：CRM 歸因不變量 ----
    crm_ops = [r for r in records if r["ev"] == "crm.op"]
    req_end = [r for r in records if r["ev"] == "request.end"]
    bg_end = [r for r in records if r["ev"] == "bg.end"]
    bg_begin = [r for r in records if r["ev"] == "bg.begin"]

    attributed = sum(r.get("crmCount", 0) for r in req_end) + sum(r.get("crmCount", 0) for r in bg_end)
    report.check(
        "F4 CRM 歸因完整",
        attributed == len(crm_ops),
        f"request.end({sum(r.get('crmCount', 0) for r in req_end)}) + "
        f"bg.end({sum(r.get('crmCount', 0) for r in bg_end)}) = {attributed}，crm.op = {len(crm_ops)}",
    )

    report.check(
        "F4 bg.begin / bg.end 成對",
        len(bg_begin) == len(bg_end),
        f"bg.begin={len(bg_begin)}，bg.end={len(bg_end)}",
    )

    if not bg_end:
        report.check(
            "F4 背景範圍已觸發",
            False,
            "沒有任何 bg.end 事件——代表 SaveIntegrate 的背景路徑未被執行，本次重現無效",
        )
    else:
        req_ids = {r["traceId"] for r in records if r["ev"] == "request.begin"}
        orphan = [r for r in bg_end if r.get("parentTraceId") and r["parentTraceId"] not in req_ids]
        report.check(
            "F4 parentTraceId 指向真實 request",
            not orphan,
            f"{len(bg_end)} 筆 bg.end，孤兒 {len(orphan)} 筆",
        )
        for r in bg_end:
            report.note(
                "背景工作",
                f"{r['traceId']} parent={r.get('parentTraceId')} op={r.get('op')} "
                f"crmCount={r.get('crmCount')} crmMs={r.get('crmMs')} durationMs={r.get('durationMs')}",
            )

    # ---- 背景業務結果（bg.accepted / bg.outcome）----
    # bg.end 只代表 scope 已釋放，例外照樣會產生它，因此不能當作成功證據。
    # 真正的成功證據是 stage=upload 且 outcome=succeeded 的 bg.outcome 事件。
    accepted = [r for r in records if r["ev"] == "bg.accepted"]
    outcomes = [r for r in records if r["ev"] == "bg.outcome"]

    if not accepted and not outcomes:
        report.note(
            "背景結果事件",
            "未觀測到 bg.accepted / bg.outcome。若本次建置已包含該功能，代表 SaveIntegrate 未被執行",
        )
    else:
        report.check(
            "每個 bg.accepted 都有對應的 bg.outcome",
            {r.get("operationId") for r in accepted} <= {r.get("operationId") for r in outcomes},
            f"accepted={len(accepted)}，outcome={len(outcomes)}",
        )

        uploads = [r for r in outcomes if r.get("stage") == "upload"]
        report.check(
            "上傳階段有明確結果",
            bool(uploads),
            f"stage=upload 的結果事件 {len(uploads)} 筆",
        )
        succeeded = [r for r in uploads if r.get("outcome") == "succeeded"]
        report.check(
            "上傳階段成功",
            bool(uploads) and len(succeeded) == len(uploads),
            f"成功 {len(succeeded)} / {len(uploads)}",
        )

        for r in outcomes:
            mark = "" if r.get("outcome") == "succeeded" else f" errorClass={r.get('errorClass')}"
            report.note(
                "背景結果",
                f"op={r.get('operationId')} stage={r.get('stage')} outcome={r.get('outcome')}{mark}",
            )

        failed_stages = collections.Counter(
            r.get("stage") for r in outcomes if r.get("outcome") == "failed"
        )
        if failed_stages:
            report.note("失敗階段分佈", dict(failed_stages))

    # ---- 既有租約不變量 ----
    acquires = [r for r in records if r["ev"] in ("pool.acquire.hit", "pool.acquire.miss")]
    returns = [r for r in records if r["ev"] == "pool.return"]
    report.check(
        "租約成對",
        len(acquires) == len(returns),
        f"acquire={len(acquires)}，return={len(returns)}",
    )

    acquired_ids = [r["leaseId"] for r in acquires]
    dup = [k for k, v in collections.Counter(acquired_ids).items() if v > 1]
    report.check("leaseId 無重複", not dup, f"重複 {len(dup)} 個")

    missing = set(acquired_ids) - {r["leaseId"] for r in returns}
    orphan_ret = {r["leaseId"] for r in returns} - set(acquired_ids)
    report.check(
        "無遺漏／多餘歸還",
        not missing and not orphan_ret,
        f"缺少歸還 {len(missing)}，多出歸還 {len(orphan_ret)}",
    )

    # 每條實體連線的最大同時租借數；> 1 代表兩個請求共用同一條連線
    timeline = []
    for r in records:
        if r["ev"] in ("pool.acquire.hit", "pool.acquire.miss"):
            timeline.append((r["clientId"], 1))
        elif r["ev"] == "pool.return":
            timeline.append((r["clientId"], -1))
    live, peak = collections.Counter(), collections.Counter()
    for client, delta in timeline:
        live[client] += delta
        peak[client] = max(peak[client], live[client])
    worst = max(peak.values()) if peak else 0
    report.check(
        "每條連線最大同時租借數 == 1",
        worst <= 1,
        f"各連線峰值 {dict(peak)}",
    )

    outstanding = {c: n for c, n in live.items() if n != 0}
    report.check("結束時無未歸還租約", not outstanding, f"未歸還 {outstanding or '無'}")

    caller_ids = collections.Counter(r.get("callerIdAtReturn", "") for r in returns)
    report.check(
        "歸還前 CallerId 已清除",
        set(caller_ids) <= {""},
        f"{dict(caller_ids)}",
    )

    scope_ends = [r for r in records if r["ev"] == "gateway.scope.end"]
    still_held = [r for r in scope_ends if r.get("leaseStillHeld")]
    report.check(
        "Gateway 釋放時未持有租約",
        not still_held,
        f"{len(scope_ends)} 筆 scope.end，仍持有 {len(still_held)} 筆",
    )

    faulted = [r for r in returns if r.get("state") != "healthy"]
    report.note("歸還狀態", f"healthy={len(returns) - len(faulted)}，其他={len(faulted)}")

    failed_ops = [r for r in crm_ops if not r.get("ok")]
    if failed_ops:
        detail = collections.Counter(f"{r.get('op')} {r.get('entity')}" for r in failed_ops)
        report.note("失敗的 CRM 操作", f"{len(failed_ops)} 次：{dict(detail)}")

    # ---- F2：NOSESSION ----
    if os.path.exists(tracelog):
        with open(tracelog, "r", encoding="utf-8", errors="replace") as handle:
            hits = sum(1 for line in handle if "NOSESSION" in line)
        report.check("F2 Trace.log 無 NOSESSION 快取鍵", hits == 0, f"出現 {hits} 次")
    else:
        report.note("F2", f"找不到 {tracelog}，略過 NOSESSION 檢查")

    print()
    print(f"通過 {report.passed} 項，失敗 {report.failed} 項")
    return 1 if report.failed else 0


if __name__ == "__main__":
    sys.exit(main())
