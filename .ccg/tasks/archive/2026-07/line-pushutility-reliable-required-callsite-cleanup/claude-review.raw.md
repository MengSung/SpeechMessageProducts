codeagent-wrapper.exe : [codeagent-wrapper]
位於 線路:4 字元:21
+ ... iewPrompt | & $wrapperPath --progress --backend claude - $cwd 2>&1 |  ...
+                 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([codeagent-wrapper]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 40144
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-40144.log
  Session-ID: 59111dfd-e759-4b5d-a6f9-854154c4b7f3

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, double-quote, backtick, dollar, length>800
claude exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-40144.log (deleted)

