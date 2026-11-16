"""
檔案整體說明
目的與定位：重現本次 .NET 10／11 離線研究，僅管理自己啟動的程序及忽略的 artifacts；不修改 SDK、產品設定、方案或正式碼。
主要功能與流程：prepare 以既有 SDK 的 csc 編譯固定 harness 和原始樹 builder，建立明確 runtimeconfig；micro 與 http 依 ABBA／BAAB 順序執行並保存原始 JSON、環境及 hash。client 固定 .NET 10，server 才切換版本。
使用邊界與維護：Windows CPU-set API 判定同類核心，父程序指定子程序初始 affinity，最多一個量測 server；逾時只終止本工具建立的 process，finally 等待排空。所有網址只允許 loopback，沒有產品 Host 或真實 SQL／LINE。重跑使用新目錄，保留失敗證據，不覆蓋既有數值。
"""
from __future__ import annotations

import argparse
import ctypes
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import statistics
import struct
import subprocess
import threading
import time

TASK = Path(__file__).resolve().parent
ROOT = next(parent for parent in TASK.parents if (parent / "SpeechMessageProducts.sln").exists())
ART = ROOT / "artifacts/net11-performance-research"
APP = ART / "app"
DOTNET = Path(os.environ["ProgramFiles"]) / "dotnet/dotnet.exe"
CONFIG = {10: "10.0.15", 11: "11.0.0"}
SOURCES = [
    ROOT / "SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs",
    ROOT / "SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs",
    ROOT / "SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs",
]
SERVER_MASK = 0
CLIENT_MASK = 0


def write_json(path, value):
    """只寫父程序擁有的研究證據；UTF-8 無 BOM、CRLF，避免 locale 造成繁中損壞。"""
    path.write_bytes((json.dumps(value, ensure_ascii=False, indent=2) + "\n").replace("\n", "\r\n").encode("utf-8"))


def sha(path):
    """內容 hash 用來驗證相同 IL；不讀取產品組態或秘密。"""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def topology():
    """以 Windows CPU sets 的 efficiency class 區分核心；原始欄位一併保存，不假設核心編號。"""
    api = ctypes.windll.kernel32.GetSystemCpuSetInformation
    needed = ctypes.c_ulong()
    api(None, 0, ctypes.byref(needed), None, 0)
    buffer = ctypes.create_string_buffer(needed.value)
    if not api(buffer, needed.value, ctypes.byref(needed), None, 0):
        raise ctypes.WinError()
    result, offset = [], 0
    data = buffer.raw[:needed.value]
    while offset < len(data):
        size, kind = struct.unpack_from("<II", data, offset)
        if not size:
            raise RuntimeError("Invalid CPU-set record.")
        if kind == 0:
            cpu_id, group, logical, core, cache, numa, efficiency, flags = struct.unpack_from("<IHBBBBBB", data, offset + 8)
            if group != 0:
                raise RuntimeError("Research currently supports one processor group.")
            result.append(dict(id=cpu_id, group=group, logical=logical, core=core, efficiency=efficiency, flags=flags))
        offset += size
    return result


def affinity_config():
    """預先固定較高效能等級的核心集合，server/client 各最多四核；不變更全機電源或其他程序。"""
    global SERVER_MASK, CLIENT_MASK
    cpus = topology()
    maximum = max(cpu["efficiency"] for cpu in cpus)
    selected = [cpu["logical"] for cpu in cpus if cpu["efficiency"] == maximum]
    if len(selected) < 4:
        raise RuntimeError("Insufficient homogeneous cores.")
    split = min(4, len(selected) // 2)
    SERVER_MASK = sum(1 << cpu for cpu in selected[:split])
    CLIENT_MASK = sum(1 << cpu for cpu in selected[split:split * 2])
    return dict(cpuSets=cpus, efficiency=maximum, serverMask=SERVER_MASK, clientMask=CLIENT_MASK)


def environment():
    """兩邊明確設定相同 GC／tiering／R2R；隔離繼承的 DOTNET/COMPlus 變數，不保留敏感環境快照。"""
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith(("DOTNET_", "COMPLUS_"))}
    env.update(DOTNET_TieredPGO="1", DOTNET_TieredCompilation="1", DOTNET_ReadyToRun="1",
               DOTNET_gcServer="1", DOTNET_GCHeapCount="4", DOTNET_gcConcurrent="1",
               DOTNET_EnableDiagnostics="0")
    return env


