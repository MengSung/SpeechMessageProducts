# 行道會聖谷教會 — LINE 大頭貼「同步與顯示」修復報告

> 日期：2026-06-17　範圍：行道會聖谷教會 App（會友資訊清單／會友細節）＋ Dynamics 365 後台聯絡人表單

---

## 一、問題描述

1. **行道會聖谷教會 App 會友清單**：部分標示「LINE」的聯絡人，頭像卻是**通用人像／破圖**，而不是真實照片；且這些「沒有真照片」的人不該出現藍色「LINE」標示，也不該在按「顯示照片」時被算進去。
2. **D365 後台聯絡人表單**：LINE 大頭貼頭像不顯示——尤其是**會員換過 LINE 照片之後**就不顯示。

---

## 二、根本原因

### App 端
- 頭像來源分三類：`primary`（有上傳照 `entityimage`，綠色「照片」標示）、`line`（有 `new_line_picture_url`，藍色「LINE」標示）、`fallback`（兩者皆無 → 依性別顯示「上半身剪影」、無標示）。
- `new_line_picture_url` **只在 LINE 綁定當下、且當時有照片才會寫入，之後不再更新／清除**。會員**換照或移除大頭貼後，LINE 會發新網址、舊網址失效**，但資料庫仍存舊網址 → 顯示破圖／通用人像。
- 「有沒有真照片」**無法單看網址判斷**（失效網址有時仍會回一張通用圖），必須實際判斷「網址能不能顯示圖片」。
- **LINE Messaging API `getProfile`（`/bot/profile/{userId}`）只有在該會員『有加官方帳號(OA)為好友』時才回得到資料**；多數「新朋友」是用 Mini App/LIFF 註冊、沒加好友 → 一律 403/404。所以**不能單靠 getProfile** 取得所有人的最新照片。

### D365 後台
- D365 聯絡人**原生大頭照（姓名左側那張）只認 `entityimage` 影像欄位**；`new_line_picture_url` 只是「文字網址」欄位，D365 不會自動把它當照片顯示。
- 表單下方那個 LINE 頭像 `new_line_user_picture`，原本是一張**靜態 JPG 圖片 Web 資源** → 永遠只顯示固定的預設人像，**根本不會去讀 `new_line_picture_url`**。（且 D365 既有 Web 資源無法變更類型。）

---

## 三、解決方案

### A. 行道會聖谷教會 App：新增「重新同步LINE」功能

**位置與權限**：會友資訊清單工具列、「顯示照片」右側；僅「全教會」管理者可見可用（`ViewBag.MemberInfoCanResync`，後端再以 `MemberInfoAccess.Church` 把關）。

**流程（前端分批、即時進度）**
1. 先取候選名單：`GET /MemberInfo/ResyncLineCandidateIds`（在籍、有 `new_lineid` 者；**不論目前有無 `new_line_picture_url`**——已清空／從未有照片者也納入，以便偵測對方「解除封鎖／重新加好友／新增照片」後補回）。
2. 每 **20 筆一批** 呼叫 `POST /MemberInfo/ResyncLineProfiles`（body：`{ contactIds: [...] }`）。
3. 網格上方**進度列持續更新**：已處理 X/總數、成功、失敗。
4. 完成 → 總結視窗（含 LINE 取資料失敗原因範例）→ 自動重整網格、重載頭像。

**每筆的判定邏輯**
1. **判斷照片狀態**：
   - 有網址者，伺服器端探測能否顯示圖片（平行抓取、限流 20、逾時 4 秒，只讀回應標頭）：
     - `2xx 且 Content-Type 為 image/*` → **可顯示** → 不動（保留照片與藍色 LINE 標示）。
     - 有回應但非圖片／非 2xx → **確定失效**。
     - 逾時／連線錯誤 → **無法判定** → 不動（避免誤清）。
   - **本來就沒有網址（已清空／從未有）→ 直接進下一步向 LINE 查詢**（用以偵測解除封鎖／新增照片後補回）。
