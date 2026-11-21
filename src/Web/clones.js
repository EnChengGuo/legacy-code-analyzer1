(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); }, esc = CA.esc;

  function code(site, el) {
    fetch('/api/code?file=' + encodeURIComponent(site.file) + '&from=' + site.start + '&to=' + Math.min(site.end, site.start + 60)).then(function (r) { return r.json(); }).then(function (d) {
      el.textContent = d.error ? d.error : d.lines.map(function (l, i) { return String(d.from + i).padStart(5) + '  ' + l; }).join('\n');
    });
  }

  function run() {
    $('out').innerHTML = '<span class="mut">偵測中…</span>'; $('status').textContent = '';
    var p = new URLSearchParams({ window: $('window').value, lines: $('lines').value, exact: $('exact').checked ? '1' : '0', gen: $('gen').checked ? '1' : '0' });
    fetch('/api/clones?' + p).then(function (r) { return r.json(); }).then(function (d) {
      if (d.error) { $('out').textContent = d.error; return; }
      $('status').textContent = '共 ' + d.total + ' 組';
      if (!d.items.length) { $('out').innerHTML = '<div class="mut">沒有找到符合條件的重複程式碼。</div>'; return; }
      $('out').innerHTML = (d.total > d.items.length ? '<div class="mut small">只顯示最長的前 ' + d.items.length + ' 組。</div>' : '') + d.items.map(function (c, i) {
        return '<div class="clone"><h3>#' + (i + 1) + ' <span class="tag' + (c.exact ? ' ex' : '') + '">' + (c.exact ? '完全相同' : '名稱或常數不同') + '</span> ' + c.lines + ' 行 · ' + c.tokens + ' 個標記</h3>' +
          '<div class="pair">' + ['a', 'b'].map(function (k) {
            var s = c[k]; return '<div class="site"><div class="loc">' + esc(s.member) + '<br>' + esc(s.name) + ':' + s.start + '–' + s.end + '</div><pre id="c' + i + k + '">載入中…</pre></div>';
          }).join('') + '</div></div>';
      }).join('');
      d.items.forEach(function (c, i) { code(c.a, $('c' + i + 'a')); code(c.b, $('c' + i + 'b')); });
    }).catch(function (e) { $('out').textContent = '失敗:' + e.message; });
  }

  $('go').addEventListener('click', run);
  if (new URLSearchParams(location.search).get('auto')) run();
})();
