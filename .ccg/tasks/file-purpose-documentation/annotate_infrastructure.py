"""本次任務的腳本／專案檔導讀整理器；只補註解，保留原命令與設定值，讀取後比對雜湊避免並行覆寫。"""
from pathlib import Path
import sys
import re
import json
import hashlib
import tempfile
import subprocess
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path('tools/RepoQuality').resolve()))
from check_file_documentation import classification

PURPOSES = {
 'Start-ChurchReportDevelopment': '以Development環境啟動ChurchReport，限制測試組織及必要開發設定，將設定限制在目前程序及其子程序，不改正式部署組態。',
 'Start-ChurchReportWithTraceOptions': '以可選診斷開關啟動ChurchReport，將Dataverse、應用效能及舊ToolUtility追蹤設定交給子程序，維持使用者可調整的單一設定區。',
 'Start-ChurchReportWithTraceOptions(有Trace)': '提供啟用追蹤的ChurchReport啟動範例，把診斷選項限定於目前程序及子程序；此版本供本機調查，不應直接套用為正式效能基準。',
 'Start-ChurchReportWithTraceOptions(無Trace)': '提供停用追蹤的ChurchReport啟動範例，讓同一主機可在不寫診斷細節的設定下執行；例外告警目的地仍依產品設定決定。',
 'Migrate-ControllerSplit-Phase1': '為舊控制器拆分建立第一階段基礎檔案與目錄，保留舊產品結構遷移的操作入口；執行會修改來源，必須先核對輸入路徑及現有修改。',
 'Update-ViewRoutes': '依固定路由對照表把舊Home畫面連結改為拆分後的控制器路徑；此為來源遷移工具，不能拿來推論現有端點的授權或自動修正任意字串。',
 'Update-ViewRoutes-Batch2': '更新第二批高優先視圖的控制器路由，支援選定Views目錄及預覽選項；正式寫入前應核對替換候選，避免誤改未屬遷移範圍的頁面。',
 'Analyze-ChurchReportTraces': '合併分析ChurchReport的Dataverse、應用效能與舊ToolUtility追蹤，按既有事件識別關聯資料以定位慢請求；輸出是診斷彙總，不代表可公開原始業務本文。',
 'parse-perf-log': '從Trace.log的Perf紀錄彙整慢端點、疑似N+1與階段耗時缺口，提供本機效能排查摘要；它只解讀既有記錄，不啟動產品或量測新的工作負載。',
 'verify-release-noperf': '檢查Release組件沒有Debug剖析輸出標記，保護正式版本不因診斷程式碼產生額外輸出；此檢查不能取代實際執行效能或所有敏感資料審查。',
 'Start-CcgDualModelRun': '將分析／審查需求保存為UTF-8提示詞後交給雙模型自癒runner，依本次run身分讀取摘要，區分完整雙模型、指定單模型與額度降級。',
 'Invoke-CcgDualModelWithSelfHealing': '協調Gemini及Claude執行、健康檢查、有界修復重試與證據保存，將本機工具失敗和供應商額度／session限制分開回報，避免把單模型輸出誤認為雙模型完成。',
 'Test-CcgDualModelHealth': '檢查CCG雙後端所需wrapper、程序環境與可選後端探測，輸出結構化健康證據；探測不代表正式任務審查成功，也不應透過修改全域登入設定強行修復供應商額度。',
 'Test-CcgReviewSummary': '以合成後端結果驗證reviewMode、requestedBackends及dualModelComplete等摘要契約，防止exit code零或單邊成功掩蓋未完成審查。',
 'Test-CcgWeeklyQuota': '以合成供應商回應驗證Claude週額度與相關quota分類，避免外部額度被當成本機PATH或環境故障反覆修復。',
 'Test-P002Settings': '檢查SDK及打包相關P0-02設定，保護產品target、文件輸出及既有相依邊界；使用來源設定與建置契約作判定，不建立正式組織連線。',
 'Test-PluginAssembly': '以獨立.NET工具唯讀解析外掛PE中繼資料、target與簽章，避免將待部署組件載入PowerShell執行；檔案及reader由單次檢查操作擁有。',
 'Test-PluginAssembly.Tests': '以獨立暫存樣本驗證外掛組件檢查會接受正確簽章net462並拒絕net48等不符產物；測試金鑰為一次性合成資料，樣本不載入執行。',
 'Test-PluginBuild.Tests': '透過故障注入驗證一般建置不自動打包、顯式打包才檢查金鑰及產出簽章外掛；只修改測試程序環境，finally恢復並清除自有暫存金鑰。',
 'Test-Coverage.Tests': '以合成Cobertura驗證每模組每target的80%門檻、缺失／重複模組及損壞XML拒絕；斷言明確原因及結束碼，不把任意工具錯誤當負向成功。',
 'Test-SixLaborsCiLicense.Tests': '執行實際CI授權準備與清理區塊，以合成授權驗證內容完整、缺secret拒絕、下游故障清理及借用檔不被刪除；不讀正式授權內容。',
 'Test-ProgressPage': '驗證實作進度HTML的工作包狀態、計數與歷史證據區一致，防止視覺上已完成卻與工作包資料矛盾；使用本機頁面內容，不修改正式產品狀態。',
 'generate_vs2026_git_guide': '產生Visual Studio 2026的Git操作說明文件，將固定教學內容、樣式及輸出結構組合成Word；這是文件建置工具，不執行文件中描述的Git操作。',
 'generate_vs2026_ide_steps_doc': '建立Visual Studio 2026介面操作步驟的Word教學，集中排版與步驟內容；產生結果需依文件流程檢視，不能把成功寫檔視為版面已驗證。',
 'merge_vs2026_client_version_docs': '合併Visual Studio 2026用戶端版本相關教學文件，維持輸入章節與輸出排版；操作對象是文件副本，不修改Visual Studio或使用者的Git設定。',
 'generate_dataverse_code_conformance_diagram': '依既有架構資料產生Dataverse程式相依／規範圖，提供架構審查的可閱讀視圖；圖形產出不會更改SDK組態或部署組織。',
 'collection-load-coordinator': '為目前頁面的集合載入管理單一有效請求與generation，取消過期transport並丟棄晚到回應；dispose清除callback與DOM參照，避免舊日期資料覆寫新畫面。',
 'Test-CollectionLoadCoordinator': '以受控延遲與transport替身驗證載入協調器的取消、過期回應拒絕及dispose後資源歸零，保護日期切換與頁面離開的時序。',
 'ActionButtonCentering': '集中修正DevExtreme動作按鈕內容的置中顯示，讓圖示與文字在各種元件狀態保持一致；只處理DOM呈現，不更改提交或授權決策。',
 'Ajax': '提供舊頁面共用AJAX互動與結果處理入口，協調資料送出、載入提示及畫面更新；前端參數仍由伺服器驗證，不能把回呼當成跨頁資料owner。',
 'DataGridAjax': '協調一般資料表格的AJAX載入與提交互動，將元件資料交對應端點並處理成功／失敗結果；表格row key須維持正式身分，不靠列順序去重。',
 'DropDownBox': '提供下拉選擇器與內嵌表格／樹元件的同步事件，讓選取值與顯示文字保持一致；選取值只屬目前畫面，不能作伺服器授權依據。',
 'FeeDataGridAjax': '協調費用／課程繳費表格的AJAX異動與回應處理，維持學員與費用列身分；前端金額與付款旗標只是候選，最終保存由伺服器驗證。',
 'LineIdLoginView': '協調LINE識別登入頁的前端初始化、資料送出及登入結果導覽；LINE來源與回傳值仍須經伺服器綁定／認證，不能以前端旗標授予身分。',
 'LoadPanel': '提供共用載入面板的顯示與隱藏控制，讓資料操作可明確結束等待狀態；新增非同步入口須在成功、失敗及取消時都完成目前操作的面板收尾。',
 'Login': '處理登入表單的前端送出、載入狀態、提示及導覽，配合伺服器固定登入回應欄位；輸入驗證只改善互動，不取代認證。',
 'SelectDate': '處理回報頁日期選取事件與資料更新接點，確保畫面提交對應目前日期；晚到回應必須沿既有載入協調器判定，不能覆寫新日期狀態。',
 'TreeView': '處理樹狀選單的節點選取及相關元件同步，讓名單／小組階層的工作識別保持一致；樹節點顯示及可選性不代替端點的資料範圍驗證。',
 '_references': '保存舊Visual Studio JavaScript編輯器的reference提示，供編輯期型別／符號補全使用；它不是瀏覽器應執行的資源載入清單。',
}

