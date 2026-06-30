[codeagent-wrapper]
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 41460
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-41460.log
  Web UI: http://localhost:65379
System.Management.Automation.RemoteException
=== Recent Errors ===
cleanupOldLogs: skipping codeagent-wrapper-41460.log: path resolution failed: Access is denied.
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, double-quote, backtick, dollar, length>800
claude command not found in PATH
Read stdout error: read |0: file already closed
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-41460.log (deleted)