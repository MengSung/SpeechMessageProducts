from pathlib import Path
from docx import Document
src=Path(r"D:\音訊科技產品\系統平台\SpeechMessageProducts\文件\產品規劃書\Claude獨自規劃\三大產品共用架構與開發路線規劃(Claude版)\三大產品共用架構與開發路線規劃_Claude版_v1.0.docx")
out=Path(r"D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.12.GoodShepherd2.0.Worktree\scratch\claude-plan-extract")
d=Document(str(src)); lines=[]
for p in d.paragraphs:
 t=p.text.strip()
 if t: lines.append(t)
for t in d.tables:
 for row in t.rows:
  vals=[c.text.strip().replace('\n',' / ') for c in row.cells]
  if any(vals): lines.append(' | '.join(vals))
(out/'claude.txt').write_text('\n'.join(lines),encoding='utf-8')
print(len(lines))
