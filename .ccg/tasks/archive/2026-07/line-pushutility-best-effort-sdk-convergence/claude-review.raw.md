codeagent-wrapper.exe : [codeagent-wrapper]
位於 線路:4 字元:21
+ ... iewPrompt | & $wrapperPath --progress --backend claude - $cwd 2>&1 |  ...
+                 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([codeagent-wrapper]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 28060
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-28060.log
  Session-ID: e5102e8f-a653-4c10-aa0b-8088071b218b

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, double-quote, backtick, length>800
claude exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-28060.log (deleted)