def command(runtime, mode, *args):
    """同一個 research DLL，只替換明確共享框架版本，並借用 publish 的 deps graph。"""
    return [str(DOTNET), "exec", "--runtimeconfig", str(APP / f"research{runtime}.runtimeconfig.json"),
            "--depsfile", str(APP / "SpeechMessageProducts.LineWebhook.deps.json"),
            str(APP / "Net11Research.dll"), mode, *map(str, args)]


def start(cmd, stdout, stderr, mask):
    """在本父程序指定 affinity 的短暫區間啟動子程序，使 GC 初始化便看見固定核心數；finally 還原。"""
    kernel = ctypes.windll.kernel32
    kernel.GetCurrentProcess.restype = ctypes.c_void_p
    handle = kernel.GetCurrentProcess()
    previous, system = ctypes.c_size_t(), ctypes.c_size_t()
    if not kernel.GetProcessAffinityMask(ctypes.c_void_p(handle), ctypes.byref(previous), ctypes.byref(system)):
        raise ctypes.WinError()
    if not kernel.SetProcessAffinityMask(ctypes.c_void_p(handle), ctypes.c_size_t(mask)):
        raise ctypes.WinError()
    try:
        return subprocess.Popen(cmd, cwd=APP, env=environment(), stdin=subprocess.PIPE,
                                stdout=stdout, stderr=stderr, text=True, encoding="utf-8")
    finally:
        if not kernel.SetProcessAffinityMask(ctypes.c_void_p(handle), previous):
            raise ctypes.WinError()


def run_owned(cmd, stem, mask, timeout=150):
    """保存 stdout/stderr；逾時只 kill 目前 Popen owner，再 wait，不留下孤兒程序。"""
    with stem.with_suffix(".stdout.jsonl").open("w", encoding="utf-8") as output, \
            stem.with_suffix(".stderr.txt").open("w", encoding="utf-8") as error:
        process = start(cmd, output, error, mask)
        try:
            code = process.wait(timeout=timeout)
        except BaseException:
            process.kill()
            process.wait()
            raise
    if code:
        raise RuntimeError(f"Research process failed: {stem.name}; see preserved stderr.")
    lines = stem.with_suffix(".stdout.jsonl").read_text(encoding="utf-8").splitlines()
    if len(lines) != 1:
        raise RuntimeError("Unexpected output record count.")
    return json.loads(lines[0])


