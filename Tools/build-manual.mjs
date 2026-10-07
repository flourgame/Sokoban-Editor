import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';

// Optional path argument lets an existing runtime provide marked without adding game dependencies.
const idx = process.argv.indexOf('--marked');
const modulePath = idx >= 0 ? pathToFileURL(path.resolve(process.argv[idx + 1])).href : 'marked';
const { marked } = await import(modulePath);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const docs = path.join(root, 'docs');
let html = marked.parse(fs.readFileSync(path.join(docs, 'USER_MANUAL.md'), 'utf8'));
const toc = [];
html = html.replace(/<h2>(.*?)<\/h2>/g, (_, title) => {
  const id = 'section-' + (toc.length + 1);
  toc.push('<a href="#' + id + '">' + title + '</a>');
  return '<h2 id="' + id + '">' + title + '</h2>';
});
let figures = 0;
html = html.replace(/<p><img src="([^"]+)" alt="([^"]*)"\s*\/?><\/p>/g, (_, src, alt) => {
  const absolute = path.resolve(docs, src);
  if (!absolute.startsWith(docs + path.sep)) throw new Error('Image outside docs: ' + src);
  const base64 = fs.readFileSync(absolute).toString('base64'); figures++;
  return '<figure><img src="data:image/png;base64,' + base64 + '" alt="' + alt + '"><figcaption>' + alt + '</figcaption></figure>';
});
html = html.replace(/<table>/g, '<div class="table"><table>').replace(/<\/table>/g, '</table></div>');
html = html.replace('<h2 id="section-1">', '<div class="print-index"><h2>目录</h2>' + toc.join('') + '</div><h2 id="section-1">');
const page = '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>推箱子游戏与关卡编辑器操作手册</title><style>' +
`
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;color:#20262c;background:#f5f6f7;font:16px/1.8 "Microsoft YaHei","Noto Sans SC",Arial,sans-serif}
nav{position:fixed;inset:0 auto 0 0;width:272px;overflow:auto;background:#18282e;padding:28px 20px;color:#fff}
nav strong{font-size:20px;display:block;margin-bottom:10px}nav small{color:#b8c9cd}nav a{display:block;padding:6px 0;color:#d2e1e5;font-size:13px;line-height:1.6;text-decoration:none}nav a:hover{color:#ffc56c}
main{margin:0 28px 0 296px;max-width:1220px;background:white;padding:40px 42px 80px}h1{color:#000;font-size:32px;line-height:1.4;margin:0 0 18px}h2{color:#000;font-size:25px;margin:56px 0 20px;scroll-margin-top:20px}h3{color:#000}
p,li{overflow-wrap:anywhere}p{margin:14px 0}li{margin:8px 0}a{color:#116376}figure{margin:24px 0 28px;break-inside:avoid}figure img{display:block;max-width:100%;height:auto;margin:auto;border:1px solid #d6dfe2}figure a{display:block}figcaption{font-size:13px;line-height:1.6;color:#56616b;margin-top:9px}
.table{overflow:auto;margin:24px 0}table{border-collapse:collapse;width:100%;font-size:14px;line-height:1.75}th,td{border:1px solid #d9d9d9;padding:10px 13px;text-align:left;vertical-align:middle}th{background:#e7eff1;color:#000}tbody tr:nth-child(even){background:#f8fafb}td:first-child{min-width:135px}thead{display:table-header-group}
code{background:#eef2f4;padding:2px 5px;border-radius:3px;font-size:.9em}pre{background:#f1f4f6;padding:18px;overflow:auto;line-height:1.5}pre code{padding:0}
.controls{margin:0 0 30px;display:flex;gap:12px;align-items:center}.print-index{display:none}button{font:inherit;font-size:14px;border:1px solid #cad5d8;background:#fff;padding:7px 16px;cursor:pointer}button:hover{background:#eaf2f4}.controls span{font-size:13px;color:#697780}
@media(max-width:1000px){nav{position:static;width:auto;max-height:240px}main{margin:0;padding:24px}h1{font-size:27px}}
@media print{@page{size:A4 portrait;margin:14mm 15mm}body{background:#fff;font-size:11pt;line-height:1.6}nav,.controls{display:none}main{margin:0;max-width:none;padding:0}h1{font-size:25pt}h2{font-size:18pt;margin:20pt 0 12pt;break-after:avoid}p{orphans:3;widows:3}h2+p,p:has(+figure){break-after:avoid}li{margin:4pt 0}figure{margin:12pt 0 16pt;break-inside:avoid}figure img{max-height:142mm;max-width:100%;width:auto}figcaption{font-size:9pt}table{font-size:10pt;line-height:1.5}th,td{padding:7pt 9pt}tr{break-inside:avoid}.table{overflow:visible;margin:12pt 0;break-inside:avoid}a{color:inherit;text-decoration:none}pre{break-inside:avoid}.print-index{display:block;columns:2;column-gap:20pt;break-after:page;margin-top:20pt}.print-index h2{column-span:all;font-size:16pt;margin:0 0 12pt}.print-index a{display:block;font-size:10pt;line-height:1.5;padding:4pt 0;break-inside:avoid}*{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
` + '</style></head><body><nav><strong>推箱子操作手册</strong><small>0.14.2 · 图文完整版本</small>' + toc.join('') +
'</nav><main><div class="controls"><button onclick="window.print()">打印或保存为 PDF</button><span>图片已内嵌，可离线阅读；放大浏览器查看原图细节。</span></div>' + html + '</main></body></html>';
fs.writeFileSync(path.join(docs, 'Sokoban-User-Manual.html'), page);
console.log('Built standalone manual: ' + toc.length + ' sections, ' + figures + ' embedded figures.');
