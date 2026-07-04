codeagent-wrapper.exe : [codeagent-wrapper]
位於 線路:1 字元:47
+ ... ) $prompt | & $wrapper --lite --backend claude - $repo *> $out; exit  ...
+                 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([codeagent-wrapper]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 28312
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-28312.log
  Session-ID: 2c159a0e-c230-4102-8cbd-8c91a4f8ac03

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, double-quote, backtick, length>800
claude exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-28312.log (deleted)
