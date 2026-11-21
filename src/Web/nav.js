// 每一頁頂端導覽列:顯示目前開啟的專案,點它回到「開啟專案」頁
(function () {
  var h = document.querySelector('header'); if (!h) return;
  fetch('/api/status').then(function (r) { return r.json(); }).then(function (s) {
    var a = document.createElement('a'); a.href = '/open'; a.className = 'projlink';
    a.textContent = s.loaded ? '📂 ' + s.name : '📂 開啟專案'; a.title = (s.loaded ? s.path + '\n' : '') + '點一下可以切換專案';
    a.style.cssText = 'border:1px solid var(--line);border-radius:6px;padding:0 8px;font-weight:500;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
    var grow = h.querySelector('.grow'); if (grow) h.insertBefore(a, grow); else h.appendChild(a);
  }).catch(function () { });
})();
