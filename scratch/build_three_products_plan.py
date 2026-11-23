from pathlib import Path
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.enum.style import WD_STYLE_TYPE
from docx.enum.text import WD_BREAK
from docx.enum.section import WD_ORIENT
from datetime import datetime

ROOT=Path(r"D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.12.GoodShepherd2.0.Worktree")
OUT=ROOT/'docs'/'planning'/'three-products'
OUT.mkdir(parents=True,exist_ok=True)
DOCX=OUT/'三大產品共用架構與開發路線實施計畫.docx'

def set_cell_shading(cell, fill):
    tcPr=cell._tc.get_or_add_tcPr(); shd=tcPr.find(qn('w:shd'))
    if shd is None: shd=OxmlElement('w:shd'); tcPr.append(shd)
    shd.set(qn('w:fill'),fill)
def set_cell_border(cell, color='D9D9D9', size='6'):
    tc=cell._tc; tcPr=tc.get_or_add_tcPr(); borders=tcPr.first_child_found_in('w:tcBorders')
    if borders is None: borders=OxmlElement('w:tcBorders'); tcPr.append(borders)
    for edge in ('top','left','bottom','right','insideH','insideV'):
        tag='w:'+edge; el=borders.find(qn(tag))
        if el is None: el=OxmlElement(tag); borders.append(el)
        el.set(qn('w:val'),'single'); el.set(qn('w:sz'),size); el.set(qn('w:space'),'0'); el.set(qn('w:color'),color)
def set_cell_margin(cell, top=100, start=120, bottom=100, end=120):
    tc=cell._tc; tcPr=tc.get_or_add_tcPr(); mar=tcPr.first_child_found_in('w:tcMar')
    if mar is None: mar=OxmlElement('w:tcMar'); tcPr.append(mar)
    for m,v in [('top',top),('start',start),('bottom',bottom),('end',end)]:
        e=mar.find(qn('w:'+m))
        if e is None: e=OxmlElement('w:'+m); mar.append(e)
        e.set(qn('w:w'),str(v)); e.set(qn('w:type'),'dxa')
def set_repeat_table_header(row):
    trPr=row._tr.get_or_add_trPr(); el=OxmlElement('w:tblHeader'); el.set(qn('w:val'),'true'); trPr.append(el)
def add_table(doc, headers, rows, widths=None):
    t=doc.add_table(rows=1, cols=len(headers)); t.alignment=WD_TABLE_ALIGNMENT.CENTER; t.style='Table Grid'
    for i,h in enumerate(headers):
        c=t.rows[0].cells[i]; c.text=h; set_cell_shading(c,'1F4E79'); set_cell_margin(c); c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
        for p in c.paragraphs:
            p.alignment=WD_ALIGN_PARAGRAPH.CENTER
            for r in p.runs: r.bold=True; r.font.color.rgb=RGBColor(255,255,255); r.font.size=Pt(9)
        set_cell_border(c)
    set_repeat_table_header(t.rows[0])
    for ri,row in enumerate(rows):
        cells=t.add_row().cells
        for i,val in enumerate(row):
            cells[i].text=str(val); set_cell_margin(cells[i]); cells[i].vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER; set_cell_border(cells[i])
            if ri%2==1: set_cell_shading(cells[i],'EAF2F8')
            for p in cells[i].paragraphs:
                p.paragraph_format.space_after=Pt(2)
                for r in p.runs: r.font.size=Pt(9)
    if widths:
        for row in t.rows:
            for i,w in enumerate(widths): row.cells[i].width=Inches(w)
    doc.add_paragraph().paragraph_format.space_after=Pt(2)
    return t

def bullet(doc,text,level=0):
    p=doc.add_paragraph(style='List Bullet' if level==0 else 'List Bullet 2'); p.add_run(text); return p

def number(doc,text):
    p=doc.add_paragraph(style='List Number'); p.add_run(text); return p