2. 對「確定失效」或「沒有網址」者，依 `new_lineid` 呼叫 `GetUserProfileAsync`：
   - 取到照片 → **更新／補回** `new_line_picture_url`（並更新 `new_line_status_message`、`new_line_displayname`）。
   - 取到但已無照片 → 原有網址者**清空**；本來就沒網址者**維持無照片**。
   - **取不到（多為非好友／封鎖 403/404）** → 原有網址者**清空失效網址**；本來就沒網址者**維持無照片**（下次再試，對方解除封鎖／重新加好友後即可補回）。
3. 清空／無照片者，既有顯示邏輯自動正確：依性別顯示「上半身剪影」（男藍／女粉／未知灰，沿用 `DefaultAvatarSvg`，**未修改剪影本身**）、無 LINE 標示、不計入「顯示照片」。

**計數類別**：照片正常（保留）、更新（取到／補回照片）、清空失效（原有網址確認無照→清空）、無照片（本來就沒、現仍無，多為未加好友）、暫時略過（逾時未判定）、失敗（批次要求失敗）。

### B. D365 後台：改用 HTML Web 資源動態顯示

- 原靜態 JPG（`new_line_user_picture`）保留不用。
- **新建 HTML 類型 Web 資源**（內容＝桌面 `DisplayPhoto.html`，見附錄）：讀表單 `new_line_picture_url`，以 `<img referrerpolicy="no-referrer">` 顯示；含「往上層找 Xrm／表單未就緒重試／欄位變更即更新」。
- 將聯絡人表單那格頭像改用此 HTML Web 資源 → 儲存 → 發行。
- 結果：後台頭像會依 `new_line_picture_url` 目前的值顯示當前照片。
- 注意：用「**上傳檔案**」方式設定內容（避免文字編輯器把標籤編碼成純文字）。

---

## 四、已知限制

- **LINE 隱私限制**：會員換照後，若**未加官方帳號好友、又不再開行道會聖谷教會 App**，則任何系統（App 或 D365）都**無法取得其新照片**——無解。
- D365 的 HTML 頭像顯示的是 `new_line_picture_url` **當下的值**，需靠 App 端保持最新。
- 「重新同步LINE」用 Messaging API，只能更新「OA 好友」的照片；非好友只會被「清空失效網址」（改顯示剪影）。

---

## 五、後續建議（讓「換照」也能自動跟上）

1. **（推薦）LINE 登入時自動更新**：在行道會聖谷教會 Mini App 登入流程加入「以本人 LIFF profile 更新該會員 `new_line_picture_url`／狀態／暱稱（set-or-clear）」。用本人授權，**不需加 OA 好友、涵蓋最廣**；會員換照後一開 App 即更新，D365 頭像也跟著正確。（尚未實作。）
2. 或排程定時執行「重新同步LINE」做批次清理（限 OA 好友）。
3. （可選）同步時將照片下載存進 `entityimage`，讓 D365 **原生大頭照**也能顯示、且完全不受網址過期/referer 影響。

---

## 六、操作與維運

- **執行**：會友資訊（全教會）→ 點「重新同步LINE」→ 看進度列跑完 → 確認總結。可重複執行。
- **完成視窗數字**：照片正常＝保留；更新＝取到／補回照片；清空失效＝舊網址失效已清成剪影；無照片＝本來就沒、現仍無（多為未加好友）；暫時略過＝逾時未判定；失敗＝批次要求失敗。「LINE 取資料失敗原因／附註」為**說明非錯誤**（多為「未加官方帳號好友」）。
- **重複執行的意義**：候選名單含「所有有 `new_lineid` 者」（不限有無照片網址），所以每次同步都會把**已清空／無照片者**再向 LINE 查一次——對方若已**解除封鎖／重新加好友／新增照片**，即會自動補回。建議定期執行（或日後改為登入時自動更新）。
- **D365 換照不顯示時**：請會員重開 App（未來做了登入自動更新後）或執行「重新同步LINE」更新網址。
- **部署提醒**：本專案為 net10.0，**.cshtml 視圖會編進 DLL**；任何 .cs/.cshtml 變更都需**重新發佈 + 部署 + 回收應用程式集區**才會在正式站生效。

