import pathlib,re
skip=('Program.cs','Diagnostics/','Logging/','Services/ChurchReportLineAdminNotificationService.cs','Middleware/UnhandledExceptionLineNotificationMiddleware.cs')
for p in pathlib.Path('SpeechMessageProducts.ChurchReport').rglob('*.cs'):
 if any(x in str(p).replace('\\','/') for x in skip) or any(x in p.parts for x in ('bin','obj','文件')): continue
 s=p.read_text(encoding='utf-8-sig'); clean=re.sub(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',lambda m:'\n'*m[0].count('\n')+' '*(len(m[0])-m[0].count('\n')),s)
 edits=[]
 for m in re.finditer(r'catch\s*\(\s*(?:Exception|[A-Za-z0-9_.<>]+Exception)\s+(\w+)\s*\)',clean):
  op=re.search(r'\{',clean[m.end():]);
  if not op: continue
  start=m.end()+op.start(); depth=1; i=start+1
  while i<len(clean) and depth: depth+=(clean[i]=='{')-(clean[i]=='}'); i+=1
  body=s[start:i]
  if re.search(r'OperationCanceledException|HandleError\(|ExceptionReporting\.Report|\bthrow\b|return Task\.FromException',body): continue
  # Only feature-impacting catches with a result/side effect; avoid pure optional probes.
  if not re.search(r'\b(return|await|Save|Update|Create|Delete|Upload|Send|Commit|Copy|Write|Trace|Log)\b',body,re.I): continue
  line=s.count('\n',0,m.start())+1; indent=re.match(r'\s*',s[start+1:]).group(0)
  call=f'\n{indent}// 失敗結果仍代表功能受影響：先落 Exception.log，再排入 LINE；只傳固定程式位置。\n{indent}ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report({m.group(1)}, "{p.stem}.Catch{line}");'
  edits.append((start+1,call))
 for pos,text in reversed(edits): s=s[:pos]+text+s[pos:]
 if edits: p.write_text(s,encoding='utf-8',newline='')
 print(p,len(edits)) if edits else None
