(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var esc = CA.esc;
  var FLOW_COLORS = ['#2563eb', '#dc2626', '#16a34a', '#d97706', '#7c3aed', '#0d9488', '#db2777', '#4b5563'];
  var cy, flows = {}, flowSeq = 0, groupSeq = 0, draggingParent = false, qTimer;

  function say(t) { $('msg').textContent = t || ''; }
  function notice(t) { var n = $('notice'); n.hidden = !t; n.textContent = t || ''; }

  // ---------- 顏色 ----------
  function tint(hex, k) { // 與白色混合:k=0 原色,k=1 全白
    var n = parseInt(hex.slice(1), 16), r = n >> 16, g = (n >> 8) & 255, b = n & 255;
    function m(c) { return Math.round(c + (255 - c) * k); }
    return '#' + [m(r), m(g), m(b)].map(function (x) { return x.toString(16).padStart(2, '0'); }).join('');
  }

  function styles() {
    var fg = getComputedStyle(document.documentElement).getPropertyValue('--fg').trim() || '#1f2430';
    return [
      { selector: 'node[kind="step"]', style: {
        'shape': 'round-rectangle', 'background-color': 'data(bg)', 'border-width': 2, 'border-color': 'data(color)',
        'label': 'data(label)', 'color': '#1f2430', 'font-size': 12, 'font-family': '"Microsoft JhengHei", Consolas, sans-serif',
        'text-wrap': 'wrap', 'text-max-width': 190, 'text-valign': 'center', 'text-halign': 'center',
        'width': 'label', 'height': 'label', 'padding': '10px' } },
      { selector: 'node[shape="decision"]', style: { 'shape': 'diamond', 'padding': '26px', 'text-max-width': 150 } },
      { selector: 'node[shape="start"], node[shape="terminal"]', style: { 'shape': 'ellipse', 'padding': '12px' } },
      { selector: 'node[shape="end"]', style: { 'shape': 'ellipse', 'padding': '12px', 'border-style': 'double', 'border-width': 4 } },
      { selector: 'node[kind="flow"], node[kind="group"]', style: {
        'shape': 'round-rectangle', 'background-color': 'data(color)', 'background-opacity': 0.08, 'border-width': 2, 'border-color': 'data(color)',
        'label': 'data(label)', 'color': fg, 'font-size': 13, 'font-weight': 'bold', 'text-valign': 'top', 'text-halign': 'center',
        'text-margin-y': -4, 'padding': '22px' } },
      { selector: 'node[kind="group"]', style: { 'border-style': 'dashed', 'border-width': 1.5, 'background-opacity': 0.05 } },
      { selector: 'edge', style: { 'curve-style': 'bezier', 'width': 1.6, 'line-color': '#6b7280', 'target-arrow-color': '#6b7280', 'target-arrow-shape': 'triangle',
        'label': 'data(label)', 'font-size': 11, 'color': '#6b7280', 'text-background-color': '#ffffff', 'text-background-opacity': 0.85, 'text-background-padding': 2 } },
      { selector: 'edge[?back]', style: { 'curve-style': 'unbundled-bezier', 'control-point-distances': 70, 'control-point-weights': 0.5, 'line-style': 'dashed' } },
      { selector: ':selected', style: { 'overlay-color': '#2563eb', 'overlay-opacity': 0.18 } },
      { selector: '.faded', style: { 'opacity': 0.15 } }
    ].concat(CA.noteStyle);
  }

  // ---------- 排版:每張流程由上往下,分支往下長並向外擴;迴圈回頭線不參與排版 ----------
  function flowKids(fid) { return cy.nodes('[flow = "' + fid + '"]').filter('[kind="step"],[kind="group"]'); }

  function layoutFlow(fid, ox, oy) {
    var parent = cy.getElementById(fid);
    var steps = cy.nodes('[kind="step"][flow = "' + fid + '"]');
    if (steps.empty()) return;
    // 暫時拆掉外框與群組,避免 dagre 處理複合節點
    var oldParent = {}; steps.forEach(function (n) { oldParent[n.id()] = n.parent().id(); });
    steps.move({ parent: null });
    var edges = steps.edgesWith(steps), backs = edges.filter('[?back]'), fwd = edges.not(backs);
    var removed = backs.remove();
    steps.union(fwd).layout({ name: 'dagre', rankDir: 'TB', nodeSep: 34, rankSep: 52, edgeSep: 10, fit: false, animate: false, padding: 0 }).run();
    var bb = steps.boundingBox();
    if (ox === undefined) { ox = bb.x1; oy = bb.y1; }
    steps.shift({ x: ox - bb.x1, y: oy - bb.y1 });
    removed.restore();
    steps.forEach(function (n) { var p = oldParent[n.id()]; n.move({ parent: p && p !== 'undefined' && p !== '' ? p : fid }); });
  }

  function nextOrigin() {
    var fl = cy.nodes('[kind="flow"]');
    if (fl.empty()) return { x: 0, y: 0 };
    var bb = fl.boundingBox();
    return { x: bb.x2 + 90, y: bb.y1 + 22 };
  }

  // ---------- 加入流程 ----------
  function addFlow(g, name, color) {
    var fid = 'F' + (++flowSeq);
    color = color || FLOW_COLORS[(flowSeq - 1) % FLOW_COLORS.length];
    flows[fid] = { id: fid, name: name, color: color, dirty: false };
    var origin = nextOrigin(), els = [{ group: 'nodes', data: { id: fid, kind: 'flow', label: shortName(name), color: color } }];
    g.nodes.forEach(function (n) {
      els.push({ group: 'nodes', data: { id: fid + '_' + n.id, kind: 'step', shape: n.shape, label: n.text, flow: fid, color: color, bg: tint(color, 0.88), parent: fid } });
    });
    g.edges.forEach(function (e, i) {
      els.push({ group: 'edges', data: { id: fid + '_e' + i, source: fid + '_' + e.from, target: fid + '_' + e.to, label: e.label || '', back: !!e.back, flow: fid } });
    });
    cy.add(els);
    layoutFlow(fid, origin.x, origin.y);
    renderFlows(); cy.fit(undefined, 40);
    return fid;
  }

  function shortName(n) { var s = n.replace(/\(.*$/, ''); return s.split('.').slice(-2).join('.'); }

  function loadMethod(name) {
    say('載入 ' + shortName(name) + '…');
    fetch('/api/flow?name=' + encodeURIComponent(name) + '&expand=' + $('expand').value).then(function (r) { return r.json(); }).then(function (d) {
      if (d.error) { say(''); notice(d.error); return; }
      notice(''); addFlow(d.graph, d.name); say('已加入 ' + shortName(d.name));
    }).catch(function (e) { say(''); notice('載入失敗:' + e.message); });
  }

  // ---------- 流程清單 ----------
  function renderFlows() {
    var ids = Object.keys(flows);
    if (!ids.length) { $('flows').innerHTML = '<div class="mut small">還沒有流程。點上方的方法加入。</div>'; return; }
    $('flows').innerHTML = ids.map(function (id) {
      var f = flows[id], hidden = cy.getElementById(id).hasClass('hidden');
      return '<div class="fl" data-id="' + id + '"><input type="checkbox" class="pick" title="選取(合併用)"><input type="color" class="col" value="' + f.color + '" title="換色">' +
        '<span class="nm" title="' + esc(f.name) + '">' + esc(shortName(f.name)) + (f.dirty ? ' ✎' : '') + '</span>' +
        '<button class="vis">' + (hidden ? '顯示' : '隱藏') + '</button><button class="rel" title="重新排版這張">排</button><button class="del">✕</button></div>';
    }).join('');
  }

  function recolorFlow(id, color) {
    var f = flows[id]; if (!f) return; f.color = color;
    cy.getElementById(id).data('color', color);
    cy.nodes('[kind="step"][flow = "' + id + '"]').forEach(function (n) { n.data('color', color); n.data('bg', tint(color, 0.88)); });
  }

  function removeFlow(id) {
    cy.getElementById(id).descendants().remove(); cy.getElementById(id).remove(); delete flows[id]; renderFlows();
  }

  function setHidden(id, hide) {
    var p = cy.getElementById(id), all = p.union(p.descendants());
    hide ? all.addClass('hidden').style('display', 'none') : all.removeClass('hidden').style('display', 'element');
    renderFlows();
  }

  // ---------- 合併 ----------
  function mergeFlows(ids, dedupe) {
    var fid = 'F' + (++flowSeq), names = ids.map(function (i) { return shortName(flows[i].name); });
    var color = flows[ids[0]].color, first = cy.getElementById(ids[0]).boundingBox();
    flows[fid] = { id: fid, name: '合併:' + names.join(' + '), color: color, dirty: false };
    cy.add({ group: 'nodes', data: { id: fid, kind: 'flow', label: flows[fid].name, color: color } });
    // 先搬走所有子節點
    ids.forEach(function (i) { cy.getElementById(i).children().move({ parent: fid }); });
    cy.nodes('[kind="step"]').filter(function (n) { return ids.indexOf(n.data('flow')) >= 0; }).forEach(function (n) { n.data('flow', fid); });
    if (dedupe) {
      var canon = {};
      cy.nodes('[kind="step"][flow = "' + fid + '"]').forEach(function (n) {
        var k = n.data('shape') + '|' + n.data('label'), c = canon[k];
        if (!c) { canon[k] = n; return; }
        n.connectedEdges().forEach(function (e) {
          var s = e.source().id() === n.id() ? c.id() : e.source().id(), t = e.target().id() === n.id() ? c.id() : e.target().id();
          if (s !== t) cy.add({ group: 'edges', data: { id: 'm' + Math.random().toString(36).slice(2, 9), source: s, target: t, label: e.data('label'), back: e.data('back'), flow: fid } });
        });
        n.remove();
      });
      // 去除重複連線
      var seen = {};
      cy.edges('[flow = "' + fid + '"]').forEach(function (e) {
        var k = e.source().id() + '>' + e.target().id() + '|' + e.data('label');
        if (seen[k]) e.remove(); else seen[k] = true;
      });
    }
    ids.forEach(function (i) { cy.getElementById(i).remove(); delete flows[i]; });
    layoutFlow(fid, first.x1 + 22, first.y1 + 22);
    renderFlows(); cy.fit(undefined, 40);
  }

  // ---------- 群組 ----------
  function groupSelected() {
    var sel = cy.nodes(':selected').filter('[kind="step"],[kind="group"],[kind="note"]');
    if (sel.length < 2) { notice('請先選取兩個以上的節點(Shift+拖曳框選,或按住 Ctrl 點選)。'); return; }
    var ps = {}; sel.forEach(function (n) { ps[n.parent().id() || ''] = 1; });
    if (Object.keys(ps).length > 1) { notice('選取的節點分屬不同流程,無法群組。請只選同一張流程裡的節點。'); return; }
    notice('');
    var parentId = Object.keys(ps)[0] || null, gid = 'G' + (++groupSeq) + '_' + Date.now().toString(36);
    var color = parentId && flows[parentId] ? flows[parentId].color : '#6b7280';
    cy.add({ group: 'nodes', data: { id: gid, kind: 'group', label: '群組', color: color, flow: parentId || undefined, parent: parentId || undefined } });
    sel.forEach(function (n) { n.move({ parent: gid }); });
    cy.getElementById(gid).select();
  }

  function ungroupSelected() {
    var gs = cy.nodes('[kind="group"]:selected');
    if (gs.empty()) { notice('請先點選一個群組的外框。'); return; }
    notice('');
    gs.forEach(function (g) { var p = g.parent().id() || null; g.children().move({ parent: p }); g.remove(); });
  }

  // ---------- 重新排版:預設不動你拖過的流程 ----------
  function relayout(force) {
    var skipped = 0;
    Object.keys(flows).forEach(function (id) {
      if (flows[id].dirty && !force) { skipped++; return; }
      layoutFlow(id); flows[id].dirty = false;
    });
    renderFlows(); cy.fit(undefined, 40);
    say(skipped ? '已略過 ' + skipped + ' 張你拖拉過的流程(要全部重排請按「全部重新排版」)' : '已重新排版');
  }

  // ---------- 儲存與載入 ----------
  function collect() {
    var nodes = cy.nodes().map(function (n) { var p = n.position(); return { data: n.data(), x: p.x, y: p.y }; });
    var edges = cy.edges().map(function (e) { return { data: e.data() }; });
    return { version: 1, page: 'canvas', flows: flows, flowSeq: flowSeq, groupSeq: groupSeq, nodes: nodes, edges: edges,
      zoom: cy.zoom(), pan: cy.pan() };
  }

  function apply(o) {
    cy.elements().remove(); flows = o.flows || {}; flowSeq = o.flowSeq || 0; groupSeq = o.groupSeq || 0;
    var parents = o.nodes.filter(function (n) { return !n.data.parent; }), kids = o.nodes.filter(function (n) { return !!n.data.parent; });
    function addAll(list) { list.forEach(function (n) { cy.add({ group: 'nodes', data: n.data, position: { x: n.x, y: n.y } }); }); }
    // 先加外層再加內層,內層節點的 parent 才存在
    var added = {}, pending = parents.concat(kids), guard = 0;
    while (pending.length && guard++ < 50) {
      var next = [];
      pending.forEach(function (n) {
        if (n.data.parent && !added[n.data.parent]) { next.push(n); return; }
        cy.add({ group: 'nodes', data: n.data, position: { x: n.x, y: n.y } }); added[n.data.id] = 1;
      });
      pending = next;
    }
    (o.edges || []).forEach(function (e) { cy.add({ group: 'edges', data: e.data }); });
    if (o.zoom) { cy.zoom(o.zoom); cy.pan(o.pan); } else cy.fit(undefined, 40);
    renderFlows();
  }

  // ---------- 啟動 ----------
  function init() {
    cytoscape.use && window.cytoscapeDagre && false; // cytoscape-dagre 載入時已自動註冊
    cy = cytoscape({ container: $('cy'), elements: [], style: styles(), wheelSensitivity: 0.25, minZoom: 0.05, maxZoom: 3, boxSelectionEnabled: true });
    CA.bindNotes(cy);

    // 只有拖「流程裡的節點」才算拖拉過;拖整個外框(群組拖拉)不算
    cy.on('grab', 'node', function (e) { draggingParent = e.target.isParent() || e.target.data('kind') === 'note'; });
    cy.on('free', 'node[kind="step"]', function (e) {
      if (draggingParent) return;
      var fid = e.target.data('flow'); if (flows[fid] && !flows[fid].dirty) { flows[fid].dirty = true; renderFlows(); }
    });
    cy.on('tap', 'node[kind="step"]', function (e) {
      cy.elements().addClass('faded'); e.target.closedNeighborhood().removeClass('faded');
    });
    cy.on('tap', function (e) { if (e.target === cy) cy.elements().removeClass('faded'); });

    // 左側:方法清單
    function loadList() {
      fetch('/api/methods?q=' + encodeURIComponent($('q').value)).then(function (r) { return r.json(); }).then(function (l) {
        $('qCount').textContent = l.length >= 80 ? '顯示前 80 個,請輸入更多文字' : '共 ' + l.length + ' 個方法';
        $('methodList').innerHTML = l.map(function (m) {
          return '<div class="t" data-n="' + esc(m.name) + '"><span>' + esc(shortName(m.name)) + '</span><span class="k">' + esc(m.file) + ':' + m.line + '</span></div>';
        }).join('') || '<div class="mut small">沒有符合的方法</div>';
      });
    }
    $('q').addEventListener('input', function () { clearTimeout(qTimer); qTimer = setTimeout(loadList, 200); });
    $('methodList').addEventListener('click', function (e) { var t = e.target.closest('.t'); if (t) loadMethod(t.getAttribute('data-n')); });

    // 流程清單
    $('flows').addEventListener('click', function (e) {
      var row = e.target.closest('.fl'); if (!row) return; var id = row.getAttribute('data-id');
      if (e.target.classList.contains('del')) removeFlow(id);
      else if (e.target.classList.contains('vis')) setHidden(id, !cy.getElementById(id).hasClass('hidden'));
      else if (e.target.classList.contains('rel')) { layoutFlow(id); flows[id].dirty = false; renderFlows(); }
    });
    $('flows').addEventListener('input', function (e) {
      if (!e.target.classList.contains('col')) return;
      recolorFlow(e.target.closest('.fl').getAttribute('data-id'), e.target.value);
    });

    $('merge').addEventListener('click', function () {
      var ids = [].slice.call(document.querySelectorAll('#flows .fl')).filter(function (r) { return r.querySelector('.pick').checked; })
        .map(function (r) { return r.getAttribute('data-id'); });
      if (ids.length < 2) { notice('請在左邊「畫布上的流程」勾選兩個以上的流程再合併。'); return; }
      notice(''); mergeFlows(ids, $('dedupe').checked);
    });

    $('relayout').addEventListener('click', function () { relayout(false); });
    $('relayoutAll').addEventListener('click', function () { if (window.confirm('這會重排所有流程,包含你手動拖過的。確定嗎?')) relayout(true); });
    $('fit').addEventListener('click', function () { cy.fit(undefined, 40); });
    $('group').addEventListener('click', groupSelected);
    $('ungroup').addEventListener('click', ungroupSelected);

    // 便利貼
    $('addNote').addEventListener('click', function () { CA.addNote(cy, { color: noteColor }).select(); });
    var noteColor = CA.palette[0];
    $('noteColors').innerHTML = CA.palette.map(function (c) { return '<span class="sw" data-c="' + c + '" style="background:' + c + '" title="便利貼顏色"></span>'; }).join('');
    $('noteColors').addEventListener('click', function (e) {
      var c = e.target.getAttribute && e.target.getAttribute('data-c'); if (!c) return;
      noteColor = c; if (!CA.colorSelectedNotes(cy, c)) say('之後新貼的便利貼會用這個顏色');
    });

    // 版面
    CA.wireLayoutUI('canvas', { list: 'layList', save: 'laySave', load: 'layLoad', del: 'layDel', exp: 'layExp', imp: 'layImp' }, collect, apply, say);
    $('svg').addEventListener('click', function () {
      var s = CA.svg(cy); if (!s) { say('畫布是空的'); return; }
      CA.download('流程畫布.svg', s, 'image/svg+xml;charset=utf-8'); say('已下載 SVG');
    });

    loadList();
    window.__cy = cy; window.__flows = function () { return flows; }; // 方便除錯
    new URLSearchParams(location.search).getAll('name').forEach(function (n, i) { setTimeout(function () { loadMethod(n); }, i * 900); });
  }
  init();
})();
