codeagent-wrapper.exe : [codeagent-wrapper]
位於 線路:3 字元:15
+     $prompt | & $wrapper --lite --backend claude - $repo *> $out
+               ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([codeagent-wrapper]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
  Backend: claude
  Command: claude -p --dangerously-skip-permissions --setting-sources  --output-format stream-json --verbose -
  PID: 11768
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-11768.log
  Session-ID: efd0de81-ad89-4b5e-8ed6-d153512f318a

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, double-quote, backtick, dollar, length>800
claude exited with status 1
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-11768.log (deleted)
