// 共用:便利貼、版面存取、SVG 匯出。類別圖頁與流程畫布頁都用這份。
(function () {
  'use strict';
  var CA = window.CA = {};

  CA.palette = ['#fde68a', '#fbcfe8', '#bfdbfe', '#bbf7d0', '#fed7aa', '#ddd6fe', '#e5e7eb'];
  CA.noteSeq = 0;

  CA.esc = function (s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); };

  CA.download = function (name, content, mime) {
    var a = document.createElement('a');
    a.href = URL.createObjectURL(content instanceof Blob ? content : new Blob([content], { type: mime || 'text/plain;charset=utf-8' }));
    a.download = name; document.body.appendChild(a); a.click(); a.remove();
  };

  // ---------- 便利貼 ----------
  CA.noteStyle = [
    { selector: 'node[kind="note"]', style: {
      'shape': 'round-rectangle', 'background-color': 'data(color)', 'border-width': 1, 'border-color': '#b45309',
      'label': 'data(label)', 'color': '#1f2430', 'font-size': 12, 'font-family': '"Microsoft JhengHei", sans-serif',
      'text-wrap': 'wrap', 'text-max-width': 170, 'text-valign': 'center', 'text-halign': 'center',
      'width': 'label', 'height': 'label', 'padding': '12px', 'z-index': 9999, 'z-index-compare': 'manual' } }
  ];

  CA.addNote = function (cy, o) {
    o = o || {};
    var id = o.id || ('note' + (++CA.noteSeq) + '_' + Date.now().toString(36));
    var ext = cy.extent();
    var pos = o.pos || { x: (ext.x1 + ext.x2) / 2 + (CA.noteSeq % 5) * 18, y: (ext.y1 + ext.y2) / 2 + (CA.noteSeq % 5) * 18 };
    return cy.add({ group: 'nodes', data: { id: id, kind: 'note', label: o.text || '新便利貼(點兩下編輯)', color: o.color || CA.palette[0] }, position: pos });
  };

  CA.serializeNotes = function (cy) {
    return cy ? cy.nodes('[kind="note"]').map(function (n) { var p = n.position(); return { id: n.id(), text: n.data('label'), color: n.data('color'), x: p.x, y: p.y }; }) : [];
  };

  CA.restoreNotes = function (cy, arr) {
    (arr || []).forEach(function (n) { if (cy.getElementById(n.id).empty()) CA.addNote(cy, { id: n.id, text: n.text, color: n.color, pos: { x: n.x, y: n.y } }); });
  };

  // 點兩下編輯文字;回呼讓頁面知道有變動
  CA.bindNotes = function (cy, onChange) {
    cy.on('dbltap', 'node[kind="note"]', function (e) {
      var n = e.target, t = window.prompt('便利貼文字(留空會刪除這張):', n.data('label'));
      if (t === null) return;
      if (t.trim() === '') n.remove(); else n.data('label', t);
      if (onChange) onChange();
    });
    cy.on('free', 'node[kind="note"]', function () { if (onChange) onChange(); });
  };

  CA.colorSelectedNotes = function (cy, color) {
    var sel = cy.nodes('[kind="note"]:selected');
    sel.forEach(function (n) { n.data('color', color); });
    return sel.length;
  };

  // ---------- 版面存取(存在使用者的 AppData,也可匯出匯入檔案) ----------
  CA.layouts = {
    list: function (page) { return fetch('/api/layout?page=' + encodeURIComponent(page)).then(function (r) { return r.json(); }); },
    get: function (page, name) {
      return fetch('/api/layout?page=' + encodeURIComponent(page) + '&name=' + encodeURIComponent(name)).then(function (r) {
        if (!r.ok) throw new Error('找不到這個版面'); return r.json();
      });
    },
    save: function (page, name, obj) {
      return fetch('/api/layout?page=' + encodeURIComponent(page) + '&name=' + encodeURIComponent(name), { method: 'POST', headers: { 'X-CodeAtlas': '1' }, body: JSON.stringify(obj) })
        .then(function (r) { if (!r.ok) throw new Error('儲存失敗'); return r.json(); });
    },
    del: function (page, name) {
      return fetch('/api/layout?page=' + encodeURIComponent(page) + '&name=' + encodeURIComponent(name), { method: 'DELETE', headers: { 'X-CodeAtlas': '1' } });
    }
  };

  // 一組「存/載/刪/匯出/匯入」控制項的共用接線;page 是頁面代號,collect() 回傳要存的物件,apply(obj) 套用
  CA.wireLayoutUI = function (page, ids, collect, apply, say) {
    var $ = function (i) { return document.getElementById(ids[i]); };
    function refresh(sel) {
      return CA.layouts.list(page).then(function (names) {
        $('list').innerHTML = '<option value="">(選擇版面)</option>' + names.map(function (n) { return '<option>' + CA.esc(n) + '</option>'; }).join('');
        if (sel) $('list').value = sel;
      });
    }
    $('save').addEventListener('click', function () {
      var n = window.prompt('版面名稱:', $('list').value || '');
      if (!n) return;
      CA.layouts.save(page, n, collect()).then(function () { say('已儲存版面「' + n + '」'); refresh(n.replace(/[^\w\-\u0080-￿]/g, '')); }).catch(function (e) { say(e.message); });
    });
    $('load').addEventListener('click', function () {
      var n = $('list').value; if (!n) { say('請先選擇一個版面'); return; }
      CA.layouts.get(page, n).then(function (o) { apply(o); say('已載入版面「' + n + '」'); }).catch(function (e) { say(e.message); });
    });
    $('del').addEventListener('click', function () {
      var n = $('list').value; if (!n) return;
      if (!window.confirm('確定刪除版面「' + n + '」?')) return;
      CA.layouts.del(page, n).then(function () { say('已刪除'); refresh(); });
    });
    $('exp').addEventListener('click', function () { CA.download(page + '-版面.json', JSON.stringify(collect(), null, 1), 'application/json;charset=utf-8'); say('已匯出版面檔'); });
    $('imp').addEventListener('change', function (e) {
      var f = e.target.files[0]; if (!f) return;
      var r = new FileReader();
      r.onload = function () { try { apply(JSON.parse(r.result)); say('已匯入版面檔'); } catch (x) { say('版面檔格式不對:' + x.message); } };
      r.readAsText(f); e.target.value = '';
    });
    refresh();
  };

  // ---------- SVG 匯出:依目前畫面上的位置與樣式重新畫成向量圖 ----------
  function rgb(s) { return s || 'none'; }
  function lines(label) { return String(label == null ? '' : label).split('\n'); }

  function arrow(shape, fill, color, p, from, size) {
    if (!shape || shape === 'none') return '';
    var dx = p.x - from.x, dy = p.y - from.y, len = Math.sqrt(dx * dx + dy * dy) || 1, ux = dx / len, uy = dy / len, nx = -uy, ny = ux, s = size;
    var f = fill === 'hollow' ? 'var(--bg,#fff)' : color;
    function pt(a, b) { return (p.x - ux * a + nx * b).toFixed(1) + ',' + (p.y - uy * a + ny * b).toFixed(1); }
    if (shape === 'diamond') return '<polygon points="' + pt(0, 0) + ' ' + pt(s, s * 0.5) + ' ' + pt(s * 2, 0) + ' ' + pt(s, -s * 0.5) + '" fill="' + (fill === 'hollow' ? '#ffffff' : color) + '" stroke="' + color + '"/>';
    if (shape === 'vee') return '<polyline points="' + pt(s, s * 0.6) + ' ' + pt(0, 0) + ' ' + pt(s, -s * 0.6) + '" fill="none" stroke="' + color + '" stroke-width="1.6"/>';
    if (shape === 'circle') return '<circle cx="' + (p.x - ux * s * 0.5).toFixed(1) + '" cy="' + (p.y - uy * s * 0.5).toFixed(1) + '" r="' + (s * 0.5) + '" fill="' + color + '" stroke="' + color + '"/>';
    return '<polygon points="' + pt(0, 0) + ' ' + pt(s * 1.2, s * 0.6) + ' ' + pt(s * 1.2, -s * 0.6) + '" fill="' + (fill === 'hollow' ? '#ffffff' : color) + '" stroke="' + color + '"/>';
  }

  CA.svg = function (cy) {
    var all = cy.elements();
    if (all.empty()) return null;
    var bb = all.boundingBox({ includeLabels: true, includeOverlays: false }), m = 24;
    var x0 = bb.x1 - m, y0 = bb.y1 - m, w = bb.w + 2 * m, h = bb.h + 2 * m, out = [];
    var bg = getComputedStyle(document.body).backgroundColor || '#ffffff';
    out.push('<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + [x0, y0, w, h].map(function (v) { return v.toFixed(1); }).join(' ') + '" width="' + Math.ceil(w) + '" height="' + Math.ceil(h) + '">');
    out.push('<rect x="' + x0 + '" y="' + y0 + '" width="' + w + '" height="' + h + '" fill="' + bg + '"/>');

    // 外框(群組)先畫,由外往內
    cy.nodes(':parent').sort(function (a, b) { return a.ancestors().length - b.ancestors().length; }).forEach(function (n) {
      var p = n.position(), ow = n.outerWidth(), oh = n.outerHeight();
      out.push('<rect x="' + (p.x - ow / 2).toFixed(1) + '" y="' + (p.y - oh / 2).toFixed(1) + '" width="' + ow.toFixed(1) + '" height="' + oh.toFixed(1) + '" rx="8" fill="' + rgb(n.style('background-color')) +
        '" fill-opacity="' + n.style('background-opacity') + '" stroke="' + rgb(n.style('border-color')) + '" stroke-width="' + n.style('border-width') + '"/>');
      if (n.data('label')) out.push('<text x="' + (p.x - ow / 2 + 8).toFixed(1) + '" y="' + (p.y - oh / 2 + 15).toFixed(1) + '" font-size="12" font-weight="600" fill="#374151" font-family="Microsoft JhengHei, sans-serif">' + CA.esc(n.data('label')) + '</text>');
    });

    // 連線
    cy.edges().forEach(function (e) {
      var s = e.sourceEndpoint(), t = e.targetEndpoint(), color = e.style('line-color'), d;
      var cps = (e.controlPoints && e.controlPoints()) || [], sp = (e.segmentPoints && e.segmentPoints()) || [];
      var pts = [s].concat(sp.length ? sp : []).concat([t]);
      if (cps.length === 1) d = 'M' + s.x + ',' + s.y + ' Q' + cps[0].x + ',' + cps[0].y + ' ' + t.x + ',' + t.y;
      else if (cps.length > 1) d = 'M' + [s].concat(cps).concat([t]).map(function (p) { return p.x + ',' + p.y; }).join(' L');
      else if (pts.length > 2) d = 'M' + pts.map(function (p) { return p.x + ',' + p.y; }).join(' L');
      else {
        var mid = e.midpoint(), cx = 2 * mid.x - (s.x + t.x) / 2, cy2 = 2 * mid.y - (s.y + t.y) / 2;
        d = (Math.abs(cx - mid.x) + Math.abs(cy2 - mid.y)) < 0.5 ? 'M' + s.x + ',' + s.y + ' L' + t.x + ',' + t.y : 'M' + s.x + ',' + s.y + ' Q' + cx + ',' + cy2 + ' ' + t.x + ',' + t.y;
      }
      var dash = e.style('line-style') === 'dashed' ? ' stroke-dasharray="6 4"' : e.style('line-style') === 'dotted' ? ' stroke-dasharray="2 3"' : '';
      out.push('<path d="' + d + '" fill="none" stroke="' + color + '" stroke-width="' + e.style('width') + '"' + dash + '/>');
      var prevT = cps.length ? cps[cps.length - 1] : sp.length ? sp[sp.length - 1] : { x: 2 * t.x - e.midpoint().x, y: 2 * t.y - e.midpoint().y };
      var prevS = cps.length ? cps[0] : sp.length ? sp[0] : { x: 2 * s.x - e.midpoint().x, y: 2 * s.y - e.midpoint().y };
      var tcol = e.style('target-arrow-color') || color, scol = e.style('source-arrow-color') || color;
      out.push(arrow(e.style('target-arrow-shape'), e.style('target-arrow-fill'), tcol, t, { x: 2 * t.x - prevT.x, y: 2 * t.y - prevT.y }, 9));
      out.push(arrow(e.style('source-arrow-shape'), e.style('source-arrow-fill'), scol, s, { x: 2 * s.x - prevS.x, y: 2 * s.y - prevS.y }, 9));
      var lab = e.data('label');
      if (lab) { var mp = e.midpoint(); out.push('<text x="' + mp.x.toFixed(1) + '" y="' + (mp.y - 3).toFixed(1) + '" font-size="11" text-anchor="middle" fill="#6b7280" font-family="Microsoft JhengHei, sans-serif">' + CA.esc(lab) + '</text>'); }
    });

    // 節點
    cy.nodes().not(':parent').forEach(function (n) {
      var p = n.position(), ow = n.outerWidth(), oh = n.outerHeight(), shape = n.style('shape');
      var fill = rgb(n.style('background-color')), stroke = rgb(n.style('border-color')), sw = n.style('border-width'), op = n.style('opacity');
      var g = '<g opacity="' + op + '">';
      if (shape === 'diamond') g += '<polygon points="' + [[p.x, p.y - oh / 2], [p.x + ow / 2, p.y], [p.x, p.y + oh / 2], [p.x - ow / 2, p.y]].map(function (q) { return q[0].toFixed(1) + ',' + q[1].toFixed(1); }).join(' ') + '" fill="' + fill + '" stroke="' + stroke + '" stroke-width="' + sw + '"/>';
      else if (shape === 'ellipse') g += '<ellipse cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) + '" rx="' + (ow / 2).toFixed(1) + '" ry="' + (oh / 2).toFixed(1) + '" fill="' + fill + '" stroke="' + stroke + '" stroke-width="' + sw + '"/>';
      else g += '<rect x="' + (p.x - ow / 2).toFixed(1) + '" y="' + (p.y - oh / 2).toFixed(1) + '" width="' + ow.toFixed(1) + '" height="' + oh.toFixed(1) + '" rx="' + (shape === 'round-rectangle' ? 8 : 0) + '" fill="' + fill + '" stroke="' + stroke + '" stroke-width="' + sw + '"/>';
      var ls = lines(n.style('label')), fs = parseFloat(n.style('font-size')) || 12, lh = fs * 1.3, left = n.style('text-justification') === 'left';
      var y = p.y - (ls.length - 1) * lh / 2, fam = CA.esc(n.style('font-family').replace(/"/g, "'"));
      ls.forEach(function (t, i) {
        var tx = left ? p.x - ow / 2 + 10 : p.x;
        g += '<text x="' + tx.toFixed(1) + '" y="' + (y + i * lh + fs * 0.35).toFixed(1) + '" font-size="' + fs + '" text-anchor="' + (left ? 'start' : 'middle') + '" fill="' + n.style('color') + '" font-family="' + fam + '" xml:space="preserve">' + CA.esc(t) + '</text>';
      });
      out.push(g + '</g>');
    });
    out.push('</svg>');
    return '<?xml version="1.0" encoding="UTF-8"?>\n' + out.join('\n');
  };
})();
