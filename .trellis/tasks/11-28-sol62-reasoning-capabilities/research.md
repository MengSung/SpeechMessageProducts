# 唯讀排查證據

本文件僅保留模型能力、程式判斷與匿名結果；不保留驗證資訊、使用者對話或網路標頭。

## 本機模型目錄（coding 路徑）

```json
[
  {
    "slug": "gpt-6.1-sol",
    "input_modalities": [
      "text",
      "image"
    ],
    "supported_reasoning_levels": [
      {
        "effort": "low",
        "description": ""
      },
      {
        "effort": "medium",
        "description": ""
      },
      {
        "effort": "high",
        "description": ""
      },
      {
        "effort": "xhigh",
        "description": ""
      },
      {
        "effort": "max",
        "description": ""
      }
    ],
    "supports_reasoning_summaries": true,
    "supports_reasoning_summary_parameter": true,
    "supported_endpoints": [
      "/v1/responses",
      "/v1/chat/completions"
    ],
    "default_reasoning_level": "medium"
  },
  {
    "slug": "gpt-6.2-sol",
    "input_modalities": [
      "text"
    ],
    "supported_reasoning_levels": [
      {
        "effort": "low",
        "description": ""
      },
      {
        "effort": "medium",
        "description": ""
      },
      {
        "effort": "high",
        "description": ""
      },
      {
        "effort": "xhigh",
        "description": ""
      },
      {
        "effort": "max",
        "description": ""
      },
      {
        "effort": "ultra",
        "description": ""
      }
    ],
    "supports_reasoning_summary_parameter": false,
    "supported_endpoints": [],
    "default_reasoning_level": "low"
  },
  {
    "slug": "gpt-6.2-astra",
    "input_modalities": [
      "text"
    ],
    "supported_reasoning_levels": [
      {
        "effort": "low",
        "description": ""
      },
      {
        "effort": "medium",
        "description": ""
      },
      {
        "effort": "high",
        "description": ""
      },
      {
        "effort": "xhigh",
        "description": ""
      },
      {
        "effort": "max",
        "description": ""
      },
      {
        "effort": "ultra",
        "description": ""
      }
    ],
    "supports_reasoning_summary_parameter": false,
    "supported_endpoints": [],
    "default_reasoning_level": "low"
  }
]
```

## 桌面 App 靜態判斷

- 安裝套件：OpenAI.Codex_26.1120.6357.0；owl-app.ini 版本 26.1120.50957。未檢查更新、未更新或重新啟動。
- Work 模型解析：webview/assets/async/366996.9782070574.js 將 configurable_thinking_effort / thinking_efforts / product_features.attachments 轉換成 modelConfigBySlug 內對應能力。
- Work 設定：webview/assets/async/181649.8480631449.js 與 603068.792cfcf2db.js 的模型推理選項讀取 modelConfigBySlug[model].thinkingEfforts。
- Coding 圖片插入：webview/assets/async/102734.cdd4b920b9.js 等，inputModalities 不含 image 時拒絕；Work 拖放走另一條 attachments 路徑。
- 上述為唯讀安裝檔觀察，不是 UI 實機重現；尚未取得 6.2 Work 當前 /models 回應，不能認定實際缺欄位或上游不支援。

## 查詢界限

- 已找到 Work 瀏覽器資料目錄，但 Chromium block cache 正被占用，讀取回報 PermissionError；未關閉程式、未解鎖、未複製認證資料、未擷取完整流量。
- 不讀 auth.json、Cookie、CodexManager rpc-token、資料庫秘密或對話檔案。
- 本地設定未變更；模型清單出現 xhigh 不能證明上游接受或實際使用。

## 外部分析

- 自癒入口已並行呼叫 Gemini / Claude，兩次健康檢查通過；Claude 產生可用結果，Gemini 兩次回覆 HTTP 503。
- summary: 20261128-162611-sol62-capabilities-analysis-analyzer/summary.json；ok=false、dualModelComplete=false、quotaBlocked=false、degradedFallback=false。
- 這是雙模型未完成，不是獲准的 quota 單模型降級；Claude 結果僅作旁證，未進入修改。
- Claude 同意最重要缺口是 Work 真正 /models 能力，不可把 coding catalog 或 summaries 旗標直接認定為根因。
- 對 Claude「reasoning token 隨等級改變可證明實際生效」的建議不採用：用量變動不是決定性證據；需供應端確認有效參數，否則標示無法驗證。
