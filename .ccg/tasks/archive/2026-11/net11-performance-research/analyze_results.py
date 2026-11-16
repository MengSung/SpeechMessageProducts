"""
檔案整體說明
目的與定位：分析本研究保存的程序級 .NET 10／11 原始 JSON，不重新執行產品或改寫原始證據。
主要功能與流程：以每個程序的 micro 中位數或 HTTP 百分位作為一個獨立觀測，按固定 pair 計算 11/10 的 log-ratio，使用固定種子 bootstrap 95% CI；同時輸出可讀 Markdown 與機器可讀 JSON。
使用邊界與維護：CI 只描述本機固定輸入、固定核心及指定 runtime 的研究樣本；不把同一程序內請求當獨立樣本，不將結果外推成正式流量或外部 I/O 效能。原始檔缺欄、pair 不完整或 hash 不一致時 fail closed。
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import random
import re
import statistics


def median(values):
    """以排序中位數保持報告與 Python 版本無關的明確定義。"""
    return statistics.median(values)


def bootstrap(values, iterations=10_000, seed=11_161_6):
    """對配對 log-ratio 重抽樣；CI 跨零代表未能辨識方向。"""
    if not values:
        return None
    rng = random.Random(seed)
    estimates = []
    for _ in range(iterations):
        sample = [values[rng.randrange(len(values))] for _ in values]
        estimates.append(math.exp(statistics.median(sample)))
    estimates.sort()
    lower = estimates[max(0, int(iterations * 0.025) - 1)]
    upper = estimates[min(iterations - 1, int(iterations * 0.975))]
    return lower, upper


def ratio_summary(name, pairs, direction="lower"):
    """將每組相鄰 10／11 配對摘要為中位數與 CI；direction 只影響文字，不改原始比值。"""
    ratios = [b / a for a, b in pairs if a > 0 and b > 0]
    logs = [math.log(value) for value in ratios]
    result = dict(name=name, n=len(ratios), ratioMedian=median(ratios) if ratios else None,
                  ratioRange=(min(ratios), max(ratios)) if ratios else None,
                  ci95=bootstrap(logs), lowerIsBetter=direction == "lower")
    if not ratios:
        result["interpretation"] = "無有效配對"
    elif len(ratios) < 10:
        favorable = result["ratioMedian"] < 1 if direction == "lower" else result["ratioMedian"] > 1
        result["interpretation"] = "方向性（低檢定力，未定論）" if favorable else "方向性退步（低檢定力，未定論）"
    elif result["ci95"][0] <= 1 <= result["ci95"][1]:
        result["interpretation"] = "未檢出可辨識差異"
    elif direction == "lower" and result["ratioMedian"] < 1:
        result["interpretation"] = "改善"
    elif direction == "higher" and result["ratioMedian"] > 1:
        result["interpretation"] = "改善"
    else:
        result["interpretation"] = "退步"
    return result


def read_micro(directory):
    """依檔名的 pair/runtime 讀取每個 process 的案例中位數，檢查同一 input/output hash。"""
    grouped = {}
    for path in sorted(directory.glob("micro-p*-r*.stdout.jsonl")):
        runtime = 11 if "-r11." in path.name else 10
        pair = int(path.name.split("-p", 1)[1][:2])
        record = json.loads(path.read_text(encoding="utf-8"))
        for item in record["results"]:
            samples = item["samples"]
            grouped.setdefault(item["name"], {}).setdefault(runtime, {})[pair] = {
                "ns": median([float(sample["ns"]) for sample in samples]),
                "cpuNs": median([float(sample["cpuNs"]) for sample in samples]),
                "bytes": median([float(sample["bytes"]) for sample in samples]),
                "hash": item["outputHash"],
                "input": item["inputHash"],
            }
    output = []
    for name, runtimes in sorted(grouped.items()):
        if set(runtimes) != {10, 11}:
            raise ValueError(f"Incomplete runtime pair for {name}.")
        pairs = sorted(set(runtimes[10]) & set(runtimes[11]))
        if not pairs or set(runtimes[10]) != set(runtimes[11]):
            raise ValueError(f"Incomplete process pair for {name}.")
        if any(runtimes[10][pair]["input"] != runtimes[11][pair]["input"]
               or runtimes[10][pair]["hash"] != runtimes[11][pair]["hash"] for pair in pairs):
            raise ValueError(f"Correctness hash mismatch for {name}.")
        output.append(dict(name=name, pairs=pairs,
                           wall=ratio_summary(name, [(runtimes[10][pair]["ns"], runtimes[11][pair]["ns"]) for pair in pairs]),
                           cpu=ratio_summary(name, [(runtimes[10][pair]["cpuNs"], runtimes[11][pair]["cpuNs"]) for pair in pairs]),
                           bytes=ratio_summary(name, [(runtimes[10][pair]["bytes"], runtimes[11][pair]["bytes"]) for pair in pairs])))
    return output


def read_http(directory):
    """依情境及 pair 配對 HTTP 吞吐與延遲；百分位已在每一 process 內計算。"""
    grouped = {}
    for path in sorted(directory.glob("http-p*-r*-d*-c*.result.json")):
        match = re.fullmatch(r"http-p(\d+)-r(10|11)-d(\d+)-c(\d+)\.result", path.stem)
        if match is None:
            raise ValueError(f"Unexpected HTTP result name: {path.name}")
        pair, runtime, delay, concurrency = (int(value) for value in match.groups())
        record = json.loads(path.read_text(encoding="utf-8"))
        if record["errors"] != 0 or record["count"] <= 0:
            raise ValueError(f"HTTP error record: {path}")
        key = (delay, concurrency)
        grouped.setdefault(key, {}).setdefault(runtime, {})[pair] = record
    output = []
    for (delay, concurrency), runtimes in sorted(grouped.items()):
        if set(runtimes) != {10, 11}:
            raise ValueError(f"Incomplete HTTP runtime pair for {delay}/{concurrency}.")
        pairs = sorted(set(runtimes[10]) & set(runtimes[11]))
        output.append(dict(delayMs=delay, concurrency=concurrency, pairs=pairs,
                           throughput=ratio_summary("rps", [(runtimes[10][p]["rps"], runtimes[11][p]["rps"]) for p in pairs], "higher"),
                           p50=ratio_summary("p50", [(runtimes[10][p]["p50"], runtimes[11][p]["p50"]) for p in pairs]),
                           p95=ratio_summary("p95", [(runtimes[10][p]["p95"], runtimes[11][p]["p95"]) for p in pairs]),
                           p99=ratio_summary("p99", [(runtimes[10][p]["p99"], runtimes[11][p]["p99"]) for p in pairs]),
                           bytes=ratio_summary("serverBytesPerRequest", [(runtimes[10][p]["serverBytesPerRequest"], runtimes[11][p]["serverBytesPerRequest"]) for p in pairs])))
    return output


def fmt(value):
    """以固定有效位呈現，避免報告誤讀過多小數。"""
    return "—" if value is None else f"{value:.4f}"


def markdown(micro, http):
    """生成繁中摘要；每個數字旁保持實測／CI 語意，不把情境當正式保證。"""
    lines = [
        "# .NET 10／11 研究結果摘要",
        "",
        "比值均為 **.NET 11 / .NET 10**；micro 的低於 1 代表較快或較少配置，HTTP throughput 高於 1 代表較高吞吐。CI 為程序級配對 bootstrap 95%。",
        "",
        "## 純 CPU／配置案例",
        "",
        "| 案例 | n | wall 比值 | 95% CI | CPU 比值 | 配置比值 | 判讀 |",
        "|---|---:|---:|---|---:|---:|---|",
    ]
    for item in micro:
        lines.append(f"| {item['name']} | {item['wall']['n']} | {fmt(item['wall']['ratioMedian'])} | "
                     f"{fmt(item['wall']['ci95'][0])}–{fmt(item['wall']['ci95'][1])} | "
                     f"{fmt(item['cpu']['ratioMedian'])} | {fmt(item['bytes']['ratioMedian'])} | {item['wall']['interpretation']} |")
    lines += ["", "## Loopback 正式 Webhook endpoint（合成 dedup／queue）", "",
              "| 模擬等待 | 併行 | n | throughput 比值 | 95% CI | p50 | p95 | p99 | 判讀 |",
              "|---:|---:|---:|---:|---|---:|---:|---:|---|"]
    for item in http:
        lines.append(f"| {item['delayMs']} ms | {item['concurrency']} | {item['throughput']['n']} | "
                     f"{fmt(item['throughput']['ratioMedian'])} | {fmt(item['throughput']['ci95'][0])}–{fmt(item['throughput']['ci95'][1])} | "
                     f"{fmt(item['p50']['ratioMedian'])} | {fmt(item['p95']['ratioMedian'])} | {fmt(item['p99']['ratioMedian'])} | "
                     f"{item['throughput']['interpretation']} |")
    return "\n".join(lines) + "\n"


def main():
    """分析一個或多個已完成研究目錄，產生 summary.json／summary.md。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("--micro", type=Path, action="append", required=True)
    parser.add_argument("--http", type=Path, action="append", default=[])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    micro = []
    for path in args.micro:
        micro.extend(read_micro(path))
    # Multiple directories are only accepted when callers intentionally provide disjoint runs;
    # the current report uses one directory so no hidden deduplication occurs.
    if len(args.micro) > 1:
        by_name = {}
        for item in micro:
            by_name.setdefault(item["name"], []).append(item)
        micro = [item for values in by_name.values() for item in values]
    http = [item for path in args.http for item in read_http(path)]
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "summary.json").write_text(json.dumps(dict(micro=micro, http=http), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.output / "summary.md").write_text(markdown(micro, http), encoding="utf-8")
    print(args.output / "summary.md")


if __name__ == "__main__":
    main()