def heading(doc,text,level=1):
    p=doc.add_heading(text,level); p.paragraph_format.keep_with_next=True; return p

def para(doc,text,boldlead=None):
    p=doc.add_paragraph(); p.paragraph_format.space_after=Pt(6); p.paragraph_format.line_spacing=1.15
    if boldlead and text.startswith(boldlead):
        p.add_run(boldlead).bold=True; p.add_run(text[len(boldlead):])
    else: p.add_run(text)
    return p

doc=Document(); sec=doc.sections[0]; sec.top_margin=Inches(.7); sec.bottom_margin=Inches(.65); sec.left_margin=Inches(.75); sec.right_margin=Inches(.75)
styles=doc.styles
for name in ['Normal','Body Text']:
    st=styles[name]; st.font.name='Aptos'; st._element.rPr.rFonts.set(qn('w:eastAsia'),'Microsoft JhengHei'); st.font.size=Pt(10.5)
for name,size,color in [('Title',25,'000000'),('Heading 1',17,'000000'),('Heading 2',13,'1F4E79'),('Heading 3',11,'365F91')]:
    st=styles[name]; st.font.name='Aptos Display'; st._element.rPr.rFonts.set(qn('w:eastAsia'),'Microsoft JhengHei'); st.font.size=Pt(size); st.font.bold=True; st.font.color.rgb=RGBColor.from_string(color)
