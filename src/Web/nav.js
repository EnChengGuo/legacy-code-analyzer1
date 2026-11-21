// 每一頁頂端導覽列:顯示目前開啟的專案,點它回到「開啟專案」頁;
// 另外每幾秒確認一次本機程式還在,斷線時在頁面最上方顯示明顯的提示(常見原因:黑色視窗被關掉了)。
(function () {
  var h = document.querySelector('header');
  if (h) {
    fetch('/api/status').then(function (r) { return r.json(); }).then(function (s) {
      var a = document.createElement('a'); a.href = '/open'; a.className = 'projlink';
      a.textContent = s.loaded ? '📂 ' + s.name : '📂 開啟專案'; a.title = (s.loaded ? s.path + '\n' : '') + '點一下可以切換專案';
      a.style.cssText = 'border:1px solid var(--line);border-radius:6px;padding:0 8px;font-weight:500;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
      var grow = h.querySelector('.grow'); if (grow) h.insertBefore(a, grow); else h.appendChild(a);
    }).catch(function () { });
  }

  var banner = null, fails = 0;
  function show(on) {
    if (on && !banner) {
      banner = document.createElement('div');
      banner.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:99999;background:#b91c1c;color:#fff;padding:10px 16px;font:14px/1.5 "Microsoft JhengHei",sans-serif;text-align:center;box-shadow:0 2px 8px #0005';
      banner.innerHTML = '<b>已和 CodeAtlas 本機程式失去連線。</b>請確認那個<b>黑色視窗還開著</b>(關掉視窗或被結束,程式就停了)。' +
        '重新執行 CodeAtlas.exe 後,按 F5 重新整理這一頁。';
      document.body.appendChild(banner);
    } else if (!on && banner) { banner.remove(); banner = null; }
  }
  function beat() {
    fetch('/api/status', { cache: 'no-store' }).then(function (r) { if (!r.ok) throw 0; fails = 0; show(false); })
      .catch(function () { if (++fails >= 2) show(true); });
  }
  setInterval(beat, 4000);
})();
