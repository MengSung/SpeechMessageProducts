codeagent-wrapper.exe : [codeagent-wrapper]
At line:16 char:11
+ $prompt | & $wrapper --lite --backend claude - (Get-Location).Path *> ...
+           ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([codeagent-wrapper]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 20740
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-20740.log
  Session-ID: 48978fd7-7473-4a86-b248-2d531b0130e0

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, double-quote, length>800
claude exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-20740.log (deleted)
