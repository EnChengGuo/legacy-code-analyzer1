(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var data = null, tab = 'seq', syntax = 'Mermaid', texts = {}, current = '', seqNo = 0;

  var TABS = [['seq', '循序圖'], ['act', '活動圖'], ['tree', '呼叫樹'], ['state', '狀態圖'], ['data', '資料流']];

  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  function notice(t) { var n = $('notice'); n.hidden = !t; n.textContent = t || ''; }

  // 每個分頁可用的語法與文字來源
  function sources(t) {
    if (!data) return {};
    if (t === 'seq') return { Mermaid: data.seqMermaid, PlantUML: data.seqPuml };
    if (t === 'act') return { Mermaid: data.actMermaid, PlantUML: data.actPuml };
    if (t === 'tree') return { Mermaid: data.tree.mermaid, '縮排文字': data.tree.text };
    if (t === 'state') return data.state && !data.state.error ? { Mermaid: data.state.mermaid, PlantUML: data.state.puml } : {};
    if (t === 'data') return data.data && !data.data.error ? { Mermaid: data.data.mermaid, PlantUML: data.data.puml, '清單': data.data.rows.join('\n') } : {};
    return {};
  }

  function renderTabs() {
    $('tabs').innerHTML = '';
    TABS.forEach(function (t) {
      var b = document.createElement('button'); b.textContent = t[1]; b.className = t[0] === tab ? 'on' : '';
      var off = (t[0] === 'state' && !$('wantState').checked) || (t[0] === 'data' && !$('wantData').checked) || !data;
      b.disabled = off; b.title = off && data ? '請先勾選上方「產生" + t[1] + "」並重新產生' : '';
      b.onclick = function () { tab = t[0]; syntax = 'Mermaid'; show(); };
      $('tabs').appendChild(b);
    });
  }

  function show() {
    renderTabs();
    var s = sources(tab), keys = Object.keys(s);
    var tabErr = (tab === 'state' && data && data.state && data.state.error) || (tab === 'data' && data && data.data && data.data.error);
    if (!keys.length) {
      $('view').innerHTML = '<div class="msg ' + (tabErr ? 'err' : 'mut') + '">' + esc(tabErr || '這個分頁沒有內容。') + '</div>'; $('src').value = ''; $('syntax').innerHTML = ''; return;
    }
    if (keys.indexOf(syntax) < 0) syntax = keys[0];
    $('syntax').innerHTML = keys.map(function (k) { return '<option' + (k === syntax ? ' selected' : '') + '>' + esc(k) + '</option>'; }).join('');
    var key = current + '|' + tab + '|' + syntax;
    if (texts[key] === undefined) texts[key] = s[syntax];
    $('src').value = texts[key];
    draw();
  }

  function draw() {
    var txt = $('src').value, out = $('view');
    if (syntax === 'Mermaid') {
      out.innerHTML = '<div class="msg mut">繪製中…</div>';
      try {
        mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', flowchart: { useMaxWidth: false }, sequence: { useMaxWidth: false },
          theme: matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'default' });
        Promise.resolve(mermaid.render('mmf' + (++seqNo), txt)).then(function (r) { out.innerHTML = r.svg; $('toolMsg').textContent = ''; })
          .catch(function (e) { out.innerHTML = '<div class="msg err">Mermaid 繪製失敗:' + esc(e && e.message || e) + '<br>請修改右側文字後重繪。</div>'; });
      } catch (e) { out.innerHTML = '<div class="msg err">Mermaid 繪製失敗:' + esc(e.message) + '</div>'; }
    } else if (syntax === 'PlantUML') {
      out.innerHTML = '<div class="msg">PlantUML 需要 PlantUML 程式才能繪製;離線版不內建。請按「複製文字」或「下載文字」,貼到你有的 PlantUML 工具(例如 Visual Studio 的 PlantUML 擴充或 plantuml.jar)。<br><br>想直接在這裡看圖,請切換到 Mermaid。</div>';
    } else {
      out.innerHTML = '<pre class="msg" style="white-space:pre-wrap">' + esc(txt) + '</pre>';
    }
  }

  function run(name) {
    if (!name) return;
    current = name; $('status').textContent = '產生中…'; notice('');
    var p = new URLSearchParams({ name: name, expand: $('expand').value, depth: $('depth').value, ext: $('ext').checked ? '1' : '0',
      state: $('wantState').checked ? '1' : '0', dataflow: $('wantData').checked ? '1' : '0' });
    fetch('/api/flow?' + p).then(function (r) { return r.json(); }).then(function (d) {
      $('status').textContent = '';
      if (d.error) {
        data = null; renderTabs();
        $('view').innerHTML = '<div class="msg err">' + esc(d.error) + (d.candidates ? '<br>' + d.candidates.map(esc).join('<br>') : '') + '</div>'; return;
      }
      data = d; texts = {}; if (tab === 'state' && !$('wantState').checked) tab = 'seq'; if (tab === 'data' && !$('wantData').checked) tab = 'seq';
      $('status').textContent = d.name; show();
    }).catch(function (e) { $('status').textContent = ''; notice('載入失敗:' + e.message); });
  }

  // ----- 方法清單 -----
  var qTimer;
  function loadList() {
    fetch('/api/methods?q=' + encodeURIComponent($('q').value)).then(function (r) { return r.json(); }).then(function (l) {
      $('qCount').textContent = l.length >= 80 ? '顯示前 80 個,請輸入更多文字縮小範圍' : '共 ' + l.length + ' 個方法';
      $('methodList').innerHTML = l.map(function (m) {
        return '<div class="t' + (m.name === current ? ' on' : '') + '" data-n="' + esc(m.name) + '"><span>' + esc(m.name.replace(/^[^()]*\.(?=[^.(]+\.[^.(]+\()/, '')) +
          '</span><span class="k">' + esc(m.file) + ':' + m.line + '</span></div>';
      }).join('') || '<div class="mut small">沒有符合的方法</div>';
    });
  }

  function insertNote() {
    var t = $('note').value.trim(), ta = $('src'); if (!t) { $('toolMsg').textContent = '請先輸入敘述文字'; return; }
    var s = ta.value, line = null, safe = t.replace(/;/g, ',').replace(/#/g, '＃');
    if (tab === 'seq') {
      var id = (s.match(/participant\s+(\w+)/) || s.match(/actor\s+(\w+)/) || [])[1] || 'P1';
      line = syntax === 'Mermaid' ? '  Note over ' + id + ': ' + safe : 'note over ' + id + ' : ' + t;
    } else if (tab === 'act') {
      line = syntax === 'Mermaid' ? '  NOTE' + (++seqNo) + '["' + safe.replace(/"/g, "'") + '"]' : '  note right: ' + t;
    } else if (tab === 'data' && syntax === 'Mermaid') {
      line = '  NOTE' + (++seqNo) + '["' + safe.replace(/"/g, "'") + '"]';
    } else { $('toolMsg').textContent = '這個圖不支援插入敘述,請直接編輯文字'; return; }
    var pos = ta.selectionStart, ls = s.lastIndexOf('\n', pos - 1) + 1, le = s.indexOf('\n', pos); if (le < 0) le = s.length;
    ta.value = s.slice(0, le) + '\n' + line + s.slice(le); texts[current + '|' + tab + '|' + syntax] = ta.value;
    $('note').value = ''; $('toolMsg').textContent = '已插入' + (syntax === 'Mermaid' && tab === 'act' ? '(流程圖的敘述是獨立方塊,可自行加連線)' : ''); draw();
  }

  function download(name, blob) { var a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = name; document.body.appendChild(a); a.click(); a.remove(); }
  function baseName() { return (current.replace(/\(.*$/, '').split('.').slice(-2).join('_') || 'flow') + '_' + tab; }

  function init() {
    $('q').addEventListener('input', function () { clearTimeout(qTimer); qTimer = setTimeout(loadList, 200); });
    $('methodList').addEventListener('click', function (e) { var t = e.target.closest('.t'); if (!t) return; current = t.getAttribute('data-n'); loadList(); run(current); });
    $('go').addEventListener('click', function () { run(current); });
    $('syntax').addEventListener('change', function () { syntax = $('syntax').value; show(); });
    $('src').addEventListener('input', function () { texts[current + '|' + tab + '|' + syntax] = $('src').value; });
    $('redraw').addEventListener('click', draw);
    $('ins').addEventListener('click', insertNote);
    $('copy').addEventListener('click', function () { $('src').select(); try { document.execCommand('copy'); $('toolMsg').textContent = '已複製'; } catch (e) { $('toolMsg').textContent = '請手動複製'; } });
    $('dlText').addEventListener('click', function () {
      var ext = syntax === 'Mermaid' ? '.mmd' : syntax === 'PlantUML' ? '.puml' : '.txt';
      download(baseName() + ext, new Blob(['﻿' + $('src').value], { type: 'text/plain;charset=utf-8' }));
    });
    $('dlSvg').addEventListener('click', function () {
      var svg = $('view').querySelector('svg'); if (!svg) { $('toolMsg').textContent = '目前沒有可下載的圖(請用 Mermaid 語法)'; return; }
      var xml = new XMLSerializer().serializeToString(svg);
      download(baseName() + '.svg', new Blob(['<?xml version="1.0" encoding="UTF-8"?>\n' + xml], { type: 'image/svg+xml;charset=utf-8' }));
    });
    var up = new URLSearchParams(location.search); if (up.get('tab')) tab = up.get('tab'); if (up.get('state')) $('wantState').checked = true; if (up.get('data')) $('wantData').checked = true; var nm = up.get('name');
    if (nm) { current = nm; run(nm); }
    renderTabs(); loadList();
  }
  init();
})();
