"""逐位元組驗證本次變更的 .cs / .cshtml 檔案是否符合 AGENTS.md 的編碼契約。

檢查項目：UTF-8 無 BOM、CRLF 行尾、檔尾 CRLF、無 Unicode 私用區碼點、無替代字元。
任何標記結尾帶 "!" 即為 release blocker。
"""
import subprocess
import sys

BOM = bytes([0xEF, 0xBB, 0xBF])
LF = bytes([10])
CRLF = bytes([13, 10])
PUA_LO, PUA_HI = chr(0xE000), chr(0xF8FF)
REPLACEMENT = chr(0xFFFD)


def changed_files():
    out = subprocess.run(
        ["git", "diff", "--name-only", "--diff-filter=ACM", "HEAD"],
        capture_output=True, text=True, check=True).stdout
    return [p for p in out.splitlines()
            if p.strip().endswith((".cs", ".cshtml"))]


def main():
    failed = False
    paths = changed_files()
    if not paths:
        print("no changed .cs / .cshtml files")
        return 0
    for path in paths:
        with open(path, "rb") as handle:
            raw = handle.read()
        text = raw.decode("utf-8", errors="replace")
        marks = [
            "BOM!" if raw.startswith(BOM) else "noBOM",
            "CRLF" if raw.count(LF) == raw.count(CRLF) else "MIXED-EOL!",
            "endsCRLF" if raw.endswith(CRLF) else "BAD-END!",
            "PUA!" if any(PUA_LO <= c <= PUA_HI for c in text) else "noPUA",
            "REPLCHAR!" if REPLACEMENT in text else "ok",
        ]
        if any(m.endswith("!") for m in marks):
            failed = True
        print(path, " ".join(marks))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
