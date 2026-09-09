import pathlib,re
pattern=r'\n\s*// 失敗結果仍代表功能受影響：先落 Exception.log，再排入 LINE；只傳固定程式位置。\s*ToolUtilityNameSpace\.Diagnostics\.ExceptionReporting\.Report\([^;]+\);'
n=0
for p in pathlib.Path('SpeechMessageProducts.ChurchReport').rglob('*.cs'):
 if any(x in p.parts for x in ('bin','obj')): continue
 s=p.read_text(encoding='utf-8-sig'); clean,count=re.subn(pattern,'',s)
 if count:
  p.write_bytes((clean.replace('\r\n','\n').replace('\n','\r\n')).encode('utf-8'));n+=count
print('Removed unreviewed mechanical insertions:',n)