# title
p=doc.add_paragraph(style='Title'); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; p.add_run('三大產品共用架構與開發路線實施計畫')
p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; r=p.add_run('好牧人 2.0、建設公司維護系統、協會管理系統'); r.bold=True; r.font.size=Pt(14); r.font.color.rgb=RGBColor(31,78,121)
p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; p.add_run('規劃版本 1.0｜2026 年 11 月 23 日｜Asia/Taipei (UTC+08:00)').italic=True
para(doc,'本計畫書的目的，是把三大產品的需求、既有程式基礎、LINE Channel 與 Webhook 邊界、Pilot Run、功能開發順序及驗證閘門，整理成可以逐工作包執行、逐功能驗證、逐階段放行的正式依據。第一優先是好牧人 2.0；建設公司與協會系統在共用能力被證明可重用後，依序啟動。')
heading(doc,'一 執行摘要',1)
para(doc,'本計畫採用「共用平台、產品隔離、垂直切片、逐功能放行」四項原則。共用的是經驗證的程式能力；不得共用未分區的 Token、租戶狀態、快取、背景工作、事件佇列或可變資料。每個功能必須先完成需求與失敗流程，再完成自動化測試、測試環境驗證、受控實機驗證與人工審核，通過後才進入下一個功能。')
add_table(doc,['決策','本計畫的固定方向','理由'],[
('產品順序','好牧人 2.0 → 建設公司 → 協會管理系統','好牧人最適合作為共用平台的第一個垂直切片，且已有較完整需求資料。'),
('第一天使用者','約 10 間全新試用教會進行 Pilot Run','可同時驗證首次導入、租戶隔離、LINE 綁定、通知與業務流程。'),
('Webhook','三個產品各自獨立','避免產品間事件、Token、佇列、告警與故障互相污染。'),
('Channel','約 50 個官方帳號，每個約 20 間教會；配額與分配須可審計','1000 個圖文選單限制會影響模板、版本與回收設計。'),
('正式組織','本階段不連線正式組織','規劃與驗證必須在沙盒、測試 Channel 或明確核准環境完成。'),
], [1.3,2.5,2.8])
heading(doc,'二 已知前提與證據分級',1)
add_table(doc,['分級','意義','本計畫中的例子'],[
('已確認','使用者明確提供，或檔案／程式可直接觀察。','三產品 Webhook 獨立；約 50 個 Channel；Pilot 約 10 間教會。'),
('待驗證','已有文件、程式或設計方向，但尚未完成執行、實機或契約驗證。','現有 Channel registry 是否完整；國稅局 API 初步程式是否可在沙盒成功交易。'),
('建議','依隔離、安全、效能、可維護性提出的設計選擇。','先做教會／使用者／權限，再做服事縱向流程。'),
('本階段不做','需要另一工作包、明確授權或正式環境作業。','正式組織連線、正式 Channel 切換、CRM 2011 生產遷移。'),
], [1.3,2.7,2.6])
heading(doc,'三 現有基礎盤點結果',1)
para(doc,'目前分支不是空白基底。從方案與目錄盤點，可確認存在下列候選共用能力；但「專案存在」不等於「能力已完成或已通過生產驗證」，後續必須以介面、DI、測試、設定與實際流程逐項確認。')
add_table(doc,['候選能力','目前可觀察到的專案或路徑','使用判定'],[
('LINE API 與訊息處理','Line.Messaging、LineMessagingProcessor 及其 Core、AspNetCore、Workflows、RichMenus 子專案','候選共用基礎；需確認 Channel、事件、重試與資源生命週期。'),
('LINE Webhook 產品入口','SpeechMessageProducts.LineWebhook','好牧人相關入口候選；需確認是否已支援多產品獨立邊界。'),
('好牧人領域核心','SpeechMessageProducts.GoodShepherd.Core 及測試專案','已有行事曆、出席等領域檔案；需依 SRS 與測試逐項對照。'),
('租戶與 Webhook Channel','SpeechMessage.Tenancy','已有 Channel resolver、secret resolver、文字覆寫快取等檔案；需確認完整隔離鍵與快取生命週期。'),
('Hosting 與錯誤處理','SpeechMessage.Hosting','已有例外診斷、通知、Logging、FailureSummaryWorker 等候選共用能力；需驗證有界佇列、停止與 Dispose。'),
('付款與 Dynamics／Dataverse','SpeechMessage.Payments、PowerPlatform.Dataverse.Client、SpeechMessage.Dynamics.*','可作為後續整合基礎；目前不可直接視為 CRM 或金流已上線。'),
], [1.5,3.0,2.1])
para(doc,'盤點結論：第一個工程工作包不是「先做所有畫面」，而是建立可測量的基線矩陣：每一項能力標記為已通過、部分完成、只有骨架、未驗證或缺少測試。任何文件中出現「已具備」的能力，都應能指向程式、測試、設定與執行證據。')
heading(doc,'四 三大產品的共用與獨立邊界',1)
add_table(doc,['層次','三產品共用內容','不可共用或必須分區的內容'],[
('平台運行層','Hosting、環境設定模式、Health Check、Logging、Correlation ID、錯誤處理、部署檢查、資源生命週期。','產品開關、環境密鑰、正式端點、告警路由與可變執行狀態。'),
('身份與隔離','User、Role、Membership、Authentication Epoch、Scope 驗證模式。','實際租戶資料、權限授權、Session、Cookie、Profile、產品內組織關係。'),
('LINE 整合','Webhook 驗證、事件解析、Reply／Push 抽象、冪等、重試、Rich Menu API 封裝。','Webhook endpoint、Channel secret／token、事件佇列、Rich Menu 綁定、使用者綁定、告警與資料庫。'),
('工作流程','狀態機、指派、通知、逾時、重試、稽核的泛型機制。','服事、報修、協會案件的業務語意、欄位、權限與狀態規則。'),
('資料與快取','Repository／adapter 介面、資料驗證、穩定 ID 驗證、快取政策範本。','產品資料、租戶資料、使用者特定結果、未分區的 Singleton、跨產品快取鍵。'),
], [1.2,3.0,2.4])
heading(doc,'五 LINE Channel 與 Webhook 運營設計',1)
para(doc,'已知的 Channel 與圖文選單配額會把 LINE 設計從「呼叫 API」提升為「資源治理」。正式分配前必須建立 Channel Registry，至少記錄產品、環境、Channel、可服務租戶、目前 Rich Menu 數量、版本、綁定狀態、停用狀態與最後稽核時間。')
add_table(doc,['治理項目','必須實作的控制','放行條件'],[
('產品邊界','每個產品獨立 Webhook 路由、設定、事件入口與錯誤處理。','產品 A 的事件不會進入產品 B 的 handler、佇列或資料範圍。'),
('租戶辨識','以伺服器端 registry、簽章驗證、事件來源與授權範圍共同決定租戶。','前端或 URL 提供的 tenant id 不得單獨成為權威。'),
('Token 管理','Token 只由受控設定／秘密來源解析；不寫入 log、Session、錯誤訊息或長期快取。','輪替、失效與解析失敗都有明確失敗閉鎖行為。'),
('Rich Menu 配額','模板、版本、綁定、回收與別名管理；更新採可追蹤版本。','不因重試或每次部署無限建立資源；達配額時 fail closed。'),
('事件冪等','使用產品、Channel、事件 ID 與必要範圍組成唯一事件鍵。','重送只產生一次業務效果，不重複通知或建立資料。'),
('排程與佇列','每個產品與 Channel 有界容量、逾時、取消、drain、Dispose。','停止服務後不再接受新工作，既有工作完成或明確失敗。'),
], [1.2,3.3,2.1])
heading(doc,'六 好牧人 2.0 第一天 Pilot Run',1)
para(doc,'第一天的主要使用者不是一般瀏覽者，而是十間全新試用教會及其管理員、同工、會員與產品營運人員。第一個成功標準不是畫面很多，而是每間教會都能完成一條不跨租戶、可稽核、可重試的完整縱向流程。')
add_table(doc,['角色','第一件事','接續行為','關鍵驗證'],[
('產品營運人員','建立或確認十間 Pilot 教會及其 Channel／環境分配。','建立管理員、測試帳號、資料清理與觀測面板。','分配可追溯、不可跨教會；不連正式組織。'),
('教會管理員','登入並完成教會基本設定。','建立或匯入第一批會員，確認角色與權限。','管理員只能看到自己的教會。'),
('同工','建立第一個服事或聚會事件。','設定時間、崗位、人員，送出通知。','重複提交不重複建立；事件有穩定 ID。'),
('會員','完成 LINE 綁定並查看自己的安排。','接受／拒絕服事，查看更新通知。','會員不能讀取其他會員或教會資料。'),
('觀測人員','查看 correlation、event、租戶與資源指標。','處理失敗、重試、回滾與清理。','正常取消不告警；真正失敗可診斷且不洩漏機密。'),
], [1.2,2.0,2.0,1.4])
heading(doc,'七 功能開發閘門',1)
para(doc,'建議的節奏是「一個功能一個閘門」，但每個功能的完成不能只等於程式碼合併。下列八步驟是每個工作包固定的 Definition of Done。')
for x in ['功能定義：角色、觸發條件、成功結果、明確禁止事項。','流程設計：正常、失敗、取消、逾時、重試、權限與資料隔離流程。','測試先行：單元、整合、A/B 隔離、重複提交、資源釋放與錯誤注入。','實作：先做一條可執行的垂直切片，不先建立未使用的大抽象。','本機與測試環境驗證：建置、測試、文件、設定、觀測與資料清理。','受控實機驗證：先單教會，再兩教會 A/B，最後擴大到十間 Pilot。','審核：需求、資安、隔離、效能、資源生命週期、告警與回滾。','放行：保存證據、記錄限制、標示可用狀態，通過後才進入下一功能。']:
    number(doc,x)
