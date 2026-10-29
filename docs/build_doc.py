from pathlib import Path
import base64
import re
import markdown

DOCS = Path(__file__).resolve().parent
MD = DOCS / "BlackTheo-实现原理.md"
HTML = DOCS / "BlackTheo-实现原理.html"

text = MD.read_text(encoding="utf-8")
body = markdown.markdown(
    text,
    extensions=["extra", "toc", "sane_lists", "fenced_code"],
    output_format="html5",
)

# Make the document self-contained: inline every image referenced from docs/.
def inline_image(match):
    src = match.group(1)
    path = DOCS / src
    if not path.exists():
        return match.group(0)
    data = base64.b64encode(path.read_bytes()).decode("ascii")
    return f'src="data:image/png;base64,{data}"'

body = re.sub(r'src="([^"]+)"', inline_image, body)

css = """
@page {
    size: A4;
    margin: 18mm 16mm 18mm 16mm;
}
* { box-sizing: border-box; }
html, body {
    margin: 0;
    padding: 0;
    color: #172033;
    font-family: "Microsoft YaHei", "PingFang SC", "Segoe UI", sans-serif;
    font-size: 10.5pt;
    line-height: 1.72;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
}
body { max-width: 186mm; margin: 0 auto; }
h1 {
    font-size: 23pt;
    line-height: 1.25;
    margin: 0 0 0.5em;
    padding-bottom: 0.25em;
    border-bottom: 3px solid #2d5f9e;
    color: #10233f;
}
h2 {
    font-size: 15.5pt;
    margin: 1.35em 0 0.45em;
    padding-left: 0.35em;
    border-left: 5px solid #2d5f9e;
    color: #10233f;
    page-break-after: avoid;
}
h3 {
    font-size: 12.2pt;
    margin: 1.05em 0 0.35em;
    color: #1c3c66;
    page-break-after: avoid;
}
p { margin: 0.55em 0; }
strong { color: #0f2d52; }
a { color: #1b5fa8; text-decoration: none; }
blockquote {
    margin: 0.8em 0;
    padding: 0.55em 0.9em;
    background: #f2f6fc;
    border-left: 4px solid #7aa3d8;
    color: #40536b;
}
code {
    font-family: "Cascadia Mono", "Consolas", monospace;
    font-size: 0.88em;
    background: #eef2f8;
    padding: 0.08em 0.3em;
    border-radius: 3px;
    color: #12345c;
}
pre {
    background: #101827;
    color: #dbe7ff;
    padding: 0.85em 1em;
    border-radius: 6px;
    overflow: hidden;
    white-space: pre-wrap;
    line-height: 1.5;
    font-size: 8.3pt;
    page-break-inside: avoid;
}
pre code {
    background: transparent;
    color: inherit;
    padding: 0;
    font-size: inherit;
}
table {
    border-collapse: collapse;
    width: 100%;
    margin: 0.65em 0;
    font-size: 9.2pt;
    page-break-inside: avoid;
}
th, td {
    border: 1px solid #c9d5e5;
    padding: 0.35em 0.5em;
    vertical-align: top;
}
th { background: #eaf1fb; color: #12345c; text-align: left; }
tr:nth-child(even) td { background: #fafcff; }
img {
    max-width: 100%;
    height: auto;
    display: block;
    margin: 0.8em auto;
    border: 1px solid #d6e0ee;
    border-radius: 6px;
}
ul, ol { margin: 0.35em 0 0.65em 1.4em; padding-left: 0.7em; }
li { margin: 0.18em 0; }
hr { border: 0; border-top: 1px solid #d4ddec; margin: 1.1em 0; }
h1 + blockquote { margin-top: 0.7em; }
"""

html = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>BlackTheo 实现原理讲解</title>
<style>{css}</style>
</head>
<body>
{body}
</body>
</html>
"""
HTML.write_text(html, encoding="utf-8")
print(HTML)
print(f"{HTML.stat().st_size} bytes")
