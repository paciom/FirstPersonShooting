/* Justice Armored Heroes — www.jah.cc
   Everything the page does at runtime. No dependencies. */

/* Where PLAY goes. The marketing site and the 257 MB player are deliberately
   two deployments (see WEB_PLAN.md); this is the one line that joins them.
   play.jah.cc is Cloudflare in front of the photonarenaweb storage site — and
   Cloudflare needs an Origin Rule rewriting the Host header, or Azure 404s. */
const PLAY_URL = 'https://play.jah.cc/';

/* The roster: one row per hero, and the only place a hero's copy lives.
   `key` is also the asset name — Tools/build_site_assets.py writes
   assets/hero-<key>.webp and assets/transform-<key>.{mp4,jpg} from the game's
   own renders, so adding a tenth robot is one row plus one asset run. */
const ROSTER = [
  { key:'ranger',  name:'Ranger',  role:'Scout hovercraft',
    text:'The robot every arena builds first: even armour, even guns, nothing to learn before you are dangerous. Folds down into a pointed white hovercraft trimmed in cyan.' },
  { key:'titan',   name:'Titan',   role:'Six-wheeled assault tank',
    text:'The heaviest plate on the roster, and a stride that says so. Its second form is six wheels of layered armour behind a short forward cannon.' },
  { key:'scout',   name:'Scout',   role:'Open-frame recon buggy',
    text:'Tall, light and quick to the corner. Transforms into a stripped recon buggy on four knobbly tyres with the suspension left showing.' },
  { key:'hawk',    name:'Hawk',    role:'Wedge speeder',
    text:'Wings folded on its back and an orange shell over dark teal. Becomes a swept-wing speeder riding on twin rear thrusters.' },
  { key:'bolt',    name:'Bolt',    role:'Rail bike',
    text:'Steel blue with yellow flashes and a short temper. The fastest thing in the arena once it drops into its forward-leaning rail bike.' },
  { key:'samurai', name:'Samurai', role:'Tracked crawler',
    text:'Turquoise lacquer edged in orange, built to stand in the doorway and hold it. Unfolds into a blade-prowed armoured crawler.' },
  { key:'panther', name:'Panther', role:'Pursuit car',
    text:'A black cat helm over orange plating lit in cyan. Its vehicle is a low pursuit car — long bonnet, haunched rear arches, no patience.' },
  { key:'knight',  name:'Knight',  role:'Shielded battle wagon',
    text:'Orange and white armour with a cyan core burning through the seams. Becomes a ram-plated battle wagon that takes the front of a push.' },
  { key:'racer',   name:'Racer',   role:'Formula racer',
    text:'White bodywork, red visor, nothing spare anywhere on it. The only hero whose second form is a proper open-wheel formula car.' },
];

/* ── play links ──────────────────────────────────────────────────── */
/* ?src=site rides along so the game's own session_start can say the player
   came from this page — the only cross-property joint there is. */
for (const a of document.querySelectorAll('.js-play')) {
  a.href = PLAY_URL + '?src=site';
  a.rel = 'noopener';
}

/* ── metrics ─────────────────────────────────────────────────────── */
/* First-party and cookieless (ANALYTICS_PLAN.md): nothing is written to the
   device, and both ids are random per page load — so nothing here is a
   persistent identifier. sendBeacon so play_click survives the navigation;
   the Blob stays untyped because a typed one forces a CORS preflight that
   beacons cannot perform. */
const METRICS_URL = 'https://jah-metrics-fn.azurewebsites.net/api/e';
const mhex = n => Array.from(crypto.getRandomValues(new Uint8Array(n)),
  b => b.toString(16).padStart(2, '0')).join('');
const MIID = mhex(16), MSID = mhex(8);
let mseq = 0;
function metric(e, p) {
  try {
    const body = JSON.stringify({ v: 1, app: 'site', env: 'prod',
      iid: MIID, sid: MSID, events: [{ e, t: Date.now(), n: mseq++, p }] });
    if (!navigator.sendBeacon || !navigator.sendBeacon(METRICS_URL, new Blob([body])))
      fetch(METRICS_URL, { method: 'POST', body, keepalive: true });
  } catch (err) { /* analytics never breaks the page */ }
}
metric('page_view', { path: location.pathname, ref: document.referrer.slice(0, 64) });
for (const a of document.querySelectorAll('.js-play'))
  a.addEventListener('click', () => metric('play_click', {}));

/* ── footer year ─────────────────────────────────────────────────── */
document.getElementById('year').textContent = new Date().getFullYear();