def prepare():
    """編譯一次 Release 最佳化 harness；來源快照 hash、IL hash、runtimeconfig 與 compiler log 都保存。"""
    harness = ART / "harness"
    harness.mkdir(parents=True, exist_ok=True)
    snapshot = harness / "source-snapshot"
    snapshot.mkdir(exist_ok=True)
    files = []
    manifest = {}
    for path in [*(TASK / "harness").glob("*.cs"), *SOURCES]:
        content = path.read_bytes()
        # authored sources normalize only whitespace encoding; original product sources stay byte-identical.
        if path.parent == TASK / "harness":
            content = (content.decode("utf-8").replace("\r\n", "\n").replace("\r", "\n").rstrip("\n") + "\n").replace("\n", "\r\n").encode("utf-8")
            path.write_bytes(content)
        copied = snapshot / path.name
        copied.write_bytes(content)
        files.append(copied)
        manifest[str(path.relative_to(ROOT))] = sha(copied)
    refdir = DOTNET.parent / "packs"
    refs = sorted((refdir / "Microsoft.NETCore.App.Ref/10.0.15/ref/net10.0").glob("*.dll"))
    refs += sorted((refdir / "Microsoft.AspNetCore.App.Ref/10.0.15/ref/net10.0").glob("*.dll"))
    names = ["SpeechMessageProducts.LineWebhook", "LineMessagingProcessor.AspNetCore", "LineMessagingProcessor.Core",
             "Line.Messaging", "SpeechMessage.Hosting", "SpeechMessage.Localization", "SpeechMessage.Tenancy",
             "Newtonsoft.Json", "ToolUtility"]
    refs += [APP / (name + ".dll") for name in names]
    output = harness / "Net11Research.dll"
    rsp = harness / "compile.rsp"
    options = ["-noconfig", "-nostdlib+", "-target:exe", "-langversion:14", "-optimize+",
               "-deterministic+", "-debug:portable", "-nullable:enable", f'-out:"{output}"']
    options += [f'-r:"{path}"' for path in refs] + [f'"{path}"' for path in files]
    rsp.write_text("\n".join(options), encoding="utf-8")
    compile_result = subprocess.run([str(DOTNET), str(DOTNET.parent / "sdk/10.0.403/Roslyn/bincore/csc.dll"), "@" + str(rsp)],
                                    cwd=ROOT, capture_output=True, timeout=60)
    (harness / "compile.stdout.txt").write_bytes(compile_result.stdout)
    (harness / "compile.stderr.txt").write_bytes(compile_result.stderr)
    if compile_result.returncode:
        raise RuntimeError("csc failed; inspect compiler logs.")
    shutil.copy2(output, APP / output.name)
    for runtime, version in CONFIG.items():
        write_json(APP / f"research{runtime}.runtimeconfig.json", dict(runtimeOptions=dict(
            tfm="net10.0", rollForward="Disable",
            frameworks=[dict(name=name, version=version) for name in ("Microsoft.NETCore.App", "Microsoft.AspNetCore.App")],
            configProperties={"System.GC.Server": True, "System.GC.Concurrent": True,
                              "System.Runtime.TieredCompilation": True, "System.Runtime.TieredPGO": True,
                              "System.Runtime.TieredCompilation.QuickJit": True,
                              "System.Runtime.TieredCompilation.QuickJitForLoops": True})))
    manifest["IL"] = sha(APP / output.name)
    manifest["productAssemblies"] = {path.name: sha(path) for path in APP.glob("*.dll") if not path.name.startswith("Net11Research")}
    write_json(harness / "manifest.json", manifest)
    print(json.dumps(dict(prepared=True, il=manifest["IL"]), ensure_ascii=False), flush=True)


def order(pairs):
    """事前固定 AB/BA 相鄰程序配對；連接起來是 ABBA／BAAB，避免事後依結果挑順序。"""
    return [(10, 11) if index % 2 == 0 else (11, 10) for index in range(pairs)]


def assert_runtime(record, runtime):
    """必須載入明確版本、CoreLib 路徑且 ServerGC／affinity 正確，否則不接受本輪數值。"""
    env = record["environment"]
    corelib = next((item["path"] for item in env["libraries"] if item["name"] == "System.Private.CoreLib"), "")
    expected = f"\\Microsoft.NETCore.App\\{CONFIG[runtime]}\\"
    if (env["version"] != CONFIG[runtime] or expected.lower() not in corelib.lower()
            or not env["serverGC"] or env["affinity"] != SERVER_MASK):
        raise RuntimeError(f"Runtime/config mismatch: {env}")


def run_micro(directory, pairs):
    """一次僅一個計時程序；程序內樣本只算描述，分析的獨立單位是相鄰程序配對。"""
    for pair, runtimes in enumerate(order(pairs)):
        for runtime in runtimes:
            print(f"micro pair={pair} runtime={runtime}", flush=True)
            value = run_owned(command(runtime, "micro"), directory / f"micro-p{pair:02d}-r{runtime}", SERVER_MASK)
            assert_runtime(value, runtime)