MODULES = {
 'Line.Messaging': 'LINE Messaging／LIFF協定與雙目標SDK',
 'LineMessagingProcessor': '舊LINE功能外觀及相容轉接',
 'LineMessagingProcessor.Core': '跨目標最小LINE能力契約',
 'LineMessagingProcessor.Workflows': '產品中性的LINE訊息工作流程',
 'LineMessagingProcessor.RichMenus': '共用RichMenu目錄、佈建、指派與狀態協調',
 'LineMessagingProcessor.AspNetCore': 'LINE共用服務的ASP.NET Core組合入口',
 'SpeechMessage.Localization': '語系、文字、金額及教會時間的雙目標純核心',
 'SpeechMessage.Tenancy': '伺服器租戶解析與教會文字覆寫快照',
 'SpeechMessage.Payments': '中性付款契約及供應商協定核心',
 'SpeechMessage.Payments.Workflows': '產品中性訂單草稿及付款後流程',
 'SpeechMessage.Payments.AspNetCore': '付款核心的HTTP輸入輸出adapter',
 'SpeechMessage.Dynamics.Deployment': '單次授權且隔離的Dataverse部署CLI',
 'SpeechMessage.Dynamics.Plugins': 'Dataverse沙箱net462外掛與打包入口',
 'SpeechMessageProducts.ChurchReport': '既有教會管理MVC主機與產品整合',
 'ToolUtility': 'net10主機的CRM連線、生命週期及相容外觀',
 'ToolUtility.Core': '借用已授權SDK的雙目標純資料操作',
 'I18nCheck': '離線來源、翻譯資源及固定生成檔品質工具',
 'PluginPackaging': '離線外掛PE與簽章中繼資料檢查工具',
 'AnalyzerProbe': '編譯器規則故障注入樣本，預期被嚴格規則拒絕',
}