---

## 附錄

### A. 相關 CRM 欄位
- `new_lineid`（LINE User Id）、`new_line_picture_url`（LINE 照片網址）、`new_line_status_message`（狀態訊息）、`new_line_displayname`（顯示名稱）、`gendercode`（性別，決定剪影）、`entityimage`（上傳大頭照）。

### B. 端點
- `GET /MemberInfo/ResyncLineCandidateIds` → `{ success, ids[] }`
- `POST /MemberInfo/ResyncLineProfiles`（`[FromBody] { contactIds[] }`）→ `{ success, scanned, okValid, updated, cleared, inconclusive, reasons[] }`

### C. 主要異動檔案
- `Controllers/MemberInfoController.cs`：新增上述兩端點、`ProbeImageDisplayableAsync`、`GetResyncLineChannelAccessToken`；`Index()` 設定 `ViewBag.MemberInfoCanResync`。
- `Views/MemberInfo/MemberInfoGrid.cshtml`：新增「重新同步LINE」按鈕（在「顯示照片」右側）、進度列、分批同步 JS。
- 既有顯示邏輯沿用：`Services/ContactAvatar/DefaultAvatarSvg.cs`、`ContactAvatarUrl.cs`。
- D365：新增 HTML Web 資源（`DisplayPhoto.html`），表單改用之。

### D. D365 HTML Web 資源內容（DisplayPhoto.html）
讀 `new_line_picture_url` 並以 `<img referrerpolicy="no-referrer">` 顯示；含 Xrm 尋找與重試。原始檔置於桌面 `DisplayPhoto.html`，以「上傳檔案」方式設定到 HTML 類型 Web 資源。



------------------------------
-----原始程式------------------
------------------------------
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <meta http-equiv="X-UA-Compatible" content="IE=edge" />
  <style>
    html,body{margin:0;padding:0;height:100%;background:transparent;}
    .wrap{display:flex;align-items:center;justify-content:center;height:100%;}
    img{max-width:100%;max-height:100%;object-fit:contain;border-radius:8px;}
  </style>
</head>
<body>
  <div class="wrap"><img id="pic" referrerpolicy="no-referrer" alt="" /></div>
  <script>
    // 在不同 D365 版本/iframe 巢狀下，往上層尋找 Xrm（含 window.top 後援）。
    function findXrm(){
      var w=window;
      for(var i=0;i<6 && w;i++){
        try{ if(w.Xrm && w.Xrm.Page && w.Xrm.Page.getAttribute){ return w.Xrm; } }catch(e){}
        if(w===w.parent){ break; }
        w=w.parent;
      }
      try{ if(window.top && window.top.Xrm && window.top.Xrm.Page){ return window.top.Xrm; } }catch(e){}
      return null;
    }
    function getUrl(){
      try{
        var xrm=findXrm();
        if(xrm){
          var a=xrm.Page.getAttribute("new_line_picture_url");
          if(a){ return a.getValue(); }
        }
      }catch(e){}
      return null;
    }
    function render(){
      var img=document.getElementById("pic");
      var url=getUrl();
      if(url){ img.style.display=""; img.src=url; } else { img.style.display="none"; }
    }
    var tries=0;
    function init(){
      var xrm=findXrm();
      if(xrm){
        try{
          var a=xrm.Page.getAttribute("new_line_picture_url");
          if(a && a.addOnChange){ a.addOnChange(render); }
        }catch(e){}
        render();
      } else if(tries++ < 20){
        setTimeout(init, 300); // 表單尚未就緒，稍後重試（最多約 6 秒）
      }
    }
    window.addEventListener("load", init);
  </script>
</body>
</html>
