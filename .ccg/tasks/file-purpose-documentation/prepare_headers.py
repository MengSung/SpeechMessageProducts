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
import tempfile
import sys
from purposes import PURPOSES, LINE_TERMS, VIEWS, TEST_PURPOSES

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


def summaries(text):
    return [clean_xml(x) for x in re.findall(r'<summary>(.*?)</summary>', text, re.S)]


def members(text):
    return list(dict.fromkeys(re.findall(r'(?m)^\s*(?:public|internal|protected|private)\s+(?:(?:static|override|virtual|async|sealed|new|readonly|partial)\s+)*(?:[\w<>\[\].?,]+\s+)+(\w+)\s*\(', text)))


def purpose(item, by_type):
    path = item['path']; stem = Path(path).stem; body = item['body']
    if path.endswith('.cshtml'):
        return VIEWS[path.split('/Views/', 1)[1]]
    if path in PURPOSES:
        return PURPOSES[path]
    if '.Tests/' in path or path.startswith('tests-shared/'):
        if stem in TEST_PURPOSES:
            return TEST_PURPOSES[stem]
        for name, value in item['types']:
            if value and re.search('[\u4e00-\u9fff]', value) and '已修復的說明' not in value:
                return value
        target = re.sub(r'Tests?$', '', stem)
        if target in by_type:
            return '保護' + target + '的既有行為與相容性。受測元件的責任為：' + by_type[target]
        if '/AttributeOperations/' in path:
            return '以操作自有的SDK Entity驗證型別化屬性的缺值預設、讀取及新增／覆寫行為，防止屬性轉換改變舊API語意；決定性案例在下列測試入口逐一列出。'
        legacy = {
            'AttachmentServiceTests': '以假SDK驗證附件下載回傳及上傳建立annotation的委派契約，不使用真實附件或外部組織。',
            'ToolUtilityClassIntegrationTests': '驗證舊ToolUtilityClass把聯絡人查詢及屬性寫入交給對應服務，避免外觀入口另建資料操作實作。',
            'ToolUtilityFacadeIntegrationTests': '以假SDK驗證Facade的實體讀寫、附件、名單成員及LINE待送記錄委派，保護薄外觀整合邊界。',
            'EntityCrudServiceTests': '以假SDK確認實體建立、更新及刪除保留正式ID與呼叫內容，保護同步CRUD委派契約。',
            'EntityQueryServiceTests': '以假SDK驗證單筆及多筆查詢回傳，保護查詢服務與外部SDK之間的委派相容性。',
            'LineMessageServiceTests': '以假SDK確認LINE待送訊息被建立為正確CRM記錄；這不是LINE實際送達驗證。',
            'ListServiceTests': '以假SDK確認名單加入與移除成員沿既有建立／刪除路徑執行，保持成員關聯的相容形狀。',
            'TraceUtilityTests': '以假logger驗證追蹤層級過濾及ILogger委派，避免低於門檻的診斷仍產生輸出。',
        }
        if stem in legacy:
            return legacy[stem]
    if path.startswith('Line.Messaging/') and stem in LINE_TERMS:
        return '本檔' + LINE_TERMS[stem] + '。'
    # 優先採同名主要型別，其次保留同檔各型別的真實契約，不能取第一個屬性說明冒充整檔目的。
    docs = [(n,s) for n,s in item['types'] if s and re.search('[\u4e00-\u9fff]',s) and '已修復的說明' not in s]
    preferred = stem.split('.')[0]
    main = next((s for n,s in docs if n == preferred), None)
    if main:
        return main
    if docs:
        return '本檔整合下列相關契約：' + '；'.join(n + '：' + s for n,s in docs[:3])
    chinese = [s for s in summaries(body) if len(s) > 35 and re.search('[\u4e00-\u9fff]',s) and '已修復的說明' not in s]
    if chinese:
        return '本檔承接以下操作邊界：' + chinese[0]
    return None