add_table(doc,['狀態','代表意義','不可宣稱的內容'],[
('程式完成','程式碼已實作並可編譯。','不能宣稱已通過實機或正式可用。'),('自動化測試通過','指定測試在受控環境通過。','不能代表真實 LINE、CRM 或外部服務已通過。'),('測試環境驗證','使用測試 Channel／沙盒完成端到端驗證。','不能代表正式組織可直接切換。'),('Pilot 通過','指定 Pilot 教會與場景完成驗收，證據完整。','不能代表所有產品與所有規模都已驗證。'),('正式可用','完成正式環境核准、部署、監控、回滾與營運交接。','需要獨立授權，不由本規劃自動授予。'),
], [1.6,2.8,2.2])
heading(doc,'八 好牧人 2.0 建議開發順序',1)
add_table(doc,['階段','功能工作包','依賴','主要驗證'],[
('GS-00','Pilot 基礎、環境、觀測、資料清理','Hosting、Tenancy、Webhook 基礎盤點','十間教會 fixture、錯誤追蹤、資源 drain。'),
('GS-01','教會、使用者、角色與權限','GS-00','A/B 教會資料隔離、權限拒絕與稽核。'),
('GS-02','LINE 身分綁定','GS-01、Channel registry','綁定、解除、過期、跨 Channel／教會邊界。'),
('GS-03','會員建立、匯入與查詢','GS-01','同名不同 ID 保留；重複穩定 ID fail closed。'),
('GS-04','第一條服事縱向流程','GS-02、GS-03','建立、指派、通知、接受／拒絕、結果查詢。'),
('GS-05','行事曆與服事結果','GS-04','重複規則、衝突、取消、修改通知、歷史紀錄。'),
('GS-06','課程與報名','GS-01、GS-02','名額、候補、取消、通知、資料隔離。'),
('GS-07','出席與點名','GS-06 或聚會事件','補登、修改權限、統計一致性。'),
('GS-08','奉獻／捐款與敏感資料','權限、稽核、金流契約','資料最小化、權限、稽核、重試與對帳。'),
('GS-09','報表與營運面板','前述交易資料穩定','報表不重複、穩定 ID、快照完整發布。'),
], [1.0,2.4,1.7,1.5])
heading(doc,'九 建設公司維護系統路線',1)
para(doc,'建設公司目前是 CRM 2011，全坤建設程式碼位於指定外部目錄。第一階段只做唯讀盤點與契約整理；不得把現有程式可執行誤認為可直接上線。')
add_table(doc,['階段','內容','必須先完成的證據'],[
('CON-00','CRM 2011 資料與流程盤點','實體、欄位、狀態、權限、整合端點與測試資料清冊。'),('CON-01','LINE 報修入口與案件建立','報修人、社區／建案、房號、案件 ID、附件與通知契約。'),('CON-02','案件指派與狀態流轉','住戶、客服、廠商、工班角色隔離與狀態機測試。'),('CON-03','預約、照片、完工確認','附件生命週期、存取權限、逾時與重試。'),('CON-04','CRM 2011 受控同步','先沙盒／測試資料，再小批量雙向或單向同步；可停用與補償。'),('CON-05','電子發票契約驗證','國稅局 API 只在沙盒或核准測試環境驗證，先完成簽章、重試、對帳與錯誤分類。'),
], [1.0,2.8,2.8])
heading(doc,'十 協會管理系統路線',1)
add_table(doc,['階段','內容','重用與獨立原則'],[
('ASSOC-00','組織、會員與角色','重用身份／租戶模式；不重用教會領域資料表。'),('ASSOC-01','課程、活動、報名與候補','重用通用事件、名額、通知；協會擁有自己的資格規則。'),('ASSOC-02','繳費、出席、證書或資格','重用付款與稽核抽象；流程與資料保留政策獨立。'),('ASSOC-03','顧問案件／行政工單','重用狀態機與通知機制；案件語意、角色與 SLA 由協會領域擁有。'),('ASSOC-04','報表、會員服務與營運','依穩定 ID 發布；不以姓名、電話或列表位置消除重複。'),
], [1.0,2.8,2.8])
heading(doc,'十一 測試、實機驗證與停止條件',1)
add_table(doc,['測試面向','必要案例','通過標準'],[
('隔離','教會 A／B、產品 A／B、Channel A／B、角色 A／B 同時操作。','任何回應、快取、佇列、Log、錯誤或附件不得越界。'),('重複與併行','同事件重送、同案件併行建立、同名不同 ID。','穩定 ID 唯一；合法不同 ID 保留；重複 ID 不靜默丟棄。'),('資源生命週期','取消、逾時、重試、停止、故障注入、佇列 drain。','Task、Timer、Stream、連線、註冊與佇列回到宣告基線。'),('通知與錯誤','真正失敗、正常取消、client disconnect、LINE 發送失敗。','原始例外保留；錯誤通知不遞迴、不洩漏機密。'),('效能','十間教會併行 Pilot、突發 Webhook、Rich Menu 配額。','延遲、配置率、錯誤率、記憶體與佇列深度在門檻內。'),
], [1.3,3.0,2.3])
para(doc,'立即停止放行的條件包括：跨教會或跨產品資料洩漏、重複穩定 ID 被靜默隱藏、非預期資源未釋放、Token／Cookie／密碼進入 Log、外部正式端點被誤呼叫、重要失敗無法告警或無法回滾。')
heading(doc,'十二 後續工作包與決策清單',1)
for x in ['完成目前分支能力基線矩陣：每個候選共用模組列出程式、測試、設定、已知缺口與證據。','建立十間 Pilot 教會、測試 Channel、測試使用者、資料清理與觀測方案；不使用正式組織。','完成 Channel Registry 與 Rich Menu 配額／版本／回收設計。','核准好牧人 GS-00 與 GS-01 的 PRD、設計、測試案例與放行門檻。','確認 50 個 Channel 的實際產品、環境與教會配置是否為容量假設或正式規則。','確認 CRM 2011、國稅局電子發票與其他外部系統的沙盒、測試帳號與資料使用界線。','每一個工作包完成後，更新進度頁、保留驗證證據，並由使用者核准才進下一工作包。']:
    bullet(doc,x)
