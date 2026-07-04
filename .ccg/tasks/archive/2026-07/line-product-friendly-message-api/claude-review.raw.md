codeagent-wrapper.exe : [codeagent-wrapper]
位於 線路:3 字元:15
+     $prompt | & $wrapper --lite --backend claude - $repo *> $out
+               ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([codeagent-wrapper]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 30292
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-30292.log
  Session-ID: 7578050f-b15e-4195-ba3c-26c592559e23

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, double-quote, single-quote, backtick, dollar, l
ength>800
claude exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-30292.log (deleted)
