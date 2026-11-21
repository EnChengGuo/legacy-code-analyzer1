(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var cy = null, lastData = null, timer = null, FAR_ZOOM = 0.45;
  var notesStore = [], pending = null, noteColor = CA.palette[0]; // 便利貼跨重繪保留;pending=載入的版面(位置不重排)
  function say(t) { $('msg').textContent = t || ''; }

  function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }

  var CATS = {
    '繼承': { label: '繼承', note: '空心三角' }, '實作': { label: '實作', note: '虛線空心三角' },
    '組合': { label: '組合', note: '實心菱形' }, '聚合': { label: '聚合', note: '空心菱形' },
    '關聯': { label: '關聯', note: '箭頭' }, '依賴': { label: '依賴', note: '虛線箭頭' }, '巢狀': { label: '巢狀', note: '點線' }
  };

  function renderLegend() {
    var c = { inh: css('--c-inh'), own: css('--c-own'), dep: css('--c-dep'), nest: css('--c-nest') };
    $('legend').innerHTML =
      '<i style="border-color:' + c.inh + '"></i>繼承/實作(空心三角,實作為虛線)' +
      '<i style="border-color:' + c.own + '"></i>組合(實心菱形)/聚合(空心菱形)/關聯(箭頭)' +
      '<i style="border-color:' + c.dep + ';border-top-style:dashed"></i>依賴' +
      '<i style="border-color:' + c.nest + ';border-top-style:dotted"></i>巢狀(外層→內層)';
  }

  function query() {
    var p = new URLSearchParams();
    p.set('ns', $('ns').value); p.set('focus', $('focus').value.trim());
    ['inh', 'own', 'dep', 'nest', 'topn', 'max'].forEach(function (k) { p.set(k, $(k).value); });
    ['showInh', 'showOwn', 'showDep', 'showNest', 'hidePrivate'].forEach(function (k) { p.set(k, $(k).checked ? '1' : '0'); });
    return p.toString();
  }

  function setStatus(t) { $('status').textContent = t; }
  function notice(t) { var n = $('notice'); n.hidden = !t; n.textContent = t || ''; }

  // ----- 類別方塊文字 -----
  function boxLabel(n) {
    var tag = n.kind === '介面' ? '«介面» ' : n.kind === '列舉' ? '«列舉» ' : n.kind === '結構' ? '«結構» ' : '';
    var lines = [tag + n.name], rule = '──────────────';
    [['enums', n.enums, n.enumsMore], ['fields', n.fields, n.fieldsMore], ['props', n.props, n.propsMore], ['methods', n.methods, n.methodsMore]]
      .forEach(function (s) {
        if (!s[1].length && !s[2]) return;
        lines.push(rule);
        s[1].forEach(function (x) { lines.push(x); });
        if (s[2]) lines.push('… 另 ' + s[2] + ' 項');
      });
    return lines.join('\n');
  }

  function styles() {
    var inh = css('--c-inh'), own = css('--c-own'), dep = css('--c-dep'), nest = css('--c-nest');
    var fg = css('--fg'), node = css('--node'), nb = css('--nodeb'), focus = css('--focus');
    return [
      { selector: 'node', style: {
        'shape': 'round-rectangle', 'background-color': node, 'border-width': 1, 'border-color': nb,
        'label': 'data(full)', 'color': fg, 'font-size': 11, 'font-family': 'Consolas, "Microsoft JhengHei", monospace',
        'text-wrap': 'wrap', 'text-justification': 'left', 'text-valign': 'center', 'text-halign': 'center',
        'width': 'label', 'height': 'label', 'padding': '10px' } },
      { selector: 'node.far, node.names', style: { 'label': 'data(short)', 'font-size': 14, 'text-justification': 'center', 'padding': '12px' } },
      { selector: 'node[?focus]', style: { 'border-width': 3, 'border-color': focus } },
      { selector: 'node.hit', style: { 'border-width': 3, 'border-color': '#f59e0b', 'background-color': css('--hit') } },
      { selector: 'edge', style: { 'curve-style': 'bezier', 'width': 1.4, 'line-color': dep, 'target-arrow-color': dep, 'arrow-scale': 1.2 } },
      { selector: 'edge[cat="繼承"]', style: { 'line-color': inh, 'target-arrow-color': inh, 'target-arrow-shape': 'triangle', 'target-arrow-fill': 'hollow' } },
      { selector: 'edge[cat="實作"]', style: { 'line-color': inh, 'target-arrow-color': inh, 'target-arrow-shape': 'triangle', 'target-arrow-fill': 'hollow', 'line-style': 'dashed' } },
      { selector: 'edge[cat="組合"]', style: { 'line-color': own, 'source-arrow-color': own, 'source-arrow-shape': 'diamond', 'source-arrow-fill': 'filled', 'target-arrow-shape': 'none' } },
      { selector: 'edge[cat="聚合"]', style: { 'line-color': own, 'source-arrow-color': own, 'source-arrow-shape': 'diamond', 'source-arrow-fill': 'hollow', 'target-arrow-shape': 'none' } },
      { selector: 'edge[cat="關聯"]', style: { 'line-color': own, 'target-arrow-color': own, 'target-arrow-shape': 'vee' } },
      { selector: 'edge[cat="依賴"]', style: { 'line-color': dep, 'target-arrow-color': dep, 'target-arrow-shape': 'vee', 'line-style': 'dashed' } },
      { selector: 'edge[cat="巢狀"]', style: { 'line-color': nest, 'line-style': 'dotted', 'width': 2, 'target-arrow-shape': 'none', 'source-arrow-shape': 'circle', 'source-arrow-color': nest } },
      { selector: '.faded', style: { 'opacity': 0.12 } }
    ].concat(CA.noteStyle);
  }

  function applyZoomLabels() {
    if (!cy) return;
    var far = $('view').value === 'graph' || cy.zoom() < FAR_ZOOM;
    cy.batch(function () { cy.nodes('[kind="class"]').toggleClass('far', far); });
  }

  function layout(fit) {
    if (!cy) return;
    cy.elements().not('[kind="note"]').layout({ name: 'dagre', rankDir: 'BT', nodeSep: 36, rankSep: 70, edgeSep: 12, fit: fit !== false, padding: 30, animate: false }).run();
  }

  function draw(d) {
    lastData = d;
    var els = [];
    d.nodes.forEach(function (n) {
      els.push({ group: 'nodes', data: { id: n.id, kind: 'class', full: boxLabel(n), short: n.name, name: n.name, ns: n.ns, focus: n.focus, file: n.file, line: n.line,
        text: (n.name + ' ' + n.fields.concat(n.props, n.methods).join(' ')).toLowerCase() } });
    });
    d.edges.forEach(function (e) { els.push({ group: 'edges', data: { id: e.id, source: e.source, target: e.target, cat: e.cat } }); });
    if (cy) { notesStore = CA.serializeNotes(cy); cy.destroy(); }
    cy = cytoscape({ container: $('cy'), elements: els, style: styles(), wheelSensitivity: 0.25, minZoom: 0.05, maxZoom: 3, boxSelectionEnabled: true });
    cy.on('zoom', applyZoomLabels);
    cy.on('tap', 'node[kind="class"]', function (ev) { highlight(ev.target); });
    cy.on('tap', function (ev) { if (ev.target === cy) clearHighlight(); });
    cy.on('dbltap', 'node[kind="class"]', function (ev) { $('focus').value = ev.target.data('name'); schedule(0); });
    CA.bindNotes(cy, function () { notesStore = CA.serializeNotes(cy); });
    $('view').value === 'graph' && cy.nodes('[kind="class"]').addClass('names');
    cy.resize();
    if (pending && pending.positions) { // 載入的版面:沿用儲存的位置,不重新排版
      var miss = 0;
      cy.nodes('[kind="class"]').forEach(function (n) { var p = pending.positions[n.id()]; if (p) n.position(p); else miss++; });
      if (miss) layout(true); else if (pending.zoom) { cy.zoom(pending.zoom); cy.pan(pending.pan); } else cy.fit(undefined, 30);
      notesStore = pending.notes || notesStore; pending = null;
    } else layout(true);
    CA.restoreNotes(cy, notesStore);
    applyZoomLabels();
    var msg = d.nodes.length + ' 個類別、' + d.edges.length + ' 條關係(方案共 ' + d.total + ' 個類別)';
    setStatus(msg);
    notice(d.truncated ? '類別太多,只顯示關聯最多的前 ' + $('max').value + ' 個。請縮小範圍(命名空間)或指定焦點類別。' : (d.error || ''));
    doFind();
    var sel = new URLSearchParams(location.search).get('select'); // 網址參數 select=類別名稱:自動選取並顯示資訊欄(截圖與分享連結用)
    if (sel) { var hit = cy.nodes('[kind="class"]').filter(function (n) { return n.data('name') === sel; }); if (hit.length) { hit.select(); highlight(hit[0]); } }
  }

  // 點選目標類別:不相干的類別與線變淡,右側顯示符號資訊(含最後編輯者)
  function highlight(n) {
    cy.batch(function () { cy.elements().addClass('faded'); n.closedNeighborhood().removeClass('faded'); });
    showInfo(n.id());
  }

  function showInfo(key) {
    var box = $('info'), body = $('infoBody'); box.hidden = false; body.innerHTML = '<div class="mut small">查詢中…</div>';
    fetch('/api/symbol?key=' + encodeURIComponent(key)).then(function (r) { return r.json(); }).then(function (d) {
      if (d.error) { body.innerHTML = '<div class="mut">' + esc(d.error) + '</div>'; return; }
      var h = '<h3>' + esc(d.display) + '</h3><div class="kv"><b>' + esc(d.kind) + '</b> · ' + esc(d.file.split('\\').pop()) + ':' + d.line + '–' + d.endLine + '</div>';
      if (d.doc) h += '<div class="kv">' + esc(d.doc) + '</div>';
      if (d.type) h += '<div class="kv"><b>類別度量</b> WMC ' + d.type.wmc + ' · Ca ' + d.type.ca + ' · Ce ' + d.type.ce + ' · ' + d.type.members + ' 個成員 · ' + d.type.loc + ' 行</div>';
      if (d.member) h += '<div class="kv"><b>方法度量</b> CC ' + d.member.cc + ' · 巢狀 ' + d.member.nesting + ' · ' + d.member.loc + ' 行 · MI ' + d.member.mi + '</div>';
      var e = d.editors;
      h += '<h3 style="font-size:13px">最後編輯者</h3>';
      if (!e) h += '<div class="mut small">未查詢</div>';
      else if (e.last) {
        h += '<div class="kv">' + esc(e.last.author) + ' <span class="mut">' + esc(e.last.date) + ' ' + esc(e.last.hash) + '</span><br><span class="mut">' + esc(e.last.subject) + '</span></div>';
        if (e.previous.length) h += '<div class="kv"><b>上幾代:</b>' + e.previous.map(function (p) { return esc(p.author) + '(' + esc(p.date) + ')'; }).join('、') + '</div>';
        if (e.note) h += '<div class="mut small">' + esc(e.note) + '</div>';
      } else h += '<div class="mut small">' + esc(e.note || '沒有紀錄') + '</div>';
      if (d.kind !== '方法' && d.kind !== '屬性' && d.kind !== '欄位' && d.kind !== '建構子') {
        h += '<div class="kv"><b>沒寫註解的成員:</b>' + d.uncommented + ' 個 <button id="debtBtn" type="button" style="padding:1px 8px">查冤親債主</button></div><div id="debtOut"></div>' +
          '<div class="kv"><a href="/god?name=' + encodeURIComponent(d.display) + '">到 God Class 解剖 →</a></div>';
      }
      body.innerHTML = h;
      var btn = $('debtBtn');
      if (btn) btn.onclick = function () {
        $('debtOut').innerHTML = '<div class="mut small">查詢中…</div>';
        fetch('/api/debtors?name=' + encodeURIComponent(d.display) + '&limit=30').then(function (r) { return r.json(); }).then(function (x) {
          if (x.error) { $('debtOut').innerHTML = '<div class="mut small">' + esc(x.error) + '</div>'; return; }
          $('debtOut').innerHTML = '<table>' + x.items.map(function (i) {
            var ed = i.editors, who = !ed ? '' : ed.last ? esc(ed.last.author) + ' <span class="mut">' + esc(ed.last.date) + '</span>' + (ed.previous.length ? '<br><span class="mut">前:' + ed.previous.map(function (p) { return esc(p.author); }).join('、') + '</span>' : '') : '<span class="mut">' + esc(ed.note || '') + '</span>';
            return '<tr><td>' + esc(i.name) + '</td><td>' + who + '</td></tr>';
          }).join('') + '</table>' + (x.total > x.items.length ? '<div class="mut small">只列前 ' + x.items.length + ' 個,共 ' + x.total + ' 個。</div>' : '');
        });
      };
    }).catch(function (e) { body.innerHTML = '<div class="mut">失敗:' + esc(e.message) + '</div>'; });
  }
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  function clearHighlight() { cy.elements().removeClass('faded'); doFind(); }

  function doFind() {
    if (!cy) return;
    var t = $('find').value.trim().toLowerCase();
    cy.batch(function () {
      cy.nodes().removeClass('hit');
      if (!t) { cy.elements().removeClass('faded'); return; }
      var hits = cy.nodes('[kind="class"]').filter(function (n) { return n.data('text').indexOf(t) >= 0; });
      cy.elements().addClass('faded'); hits.addClass('hit').removeClass('faded');
    });
  }

  // ----- 資料載入 -----
  function load() {
    var v = $('view').value;
    setStatus('載入中…');
    if (v === 'mermaid') {
      $('cy').hidden = true; $('mm').hidden = false;
      return fetch('/api/mermaid?' + query()).then(function (r) { return r.json(); }).then(renderMermaid).catch(fail);
    }
    $('cy').hidden = false; $('mm').hidden = true;
    return fetch('/api/diagram?' + query()).then(function (r) { return r.json(); }).then(function (d) {
      if (d.error && !d.nodes.length) { notice(d.error); setStatus(''); if (cy) { cy.destroy(); cy = null; } return; }
      draw(d);
    }).catch(fail);
  }
  function fail(e) { setStatus(''); notice('載入失敗:' + e.message); }

  function renderMermaid(d) {
    notice(d.error || (d.truncated ? '類別太多,只顯示部分。請縮小範圍或指定焦點類別。' : ''));
    $('mmSrc').value = d.text; $('mmOut').innerHTML = ''; $('mmMsg').textContent = '';
    if (!d.text) { setStatus(''); return; }
    try {
      mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', theme: matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'default' });
      Promise.resolve(mermaid.render('mm' + Date.now(), d.text)).then(function (r) {
        $('mmOut').innerHTML = r.svg; setStatus('Mermaid 圖已產生');
      }).catch(function (e) { $('mmMsg').textContent = 'Mermaid 繪製失敗:' + (e && e.message || e) + '(下方文字仍可複製)'; setStatus(''); });
    } catch (e) { $('mmMsg').textContent = 'Mermaid 繪製失敗:' + e.message; }
  }

  function schedule(ms) { clearTimeout(timer); timer = setTimeout(load, ms === undefined ? 250 : ms); }

  // ----- 檔案大綱(可搜尋) -----
  var outlineData = [];
  function shortFile(p) { var a = p.replace(/\\/g, '/').split('/'); return a.slice(-2).join('/'); }
  function renderOutline() {
    var t = $('olSearch').value.trim().toLowerCase(), html = [], n = 0;
    outlineData.forEach(function (f) {
      var fileHit = f.file.toLowerCase().indexOf(t) >= 0;
      var ts = f.types.filter(function (x) {
        return !t || fileHit || x.name.toLowerCase().indexOf(t) >= 0 ||
          x.members.some(function (m) { return m.toLowerCase().indexOf(t) >= 0; });
      });
      if (!ts.length) return;
      html.push('<div class="f" title="' + esc(f.file) + '">' + esc(shortFile(f.file)) + '</div>');
      ts.forEach(function (x) {
        n++;
        var memHit = t && !fileHit && x.name.toLowerCase().indexOf(t) < 0
          ? x.members.filter(function (m) { return m.toLowerCase().indexOf(t) >= 0; }).slice(0, 3).join('、') : '';
        html.push('<div class="t' + ($('focus').value === x.name ? ' on' : '') + '" data-n="' + esc(x.name) + '"><span>' + esc(x.name) +
          (memHit ? ' <span class="k">(含 ' + esc(memHit) + ')</span>' : '') + '</span><span class="k">' + esc(x.kind) + ' :' + x.line + '</span></div>');
      });
    });
    $('outline').innerHTML = html.join('') || '<div class="mut small">沒有符合的項目</div>';
    $('olCount').textContent = t ? '找到 ' + n + ' 個類別' : '共 ' + n + ' 個類別(點選設為焦點)';
  }
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }

  // ----- 啟動 -----
  function init() {
    renderLegend();
    fetch('/api/meta').then(function (r) { return r.json(); }).then(function (m) {
      m.namespaces.forEach(function (x) { var o = document.createElement('option'); o.value = x.name; o.textContent = x.name + ' (' + x.count + ')'; $('ns').appendChild(o); });
    });
    fetch('/api/outline').then(function (r) { return r.json(); }).then(function (o) { outlineData = o; renderOutline(); });
    $('olSearch').addEventListener('input', renderOutline);
    $('outline').addEventListener('click', function (e) {
      var t = e.target.closest('.t'); if (!t) return;
      $('focus').value = t.getAttribute('data-n'); renderOutline(); schedule(0);
    });
    ['view', 'ns', 'inh', 'own', 'dep', 'nest', 'topn', 'max', 'showInh', 'showOwn', 'showDep', 'showNest', 'hidePrivate'].forEach(function (k) {
      $(k).addEventListener('change', function () { schedule(); });
    });
    $('focus').addEventListener('keydown', function (e) { if (e.key === 'Enter') schedule(0); });
    $('focus').addEventListener('change', function () { renderOutline(); schedule(0); });
    $('clearFocus').addEventListener('click', function () { $('focus').value = ''; renderOutline(); schedule(0); });
    $('find').addEventListener('input', doFind);
    $('find').addEventListener('keydown', function (e) {
      if (e.key !== 'Enter' || !cy) return; var h = cy.nodes('.hit'); if (h.length) cy.animate({ center: { eles: h[0] }, zoom: Math.max(cy.zoom(), 0.9) }, { duration: 200 });
    });
    $('infoClose').addEventListener('click', function () { $('info').hidden = true; });
    $('relayout').addEventListener('click', function () { layout(true); });
    $('fit').addEventListener('click', function () { if (cy) cy.fit(undefined, 30); });
    $('copyMm').addEventListener('click', function () {
      var s = $('mmSrc'); s.select();
      try { document.execCommand('copy'); $('mmMsg').textContent = '已複製'; } catch (e) { $('mmMsg').textContent = '請手動複製下方文字'; }
    });
    // 便利貼
    $('addNote').addEventListener('click', function () {
      if (!cy) { say('請先產生圖'); return; }
      CA.addNote(cy, { color: noteColor }).select(); notesStore = CA.serializeNotes(cy);
    });
    $('noteColors').innerHTML = CA.palette.map(function (c) { return '<span class="sw" data-c="' + c + '" style="display:inline-block;width:20px;height:20px;border-radius:4px;border:1px solid #0003;cursor:pointer;vertical-align:middle;margin-right:3px;background:' + c + '" title="便利貼顏色"></span>'; }).join('');
    $('noteColors').addEventListener('click', function (e) {
      var c = e.target.getAttribute && e.target.getAttribute('data-c'); if (!c) return;
      noteColor = c; if (!cy || !CA.colorSelectedNotes(cy, c)) say('之後新貼的便利貼會用這個顏色'); else notesStore = CA.serializeNotes(cy);
    });
    // 版面存取:存圖上的位置、便利貼、縮放、以及上方的各項設定
    var CTRL = ['view', 'ns', 'focus', 'inh', 'own', 'dep', 'nest', 'topn', 'max'], CHK = ['showInh', 'showOwn', 'showDep', 'showNest', 'hidePrivate'];
    function collect() {
      var pos = {}; if (cy) cy.nodes('[kind="class"]').forEach(function (n) { pos[n.id()] = n.position(); });
      var c = {}; CTRL.forEach(function (k) { c[k] = $(k).value; }); CHK.forEach(function (k) { c[k] = $(k).checked; });
      return { version: 1, page: 'classes', controls: c, positions: pos, notes: CA.serializeNotes(cy), zoom: cy ? cy.zoom() : 1, pan: cy ? cy.pan() : { x: 0, y: 0 } };
    }
    function apply(o) {
      var c = o.controls || {};
      CTRL.forEach(function (k) { if (c[k] !== undefined) $(k).value = c[k]; }); CHK.forEach(function (k) { if (c[k] !== undefined) $(k).checked = !!c[k]; });
      pending = o; renderOutline(); load();
    }
    CA.wireLayoutUI('classes', { list: 'layList', save: 'laySave', load: 'layLoad', del: 'layDel', exp: 'layExp', imp: 'layImp' }, collect, apply, say);
    $('svg').addEventListener('click', function () {
      if ($('view').value === 'mermaid') {
        var sv = $('mmOut').querySelector('svg'); if (!sv) { say('Mermaid 圖還沒畫好'); return; }
        CA.download('mermaid圖.svg', '<?xml version="1.0" encoding="UTF-8"?>\n' + new XMLSerializer().serializeToString(sv), 'image/svg+xml;charset=utf-8'); return;
      }
      var s = cy && CA.svg(cy); if (!s) { say('目前沒有圖可下載'); return; }
      CA.download('類別圖.svg', s, 'image/svg+xml;charset=utf-8'); say('已下載 SVG');
    });
    window.__cyGet = function () { return cy; };
    var u = new URLSearchParams(location.search);
    if (u.get('focus')) $('focus').value = u.get('focus');
    load();
  }
  init();
})();