heading(doc,'附錄 A Pilot Run 最小驗收清單',1)
add_table(doc,['編號','驗收項目','證據','結果'],[(f'P-{i:02d}',x,'測試紀錄、correlation id、畫面或匯出報表','□ 通過  □ 不通過') for i,x in enumerate([
'十間教會均有獨立測試範圍與管理員','管理員登入後只能看到所屬教會','會員建立／匯入不合併合法同名資料','LINE 綁定與解除綁定可追蹤','服事建立、指派與通知完成','會員接受／拒絕可回寫結果','同事件重送不產生重複業務效果','教會 A 不能查詢教會 B 的資料','取消、逾時與停止後資源可回收','Pilot 失敗時可停用、清理與回滾'
],1)])
heading(doc,'附錄 B 文件使用方式',1)
para(doc,'Word 計畫書適合作為正式決策、工作包拆分與驗收依據；同一資料夾中的 HTML 圖形教學適合用來理解架構、向團隊說明、在規劃會議中逐步討論。兩份文件均不是正式上線核准，也不取代每個工作包自己的需求、設計、測試與部署紀錄。')
# footer
for s in doc.sections:
    f=s.footer.paragraphs[0]; f.alignment=WD_ALIGN_PARAGRAPH.CENTER; f.add_run('三大產品共用架構與開發路線實施計畫｜2026-11-23 Asia/Taipei (UTC+08:00)').font.size=Pt(8)