def header(item, description, references):
    path = item['path']; body = item['body']; is_view = path.endswith('.cshtml')
    lines = ['檔案整體說明', '', '目的與定位：' + description, '']
    if is_view:
        models = re.findall(r'(?m)^\s*@model\s+([^\r\n]+)', body)
        actions = list(dict.fromkeys(re.findall(r'(?:asp-action="|\.\s*(?:LoadAction|InsertAction|UpdateAction|DeleteAction|Controller)\(")([\w.]+)', body)))
        handlers = list(dict.fromkeys(re.findall(r'\.On\w+\("([\w.]+)"\)', body)))
        lines += ['主要功能與流程：']
        if models: lines += ['畫面輸入採用 ' + '、'.join(models) + '，欄位語意須與控制器組裝的模型一致。']
        if actions: lines += ['本頁引用的控制器／動作包含 ' + '、'.join(actions[:14]) + '；實際網址及參數以頁內設定為準。']
        if handlers: lines += ['元件事件交給 ' + '、'.join(handlers[:10]) + '，維護時須一併核對宣告、載入順序與元件ID。']
        if not (models or actions or handlers): lines += ['透過下方Razor／HTML及資源宣告完成呈現；是否為全頁、partial或初始化設定依本檔宣告決定。']
        lines += ['', '使用邊界與維護：畫面僅呈現目前請求取得的資料；使用者輸入與元件傳回值仍須在伺服器驗證。',
                  '修改模型、元素ID、路由或callback時，須同步檢查引用此頁的父頁／控制器；非同步回應不能覆寫較新畫面狀態。',
                  '本段使用Razor註解，編譯後不會將維護說明或內部路徑傳到瀏覽器。']
    else:
        type_names = [n for n,s in item['types'] if len(n)>1]
        names = members(body)
        test = '.Tests/' in path or path.startswith('tests-shared/')
        lines += ['主要功能與流程：']
        if type_names: lines += ['本檔宣告 ' + '、'.join(type_names[:12]) + (' 等相關型別。' if len(type_names)>12 else '。')]
        own_summaries = [s for s in summaries(body) if s not in [v for _,v in item['types']] and s != description and 28 <= len(s) <= 420 and re.search('[\u4e00-\u9fff]',s) and '已修復的說明' not in s]
        for detail in list(dict.fromkeys(own_summaries))[:4]: lines += ['- ' + detail]
        if names:
            lines += [('測試案例入口' if test else '主要操作入口') + '包含 ' + '、'.join(names[:8]) + ('；其餘入口見下方成員契約。' if len(names)>8 else '。')]
        else:
            props = list(dict.fromkeys(re.findall(r'(?m)^\s*(?:public|internal)\s+(?:(?:static|readonly|const)\s+)*(?:[\w<>\[\].?,]+\s+)+(\w+)\s*(?:\{|=>|=)', body)))
            if props: lines += ['資料／選項欄位包含 ' + '、'.join(props[:16]) + '，名稱、型別與序列化規約須與消費端保持一致。']
            else: lines += ['功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。']
        lines += ['', '使用邊界與維護：']
        if test:
            lines += ['以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。',
                      '新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。']
        elif path.startswith('Line.Messaging/') and (path.split('/')[1] in ('Messages','LineObjects','Webhooks','Liff')):
            lines += ['協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。',
                      '可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。']
        elif '/Controllers/' in path:
            lines += ['控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。',
                      '外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。']
        elif '/Models/' in path or '/ViewModels/' in path:
            lines += ['模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。',
                      '修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。']
        else:
            lines += ['本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。',
                      '變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。']
        refs = references.get(path, [])
        if refs: lines += ['可沿 ' + '、'.join(refs[:5]) + ' 追查目前工作樹中的合作／使用位置。']
    # 固定換行，避免中文被textwrap錯誤拆成單字；長行按標點與合理字元預算拆開。
    wrapped = []
    for line in lines:
        while len(line)>112:
            stop = max(line.rfind(c, 45, 112) + 1 for c in '；。，、 ')
            if stop<45:
                # 檔案路徑與識別字必須完整；寧可保留長行，也不能把可追查名稱截成兩半。
                stops=[line.find(c,45) for c in '；。，、 ']
                stop=min((n+1 for n in stops if n>=0),default=len(line))
            wrapped.append(line[:stop]); line=line[stop:]
        wrapped.append(line)
    if is_view:
        return '@*\n' + '\n'.join('    '+s if s else '' for s in wrapped) + '\n*@\n'
    return '/*\n' + '\n'.join(' * '+s if s else ' *' for s in wrapped) + '\n */\n'


def apply_headers():
    values = inventory()
    # 跨檔合作線索來自完整來源中的識別字，目錄及歷史快照不參與推導。
    type_paths = {}
    by_type = {}
    for x in values:
        if '.Tests/' in x['path'] or x['path'].endswith('.cshtml'): continue
        for name, doc in x['types']:
            type_paths.setdefault(name, []).append(x['path'])
            if doc and re.search('[\u4e00-\u9fff]',doc): by_type[name]=doc
    references = {}
    for x in values:
        for symbol in set(re.findall(r'\b[A-Za-z_]\w*\b', x['body'])) & type_paths.keys():
            for target in type_paths[symbol]:
                if target != x['path']: references.setdefault(target, []).append(x['path'])
    for path in references:
        references[path] = sorted(set(references[path]), key=lambda p: ('.Tests/' not in p, p))
    missing = []
    for x in values:
        if x['path'].endswith('.g.cs'): continue
        if x['path'].startswith('SpeechMessage.Dynamics.Deployment'): continue
        if not purpose(x, by_type): missing.append(x['path'])
    if missing:
        print('MISSING PURPOSE:\n'+'\n'.join(missing)); return 1
    backup = Path(tempfile.mkdtemp(prefix='speechmessage-file-documentation-'))
    changes=[]
    for x in values:
        path=x['path']
        if path.endswith('.g.cs') or path.startswith('SpeechMessage.Dynamics.Deployment'): continue
        if x['body'].lstrip().startswith('/*\n * 檔案整體說明'): continue
        description=purpose(x, by_type)
        new=(header(x,description,references)+x['body']).replace('\n','\r\n').rstrip('\r\n')+'\r\n'
        raw=Path(path).read_bytes()
        if hashlib.sha256(raw).hexdigest()!=x['sha256']: raise ValueError('Concurrent edit; retry: '+path)
        destination=backup/path;destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(raw)
        Path(path).write_bytes(new.encode('utf-8'))
        changes.append(dict(path=path,backup=str(destination),before=x['sha256'],after=hashlib.sha256(new.encode()).hexdigest(),encoding=x['encoding']))
    (TASK/'changes.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Updated',len(changes),'source files; reversible pre-edit copies:',backup)
    return 0


if __name__ == '__main__':
    if '--apply' in sys.argv:
        raise SystemExit(apply_headers())
    values = inventory()
    (TASK / 'source-semantics.json').write_text(json.dumps([{k:v for k,v in x.items() if k not in ('text','body')} for x in values], ensure_ascii=False, indent=2), encoding='utf-8')
    for x in values:
        if not x['path'].endswith('.cs') or x['path'].endswith('.g.cs') or x['path'].startswith(('Line.Messaging/', 'SpeechMessageProducts.ChurchReport/Views/')):
            continue
        if not any(len(s) >= 35 and re.search('[\u4e00-\u9fff]', s) for _, s in x['types']):
            print(x['path'] + ' | ' + ' ; '.join(n + ': ' + s[:160] for n,s in x['types'][:3]))
