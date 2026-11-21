(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); }, esc = CA.esc;
  var H = { 'X-CodeAtlas': '1' }, poll = null, browsePath = '';

  function post(url, body) {
    return fetch(url, { method: 'POST', headers: Object.assign({ 'Content-Type': 'application/json' }, H), body: JSON.stringify(body || {}) }).then(function (r) { return r.json(); });
  }
  function msg(cls, html) { $('result').innerHTML = html ? '<div class="msg ' + cls + '">' + html + '</div>' : ''; }

  // ---------- 開啟 ----------
  function openPath(p) {
    p = (p || $('path').value).trim(); if (!p) { msg('err', '請先輸入路徑。'); return; }
    $('cands').hidden = true; msg('info', '<span class="spin"></span>準備載入…'); $('go').disabled = true;
    post('/api/open', { path: p, sources: $('sources').value }).then(function (d) {
      if (d.error) {
        $('go').disabled = false; msg('err', esc(d.error));
        if (d.candidates) {
          $('cands').hidden = false;
          $('cands').innerHTML = d.candidates.map(function (c) { return '<div class="it file" data-p="' + esc(c) + '"><span>' + esc(c.split('\\').pop()) + '</span><span class="sub">' + esc(c) + '</span></div>'; }).join('');
        }
        return;
      }
      $('path').value = d.path; startPolling();
    }).catch(function (e) { $('go').disabled = false; msg('err', '連不上本機程式:' + esc(e.message)); });
  }

  function startPolling() {
    clearInterval(poll);
    var tick = function () {
      fetch('/api/status').then(function (r) { return r.json(); }).then(function (s) {
        if (s.state === 'loading') {
          msg('info', '<span class="spin"></span>載入中:' + esc(s.opening || '') + '<div id="log">' + esc((s.log || []).join('\n')) + '</div>');
          var l = $('log'); if (l) l.scrollTop = l.scrollHeight;
        } else {
          clearInterval(poll); $('go').disabled = false;
          if (s.state === 'ready' && !s.error) {
            msg('ok', '已載入「' + esc(s.name) + '」:' + s.projects + ' 個專案、' + s.files + ' 個原始檔、' + s.types + ' 個型別。<b>正在前往總覽…</b>' +
              ((s.log || []).filter(function (x) { return x.indexOf('找不到') >= 0 || x.indexOf('缺少') >= 0; }).length ? '<div id="log">' + esc(s.log.join('\n')) + '</div>' : ''));
            loadRecent(); showCurrent(s);
            setTimeout(function () { location.href = '/'; }, 900);
          } else {
            msg('err', '載入失敗:' + esc(s.error || '未知原因') + '<div id="log">' + esc((s.log || []).join('\n')) + '</div>');
          }
        }
      });
    };
    tick(); poll = setInterval(tick, 600);
  }

  // ---------- 目前專案 ----------
  function showCurrent(s) {
    var c = $('current');
    if (!s || !s.loaded) { c.hidden = true; return; }
    c.hidden = false;
    c.innerHTML = '<h2>目前已開啟</h2><div><b>' + esc(s.name) + '</b> <span class="mut small">(' + s.projects + ' 個專案、' + s.files + ' 個原始檔、' + s.types + ' 個型別,載入於 ' + esc(s.at) + ')</span></div>' +
      '<div class="mut small" style="word-break:break-all">' + esc(s.path) + '</div><div style="margin-top:8px"><a href="/"><button type="button">前往總覽 →</button></a></div>';
  }

  // ---------- 最近開啟 ----------
  function loadRecent() {
    fetch('/api/recent').then(function (r) { return r.json(); }).then(function (l) {
      $('recent').innerHTML = l.length ? l.map(function (x) {
        return '<div class="it file" data-p="' + esc(x.path) + '" data-s="' + esc(x.sources || '') + '"><span>' + esc(x.path.split('\\').pop()) + (x.exists ? '' : ' <span class="mut">(已不存在)</span>') +
          '</span><span class="sub">' + esc(x.when) + '</span><span class="x" data-forget="' + esc(x.path) + '" title="從清單移除">✕</span></div>' +
          '<div class="it sub" style="cursor:default;padding-top:0;border-bottom:1px solid var(--line)">' + esc(x.path) + '</div>';
      }).join('') : '<div class="it sub" style="cursor:default">還沒有紀錄</div>';
    });
  }

  // ---------- 瀏覽 ----------
  function browse(p) {
    fetch('/api/browse?path=' + encodeURIComponent(p || '')).then(function (r) { return r.json(); }).then(function (d) {
      if (d.error) { $('browse').innerHTML = '<div class="it sub" style="cursor:default;color:#dc2626">' + esc(d.error) + '</div>'; return; }
      browsePath = d.path;
      var crumb = '<a data-b="">本機</a>';
      if (d.path) {
        var parts = d.path.split('\\').filter(Boolean), acc = '';
        parts.forEach(function (part, i) { acc += (i === 0 ? '' : '\\') + part; crumb += ' › <a data-b="' + esc(i === 0 ? acc + '\\' : acc) + '">' + esc(part) + '</a>'; });
      }
      $('crumb').innerHTML = crumb;
      var h = '';
      if (d.path) h += '<div class="it" data-b="' + esc(d.parent || '') + '"><span>⬆ 上一層</span></div>';
      (d.shortcuts || []).forEach(function (s) { h += '<div class="it" data-b="' + esc(s.path) + '"><span>⭐ ' + esc(s.name) + '</span><span class="sub">' + esc(s.path) + '</span></div>'; });
      d.dirs.forEach(function (n) { var full = d.path ? (d.path.replace(/\\$/, '') + '\\' + n) : n; h += '<div class="it" data-b="' + esc(full) + '"><span>📁 ' + esc(n) + '</span></div>'; });
      d.files.forEach(function (n) { var full = d.path.replace(/\\$/, '') + '\\' + n; h += '<div class="it file" data-p="' + esc(full) + '" title="點一下填入路徑,點兩下直接開啟"><span>📄 ' + esc(n) + '</span></div>'; });
      if (!d.dirs.length && !d.files.length) h += '<div class="it sub" style="cursor:default">(空的)</div>';
      $('browse').innerHTML = h;
    });
  }

  // ---------- 事件 ----------
  $('go').addEventListener('click', function () { openPath(); });
  $('path').addEventListener('keydown', function (e) { if (e.key === 'Enter') openPath(); });
  $('cands').addEventListener('click', function (e) { var t = e.target.closest('.it'); if (t) { $('path').value = t.getAttribute('data-p'); openPath(t.getAttribute('data-p')); } });
  $('recent').addEventListener('click', function (e) {
    var f = e.target.getAttribute('data-forget');
    if (f) { post('/api/recent?forget=' + encodeURIComponent(f)).then(loadRecent); e.stopPropagation(); return; }
    var t = e.target.closest('.it[data-p]'); if (!t) return;
    $('path').value = t.getAttribute('data-p'); $('sources').value = t.getAttribute('data-s') || ''; if ($('sources').value) $('sources').closest('details').open = true;
    openPath(t.getAttribute('data-p'));
  });
  $('browse').addEventListener('click', function (e) {
    var t = e.target.closest('.it'); if (!t) return;
    if (t.hasAttribute('data-b')) browse(t.getAttribute('data-b'));
    else if (t.hasAttribute('data-p')) $('path').value = t.getAttribute('data-p');
  });
  $('browse').addEventListener('dblclick', function (e) { var t = e.target.closest('.it[data-p]'); if (t) { $('path').value = t.getAttribute('data-p'); openPath(t.getAttribute('data-p')); } });
  $('crumb').addEventListener('click', function (e) { var b = e.target.getAttribute('data-b'); if (b !== null) browse(b); });

  fetch('/api/status').then(function (r) { return r.json(); }).then(function (s) {
    showCurrent(s);
    if (s.state === 'loading') startPolling();
    if (s.loaded) { $('path').value = s.path; $('sources').value = s.sources || ''; }
    browse(s.loaded ? s.path.replace(/\\[^\\]*$/, '') : '');
  });
  loadRecent();
})();
