namespace Glassify
{
    /// JavaScript Glassify adds to the Spotify page: theme + look levels, window controls, resize edges,
    /// mini-player button, settings panel, and state/controls for the mini player and media keys.
    static class PageScript
    {
        public static string Build(string cssJson) => @"(()=>{
const CSS=" + cssJson + @";
const BASE=`
#ms-winctl { position: fixed; top: 10px; right: 12px; z-index: 2147483646; display: flex; align-items: center; gap: 2px; padding: 3px;
  border-radius: 999px; background: rgba(24,24,32,.55); border: 1px solid rgba(255,255,255,.12); opacity: .45; }
#ms-winctl:hover { opacity: 1; background: rgba(24,24,32,.9); }
#ms-winctl .ms-grip { width: 14px; height: 26px; cursor: grab; border-radius: 8px;
  background: radial-gradient(circle, rgba(255,255,255,.55) 1px, transparent 1.5px) center / 5px 5px; margin: 0 2px 0 4px; }
#ms-winctl button { width: 28px; height: 28px; border: 0; border-radius: 50%; background: transparent; color: #eee; cursor: pointer; padding: 0;
  font: 11px 'Segoe Fluent Icons','Segoe MDL2 Assets'; display: grid; place-items: center; }
#ms-winctl button:hover { background: rgba(255,255,255,.16); color: #fff; }
#ms-winctl button.ms-close:hover { background: #e5484d; }
#ms-settings { position: fixed; top: 52px; right: 12px; z-index: 2147483646; width: 262px; padding: 14px 16px 12px;
  max-height: calc(100vh - 70px); overflow-y: auto; border-radius: 18px; background: rgb(24,20,28); border: 1px solid rgba(255,255,255,.16); color: #eee;
  font: 13px 'Segoe UI Variable Text','Segoe UI',sans-serif; box-shadow: 0 10px 30px rgba(0,0,0,.45); }
#ms-settings .ms-h { font-weight: 600; margin: 4px 0 8px; display: flex; justify-content: space-between; }
#ms-settings .ms-val { font-weight: 400; opacity: .7; }
#ms-settings .ms-sw { display: flex; flex-wrap: wrap; gap: 7px; margin-bottom: 14px; }
#ms-settings .ms-sw button, #ms-settings .ms-pick { width: 26px; height: 26px; border-radius: 50%; border: 2px solid rgba(255,255,255,.35); cursor: pointer; padding: 0; }
#ms-settings .ms-pick { position: relative; overflow: hidden; background: conic-gradient(red, yellow, lime, cyan, blue, magenta, red); }
#ms-settings .ms-pick input { position: absolute; inset: -6px; opacity: 0; cursor: pointer; }
#ms-settings input[type=range] { width: 100%; accent-color: var(--ms-accent, #ff5fa8); margin: 0 0 6px; }
#ms-settings .ms-desc { font-size: 12px; opacity: .72; margin: 0 0 12px; min-height: 32px; line-height: 1.35; }
#ms-settings .ms-check { display: flex; gap: 10px; align-items: flex-start; margin: 2px 0 12px; cursor: pointer; font-size: 12px; line-height: 1.35; opacity: .9; }
#ms-settings .ms-check b { font-size: 13px; }
#ms-settings .ms-check input { margin-top: 3px; accent-color: var(--ms-accent, #ff5fa8); width: 16px; height: 16px; flex: none; }
#ms-settings .ms-check input:disabled { cursor: default; }
#ms-settings .ms-note { font-size: 12px; color: var(--ms-accent, #ff5fa8); margin: 0 0 10px; }
#ms-settings .ms-dim { opacity: .4; pointer-events: none; }
#ms-settings .ms-keys { min-height: 0; line-height: 1.6; }
#ms-settings .ms-ver { min-height: 0; margin-bottom: 8px; }
#ms-settings .ms-ver a { color: var(--ms-accent, #ff5fa8); }
#ms-winctl #ms-update { color: var(--ms-accent, #ff5fa8); }
#ms-winctl #ms-update.ms-busy { animation: ms-pulse 1s ease-in-out infinite !important; animation-play-state: running !important; cursor: progress; }
@keyframes ms-pulse { 50% { opacity: .3; } }
#ms-settings .ms-row { display: flex; justify-content: flex-end; gap: 8px; }
#ms-settings .ms-row button { border: 0; border-radius: 999px; padding: 6px 14px; cursor: pointer; font: inherit; background: rgba(255,255,255,.1); color: #eee; }
#ms-settings .ms-row .ms-done { background: var(--ms-accent, #ff5fa8); color: var(--ms-accent-ink, #1a0710); font-weight: 600; }
html.ms-eco *, html.ms-eco *::before, html.ms-eco *::after { animation-play-state: paused !important; }
html.ms-lite #main-view img, html.ms-lite #Desktop_LeftSidebar_Id img, html.ms-lite [data-testid=""card-image""],
html.ms-lite [data-testid=""shortcut-image""], html.ms-lite video { display: none !important; }
`;
if (window.__ms) { window.__ms.cssText = CSS; try { window.__ms.apply(); } catch (e) {} return 'updated'; }
const id='minispotify-theme';
const q=s=>document.querySelector(s);
const post=m=>{ try { window.chrome.webview.postMessage(m); } catch(e){} };
// Look levels: 5 Glass, 4 Glass focused, 3 Solid, 2 Light (lightest). (A graphics-off level was removed:
// the browser engine keeps its graphics process anyway, so it saved nothing and cost CPU.)
const LEVELS = {
  5: ['Glass', 'Floating see-through glass, everything shown.'],
  4: ['Glass, focused', 'Floating glass; Now Playing panel closed.'],
  3: ['Solid', 'Normal solid window: no see-through or glass effects. Now Playing panel closed.'],
  2: ['Light', 'The lightest: solid, no cover art in lists, animations paused, low priority, memory saver. Every button still works.'] };
const glassOf = l => l >= 4, gpuOf = l => true;
const ms = window.__ms = {
  cssText: CSS,
  art() {
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    if (md && md.artwork && md.artwork.length) {
      const a = [...md.artwork].sort((x,y)=>parseInt(x.sizes)-parseInt(y.sizes));
      return (a.find(x=>parseInt(x.sizes)>=280) || a[a.length-1]).src;
    }
    const i = q('[data-testid=""cover-art-image""]'); return i ? i.src : '';
  },
  spotify: /(^|\.)open\.spotify\.com$/.test(location.hostname),
  apply() {
    let s = document.getElementById(id);
    if (!s) { s = document.createElement('style'); s.id = id; (document.head || document.documentElement).appendChild(s); }
    // The theme is for Spotify only; other pages (sign-in) get a plain solid background.
    const css = BASE + (this.spotify ? this.cssText : 'html{background:#16161d !important}');
    if (s.textContent !== css) s.textContent = css;
    if (!document.body) return;
    this.controls(); this.edges();
    if (!this.spotify) return;
    if (!this._looked) { this._looked = true; this.startLevel = this.level(); this.applyLook(); }
    this.nowPlayingPanel();
    this.resume();
    this.heartbeat();
    if (this._update && !document.getElementById('ms-update')) this.showUpdate(this._update.tag, this._update.url, this._update.auto);
    this.fitControls();
    this.button();
    this.tag();
    this.clearContent();
    this.dragBar();
  },
  // Pages (playlists, albums...) are created after the panels and put a solid dark layer behind their lists.
  // Clear it like the panel layers, found cheaply by walking up from two points in the middle panel.
  clearContent() {
    if (document.hidden) return;
    const mv = document.getElementById('main-view'); if (!mv) return;
    mv.toggleAttribute('data-ms-photo', !!mv.querySelector('[data-testid=""background-image""]'));   // big photo header
    const r = mv.getBoundingClientRect(); if (r.width < 60 || r.height < 60) return;
    for (const fy of [0.5, 0.85]) {
      let e = document.elementFromPoint(r.left + r.width * 0.5, r.top + r.height * fy);
      if (!e || !mv.contains(e)) continue;   // a menu or popup is on top: leave it alone
      for (let k = 0; e && e !== mv && k < 40; k++, e = e.parentElement) {
        if (e.hasAttribute('data-ms-clear') || e.tagName === 'IMG' || e.tagName === 'BUTTON') continue;
        const m = getComputedStyle(e).backgroundColor.match(/^rgba?\((\d+), (\d+), (\d+)(?:, ([\d.]+))?\)$/);
        if (m && (m[4] === undefined || +m[4] > .5) && Math.max(+m[1], +m[2], +m[3]) < 40) e.setAttribute('data-ms-clear', '');
      }
    }
  },
  // Press on an empty spot of the top bar to move the window; double-click to maximize/restore.
  dragBar() {
    const bar = q('[data-testid=""global-nav-bar""]');
    if (!bar || bar.dataset.msDrag) return;
    bar.dataset.msDrag = '1';
    const empty = t => !t.closest('button,a,input,form,[role=""button""],[role=""search""],[role=""combobox""],#ms-winctl');
    bar.addEventListener('mousedown', e => { if (e.button === 0 && e.detail === 1 && empty(e.target)) { e.preventDefault(); post('drag'); } });
    bar.addEventListener('dblclick', e => { if (empty(e.target)) post('max'); });
  },
  // Tag only the handful of layers that matter, once per panel instance:
  //  - wrappers between the page root and each panel -> see-through (so the gaps float)
  //  - solid dark layers in the first few levels inside each panel -> show the panel colour
  tagged: new WeakSet(),
  tag() {
    const root = q('[data-testid=""root""]');
    if (!root) return;
    for (const p of document.querySelectorAll('#Desktop_LeftSidebar_Id > nav, #main-view, #Desktop_PanelContainer_Id, [data-testid=""now-playing-bar""]')) {
      if (this.tagged.has(p)) continue;
      this.tagged.add(p);
      for (let a = p.parentElement; a && a !== root; a = a.parentElement) a.setAttribute('data-ms-wrap', '');
      const walk = (el, depth) => {
        for (const c of el.children) {
          if (c.tagName === 'IMG' || c.tagName === 'BUTTON' || c.tagName === 'VIDEO') continue;
          const cs = getComputedStyle(c);
          const m = cs.backgroundColor.match(/^rgba?\((\d+), (\d+), (\d+)(?:, ([\d.]+))?\)$/);
          if (m && (m[4] === undefined || +m[4] > .5) && Math.max(+m[1], +m[2], +m[3]) < 40) c.setAttribute('data-ms-clear', '');
          if (depth < 3) walk(c, depth + 1);
        }
      };
      walk(p, 0);
    }
  },
  button() {
    if (document.getElementById('ms-mini-btn')) return;
    const anchor = q('[data-testid=""pip-toggle-button""]') || q('[data-testid=""volume-bar""]');
    if (!anchor || !anchor.parentElement) return;
    const b = document.createElement('button');
    b.id = 'ms-mini-btn'; b.title = 'Mini player (Ctrl+Alt+M)';
    b.innerHTML = '<svg width=""16"" height=""16"" viewBox=""0 0 16 16"" fill=""currentColor""><rect x=""1"" y=""2.5"" width=""14"" height=""11"" rx=""2.5"" fill=""none"" stroke=""currentColor"" stroke-width=""1.4""/><rect x=""7.5"" y=""7.5"" width=""6"" height=""4"" rx=""1.2""/></svg>';
    b.onclick = () => post('mini');
    anchor.parentElement.insertBefore(b, anchor);
  },
  // Faint window buttons pinned to the top-right corner on every page; they brighten on hover.
  controls() {
    if (document.getElementById('ms-winctl')) return;
    const w = document.createElement('div'); w.id = 'ms-winctl';
    w.innerHTML = '<span class=""ms-grip"" title=""Drag to move""></span>' +
      (this.spotify ? '<button title=""Look &amp; settings"" data-a=""settings"">&#xE790;</button>' : '') +
      '<button title=""Minimize"" data-a=""min"">&#xE921;</button><button title=""Maximize"" data-a=""max"">&#xE922;</button><button class=""ms-close"" title=""Close Glassify"" data-a=""close"">&#xE8BB;</button>';
    w.querySelectorAll('button').forEach(b => b.onclick = () => b.dataset.a === 'settings' ? this.toggleSettings() : post(b.dataset.a));
    w.querySelector('.ms-grip').addEventListener('mousedown', e => { if (e.button === 0) { e.preventDefault(); post('drag'); } });
    document.body.appendChild(w);
  },
  // ---- settings, remembered between launches ----
  look() {
    let v = { color: '#5a2448', alpha: 82, accent: '#ff5fa8', eco: false, lvl: 5, miniOnMin: false };
    try { v = Object.assign(v, JSON.parse(localStorage.getItem('ms-look') || '{}')); } catch (e) {}
    if (v.lvl === undefined && v.mem !== undefined) v.lvl = 5;   // older settings
    return v;
  },
  level() { return Math.max(2, Math.min(5, Math.round(+this.look().lvl) || 5)); },
  setLook(v) {
    try { localStorage.setItem('ms-look', JSON.stringify(v)); } catch (e) {}
    this.applyLook();
  },
  applyLook() {
    const v = this.look(), lvl = this.level();
    const rgb = x => { const h = (x || '#000000').replace('#',''); return [parseInt(h.substr(0,2),16), parseInt(h.substr(2,2),16), parseInt(h.substr(4,2),16)]; };
    const [r, g, b] = rgb(v.color);
    const lum = (0.299*r + 0.587*g + 0.114*b) / 255;
    const st = document.documentElement.style;
    // Glass levels can only be see-through if Glassify started in glass mode (switching needs its quick restart).
    const glass = glassOf(lvl) && glassOf(this.startLevel || lvl);
    const alpha = glass ? v.alpha / 100 : 1;
    st.setProperty('--ms-rgb', r + ',' + g + ',' + b);
    st.setProperty('--ms-rgb-dark', Math.round(r*.45+8) + ',' + Math.round(g*.45+8) + ',' + Math.round(b*.45+10));
    st.setProperty('--ms-alpha', alpha.toFixed(2));
    st.setProperty('--ms-alpha-strong', Math.min(1, alpha + .12).toFixed(2));
    const [ar, ag, ab] = rgb(v.accent);
    const alum = (0.299*ar + 0.587*ag + 0.114*ab) / 255;
    st.setProperty('--ms-accent', v.accent);
    st.setProperty('--ms-accent-hi', 'rgb(' + [ar, ag, ab].map(c => Math.round(c + (255 - c) * .2)).join(',') + ')');
    st.setProperty('--ms-accent-ink', alum > .55 ? '#141414' : '#ffffff');
    const cl = document.documentElement.classList;
    const eco = lvl <= 2;   // Light level includes the CPU savings (the separate Eco switch was removed: no noticeable effect)
    cl.toggle('ms-light', lum > .62 && (alpha > .55));
    cl.toggle('ms-solid', !glass);
    cl.toggle('ms-lite', lvl <= 2);
    cl.toggle('ms-eco', eco);
    const state = 'eco=' + (eco ? 1 : 0) + ',lvl=' + lvl + ',mini=' + (v.miniOnMin ? 1 : 0);
    if (this._state !== state) { this._state = state; post('power:' + state); }
  },
  // Levels 4 and below close Spotify's right 'Now playing' panel whenever it appears.
  // Back at level 5 it re-opens once (the flag survives the restart), then it's up to you.
  nowPlayingPanel() {
    const lvl = this.level();
    const panel = document.getElementById('Desktop_PanelContainer_Id');
    // When closed, Spotify shrinks the panel's column to a thin strip but keeps the panel itself full width inside it,
    // so 'open' means the narrowest of its containers is wide.
    let visible = false;
    if (panel) { let w = panel.getBoundingClientRect().width; for (let a = panel.parentElement, k = 0; a && k < 5; a = a.parentElement, k++) w = Math.min(w, a.getBoundingClientRect().width); visible = w > 120; }
    if (lvl <= 4) {
      if (!visible) return;
      const hide = [...panel.querySelectorAll('button')].find(b => /^(hide|close)/i.test(b.getAttribute('aria-label') || ''));
      if (hide) hide.click();
      return;
    }
    let reopen = false; try { reopen = localStorage.getItem('ms-npv-open') === '1'; } catch (e) {}
    if (!reopen) return;
    const done = () => { try { localStorage.removeItem('ms-npv-open'); } catch (e) {} };
    if (visible) { done(); return; }
    const open = [...document.querySelectorAll('button')].find(b => /^show now playing/i.test(b.getAttribute('aria-label') || ''))
              || q('[data-testid=""now-playing-bar""] [data-testid=""cover-art-button""]');
    if (!open) return;
    done(); open.click();
  },
  // After Glassify restarts itself (look change) or recovers from a crash, carry on playing:
  // if music was playing within the last ~20 seconds before this page loaded, press play once it's ready.
  resume() {
    if (this._resumed) return;
    if (this._loadedAt === undefined) {
      this._loadedAt = Date.now();
      let a = 0, b = 0; try { a = +localStorage.getItem('ms-resume') || 0; b = +localStorage.getItem('ms-lastplay') || 0; } catch (e) {}
      this._resumeWanted = (a && Date.now() - a < 120000) || (b && Date.now() - b < 20000);
      try { localStorage.removeItem('ms-resume'); } catch (e) {}
    }
    if (!this._resumeWanted) { this._resumed = true; return; }
    if (Date.now() - this._loadedAt > 60000) { this._resumed = true; return; }   // give up after a minute
    const pp = q('[data-testid=""control-button-playpause""]');
    if (!pp || pp.disabled) return;
    this._resumed = true;
    if (!/pause/i.test(pp.getAttribute('aria-label') || '')) setTimeout(() => pp.click(), 600);
  },
  toggleSettings() {
    let p = document.getElementById('ms-settings');
    if (p) { p.remove(); return; }
    const v = this.look(), start = this.startLevel || this.level();
    p = document.createElement('div'); p.id = 'ms-settings';
    const panelSw = ['#5a2448','#ff5fa8','#3b2a6e','#1e3a6e','#6e1e2a','#101014','#f2eef0'];
    const accentSw = ['#ff5fa8','#1ed760','#4aa8ff','#a77bff','#ff4d4d','#ffb020','#ffffff'];
    const row = (cls, list, val) => '<div class=""ms-sw ' + cls + '"">' + list.map(c => '<button style=""background:' + c + '"" data-c=""' + c + '"" title=""' + c + '""></button>').join('') +
      '<label class=""ms-pick"" title=""Any colour""><input type=""color"" value=""' + val + '""></label></div>';
    p.innerHTML =
      '<div class=""ms-h"">Look <span class=""ms-lvlname ms-val""></span></div>' +
      '<input class=""ms-lvl"" type=""range"" min=""2"" max=""5"" step=""1"" value=""' + this.level() + '"">' +
      '<div class=""ms-desc ms-lvldesc""></div>' +
      '<div class=""ms-h"">Panel colour</div>' + row('ms-panel', panelSw, v.color) +
      '<div class=""ms-alphawrap""><div class=""ms-h"">Transparency <span class=""ms-val ms-alphaval""></span></div><input class=""ms-range"" type=""range"" min=""0"" max=""95"" value=""' + (100 - v.alpha) + '""></div>' +
      '<div class=""ms-h"">Highlight colour</div>' + row('ms-accent', accentSw, v.accent) +
      '<label class=""ms-check ms-minimin""><input type=""checkbox""' + (v.miniOnMin ? ' checked' : '') + '><span><b>Minimize to mini player</b><br>Minimizing hides the main window and leaves only the mini player</span></label>' +
      '<div class=""ms-h"">Gaming</div>' +
      '<label class=""ms-check""><input type=""checkbox"" data-o=""game""><span><b>Automatic game mode</b><br>When a fullscreen game starts, switch to the mini player and low priority; restore after</span></label>' +
      '<label class=""ms-check""><input type=""checkbox"" data-o=""ghost""><span><b>Ghost mini player in games</b><br>See-through and click-through while gaming, so it never blocks a click</span></label>' +
      '<div class=""ms-h"">Extras</div>' +
      '<label class=""ms-check""><input type=""checkbox"" data-o=""toast""><span><b>Song-change popup</b><br>A small popup with the cover when the song changes (when Glassify isn\'t in front)</span></label>' +
      '<label class=""ms-check""><input type=""checkbox"" data-o=""auto""><span><b>Start with Windows</b></span></label>' +
      '<label class=""ms-check""><input type=""checkbox"" data-o=""startmini""><span><b>Start in the mini player</b><br>When started with Windows, open as just the mini player</span></label>' +
      '<div class=""ms-h"">Shortcuts</div>' +
      '<div class=""ms-desc ms-keys"">Ctrl+Alt+M &nbsp;mini player<br>Ctrl+Alt+G &nbsp;ghost mini player<br>Ctrl+Alt+L &nbsp;like song<br>Ctrl+Alt+&uarr; / &darr; &nbsp;volume<br>Media keys &nbsp;play/pause, next, previous</div>' +
      '<div class=""ms-desc ms-ver""></div>' +
      '<div class=""ms-note""></div>' +
      '<div class=""ms-row""><button class=""ms-reset"">Reset</button><button class=""ms-done"">Done</button></div>';
    const $ = s => p.querySelector(s);
    const lvlR = $('.ms-lvl'), range = $('.ms-range'), pick = $('.ms-panel input'), apick = $('.ms-accent input');
    const minimin = $('.ms-minimin input'), note = $('.ms-note');
    const needsRestart = () => { const l = +lvlR.value; return glassOf(l) !== glassOf(start) || gpuOf(l) !== gpuOf(start); };
    const show = () => {
      const l = +lvlR.value;
      $('.ms-lvlname').textContent = LEVELS[l][0];
      $('.ms-lvldesc').textContent = LEVELS[l][1];
      $('.ms-alphaval').textContent = range.value + '%';
      $('.ms-alphawrap').classList.toggle('ms-dim', !glassOf(l));
      note.textContent = needsRestart() ? 'Done restarts Glassify (about 3 s) to apply this. Your song keeps going.' : '';
    };
    const save = () => this.setLook({ color: pick.value, alpha: 100 - (+range.value), accent: apick.value, eco: false, lvl: +lvlR.value, miniOnMin: minimin.checked });
    const update = () => { show(); save(); };
    lvlR.oninput = update; minimin.onchange = update;
    // gaming & extras switches are app options (saved by Glassify itself, so they work before the page loads)
    const o = this.opts();
    p.querySelectorAll('input[data-o]').forEach(c => { c.checked = !!o[c.dataset.o]; c.onchange = () => { const n = this.opts(); n[c.dataset.o] = c.checked; this.setOpts(n); }; });
    $('.ms-ver').innerHTML = 'Glassify ' + (window.__msVersion || '') + (this._update ? ' &middot; <a href=""#"" class=""ms-upd"">' + this._update.tag + ' available</a>' : '');
    const upd = $('.ms-upd'); if (upd) upd.onclick = e => { e.preventDefault(); this.startUpdate(); };
    p.querySelectorAll('.ms-panel button').forEach(b => b.onclick = () => { pick.value = b.dataset.c; update(); });
    p.querySelectorAll('.ms-accent button').forEach(b => b.onclick = () => { apick.value = b.dataset.c; update(); });
    pick.oninput = update; apick.oninput = update; range.oninput = update;
    $('.ms-reset').onclick = () => { lvlR.value = 5; pick.value = '#5a2448'; apick.value = '#ff5fa8'; range.value = 18; minimin.checked = false; update(); };
    $('.ms-done').onclick = () => {
      p.remove();
      if (+lvlR.value === 5 && start <= 4) { try { localStorage.setItem('ms-npv-open', '1'); } catch (e) {} }
      if (needsRestart()) {
        const pp = q('[data-testid=""control-button-playpause""]');
        if (pp && /pause/i.test(pp.getAttribute('aria-label') || '')) { try { localStorage.setItem('ms-resume', String(Date.now())); } catch (e) {} }
        post('restart');
      }
    };
    show();
    document.body.appendChild(p);
  },
  edges() {
    if (document.getElementById('ms-edges')) return;
    const e = document.createElement('div'); e.id = 'ms-edges';
    // WMSZ codes: 1 left, 2 right, 3 top, 4 top-left, 5 top-right, 6 bottom, 7 bottom-left, 8 bottom-right
    e.innerHTML = [['l',1],['r',2],['t',3],['tl',4],['tr',5],['b',6],['bl',7],['br',8]].map(([c,n]) => '<div class=""ms-e ms-' + c + '"" data-n=""' + n + '""></div>').join('');
    e.querySelectorAll('.ms-e').forEach(d => d.addEventListener('mousedown', ev => { if (ev.button === 0) { ev.preventDefault(); post('size:' + d.dataset.n); } }));
    document.body.appendChild(e);
  },
  state() {
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    const pp = q('[data-testid=""control-button-playpause""]');
    const label = (pp && pp.getAttribute('aria-label')) || '';
    const ps = navigator.mediaSession.playbackState;
    const playing = ps === 'playing' || (ps !== 'paused' && /pause/i.test(label));
    const v = this.look();
    return JSON.stringify({
      t: md ? md.title : ((q('[data-testid=""context-item-link""]')||{}).textContent || ''),
      a: md ? md.artist : '',
      art: this.art() || '',
      playing,
      pos: (q('[data-testid=""playback-position""]')||{}).textContent || '',
      dur: (q('[data-testid=""playback-duration""]')||{}).textContent || '',
      liked: this.liked(),
      color: v.color, accent: v.accent, alpha: v.alpha
    });
  },
  click(t) { const b = q('[data-testid=""' + t + '""]'); if (b) { b.click(); return 'ok'; } return 'missing'; },
  // set a Spotify slider (progress / volume) the way a user dragging it would
  setRange(sel, fn) {
    const r = q(sel);
    if (!r) return 'missing';
    const min = +r.min || 0, max = +r.max || 1;
    const v = Math.max(min, Math.min(max, fn(+r.value, min, max)));
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(r, String(v));
    r.dispatchEvent(new Event('input', { bubbles: true }));
    r.dispatchEvent(new Event('change', { bubbles: true }));
    return 'ok';
  },
  seek(frac) { return this.setRange('[data-testid=""playback-progressbar""] input[type=range]', (v, min, max) => min + frac * (max - min)); },
  volume(d) { return this.setRange('[data-testid=""volume-bar""] input[type=range]', (v, min, max) => v + d * (max - min)); },
  // the save/like button next to the song in the player bar ('Add to Liked Songs', or 'Add to playlist' once liked)
  likeButton() { return [...document.querySelectorAll('[data-testid=""now-playing-bar""] button[aria-checked]')].find(b => /liked songs|playlist/i.test(b.getAttribute('aria-label') || '') && !/shuffle|repeat/i.test(b.getAttribute('aria-label') || '')); },
  liked() { const b = this.likeButton(); return !!b && b.getAttribute('aria-checked') === 'true'; },
  // saves the current song to Liked Songs (never un-likes by accident: Spotify's own button opens a menu when already liked)
  like() { const b = this.likeButton(); if (!b) return 'missing'; if (!this.liked()) b.click(); return 'ok'; },
  // remember we're playing (so a crash/restart can carry on) and announce song changes (for the popup)
  heartbeat() {
    const pp = q('[data-testid=""control-button-playpause""]');
    if (pp && /pause/i.test(pp.getAttribute('aria-label') || '')) { try { localStorage.setItem('ms-lastplay', String(Date.now())); } catch (e) {} }
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    const key = md ? md.title + '|' + md.artist : '';
    if (key && this._track !== undefined && key !== this._track) post('track:' + JSON.stringify({ t: md.title, a: md.artist, art: this.art() }));
    if (key) this._track = key;
  },
  showUpdate(tag, url, auto) {
    this._update = { tag, url, auto: !!auto };
    const w = document.getElementById('ms-winctl');
    if (!w) return;
    let b = document.getElementById('ms-update');
    if (!b) {
      b = document.createElement('button');
      b.id = 'ms-update'; b.innerHTML = '&#xE896;'; b.onclick = () => this.startUpdate();
      w.insertBefore(b, w.children[1]);
    }
    b.classList.remove('ms-busy');
    b.title = auto ? 'Glassify ' + tag + ' is available - click to update (a few seconds, your music keeps playing)'
                   : 'Glassify ' + tag + ' is available - click to open the download page';
    this.fitControls();
  },
  hideUpdate() {
    this._update = null;
    const b = document.getElementById('ms-update'); if (b) b.remove();
    this.fitControls();
  },
  // the top bar keeps exactly as much room on the right as the window buttons take, so they never cover anything
  fitControls() {
    const w = document.getElementById('ms-winctl'); if (!w) return;
    const px = Math.ceil(w.getBoundingClientRect().width) + 26;
    if (px > 26 && this._ctlw !== px) { this._ctlw = px; document.documentElement.style.setProperty('--ms-ctl-w', px + 'px'); }
  },
  // one click: Glassify downloads, checks and installs the update, then restarts itself
  startUpdate() {
    const u = this._update; if (!u) return;
    if (!u.auto) { post('open:' + u.url); return; }
    const b = document.getElementById('ms-update');
    if (b) { if (b.classList.contains('ms-busy')) return; b.classList.add('ms-busy'); b.title = 'Updating Glassify to ' + u.tag + '...'; }
    post('update');
  },
  updateFailed() {
    if (this._update) this._update.auto = false;   // next click opens the download page
    const b = document.getElementById('ms-update');
    if (b) { b.classList.remove('ms-busy'); b.title = 'Automatic update didn\'t work - click to open the download page'; }
  },
  opts() { return Object.assign({ game: true, ghost: true, toast: false, auto: false, startmini: false }, window.__msOpts || {}); },
  setOpts(o) { window.__msOpts = o; post('opts:' + ['game','ghost','toast','auto','startmini'].map(k => k + '=' + (o[k] ? 1 : 0)).join(',')); }
};
// The script can run before the page exists, so every step is guarded and the upkeep timer is always set.
const safe = () => { try { if (document.documentElement) ms.apply(); } catch (e) { window.__msErr = String(e && e.stack || e); } };
setInterval(safe, 2000);
document.addEventListener('DOMContentLoaded', safe);
const watch = () => { if (!document.documentElement) return setTimeout(watch, 50);
  new MutationObserver(() => { if (!document.getElementById(id)) safe(); }).observe(document.documentElement, { childList: true }); };
watch();
safe();
return 'installed';
})()";
    }
}