def native(text, extension):
    if extension in ('.ps1',): return '<#\n'+text+'\n#>\n'
    if extension in ('.py','.yml','.yaml'): return '\n'.join('# '+x if x else '#' for x in text.splitlines())+'\n'
    if extension=='.bat': return '\n'.join('@rem '+x for x in text.splitlines())+'\n'
    if extension in ('.csproj','.props','.targets','.resx','.config'): return '<!--\n'+text.replace('--','－')+'\n-->\n'
    return '/*\n'+text+'\n*/\n'


def main():
    paths=[p.decode() for p in subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z']).split(b'\0') if p]
    changes=[];companions={};missing=[]
    backup=Path(tempfile.mkdtemp(prefix='speechmessage-infrastructure-documentation-'))
    for name in sorted(set(paths)):
        path=Path(name);ext=path.suffix.lower();mode=classification(name)
        if mode=='companion':
            if name.endswith('.json'):
                if path.name.startswith('appsettings'):
                    p='宣告產品主機的部署組態，將資料來源、頻道、付款、診斷與功能選項交啟動流程讀取；環境檔覆寫順序由主機決定，不能把基底正式值當開發環境後備，敏感值不得加入文件或日誌。'
                elif path.name=='launchSettings.json':
                    p='提供本機IDE啟動profile、環境名稱及開發網址，影響開發除錯而非正式部署；profile不是使用者授權來源，新增環境變數須核對程序範圍及秘密不得進版控。'
                elif path.name=='global.json':
                    p='固定本工作樹使用的.NET SDK選擇及roll-forward政策，讓本機與CI以相同工具鏈建置；變更須核對所有產品target及離線工具，不代表專案本身已升級target。'
                elif name.startswith('d365/'):
                    p='宣告受版控的Dataverse註冊或結構部署需求，供離線驗證、plan及受控apply比較；它只描述metadata，不能保存業務記錄或秘密，也不能取代命令白名單、組織指紋及寫入授權。'
                elif '/baselines/' in name:
                    p='保存品質工具的既有棘輪基準或已核定例外身分，供本機及CI比較新增違規；不能以重新產生或提高數量掩蓋回歸，更新須附獨立審查及驗證證據。'
                elif '/Golden/' in name or '/golden/' in name or '/Fixtures/' in name or '/fixtures/' in name:
                    p='保存測試用的固定合成JSON樣本或黃金契約，供對應測試比對協定欄位、序列化及相容性；不能改成真實個資或憑證，更新必須說明外部契約為何允許變動。'
                elif name== 'docs/file-purpose-companions.json':
                    p='保存不允許原生註解的設定／方案檔之逐檔整體說明，以相對路徑為唯一身分供文件守門查詢；新增檔案須新增實際目的與邊界，不能放入設定值、秘密或藉此跳過原生程式檔頭。'
                else:
                    p='提供'+name+'所屬工具／測試的結構化輸入或固定設定，實際欄位契約由相鄰README與消費端決定；格式不接受註解，因此在此記錄用途，修改須同步核對解析、驗證及對應測試，不加入秘密。'
            elif ext in ('.sln','.slnf'):
                p='宣告方案專案及建置／CI篩選範圍，讓IDE與dotnet解析既定依賴圖；篩選方案不代表全方案已驗證，新增或移除專案須同步核對架構、套件、測試及覆蓋率守門。'
            else:
                p='宣告'+name+'所屬開發工具的受版控組態及執行政策，實際能力與權限由工具解析；本檔不接受額外非標準註解時以伴隨說明保存目的，變更須核對呼叫端且不可混入產品或使用者秘密。'
            companions[name]=p
            continue
        if mode!='inline' or ext in ('.cs','.cshtml') or name.endswith('check_file_documentation.py') or name.endswith('test_file_documentation.py'):continue
        raw=path.read_bytes()
        try:body=raw.decode('utf-8-sig')
        except UnicodeDecodeError:
            body=raw.decode('cp950')
            if body.encode('cp950')!=raw:raise ValueError('Lossy legacy decode: '+name)
        body=body.replace('\r\n','\n')
        if '檔案整體說明' in body[:1500]:continue
        description=PURPOSES.get(path.stem)
        details=[]
        if ext in ('.csproj','.props','.targets','.resx','.config'):
            tree=ET.fromstring(body)
            if ext=='.csproj':
                module=MODULES.get(path.stem, '自動化測試與契約驗證' if '.Tests' in path.stem else None)
                if not module:missing.append(name);continue
                description='定義'+module+'的MSBuild專案邊界，集中target、套件及專案參考，讓產品／測試依正確方向編譯。'
                targets=[e.text for e in tree.iter() if e.tag.split('}')[-1] in ('TargetFramework','TargetFrameworks') and e.text]
                if targets:details.append('宣告的產品／測試target為'+ '、'.join(targets)+'；條件評估後的實際輸出以MSBuild為準。')
                refs=[Path(e.attrib['Include'].replace('\\','/')).name for e in tree.iter() if e.tag.split('}')[-1]=='ProjectReference' and 'Include' in e.attrib]
                if refs:details.append('專案參考包含'+'、'.join(refs)+'；不能新增反向相依或將主機狀態引入純核心。')
            elif ext=='.resx':
                count=sum(1 for e in tree.iter() if e.tag.split('}')[-1]=='data')
                description='保存'+path.stem+'的正式文字資源，提供固定key、翻譯與具名參數給I18nCheck驗證及產生器；本檔是文字的單一來源，不在生成C#內手改文案。'
                details.append('目前宣告'+str(count)+'個資源列；鍵身分、參數集合、發布語系與長度受資源守門限制，重複鍵不能用覆寫隱藏。')
            elif ext=='.config':
                description='提供'+path.name+'的套件還原或應用框架設定，交對應工具按原生XML規約讀取；設定內容不是一般產品資料，註解不得包含私有來源憑證或簽章金鑰。'
            else:
                description={
                    'Directory.Build.props':'在專案評估前設定方案共用建置預設與target相關政策，讓各專案沿相同SDK、文件及相依邊界建置。',
                    'Directory.Build.targets':'在專案屬性已完成評估後套用嚴格分析器、禁用API、測試命名及中央套件等方案守門，避免早期讀不到opt-in造成假通過。',
                    'Directory.Packages.props':'集中管理方案NuGet版本，讓還原與各專案使用同一版號來源；專案不得另設內嵌Version或以NoWarn掩蓋降版。',
                    'PluginMerge.targets':'只在明確要求時將核定輸入合併為net462沙箱外掛，依程序環境取得簽章金鑰並保持一般建置不自動打包。',
                }.get(path.name)
        elif ext=='.ps1' and not description:
            match=re.search(r'\.SYNOPSIS\s*\n([^\n]+)',body)
            if match:description=match[1].strip()
            elif body.startswith('<#'):
                description=re.sub(r'\s+',' ',body[2:body.find('#>')]).strip()
        elif ext=='.css':
            names={'DonationPaymentView':'奉獻付款表單、多類別明細及付款方式','Gallery':'登入／註冊圖片與表單','LineIdLoginView':'LINE識別登入頁','LineLiffView':'LIFF登入頁','Login':'共用登入卡片與表單','MasterDetail':'主從表格明細','NewPerson':'新人表單','ReportChart':'週報統計圖','Scheduler':'行事曆及約會元件','Site':'全站佈局、共用元件及主題','Site - 複製':'全站樣式的舊副本，是否載入須由頁面資源引用判斷','SmallGroupReportController':'小組回報畫面','WeeklyReport':'週報內容及統計','arch-dedication':'奉獻畫面區塊','chinese-support':'中文字型、換行與字距','mini-app-safe-area':'內嵌App視窗的安全邊界與底部空間'}
            if path.stem in names:description='定義'+names[path.stem]+'的視覺規則，讓對應Razor／HTML在桌面與行動顯示一致；CSS只控制呈現，不負責資料載入或授權。'
        elif ext=='.bat':
            description='提供'+path.stem+'選定的ChurchReport發佈組態，透過dotnet publish及既有步驟產生部署輸出；執行前須核對腳本中固定路徑、Debug／Release及發佈模式，不因名稱含Production就自動取得正式部署授權。'
        elif ext in ('.yml','.yaml'):
            description='宣告Windows CI的套件還原、品質守門、建置、測試及選用簽章流程；授權檔與暫存金鑰由job擁有，失敗時仍須清理，過渡篩選範圍不能假稱完整方案通過。'
        if not description:missing.append(name);continue
        funcs=list(dict.fromkeys(re.findall(r'(?mi)^\s*(?:function\s+([\w-]+)|def\s+(\w+)\s*\(|(?:async\s+)?function\s+(\w+)\s*\()',body)))
        symbols=[next(y for y in x if y) for x in funcs]
        if symbols:details.append('操作入口包含'+'、'.join(symbols[:10])+'；呼叫參數與輸出形狀以各入口宣告為準。')
        text='檔案整體說明\n\n目的與定位：'+description+'\n\n主要功能與流程：\n'+('\n'.join(details) if details else '依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。')+'\n\n使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。\n涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。'
        comment=native(text,ext)
        prefix=''
        if body.startswith('<?xml'):
            stop=body.index('?>')+2;prefix=body[:stop]+'\n';body=body[stop:].lstrip('\n')
        elif body.startswith('#!'):
            prefix,_,body=body.partition('\n');prefix+='\n'
        new=(prefix+comment+body).replace('\n','\r\n').rstrip('\r\n')+'\r\n'
        if path.read_bytes()!=raw:raise ValueError('Concurrent edit: '+name)
        dest=backup/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(raw)
        path.write_bytes(new.encode())
        changes.append({'path':name,'backup':str(dest),'before':hashlib.sha256(raw).hexdigest(),'after':hashlib.sha256(new.encode()).hexdigest()})
    companions['docs/file-purpose-companions.json']='保存不允許原生註解的設定／方案檔之逐檔整體說明，以相對路徑為唯一身分供文件守門查詢；新增檔案須新增實際目的與邊界，不能放入設定值、秘密或藉此跳過原生程式檔頭。'
    Path('docs/file-purpose-companions.json').write_bytes((json.dumps(companions,ensure_ascii=False,indent=2)+'\n').replace('\n','\r\n').encode())
    Path('.ccg/tasks/file-purpose-documentation/infrastructure-changes.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Updated',len(changes),'infrastructure files; companions',len(companions))
    if missing:print('MISSING:\n'+'\n'.join(missing))


if __name__=='__main__':main()
