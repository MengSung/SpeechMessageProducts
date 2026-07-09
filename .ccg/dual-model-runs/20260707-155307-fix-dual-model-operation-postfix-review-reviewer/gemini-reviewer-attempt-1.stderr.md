[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts
  PID: 26852
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-26852.log
Ripgrep is not available. Falling back to GrepTool.
  Session-ID: ed793802-b2ef-42ce-b974-02a35ad6f7f4
Error when talking to Gemini API Full report available at: C:\Users\Administrator\AppData\Local\Temp\gemini-client-error-Turn.run-sendMessageStream-2026-07-07T07-53-12-539Z.json _ApiError: {"error":"余额不足"}
    at throwErrorIfNotOK (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:258424:24)
    at async file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:258187:7
    at async Models.generateContentStream (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:259283:16)
    at async file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:301279:19
    at async file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:278341:23
    at async retryWithBackoff (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:298287:23)
    at async GeminiChat.makeApiCallAndProcessStream (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:321564:28)
    at async GeminiChat.streamWithRetries (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:321382:29)
    at async Turn.run (file:///C:/Users/Administrator/AppData/Roaming/npm/node_modules/@google/gemini-cli/bundle/chunk-VLV2BYPM.js:322128:24) {
  status: 403
}

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, double-quote, single-quote, backtick, dollar, length>800
gemini exited with status 403
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-26852.log (deleted)
