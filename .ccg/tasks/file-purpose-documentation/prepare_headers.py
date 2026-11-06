"""一次性逐檔文件整理：先讀取完整來源、擷取型別契約及實際入口，再以人工核對的目的補齊檔頭。

此工具只服務本次文件任務，不能作為日後以檔名自動產生空泛註解的入口。
寫入前另存原位元組並比對目前檔案，避免覆蓋另一個 P1 對話的並行修改。
"""
from pathlib import Path
import re
import json
import subprocess
import textwrap
import hashlib

TASK = Path(__file__).resolve().parent
ROOT = Path.cwd()


def clean_xml(value):
    value = value.replace('///', '')
    value = re.sub(r'<(?:see|paramref|typeparamref)\s+[^>]*?(?:cref|name)="([^"]+)"[^>]*/>', r'\1', value)
    value = re.sub(r'<[^>]+>', ' ', value)
    return re.sub(r'\s+', ' ', value).strip()


def remove_legacy_header(text):
    # 舊有自動檔頭只有目錄分類，沒有實際業務說明；替換而非在它上面堆疊第二份。
    text = re.sub(r'\A\s*// =+\n// AI-繁體中文檔案註解\n.*?\n// =+\n', '', text, flags=re.S)
    text = re.sub(r'\A\s*@\*\s*\n?\s*(?:=+\s*\n)?\s*AI-繁體中文檔案註解.*?\*@\s*\n', '', text, flags=re.S)
    return text


def type_docs(text):
    lines = text.splitlines()
    result = []
    for i, line in enumerate(lines):
        match = re.match(r'^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly)\s+)*(class|interface|struct|enum|record(?:\s+(?:struct|class))?)\s+(\w+)', line)
        if not match or line.lstrip().startswith('///'):
            continue
        j = i - 1
        while j >= 0 and (lines[j].lstrip().startswith('[') or not lines[j].strip()):
            j -= 1
        docs = []
        while j >= 0 and lines[j].lstrip().startswith('///'):
            docs.insert(0, lines[j]); j -= 1
        raw = '\n'.join(docs)
        summary = re.search(r'<summary>(.*?)</summary>', raw, re.S)
        result.append((match[2], clean_xml(summary[1]) if summary else ''))
    return result


def inventory():
    names = subprocess.check_output(['git', 'ls-files', '-z']).decode('utf-8').split('\0')
    result = []
    for name in names:
        if not name or Path(name).suffix not in ('.cs', '.cshtml') or name.startswith(('.ccg/', '.trellis/', 'PowerPlatform.Dataverse.Client/', 'docs/', 'scratch/')):
            continue
        raw = Path(name).read_bytes()
        try:
            text = raw.decode('utf-8-sig'); encoding = 'utf-8-sig'
        except UnicodeDecodeError:
            text = raw.decode('cp950'); encoding = 'cp950'
            if text.encode('cp950') != raw:
                raise ValueError('Legacy encoding is not lossless: ' + name)
        text = text.replace('\r\n', '\n')
        result.append(dict(path=name, text=text, body=remove_legacy_header(text), encoding=encoding,
                           sha256=hashlib.sha256(raw).hexdigest(), types=type_docs(remove_legacy_header(text))))
    return result


if __name__ == '__main__':
    values = inventory()
    (TASK / 'source-semantics.json').write_text(json.dumps([{k:v for k,v in x.items() if k not in ('text','body')} for x in values], ensure_ascii=False, indent=2), encoding='utf-8')
    for x in values:
        if not x['path'].endswith('.cs') or x['path'].endswith('.g.cs') or x['path'].startswith(('Line.Messaging/', 'SpeechMessageProducts.ChurchReport/Views/')):
            continue
        if not any(len(s) >= 35 and re.search('[\u4e00-\u9fff]', s) for _, s in x['types']):
            print(x['path'] + ' | ' + ' ; '.join(n + ': ' + s[:160] for n,s in x['types'][:3]))
