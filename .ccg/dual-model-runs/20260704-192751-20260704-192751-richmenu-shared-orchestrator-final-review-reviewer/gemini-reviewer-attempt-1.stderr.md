[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRefactorRichMenu
  PID: 29560
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-29560.log
Ripgrep is not available. Falling back to GrepTool.
  Session-ID: e9e99840-4f00-4d8a-87f0-3bfc23904436
Attempt 1 failed with status 500. Retrying with backoff... _ApiError: {"error":{"message":"{\"error\":{\"message\":\"openai_error\",\"type\":\"bad_response_status_code\",\"param\":\"\",\"code\":\"bad_response_status_code\"}}","code":500,"status":"Internal Server Error"}}
    at throwErrorIfNotOK (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:258424:24)
    at async file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:258187:7
    at async Models.generateContentStream (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:259283:16)
    at async file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:301279:19
    at async file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:278341:23
    at async retryWithBackoff (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:298287:23)
    at async GeminiChat.makeApiCallAndProcessStream (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:321564:28)
    at async GeminiChat.streamWithRetries (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:321382:29)
    at async Turn.run (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:322128:24) {
  status: 500
}
(Use `node --trace-deprecation ...` to show where the warning was created)
Error executing tool grep_search: params must have required property 'pattern'
