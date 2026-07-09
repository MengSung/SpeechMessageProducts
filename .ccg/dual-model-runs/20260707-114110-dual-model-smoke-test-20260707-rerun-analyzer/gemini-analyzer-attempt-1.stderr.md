[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts
  PID: 29048
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-29048.log
Ripgrep is not available. Falling back to GrepTool.
  Session-ID: cb53617d-cd4d-4f9b-a67e-82852272100a
Error when talking to Gemini API Full report available at: C:\Users\Administrator\AppData\Local\Temp\gemini-client-error-Turn.run-sendMessageStream-2026-07-07T03-41-15-964Z.json _ApiError: {"error":"余额不足"}
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
Assertion failed: !(handle->flags & UV_HANDLE_CLOSING), file src\win\async.c, line 76

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, backtick, length>800
gemini exited with status 3221226505
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-29048.log (deleted)
