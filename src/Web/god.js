(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); }, esc = CA.esc;
  var data = null, tab = 'inv', debt = null, current = '', qTimer, hubTimer;

  var TABS = [['inv', '欄位清冊'], ['cl', '職責分群'], ['sto', '儲存對照'], ['ui', 'UI 對照'], ['debt', '冤親債主']];
  var HINT = {
    inv: '每個欄位/屬性被幾個成員讀、寫、外部用了幾次。旗標:未使用、只寫不讀、只被一個成員使用(可能該是區域變數)、樞紐。',
    cl: '依「共用的欄位」與「互相呼叫」把方法綁成群;每一群是一個可能獨立出去的職責。分群是統計推測,請依業務判斷。',
    sto: '推測哪些欄位對應到儲存(設定檔、資料庫、序列化):屬性標註、字串鍵(例 ini.Write("鍵", 值)、row["欄位"])、同名字串。這些是線索,不是定論。',
    ui: '控制項(WinForms/WPF)的事件掛接,以及控制項屬性與資料欄位之間的賦值方向。',
    debt: '沒寫註解的成員,最後是誰改的、再往前是哪幾代的人改的(用 git log -L 追蹤那幾行)。'
  };

  function say(t) { $('status').textContent = t || ''; }
  function chips(fl) { return (fl || []).map(function (f) { return '<span class="flag">' + esc(f) + '</span>'; }).join(''); }
  function table(cols, rows) {
    return '<table><tr>' + cols.map(function (c) { return '<th>' + esc(c[0]) + '</th>'; }).join('') + '</tr>' +
      rows.map(function (r) { return '<tr>' + cols.map(function (c) { var v = c[1](r); return '<td class="' + (c[2] || '') + '">' + v + '</td>'; }).join('') + '</tr>'; }).join('') + '</table>';
  }

  function cards() {
    if (!data) { $('cards').innerHTML = ''; return; }
    var c = [['行數', data.lines], ['成員', data.members], ['WMC', data.wmc], ['Ca(被用)', data.ca], ['Ce(用別人)', data.ce], ['資料欄位', data.dataMembers],
      ['有內文的成員', data.units], ['LCOM4(分群數)', data.lcom4], ['控制項', data.controlCount]];
    $('cards').innerHTML = c.map(function (x) { return '<div class="card"><b>' + esc(x[1]) + '</b><span>' + esc(x[0]) + '</span></div>'; }).join('');
  }

  function tabs() {
    $('tabs').innerHTML = '';
    TABS.forEach(function (t) {
      var b = document.createElement('button'); b.textContent = t[1]; b.className = t[0] === tab ? 'on' : ''; b.disabled = !data;
      b.onclick = function () { tab = t[0]; if (t[0] === 'debt' && !debt) loadDebt(); render(); }; $('tabs').appendChild(b);
    });
  }

  var rowsNow = [], colsNow = [];
  function match(f, s) { return !f || JSON.stringify(s).toLowerCase().indexOf(f) >= 0; }

  function render() {
    tabs(); $('hint').textContent = HINT[tab] || '';
    if (!data) return;
    var f = $('filter').value.trim().toLowerCase(), out = '';
    if (tab === 'inv') {
      var cols = [['名稱', function (r) { return esc(r.access + ' ' + r.name); }], ['種類', function (r) { return r.kind; }], ['型別', function (r) { return esc(r.type); }],
        ['行', function (r) { return r.line; }, 'n'], ['讀(成員數)', function (r) { return r.readers; }, 'n'], ['寫(成員數)', function (r) { return r.writers; }, 'n'],
        ['類別內使用', function (r) { return r.refsInside; }, 'n'], ['外部使用', function (r) { return r.refsOutside; }, 'n'], ['使用它的成員數', function (r) { return r.usedBy; }, 'n'], ['旗標', function (r) { return chips(r.flags); }]];
      rowsNow = data.inventory.filter(function (r) { return match(f, r); }); colsNow = cols; out = table(cols, rowsNow);
    } else if (tab === 'cl') {
      var cl = data.clusters.filter(function (c) { return match(f, c); }); rowsNow = []; colsNow = [];
      out = cl.map(function (c, i) {
        return '<div class="cl"><h3>' + (i + 1) + '. ' + esc(c.label) + ' <span class="mut small">(' + c.count + ' 個成員,' + c.loc + ' 行,CC 合計 ' + c.cc + ')</span></h3>' +
          '<div class="ms"><b>專屬欄位:</b>' + (c.ownFields.length ? esc(c.ownFields.join('、')) : '(無)') + '　<b>用到的樞紐欄位:</b>' + (c.hubFields.length ? esc(c.hubFields.join('、')) : '(無)') + '</div>' +
          '<div class="ms">' + c.methods.map(function (m) { return esc(m.name) + ' <span class="small">(:' + m.line + ',' + m.loc + '行,CC ' + m.cc + ')</span>'; }).join('、 ') + '</div></div>';
      }).join('') || '<div class="mut">沒有符合的分群。</div>';
    } else if (tab === 'sto') {
      var c2 = [['欄位', function (r) { return esc(r.name); }], ['型別', function (r) { return esc(r.type); }], ['行', function (r) { return r.line; }, 'n'],
        ['儲存線索', function (r) { return r.evidence.map(esc).join('<br>'); }]];
      rowsNow = data.storage.filter(function (r) { return match(f, r); }); colsNow = c2;
      out = (rowsNow.length ? table(c2, rowsNow) : '<div class="mut">沒有找到儲存線索。</div>');
    } else if (tab === 'ui') {
      var c3 = [['控制項', function (r) { return esc(r.name); }], ['型別', function (r) { return esc(r.type); }], ['行', function (r) { return r.line; }, 'n'], ['Designer', function (r) { return r.designer ? '是' : ''; }],
        ['事件掛接', function (r) { return r.handlers.map(function (h) { return esc(h.evt + ' → ' + h.handler); }).join('<br>'); }],
        ['被哪些成員使用', function (r) { return esc(r.usedBy.join('、')) + (r.usedByCount > r.usedBy.length ? ' …共 ' + r.usedByCount + ' 個' : ''); }]];
      var ctl = data.controls.filter(function (r) { return match(f, r); }); rowsNow = ctl; colsNow = c3;
      var c4 = [['控制項.屬性', function (r) { return esc(r.control + '.' + r.prop); }], ['方向', function (r) { return esc(r.dir); }], ['資料欄位', function (r) { return esc(r.member); }],
        ['在成員', function (r) { return esc(r.method); }], ['行', function (r) { return r.line; }, 'n']];
      var pr = data.ui.filter(function (r) { return match(f, r); });
      out = '<h3>控制項清冊(' + ctl.length + ')</h3>' + (ctl.length ? table(c3, ctl) : '<div class="mut">這個類別沒有 WinForms/WPF 控制項。</div>') +
        '<h3 style="margin-top:16px">控制項 ↔ 資料欄位 對照(' + pr.length + ')</h3>' + (pr.length ? table(c4, pr) : '<div class="mut">沒有找到。</div>');
    } else if (tab === 'debt') {
      if (!debt) { out = '<div class="mut">查詢中…</div>'; }
      else if (debt.error) out = '<div class="mut">' + esc(debt.error) + '</div>';
      else {
        var c5 = [['成員', function (r) { return esc(r.name); }], ['種類', function (r) { return r.kind; }], ['行', function (r) { return r.line + '–' + r.endLine; }, 'n'],
          ['最後編輯者', function (r) { var e = r.editors; return !e ? '' : e.last ? esc(e.last.author) + ' <span class="mut small">' + esc(e.last.date) + ' ' + esc(e.last.hash) + '<br>' + esc(e.last.subject) + '</span>' : '<span class="mut">' + esc(e.note || '') + '</span>'; }],
          ['上幾代', function (r) { var e = r.editors; return !e || !e.previous.length ? '<span class="mut">(只有這個人改過)</span>' : e.previous.map(function (p) { return esc(p.author) + ' <span class="mut small">' + esc(p.date) + '</span>'; }).join('<br>'); }]];
        rowsNow = debt.items.filter(function (r) { return match(f, r); }); colsNow = c5;
        out = '<div class="hint" style="padding:0 0 6px">沒寫註解的成員共 ' + debt.total + ' 個' + (debt.total > debt.items.length ? ',只列前 ' + debt.items.length + ' 個' : '') + '。</div>' + table(c5, rowsNow);
      }
    }
    $('body').innerHTML = out;
  }

  function loadDebt() {
    debt = null; render();
    fetch('/api/debtors?name=' + encodeURIComponent(current) + '&limit=60').then(function (r) { return r.json(); }).then(function (d) { debt = d; render(); });
  }

  function load(name) {
    current = name; data = null; debt = null; say('分析中…'); render();
    fetch('/api/godclass?name=' + encodeURIComponent(name) + '&hub=' + ($('hub').value / 100)).then(function (r) { return r.json(); }).then(function (d) {
      say('');
      if (d.error) { $('body').innerHTML = '<div class="mut">' + esc(d.error) + '</div>'; return; }
      data = d; say(d.name); cards(); if (tab === 'debt') loadDebt(); render();
    }).catch(function (e) { say('失敗:' + e.message); });
  }

  function loadList() {
    fetch('/api/types?q=' + encodeURIComponent($('q').value)).then(function (r) { return r.json(); }).then(function (l) {
      $('qCount').textContent = '顯示 ' + l.length + ' 個';
      $('list').innerHTML = l.map(function (t) { return '<div class="t' + (t.name === current ? ' on' : '') + '" data-n="' + esc(t.name) + '"><span>' + esc(t.name.split('.').slice(-2).join('.')) + '</span><span class="k">' + t.loc + ' 行 · ' + t.members + ' 員</span></div>'; }).join('');
    });
  }

  function csv() {
    if (!colsNow.length || !rowsNow.length) { say('這個分頁沒有可匯出的表格'); return; }
    function cell(h) { var s = String(h).replace(/<br>/g, ' / ').replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"'); return '"' + s.replace(/"/g, '""') + '"'; }
    var lines = [colsNow.map(function (c) { return cell(c[0]); }).join(',')];
    rowsNow.forEach(function (r) { lines.push(colsNow.map(function (c) { return cell(c[1](r)); }).join(',')); });
    CA.download((current.split('.').pop() || 'class') + '_' + tab + '.csv', '﻿' + lines.join('\r\n'), 'text/csv;charset=utf-8');
  }

  $('q').addEventListener('input', function () { clearTimeout(qTimer); qTimer = setTimeout(loadList, 200); });
  $('list').addEventListener('click', function (e) { var t = e.target.closest('.t'); if (t) { load(t.getAttribute('data-n')); loadList(); } });
  $('hub').addEventListener('input', function () { $('hubV').textContent = $('hub').value + '%'; clearTimeout(hubTimer); hubTimer = setTimeout(function () { if (current) load(current); }, 400); });
  $('filter').addEventListener('input', render);
  $('csv').addEventListener('click', csv);
  var up = new URLSearchParams(location.search); if (up.get('tab')) tab = up.get('tab'); var nm = up.get('name'); if (nm) load(nm);
  tabs(); loadList();
})();