# comparison additions\nheading(doc,'十三 Claude 計畫比較後的整合修正版',1)\npara(doc,'本章把 Claude 版本中有價值的執行細節納入本計畫，同時把快照資料、產品決策與尚未驗證的技術假設降級為待確認項目。這樣可以保留可實施性，又不把推估誤當成既定事實。')\nheading(doc,'13.1 建議保留的強化設計',2)\nfor x in ['以薄地基先打通同一 Channel 兩間測試教會的最薄通路，再擴大至十間 Pilot。','建立已有、要改造、全新共用元件三類清單，並標示首次建立的產品階段。','以產品處理器插槽與架構測試修正共用層反向參考 GoodShepherd.Core 的風險。','建立通用排程、權限矩陣與修改紀錄、圖片／附件代管、審核卡片、時段預約、報名收費狀態機、名單推播、匯入、統計彙總與搬移工具的候選清單。','採用 L0 單元、L1 整合、L2 本機主機、L3 測試環境實機、L4 Pilot 的驗收層級。','每個功能原則上設定五個工作天時間盒；超過就拆小或明確移入後續清單。','每個階段結束重新彩排完整流程，以實際吞吐量重估後續時程。']:\n    bullet(doc,x)\nheading(doc,'13.2 必須降級為待確認的內容',2)\nadd_table(doc,['Claude 版本的主張','本計畫處理方式','在什麼條件下才能核定'],[('42 個專案、特定 HEAD、13／6／14 數量','視為當時快照與估算，不作目前事實。','以目前工作樹、方案清單、測試與程式盤點重新確認。'),('每產品獨立 D365 組織、IIS 與服務帳號','列為隔離建議，不宣稱已決定。','使用者確認部署拓撲、成本與維運責任。'),('同一 Provider 可共用 LINE userId','列為平台假設，先建立測試案例。','以官方文件與測試 Channel 實測確認。'),('每教會 49 個 Rich Menu、每 Channel 保留 20 個','列為配額策略候選，不與 1,000 個限制混用。','核對實際 API 配額、UI 版本、模板數與租戶需求。'),('綁卡後配發、保留期 30 天、D365 鎖定','列為產品決策候選。','使用者核准，且完成金流／Dataverse 交易與補償測試。'),('2027 年各階段週次','只作估算，不作承諾。','地基完成後依實際速度出新版時程。'),('Pilot 前 35 至 37 項','拆成核心 Pilot、可選 Pilot、Pilot 後。','使用者核定最小成功流程與財務功能 go/no-go。'),], [2.2,2.3,2.3])\nheading(doc,'13.3 Pilot 範圍分層',2)\nadd_table(doc,['層級','納入內容','放行原則'],[('核心 Pilot','教會開通、管理員、會員／LINE 綁定、基本通知、第一條服事流程、結果查詢、A/B 隔離。','核心流程全部通過才可開始 Pilot。'),('擴充 Pilot','行事曆、課程、出席、場地、差勤與關懷等已核定功能。','可分批開啟；不影響核心流程回滾。'),('高風險功能','奉獻、月結扣款、電子發票、外部資料搬移。','各自獨立 go/no-go；未通過不得阻塞核心 Pilot，也不得被標示為正式可用。'),('Pilot 後','多語、ICS、農曆生日、白牌、1.5 搬遷等。','另立工作包，不在 Pilot 範圍內。')], [1.4,3.2,2.2])\nheading(doc,'十四 最終採用的執行循環',1)\nadd_table(doc,['步驟','負責者','產出'],[('1 一頁規格','Codex 撰寫、使用者確認','範圍、不做什麼、驗收條件、依賴。'),('2 共用判斷','Codex 提案、使用者決定','共用層或產品層的邊界與理由。'),('3 實作與自動測試','Codex','程式、測試、文件、編碼與資源生命週期證據。'),('4 本機整合','Codex','本機主機、假 LINE、測試組織的 L2 結果。'),('5 測試環境實機','Codex 部署、使用者手機驗證','L3 腳本、截圖、API／資料狀態核對。'),('6 一頁驗收報告','Codex','每條條件通過／不通過、限制、回滾與後續項目。'),('7 使用者審核','使用者','通過才進下一功能；不通過回到修正。')], [1.4,2.0,3.4])\npara(doc,'最後判斷：Claude 計畫在執行節奏、共用引擎清單、依賴方向、里程碑與時間盒方面比原規劃更具體；原規劃在資料隔離、重複資料、資源生命週期、事實分級與正式組織限制方面較完整。本整合版採取兩者交集，並把未經核准的數字與平台假設全部標示為待確認。')\ndoc.save(DOCX)
print(DOCX)

