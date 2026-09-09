import pathlib,re,json
root=pathlib.Path.cwd()
records=[]
for folder in ['SpeechMessageProducts.ChurchReport','ToolUtility']:
 for p in (root/folder).rglob('*.cs'):
  if any(x in p.parts for x in ('bin','obj','文件')):continue
  s=p.read_text(encoding='utf-8-sig')
  # Hide comments/strings to match catch nesting without changing offsets.
  clean=re.sub(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',lambda m:'\n'*m[0].count('\n')+' '*(len(m[0])-m[0].count('\n')),s)
  for m in re.finditer(r'\bcatch\b',clean):
   start=clean.find('{',m.end())
   if start<0:continue
   depth=1;end=start+1
   while end<len(clean) and depth:
    depth+= (clean[end]=='{')-(clean[end]=='}');end+=1
   body=s[start:end];sig=s[m.start():start]
   kind=('reported' if re.search(r'HandleError\(|ExceptionReporting.Report|\.LogError\(|\.LogCritical\(',body) else 'rethrow' if re.search(r'\bthrow\b',clean[start:end]) else 'terminal')
   records.append(dict(file=str(p.relative_to(root)),line=s.count('\n',0,m.start())+1,signature=sig.strip(),kind=kind,body=body[:2000]))
(root/'.ccg/tasks/line-actionable-exceptions/catch-audit.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
from collections import Counter
print(Counter(x['kind'] for x in records))
for x in records:
 if x['kind']=='terminal': print(x['file'],x['line'],x['signature'].replace('\n',' '),x['body'][:100].replace('\n',' '))
