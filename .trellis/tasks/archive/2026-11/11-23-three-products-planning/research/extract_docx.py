import os, sys, json, zipfile, re
from pathlib import Path
from docx import Document
src=Path(r"E:\電子書籍\音訊科技產品市場、競爭對手==分析研究\音訊產品市場功能需求分析\好牧人2.0市場研究與分析、設計、流程圖\好牧人2.0系統規劃、設計、流程圖\需求分析規格報告")
out=Path(r"D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.12.GoodShepherd2.0.Worktree\.trellis\tasks\11-23-three-products-planning\research")
for p in src.glob('*.docx'):
    d=Document(str(p)); lines=[]
    for para in d.paragraphs:
        t=para.text.strip()
        if t: lines.append(t)
    for table in d.tables:
        for row in table.rows:
            vals=[c.text.strip().replace('\n',' / ') for c in row.cells]
            if any(vals): lines.append(' | '.join(vals))
    (out/(p.stem+'.txt')).write_text('\n'.join(lines),encoding='utf-8')
    print(p.name, len(lines))