/* ── roster ──────────────────────────────────────────────────────── */
const roster = document.getElementById('roster');
if (roster) {
  roster.innerHTML = ROSTER.map(h => `
    <button class="unit" type="button" data-key="${h.key}" aria-pressed="false">
      <span class="unit__stage">
        <span class="unit__flash">Transform ▸</span>
        <img src="assets/hero-${h.key}.webp" alt="${h.name}, in robot form" loading="lazy">
      </span>
      <span class="unit__body">
        <h3>${h.name}</h3>
        <span class="unit__role">${h.role}</span>
        <p>${h.text}</p>
      </span>
    </button>`).join('');

  /* The clip is only fetched when a hero is actually asked to transform —
     nine 150 KB videos on load would cost more than the rest of the page. */
  const play = unit => {
    const key = unit.dataset.key;
    let video = unit.querySelector('video');
    if (!video) {
      video = document.createElement('video');
      video.src = `assets/transform-${key}.mp4`;
      video.poster = `assets/transform-${key}.jpg`;
      video.muted = true; video.loop = true; video.playsInline = true;
      video.setAttribute('aria-hidden', 'true');
      unit.querySelector('.unit__stage').appendChild(video);
    }
    unit.classList.add('is-live');
    unit.setAttribute('aria-pressed', 'true');
    video.play().catch(() => {});   /* autoplay refusal is not an error here */
  };

  const stop = unit => {
    const video = unit.querySelector('video');
    if (video) { video.pause(); video.currentTime = 0; }
    unit.classList.remove('is-live');
    unit.setAttribute('aria-pressed', 'false');
  };

  const hoverable = window.matchMedia('(hover:hover)').matches;
  for (const unit of roster.querySelectorAll('.unit')) {
    if (hoverable) {
      unit.addEventListener('pointerenter', () => play(unit));
      unit.addEventListener('pointerleave', () => stop(unit));
      unit.addEventListener('focus', () => play(unit));
      unit.addEventListener('blur', () => stop(unit));
    }
    /* Touch (and keyboard Enter) toggles instead, since there is no leave. */
    unit.addEventListener('click', () => {
      unit.classList.contains('is-live') ? stop(unit) : play(unit);
    });
  }
}

/* ── scroll reveal ───────────────────────────────────────────────── */
const reduced = window.matchMedia('(prefers-reduced-motion:reduce)').matches;
const reveals = document.querySelectorAll('.reveal');
if (reduced || !('IntersectionObserver' in window)) {
  reveals.forEach(el => el.classList.add('is-in'));
} else {
  const io = new IntersectionObserver((entries, obs) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      e.target.classList.add('is-in');
      obs.unobserve(e.target);
    }
  }, { rootMargin: '0px 0px -12% 0px' });
  reveals.forEach(el => io.observe(el));
}

/* ── nav: stick, and open on small screens ───────────────────────── */
const nav = document.getElementById('nav');
const burger = nav.querySelector('.nav__burger');
burger.addEventListener('click', () => {
  const open = nav.classList.toggle('is-open');
  burger.setAttribute('aria-expanded', String(open));
});
for (const link of nav.querySelectorAll('.nav__links a')) {
  link.addEventListener('click', () => {
    nav.classList.remove('is-open');
    burger.setAttribute('aria-expanded', 'false');
  });
}

/* ── hero parallax ───────────────────────────────────────────────── */
const keyart = document.getElementById('keyart');
const cast = document.getElementById('cast');
if (keyart && !reduced) {
  let ticking = false;
  const move = () => {
    /* The plate sits 6% proud of the top of the section, so it can drift down
       as the page scrolls without ever exposing an edge. The cast is a separate
       layer and moves less, which is the whole reason for splitting them. */
    const s = Math.min(window.scrollY, 700);
    keyart.style.transform = `translate3d(0,${s * 0.18}px,0) scale(1.06)`;
    if (cast) cast.style.transform = `translate3d(0,${s * -0.06}px,0)`;
    ticking = false;
  };
  addEventListener('scroll', () => {
    if (!ticking) { ticking = true; requestAnimationFrame(move); }
  }, { passive: true });
  move();
}

/* The nav only grows its backdrop once it has something to sit on top of. */
if ('IntersectionObserver' in window) {
  const sentinel = document.createElement('div');
  sentinel.style.cssText = 'position:absolute;top:0;height:90px;width:1px;pointer-events:none';
  document.body.prepend(sentinel);
  new IntersectionObserver(
    ([e]) => nav.classList.toggle('is-stuck', !e.isIntersecting)
  ).observe(sentinel);
}
