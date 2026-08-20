from __future__ import annotations

"""產生產品 A Dataverse 架構符合性證明圖。

這不是執行時程式；它把已由原始碼與測試確認的生命週期邊界，和仍屬
過渡狀態的項目，繪成一張可供架構審查使用的高解析度 PNG。圖面特別把
「產品 A 已部署」與「產品 B/C/D 只是後續擴充」分開，避免把目標架構
誤讀成四個產品都已落地。
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT / "dataverse-architecture-code-conformance-v1.png"
WIDTH, HEIGHT = 3000, 4400
FONT_PATH = r"C:\Windows\Fonts\NotoSansTC-VF.ttf"


BG = "#F4F7FB"
INK = "#14233A"
MUTED = "#56657A"
LINE = "#AEBBCA"
BLUE = "#2264D1"
BLUE_SOFT = "#E7F0FF"
GREEN = "#16824B"
GREEN_SOFT = "#E8F7EF"
TEAL = "#0E8F8A"
TEAL_SOFT = "#E7F8F7"
ORANGE = "#C25A0A"
ORANGE_SOFT = "#FFF1E7"
RED = "#B42318"
RED_SOFT = "#FDECEB"
PURPLE = "#6B35C8"
PURPLE_SOFT = "#F1EBFF"
GRAY = "#718096"
GRAY_SOFT = "#EEF2F6"
WHITE = "#FFFFFF"


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    # NotoSansTC 的 variable font 在 Windows/Pillow 上可直接使用；粗體以較大的
    # 字級與色彩層次表達，避免不同執行環境缺少另一個字型檔而失敗。
    return ImageFont.truetype(FONT_PATH, size=size)


def text_width(draw: ImageDraw.ImageDraw, text: str, fnt: ImageFont.FreeTypeFont) -> int:
    return draw.textbbox((0, 0), text, font=fnt)[2]


def wrap_text(draw: ImageDraw.ImageDraw, text: str, fnt: ImageFont.FreeTypeFont, width: int) -> list[str]:
    """以像素寬度換行，中文按字元切分、英文 token 盡量保留。"""
    lines: list[str] = []
    for paragraph in text.split("\n"):
        if not paragraph:
            lines.append("")
            continue
        current = ""
        for ch in paragraph:
            candidate = current + ch
            if current and text_width(draw, candidate, fnt) > width:
                lines.append(current)
                current = ch
            else:
                current = candidate
        if current:
            lines.append(current)
    return lines


def draw_wrapped(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int],
    text: str,
    fnt: ImageFont.FreeTypeFont,
    fill: str = INK,
    max_width: int = 1000,
    line_gap: int = 8,
    anchor: str = "la",
) -> int:
    x, y = xy
    lines = wrap_text(draw, text, fnt, max_width)
    bbox = draw.textbbox((x, y), "字", font=fnt, anchor=anchor)
    line_height = bbox[3] - bbox[1] + line_gap
    for line in lines:
        draw.text((x, y), line, font=fnt, fill=fill, anchor=anchor)
        y += line_height
    return y


def rounded(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], fill: str, outline: str | None = None, width: int = 2, radius: int = 22) -> None:
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def arrow(draw: ImageDraw.ImageDraw, start: tuple[int, int], end: tuple[int, int], fill: str = BLUE, width: int = 7) -> None:
    x1, y1 = start
    x2, y2 = end
    draw.line((x1, y1, x2, y2 - 16), fill=fill, width=width)
    draw.polygon([(x2 - 15, y2 - 18), (x2 + 15, y2 - 18), (x2, y2 + 12)], fill=fill)


def bullet_list(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    items: list[str],
    width: int,
    fill: str = INK,
    bullet_fill: str = GREEN,
    size: int = 23,
    gap: int = 14,
) -> int:
    fnt = font(size)
    for item in items:
        draw.ellipse((x, y + 10, x + 12, y + 22), fill=bullet_fill)
        y = draw_wrapped(draw, (x + 28, y), item, fnt, fill=fill, max_width=width - 28, line_gap=5)
        y += gap
    return y


def flow_box(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], title: str, body: str, evidence: str, color: str, fill: str) -> None:
    x1, y1, x2, y2 = box
    rounded(draw, box, WHITE, color, width=4, radius=20)
    draw.text(((x1 + x2) // 2, y1 + 27), title, font=font(29), fill=color, anchor="ma")
    draw_wrapped(draw, ((x1 + x2) // 2, y1 + 65), body, font(19), fill=INK, max_width=x2 - x1 - 60, anchor="ma", line_gap=2)
    # 證據列使用柔和底色，提醒讀者這是程式位置，不是設計假設。
    rounded(draw, (x1 + 18, y2 - 28, x2 - 18, y2 - 10), fill, None, radius=8)
    draw.text(((x1 + x2) // 2, y2 - 19), evidence, font=font(15), fill=color, anchor="mm")


def panel(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], title: str, subtitle: str, header: str, header_fill: str, body_fill: str) -> None:
    x1, y1, x2, y2 = box
    rounded(draw, box, WHITE, header_fill, width=3, radius=24)
    draw.rounded_rectangle((x1, y1, x2, y1 + 86), radius=22, fill=header_fill)
    draw.rectangle((x1, y1 + 44, x2, y1 + 86), fill=header_fill)
    draw.text((x1 + 28, y1 + 27), title, font=font(31), fill=WHITE, anchor="lm")
    draw.text((x1 + 28, y1 + 65), subtitle, font=font(18), fill=WHITE, anchor="lm")
    rounded(draw, (x1 + 18, y1 + 106, x2 - 18, y2 - 18), body_fill, None, radius=16)


def product_card(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], title: str, subtitle: str, status: str, color: str, active: bool) -> None:
    x1, y1, x2, y2 = box
    fill = color if active else GRAY_SOFT
    outline = color if active else "#B8C3D0"
    rounded(draw, box, fill, outline, width=4, radius=24)
    text_color = WHITE if active else INK
    muted_color = "#DCE8FF" if active else MUTED
    draw.text(((x1 + x2) // 2, y1 + 42), title, font=font(30), fill=text_color, anchor="ma")
    draw.text(((x1 + x2) // 2, y1 + 88), subtitle, font=font(19), fill=muted_color, anchor="ma")
    draw.line((x1 + 28, y1 + 125, x2 - 28, y1 + 125), fill=muted_color, width=2)
    draw.text(((x1 + x2) // 2, y1 + 158), status, font=font(24), fill=text_color, anchor="ma")
    footer = "已註冊 A 專屬 Manager／Pool" if active else "導入時各自註冊自己的 Manager／Pool"
    draw.text(((x1 + x2) // 2, y1 + 211), footer, font=font(16), fill=muted_color, anchor="ma")


def main() -> None:
    image = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(image)

    # 標題與範圍
    draw.text((90, 62), "產品 A（好牧人 1.5）Dataverse 連線架構——現況符合性與後續範圍", font=font(52), fill=INK)
    draw.text((92, 138), "產品版圖：目前部署順序僅 A；B／C／D 為後續導入範圍。本圖不宣稱其 Host、DI、連線池或測試已完成。", font=font(23), fill=MUTED)
    rounded(draw, (90, 195, 2910, 365), "#19365F", None, radius=24)
    draw.text((125, 238), "判讀方式", font=font(28), fill="#A8D3FF")
    draw_wrapped(draw, (330, 225), "主流程色彩只區分元件層次；下方狀態區才表示判定：綠＝A 原始碼／測試已查證，橙＝相容過渡，紅＝A 上線前必須處理，灰＝B／C／D 後續導入。", font(22), fill=WHITE, max_width=2460, line_gap=6)

    # 四產品部署狀態
    card_y = 430
    card_h = 260
    card_w = 690
    gap = 30
    product_card(draw, (90, card_y, 90 + card_w, card_y + card_h), "產品 A｜好牧人 1.5", "SpeechMessageProducts.ChurchReport", "目前部署優先／本圖驗證範圍", BLUE, True)
    product_card(draw, (90 + (card_w + gap), card_y, 90 + (card_w + gap) + card_w, card_y + card_h), "產品 B｜好牧人 2.0", "未來雲端產品", "後續導入｜未納入本次驗證", TEAL, False)
    product_card(draw, (90 + 2 * (card_w + gap), card_y, 90 + 2 * (card_w + gap) + card_w, card_y + card_h), "產品 C｜建設公司維修系統", "企業維修服務產品", "後續導入｜未納入本次驗證", GREEN, False)
    product_card(draw, (90 + 3 * (card_w + gap), card_y, 90 + 3 * (card_w + gap) + card_w, card_y + card_h), "產品 D｜會員管理系統", "會員服務產品", "後續導入｜未納入本次驗證", ORANGE, False)

    # 主流程面板
    flow_panel = (90, 760, 1840, 2315)
    panel(draw, flow_panel, "A 的實際資料存取路徑", "每次 CRM 操作才取得 lease；Session 不應保存 client（cache 邊界另列風險）。", "", BLUE, BLUE_SOFT)
    fx1, fy1, fx2, fy2 = flow_panel
    bx1, bx2 = fx1 + 42, fx2 - 42
    boxes = [
        ("HTTP Request／DI Scope", "Controller 與 request scope 只持有 scoped 服務；直接 client 不進 Session／Cookie，但 legacy cache 仍需清理。", "產品組合根；DI 生命週期", BLUE, BLUE_SOFT),
        ("Scoped IOrganizationService", "GatewayOrganizationService 是無狀態代理，8 個 IOrganizationService 方法全部委派到 Gateway。", "GatewayOrganizationService.cs:7-50", TEAL, TEAL_SOFT),
        ("Scoped DataverseGateway", "序列巢狀 Execute 取得一次 lease；巢狀呼叫只增加深度，finally 歸還。例外先 MarkFaulted。", "DataverseGateway.cs:7-80", PURPLE, PURPLE_SOFT),
        ("Singleton DataverseConnectionManager", "唯一建立 pool 與 client 的管理入口；key 固定為 Product／Environment／OrganizationUrl／EffectiveIdentity。", "ServiceCollectionExtensions.cs:69-82；Manager.cs:11-93", BLUE, BLUE_SOFT),
        ("Singleton BoundedClientPool", "每個完整 key 一個 per-key bounded sub-pool；SemaphoreSlim 限制 MaxN，slot wait 有 timeout，另有 WhoAmI、idle cleanup、shutdown disposal。", "BoundedClientPool.cs:12-19,145-217,261-330", GREEN, GREEN_SOFT),
        ("ClientLease＋PooledClient", "狀態 Idle → Leased → Idle／Faulted → Disposed；同一 client 不會同時租給兩個 request。歸還前清除 CallerId。", "PooledClient.cs:76-199", ORANGE, ORANGE_SOFT),
        ("OnPremiseClient : IDisposable", "池的唯一擁有者在淘汰或關閉時 Dispose；WCF 通道依狀態 Close／Abort，避免 socket／通道殘留。", "OnPremiseClient.cs:28-37,396-440", TEAL, TEAL_SOFT),
        ("Dynamics 365 9.1／Dataverse Organization", "外部 CRM 只看到短時間的組織操作；下一個 request 可以重用健康 client，但不會重用上一個 request 的 Session 狀態。", "外部依賴；非應用程式 Session Pool", "#19365F", "#EAF1FB"),
    ]
    y = 905
    box_h = 145
    for idx, (title, body, evidence, color, fill) in enumerate(boxes):
        flow_box(draw, (bx1, y, bx2, y + box_h), title, body, evidence, color, fill)
        if idx < len(boxes) - 1:
            arrow(draw, ((bx1 + bx2) // 2, y + box_h + 5), ((bx1 + bx2) // 2, y + box_h + 28), color)
        y += 160
    draw.text((fx1 + 45, 2205), "規則：重用的是無 request 狀態的池化 client；Session、Cookie、Claims、HttpContext、request cache、CallerId 不得進入 pool。", font=font(21), fill=RED)

    # 生命週期面板
    life_panel = (1900, 760, 2910, 2315)
    panel(draw, life_panel, "單次 CRM 操作／Lease 生命週期", "Lease 是 per-operation；request scope 只持有 proxy／Gateway。", "", ORANGE, ORANGE_SOFT)
    lx1, ly1, lx2, ly2 = life_panel
    draw.text((lx1 + 48, ly1 + 135), "① 進入", font=font(27), fill=ORANGE)
    draw_wrapped(draw, (lx1 + 175, ly1 + 135), "建立 DI scope；此時沒有為 Session 保留 raw client。", font(21), fill=INK, max_width=680, line_gap=5)
    draw.text((lx1 + 48, ly1 + 295), "② 取得", font=font(27), fill=ORANGE)
    draw_wrapped(draw, (lx1 + 175, ly1 + 295), "Gateway.Execute → Manager.Acquire → Pool.TryRent，受 MaxN 與 AcquireTimeout 約束。", font(21), fill=INK, max_width=680, line_gap=5)
    draw.text((lx1 + 48, ly1 + 490), "③ 使用", font=font(27), fill=ORANGE)
    draw_wrapped(draw, (lx1 + 175, ly1 + 490), "在 lease 內執行 CRM 操作；巢狀呼叫共用同一 lease。", font(21), fill=INK, max_width=680, line_gap=5)
    draw.text((lx1 + 48, ly1 + 650), "④ 正常", font=font(27), fill=GREEN)
    draw_wrapped(draw, (lx1 + 175, ly1 + 650), "finally Dispose lease → 清除 CallerId → Healthy 回 Idle → semaphore 釋放。", font(21), fill=INK, max_width=680, line_gap=5)
    draw.text((lx1 + 48, ly1 + 845), "⑤ 例外", font=font(27), fill=RED)
    draw_wrapped(draw, (lx1 + 175, ly1 + 845), "MarkFaulted → 絕不回池 → Dispose／淘汰；下一個 request 取得新 client 或其他健康 client。", font(21), fill=INK, max_width=680, line_gap=5)
    draw.text((lx1 + 48, ly1 + 1040), "⑥ 關閉", font=font(27), fill=BLUE)
    draw_wrapped(draw, (lx1 + 175, ly1 + 1040), "應用程式停止時 Manager.Dispose → Pool 停止 timer 並清理可安全釋放的 client。", font(21), fill=INK, max_width=680, line_gap=5)
    rounded(draw, (lx1 + 36, ly1 + 1240, lx2 - 36, ly2 - 34), "#FFF8E7", None, radius=14)
    draw.text((lx1 + 58, ly1 + 1280), "目前 A 的 Pool Key", font=font(24), fill=ORANGE)
    draw_wrapped(draw, (lx1 + 58, ly1 + 1330), "ChurchReport  ＋  環境名稱  ＋  CRM ServerUrl  ＋  服務帳號 Username", font(21), fill=INK, max_width=lx2 - lx1 - 116, line_gap=5)
    draw.text((lx1 + 58, ly1 + 1435), "EffectiveIdentity 目前是固定服務帳號；尚未啟用 per-user impersonation。", font=font(19), fill=RED)

    # 符合性矩陣
    matrix_y = 2380
    col_w = 900
    gap = 50
    panels = [
        (90, matrix_y, 90 + col_w, 3880, "已由程式與測試證明", "37 個 Dataverse 架構測試全數通過", GREEN, GREEN_SOFT),
        (90 + col_w + gap, matrix_y, 90 + 2 * col_w + gap, 3880, "部分符合／過渡狀態", "核心路徑可用，但仍有相容邊界", ORANGE, ORANGE_SOFT),
        (90 + 2 * (col_w + gap), matrix_y, 90 + 3 * col_w + 2 * gap, 3880, "後續導入／A 上線前技術債", "灰色是未納入範圍；紅色是 A 必須處理", GRAY, GRAY_SOFT),
    ]
    for p in panels:
        panel(draw, p[:4], p[4], p[5], "", p[6], p[7])

    # 左矩陣內容
    x = panels[0][0] + 55
    y = matrix_y + 135
    y = bullet_list(draw, x, y, [
        "DI：DataverseConnectionManager 與 BoundedClientPool 是 Singleton；Gateway 與 IOrganizationService 是 Scoped。",
        "Gateway：序列巢狀操作只租一條 lease；同一 scoped Gateway 的並行呼叫尚未證明。",
        "Bounded：MaxN／AcquireTimeout／IdleTimeout／HealthInterval 由 Dataverse:Pool 組態控制。",
        "隔離：完整 Pool Key 分隔 Product、Environment、OrganizationUrl、EffectiveIdentity。",
        "健康：WhoAmI 失敗、操作例外、CallerId 清除失敗的 client 都不回池。",
        "清理：PooledClient 與 OnPremiseClient 有明確的 Close／Abort／Dispose 路徑。",
        "相容：Factory 保存 ambient proxy，不保存 scope、lease、raw client；背景 fallback scope 會釋放。",
        "HEAD trace 快照：112 個 lease 全部成對歸還；同一 client 最大同時租借數為 1；112 次 return 的 CallerId 都已清空。",
    ], panels[0][2] - panels[0][0] - 110, fill=INK, bullet_fill=GREEN, size=22, gap=13)
    rounded(draw, (panels[0][0] + 35, 3690, panels[0][2] - 35, 3825), "#DDF4E7", None, radius=14)
    draw_wrapped(draw, (panels[0][0] + 58, 3720), "結論：產品 A 的 Gateway → Manager → Pool → Lease → OnPremiseClient 核心路徑，已與目標架構的生命週期原則一致。", font(22), fill=GREEN, max_width=col_w - 116, line_gap=6)

    # 中矩陣內容
    x = panels[1][0] + 55
    y = matrix_y + 135
    y = bullet_list(draw, x, y, [
        "ToolUtilityClass 仍保留 public mutable 欄位 m_Crm2011OrganizationService；目前實際注入的是 Gateway proxy，不是 raw OnPremiseClient。",
        "多個既有 Controller 仍注入 ICrmConnectionPool；ConnectionPoolStatsAdapter 只提供 GetStats，raw Acquire／Release／Validate 會明確拒絕。",
        "部分業務程式仍直接讀取 ToolUtilityClass 的相容欄位；完整移轉到更窄的 Gateway API 尚未完成。",
        "目前單一產品 A、單一服務帳號的配置不需要 per-user key；若未來啟用 impersonation，需再補 key resolver 與巢狀 key 驗證。",
        "共用 AddToolUtility() 目前仍把 Product 固定成 ChurchReport；B／C／D 導入時必須由各自組合根明確提供 Product／Environment／服務身分。",
        "ToolUtilityFacade／CrmConnectionService 的 legacy connection-creation API 仍存在，但 A 的 active 路徑未以它們繞過 Gateway。",
        "這些是過渡設計，不代表已有 Session Leakage；但會限制『圖與程式 100% 字面吻合』的宣稱。",
    ], panels[1][2] - panels[1][0] - 110, fill=INK, bullet_fill=ORANGE, size=22, gap=14)
    rounded(draw, (panels[1][0] + 35, 3590, panels[1][2] - 35, 3825), "#FFF0D8", None, radius=14)
    draw_wrapped(draw, (panels[1][0] + 58, 3620), "判定：核心連線生命週期已符合；產品層 API 還在相容遷移期，不能說所有呼叫點都已是理想化 Gateway 介面。", font(22), fill=ORANGE, max_width=col_w - 116, line_gap=6)

    # 右矩陣內容
    x = panels[2][0] + 55
    y = matrix_y + 135
    y = bullet_list(draw, x, y, [
        "【後續導入】產品 B：好牧人 2.0 未納入本次部署與驗證。",
        "【後續導入】產品 C：建設公司維修系統未納入本次部署與驗證。",
        "【後續導入】產品 D：會員管理系統未納入本次部署與驗證。",
        "不能宣稱四產品的組合根、IIS worker process、設定與測試都已驗證；目前證據只涵蓋 A。",
        "appsettings.json 仍可看到明文 CRM Password；正式雲端部署前必須改由 User Secrets、環境變數或受管機密注入並輪替既有密碼。",
        "架構測試以 fake／stub client 為主；真實 Dynamics 365 9.1 登入、正式容量、soak 與故障演練尚未由本次稽核證明。",
        "目前 DynamicsAccess:ExecutionMode=Embedded；本圖不代表已切換到遠端 Dynamics Gateway Web API。",
        "同一 scoped Gateway 的 Task.WhenAll 並行呼叫尚未驗證；_depth／_lease 競態需補測，或明確禁止同一 proxy 的並行使用。",
        "trace 仍可能含服務身分與 CallerId GUID 等敏感 metadata；不含明文密碼不等於可無限制保存，須設檔案權限與保留期。",
        "SmallGroup 的 Session-keyed IMemoryCache 仍保存含 scoped IToolUtilityProvider 的資料管理器；跨 request／重新登入的 scope 隔離與釋放尚未證明，屬 A 的 release blocker。",
        "現行圖的 per-user／多 sub-pool 是可擴充邊界，不是 A 現在已啟用的使用者級連線池。",
    ], panels[2][2] - panels[2][0] - 110, fill=INK, bullet_fill=GRAY, size=22, gap=14)
    rounded(draw, (panels[2][0] + 35, 3585, panels[2][2] - 35, 3825), "#FDE2E0", None, radius=14)
    draw_wrapped(draw, (panels[2][0] + 58, 3615), "A 的上線阻擋項包含機密治理、scoped Gateway 並行契約與 Session-keyed cache 隔離；B／C／D 則是未納入本次範圍，不應誤稱為 A 的缺陷。", font(22), fill=RED, max_width=col_w - 116, line_gap=6)

    # 測試與最終判讀
    rounded(draw, (90, 3970, 2910, 4310), "#19365F", None, radius=24)
    draw.text((130, 4020), "驗證證據與最終判讀", font=font(31), fill=WHITE)
    draw_wrapped(draw, (130, 4085), "dotnet test ToolUtility.Dataverse.Tests：37／37 成功；dotnet test ToolUtility.Tests：63／63 成功。這些測試涵蓋巢狀 lease、並行隔離、MaxN 超時、fault eviction、CallerId 清除、cleanup 競態、DI graph 與 legacy Factory fallback scope。", font(22), fill="#DCE8FF", max_width=2700, line_gap=6)
    draw.text((130, 4230), "結論：A 的目前程式分支具備符合目標原則的 Gateway／Manager／Pool／Lease 核心生命週期；B／C／D 尚待導入，A 的相容 API 與機密治理仍須完成。", font=font(22), fill="#FFD166")
    draw.text((130, 4270), "證據邊界：原始碼、本地自動化測試與 HEAD trace 快照；不等同正式環境容量、長時間 soak 或故障演練。", font=font(18), fill="#DCE8FF")

    image.save(OUTPUT, format="PNG", optimize=True)
    print(OUTPUT)


if __name__ == "__main__":
    main()