def one_server(directory, pair, runtime, delay):
    """擁有一個 Kestrel server；warmup/計時 client 依序執行，EOF 後要求正常停止，逾時才 kill 自有程序。"""
    stem = directory / f"http-p{pair:02d}-r{runtime}-d{delay}"
    with stem.with_suffix(".server.stderr.txt").open("w", encoding="utf-8") as error:
        process = start(command(runtime, "server", delay), subprocess.PIPE, error, SERVER_MASK)
        try:
            messages = queue.Queue()
            reader = threading.Thread(target=lambda: messages.put(process.stdout.readline()), daemon=True)
            reader.start()
            first = messages.get(timeout=30)
            if not first:
                raise RuntimeError("Server failed before readiness.")
            ready = json.loads(first)
            assert_runtime(ready, runtime)
            write_json(stem.with_suffix(".server.json"), ready)
            scenarios = [16] if delay else [1, 16]
            for concurrency in scenarios:
                suffix = f"-c{concurrency}"
                run_owned(command(10, "client", ready["url"], concurrency, 1500, 1),
                          directory / (stem.name + suffix + "-warm"), CLIENT_MASK)
                measured = run_owned(command(10, "client", ready["url"], concurrency, 5000, 0),
                                     directory / (stem.name + suffix), CLIENT_MASK)
                before, after = measured["before"], measured["after"]
                if after["active"] or after["scopeErrors"] or after["pending"] \
                        or after["accepted"] - before["accepted"] != measured["count"] \
                        or after["committed"] - before["committed"] != measured["count"]:
                    raise RuntimeError("HTTP count/isolation/drain assertion failed.")
                # Both metrics requests allocate too; keep raw figures and label this small instrumentation cost.
                measured["serverBytesPerRequest"] = (after["allocated"] - before["allocated"]) / measured["count"]
                measured["serverCpuUsPerRequest"] = (after["cpuMs"] - before["cpuMs"]) * 1000 / measured["count"]
                write_json(directory / (stem.name + suffix + ".result.json"), measured)
        finally:
            if process.poll() is None:
                try:
                    process.communicate("\n", timeout=20)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
                    raise
            if process.returncode:
                raise RuntimeError("Server did not stop cleanly.")


def run_http(directory, pairs):
    """每個情境都用新 server process，固定 client runtime；保留暖機與各輪結果，不挑最快一次。"""
    for delay in (0, 10):
        for pair, runtimes in enumerate(order(pairs)):
            for runtime in runtimes:
                print(f"http pair={pair} runtime={runtime} delay={delay}", flush=True)
                one_server(directory, pair, runtime, delay)


def main():
    """prepare 可重建工具；micro/http 每次建立新時間戳資料夾，任何失敗保留該資料夾與非零碼。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("prepare", "micro", "http"))
    parser.add_argument("--pairs", type=int, default=6)
    args = parser.parse_args()
    if not 1 <= args.pairs <= 30:
        raise ValueError("Pair budget must be 1..30.")
    if args.mode == "prepare":
        prepare()
        return
    cpus = affinity_config()
    directory = ART / (dt.datetime.now().strftime("%Y%m%d-%H%M%S-") + args.mode)
    directory.mkdir()
    write_json(directory / "environment.json", dict(time=dt.datetime.now().astimezone().isoformat(),
               topology=cpus, controlledEnvironment={k: v for k, v in environment().items() if k.startswith("DOTNET_")},
               pairs=args.pairs, order=order(args.pairs), manifest=sha(ART / "harness/manifest.json")))
    if args.mode == "micro":
        run_micro(directory, args.pairs)
    else:
        run_http(directory, args.pairs)
    write_json(directory / "completed.json", dict(completed=True))
    print(str(directory), flush=True)


if __name__ == "__main__":
    main()
