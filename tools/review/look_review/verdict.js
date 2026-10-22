/* Verdicts (note 967; the director, 21 Oct: "give notes on latest, approve, mark for change, or reject (redo)").
   Each subject is one card: its first <article class="shot"> (the newest, as rounds are posted) large, the rest of that
   round beside it, every older round folded under History. Each model in Animations is one card, and each clip has its
   own verdict. A verdict is a row in the artifact's db, collection "verdicts": {subject, kind, title, shot, round,
   verdict: approve|change|redo, note, checklist, author, at}. The newest row for a subject is its verdict while `shot`
   is still that subject's newest; a newer shot puts it back to Awaiting you ("New since your verdict").
   Art agents read the rows and act: COORDINATION.md, "Look Review verdicts". */
(() => {
  // Each subject's Art Checklist lines (its `items` ids), the first the one the subject is mainly of.
  const CHECKLIST = {
    'cap-choir': ['choir', 'choir-fx'], 'cap-ribbits': ['ribbits'], 'cap-whistler': ['whistler', 'whistler-nest'],
    'cap-tippy': ['tippy-toesie'], 'cap-track-doll': ['track-doll'], 'cap-car-hugger': ['car-hugger'], 'cap-gaunt': ['gaunt'],
    'cap-grumbler': ['grumbler'], 'cap-stoker': ['stoker', 'stoker-glow'], 'cap-follower': ['followers', 'follower-nest'],
    'cap-climber': ['climbers'], 'cap-fire-fly': ['fire-flies'], 'cap-passenger': ['passenger'], 'cap-soot': ['soot-children'],
    'cap-switchman': ['switchman'], 'cap-cinder-hound': ['cinder-hounds'], 'cap-dragger': ['draggers'],
    'cap-moose': ['moose', 'moose-pinned'], 'cap-gannet': ['gannet'], 'cap-mourners': ['mourners'], 'cap-tower-jaw': ['tower-jaw'],
    'cap-brakeman': ['brakeman'], 'cap-knotter': ['knotter'], 'cap-freight-beetle': ['freight-beetle'], 'cap-hotbox': ['hotbox'],
    'cap-pickers': ['pickers'], 'cap-lodger': ['lodger'], 'cap-householder': ['householder'], 'cap-mouse-moose': ['mouse-moose'],
    'cap-wakers': ['waker'], 'cap-hollow-house': ['hollow-house'], 'cap-hanger': ['hanger'],
    'cap-weight': ['cut-weight'], 'cap-clinger-sleeper': ['cut-clingers', 'cut-sleepers'], 'cap-hollow': ['cut-hollow'],
    'cap-crew-model': ['crew-model', 'crew-variants', 'body'], 'cap-crew-first-person': ['crew-fp-arms'],
    'cap-crew-bodies': ['crew-carry', 'crew-rescue', 'crew-death', 'crew-grabbed', 'wreck-ragdolls', 'wreck-task-poses'],
    'cap-survivors': ['survivor-prisoner', 'survivor-wildlander', 'child-survivor', 'crew-freed', 'holdout-glow', 'holdout-lamp', 'holdout-lockup', 'holdout-prison', 'holdout-shelter'],
    'cap-crew-acts': ['crew-boiler', 'crew-breach', 'crew-cab', 'crew-cannon', 'crew-extinguisher', 'crew-ladder', 'crew-lantern', 'crew-loading', 'crew-melee', 'crew-switch', 'crew-emotes', 'crew-friendslop', 'crew-lounge'],
    'cap-firebox': ['firebox', 'furnace'], 'cap-cab': ['cab', 'loco', 'cab-doors', 'gauges', 'whistle-cord'],
    'cap-cannon': ['cannon', 'cannon-fx', 'cannon-impacts', 'gun-car', 'powder-shot'], 'cap-car-fire': ['fire', 'car-burnt'],
    'cap-derail': ['derail', 'engine-damage', 'failing-stages', 'boiler-rupture', 'car-eaten'],
    'cap-stores': ['stores', 'repair-kit', 'crew-lockers', 'tools', 'toys', 'extinguisher-mount'],
    'cap-cargo': ['cargo-car', 'freight', 'livestock', 'comet'], 'cap-gap': ['crew-gap', 'uncouple-anim'],
    'cap-cars': ['car-lamps', 'car-doors', 'car-numbers', 'train-lights', 'utility-car', 'guard-car'],
    'cap-sky-weather': ['cold', 'fog', 'fog-banks', 'rain', 'wind', 'dawn', 'breath', 'corruption', 'wet-rail'],
    'cap-trackside': ['biomes', 'marsh'], 'cap-corrupted-trees': ['corrupted-trees'],
    'cap-structures': ['iron-bridges', 'tunnels', 'weak-bridge', 'causeway'], 'cap-debris': ['debris', 'debris-telegraph'],
    'cap-sites': ['chemical-works', 'coaling-tower', 'foundry', 'grain-elevator', 'military-depot', 'mine-head', 'slaughterhouse', 'switchyard', 'wreck-yard'],
    'cap-lineside': ['lineside', 'boards', 'gameplay-boards', 'junction', 'overspeed-telegraph'], 'cap-stations': ['halts', 'fortress'],
    'cap-store': ['screens', 'capsule', 'app-icon', 'trailer'],
    'cap-hud': ['hud-look', 'menus', 'prompts', 'dead-ui', 'film-cards', 'incident', 'commendations', 'credits'],
    'anim-crew': ['crew-locomotion'], 'anim-survivor_prisoner': ['survivor-prisoner'], 'anim-survivor_wildlander': ['survivor-wildlander'],
    'anim-sheep': ['livestock-anim'], 'anim-cinder_hound': ['cinder-hounds-anim'], 'anim-switchman': ['switchman-anim'],
    'anim-soot_child': ['soot-children-anim'], 'anim-dragger': ['draggers-anim'], 'anim-track_doll': ['track-doll-anim'],
    'anim-car_hugger': ['car-hugger-anim'], 'anim-tippy_toesie': ['tippy-toesie-anim'], 'anim-whistler': ['whistler-anim'],
    'anim-ribbit': ['ribbits-anim'], 'anim-choir': ['choir-anim'], 'anim-gaunt': ['gaunt-anim'], 'anim-grumbler': ['grumbler-anim'],
    'anim-stoker': ['stoker-anim'], 'anim-follower': ['followers-anim'], 'anim-climber': ['climbers-anim'], 'anim-fire_fly': ['fire-flies'],
    'anim-passenger': ['passenger-anim'], 'anim-mouse_moose': ['mouse-moose'], 'anim-waker': ['waker'], 'anim-moose': ['moose-anim'],
  };
  const MAX = 4000;
  const LABEL = { awaiting: 'Awaiting you', approve: 'Approved', change: 'Change asked', redo: 'Redo asked' };
  const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
  const when = iso => { const d = new Date(iso); return isNaN(d) ? '' : d.toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); };
  const plural = (n, w) => n + ' ' + w + (n === 1 ? '' : 's');
  const hash = s => { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0).toString(36); };
  const imgKey = img => img ? (img.getAttribute('src') || img.dataset.pack || '') : '';
  const store = { get: k => { try { return localStorage.getItem(k); } catch { return null; } }, set: (k, v) => { try { localStorage.setItem(k, v); } catch {} } };

  const units = [];   // everything that takes a verdict: {key, kind, title, shot, round, panel, node, chip, ui}
  let db = null, user = null, meId = null, canWrite = null, verdicts = [], notes = [], seq = 0, live = false;

  // The subjects, as cards.
  for (const sec of document.querySelectorAll('#panel-captures .subject')) {
    const shots = sec.querySelector(':scope > .shots');
    const arts = shots ? [...shots.querySelectorAll(':scope > article.shot')] : [];
    if (!arts.length) continue;
    sec.classList.add('card');
    const rnd = a => a.querySelector(':scope > .nb')?.dataset.version || '';
    const hero = arts[0], r0 = rnd(hero);
    hero.classList.add('hero');
    const mates = arts.slice(1).filter(a => rnd(a) === r0);
    const older = arts.slice(1).filter(a => rnd(a) !== r0);
    const name = sec.querySelector('h2').firstChild.textContent.trim();
    const tag = hero.querySelector('.version.is-latest .ver-tag');
    const label = hero.querySelector('.version.is-latest .label')?.textContent.trim() || '';
    const u = { key: sec.id, kind: 'subject', title: name, shot: hero.id, round: r0, panel: 'captures', node: sec,
      judged: { id: hero.id, label, date: tag?.querySelector('span:not([class])')?.textContent.trim() || '', log: tag?.querySelector('a')?.getAttribute('href') || '' },
      also: mates.length };
    const ui = verdictUi(u);
    hero.querySelector(':scope > .version.is-latest').after(ui.root);
    if (mates.length) hero.after(el('p', 'round-mates-title', 'Also in this round · ' + mates.length));
    if (older.length) {
      const d = el('details', 'card-history');
      const rounds = new Set(older.map(rnd)).size;
      d.append(el('summary', null, 'History · ' + plural(older.length, 'earlier shot') + ' from ' + plural(rounds, 'round')));
      const g = el('div', 'shots'); g.append(...older); d.append(g);
      shots.after(d);
    }
    units.push(u);
  }

  // The models and their clips.
  for (const m of document.querySelectorAll('#panel-animations .model')) {
    const entity = m.id.replace(/^anim-/, '');
    const name = m.querySelector('h2').firstChild.textContent.trim();
    const figs = [...m.querySelectorAll('.clip')];
    const keys = figs.map(f => (f.querySelector('.label')?.textContent.trim() || '') + '=' + imgKey(f.querySelector('img')));
    m.classList.add('card');
    const u = { key: m.id, kind: 'model', title: name + ', the whole model', shot: 'clips-' + figs.length + '-' + hash(keys.join('|')), round: '', panel: 'animations', node: m,
      judged: { id: m.id, label: plural(figs.length, 'clip') + ', as they are now', date: '', log: '' } };
    const ui = verdictUi(u);
    m.querySelector('h2').after(ui.root);
    units.push(u);
    for (const f of figs) {
      const clip = f.querySelector('.label')?.textContent.trim() || '';
      const c = { key: 'clip:' + entity + '/' + clip, kind: 'clip', title: name + ', clip ' + clip, shot: imgKey(f.querySelector('img')), round: '', panel: 'animations', node: f, compact: true, parent: m.id };
      f.append(verdictUi(c).root);
      units.push(c);
    }
  }
  for (const u of units) u.checklist = CHECKLIST[u.key] || (u.parent ? CHECKLIST[u.parent] : null) || [];
  for (const a of document.querySelectorAll('.chips a')) {
    const u = units.find(x => '#' + x.key === a.getAttribute('href'));
    if (u) u.chip = a;
  }

  function verdictUi(u) {
    const root = el('div', 'vd');
    const head = el('div', 'vd-head');
    const state = el('span', 'vd-state', LABEL.awaiting); state.dataset.s = 'awaiting';
    const fresh = el('span', 'vd-new'); fresh.hidden = true;
    head.append(state, fresh);
    if (u.judged) {
      const j = el('span', 'vd-judged');
      const short = (u.judged.label || u.judged.id).replace(/^(.{70}\S*).+$/s, '$1…');
      const a = el('a', null, u.kind === 'subject' ? 'Judging the newest: ' + short : 'Judging ' + u.judged.label);
      a.href = '#' + u.judged.id;
      j.append(a);
      if (u.judged.date) j.append(' · ' + u.judged.date);
      head.append(j);
    }
    const last = el('div', 'vd-last'); last.hidden = true;
    const acts = el('div', 'vd-acts');
    const btn = {};
    for (const v of ['approve', 'change', 'redo']) {
      const b = el('button', null, u.compact ? { approve: 'Approve', change: 'Change', redo: 'Redo' }[v] : { approve: 'Approve', change: 'Change…', redo: 'Redo…' }[v]);
      b.type = 'button'; b.dataset.v = v; b.setAttribute('aria-pressed', 'false');
      b.setAttribute('aria-label', { approve: 'Approve', change: 'Ask for a change to', redo: 'Ask for a redo of' }[v] + ' ' + u.title);
      b.disabled = true;
      b.addEventListener('click', () => v === 'approve' ? send(u, 'approve', '') : openForm(u, v));
      acts.append(b); btn[v] = b;
    }
    const undo = el('button', 'vd-undo', 'Undo my verdict'); undo.type = 'button'; undo.hidden = true;
    undo.addEventListener('click', () => withdraw(u));
    acts.append(undo);
    const form = el('form', 'vd-form'); form.hidden = true; form.noValidate = true;
    const fid = 'vd-' + u.key.replace(/[^a-z0-9_-]/gi, '_');
    const lab = el('label'); lab.htmlFor = fid;
    const ta = el('textarea'); ta.id = fid; ta.maxLength = MAX;
    const row = el('div', 'vd-row');
    const go = el('button', 'vd-send', 'Send'); go.type = 'submit';
    const cancel = el('button', 'vd-cancel', 'Cancel'); cancel.type = 'button';
    row.append(go, cancel);
    form.append(lab, ta, row);
    const msg = el('p', 'vd-msg'); msg.setAttribute('role', 'status');
    root.append(head, last, acts, form, msg);
    let meta = null, past = null;
    if (!u.compact) {
      meta = el('div', 'vd-meta');
      past = el('details', 'vd-past'); past.hidden = true;
      root.append(meta, past);
    }
    ta.addEventListener('input', () => { go.disabled = !ta.value.trim() || u.sending; });
    ta.addEventListener('keydown', ev => { if (ev.key === 'Enter' && (ev.ctrlKey || ev.metaKey)) { ev.preventDefault(); form.requestSubmit(); } if (ev.key === 'Escape') closeForm(u); });
    cancel.addEventListener('click', () => closeForm(u));
    form.addEventListener('submit', ev => { ev.preventDefault(); if (ta.value.trim()) send(u, form.dataset.v, ta.value.trim()); });
    u.ui = { root, state, fresh, last, btn, undo, form, lab, ta, go, msg, meta, past };
    return u.ui;
  }
  function openForm(u, v) {
    const ui = u.ui;
    ui.form.hidden = false; ui.form.dataset.v = v;
    ui.lab.textContent = v === 'change' ? 'What should change? (needed)' : 'Why redo it, and what should it be instead? (needed)';
    ui.ta.placeholder = v === 'change' ? 'Shape, light, colour, the read at distance, the timing…' : 'What is wrong with it, and what it should be…';
    ui.go.textContent = v === 'change' ? 'Ask for the change' : 'Ask for a redo';
    ui.go.disabled = !ui.ta.value.trim();
    ui.ta.focus();
  }
  function closeForm(u) { u.ui.form.hidden = true; u.ui.msg.textContent = ''; }
  function say(u, text, bad) { u.ui.msg.textContent = text; u.ui.msg.classList.toggle('bad', !!bad); }

  async function send(u, verdict, note) {
    if (!db || canWrite === false || u.sending) return;
    u.sending = true; sync(u); say(u, 'Saving…');
    const doc = { subject: u.key, kind: u.kind, title: u.title, shot: u.shot, round: u.round || '', verdict, note: (note || '').slice(0, MAX),
      checklist: u.checklist, author: meId || '', at: new Date().toISOString() };
    try {
      await db.collection('verdicts').add(doc);
      u.ui.ta.value = ''; u.ui.form.hidden = true;
      say(u, LABEL[verdict] + '. The art team picks it up from here.');
    } catch (e) {
      say(u, e?.code === 'not_granted' || e?.code === 'invalid_argument' ? 'This view can read verdicts but not give them: it needs Contributor access.'
        : 'Not saved (' + (e?.code || 'error') + '). Try again.', true);
    }
    u.sending = false; sync(u);
  }
  async function withdraw(u) {
    const v = current(u).v;
    if (!db || !v || v.author !== meId) return;
    u.sending = true; sync(u);
    try { await db.collection('verdicts').doc(v.id).delete(); say(u, 'Verdict withdrawn.'); }
    catch (e) { say(u, 'Not withdrawn (' + (e?.code || 'error') + ').', true); }
    u.sending = false; sync(u);
  }

  // The newest verdict on a subject decides it while it judged the subject's newest shot.
  const byKey = new Map();
  function current(u) {
    const vs = byKey.get(u.key) || [];
    if (!vs.length) return { s: 'awaiting', v: null, stale: null, vs };
    if (vs[0].shot === u.shot) return { s: vs[0].verdict, v: vs[0], stale: null, vs };
    return { s: 'awaiting', v: null, stale: vs[0], vs };
  }
  function sync(u) {
    const on = !!db && canWrite !== false && !u.sending;
    for (const b of Object.values(u.ui.btn)) b.disabled = !on;
    u.ui.go.disabled = !on || !u.ui.ta.value.trim();
  }

  async function render() {
    const mine = ++seq;
    byKey.clear();
    for (const v of verdicts) { if (!byKey.has(v.subject)) byKey.set(v.subject, []); byKey.get(v.subject).push(v); }
    for (const vs of byKey.values()) vs.sort((a, b) => (b.at || '').localeCompare(a.at || '') || (a.id < b.id ? 1 : -1));
    const ids = [...new Set(verdicts.map(v => v.author).filter(Boolean))];
    const ps = user && ids.length ? await user.profiles(ids) : {};
    if (mine !== seq) return;
    const who = id => id && id === meId ? 'you' : (ps[id]?.name || 'Someone');
    const openNotes = new Map();
    for (const n of notes) {
      if (n.status === 'addressed') continue;
      const k = n.kind === 'shot' ? 'cap-' + n.subject : n.kind === 'model' ? 'anim-' + n.entity : n.kind === 'clip' ? 'clip:' + n.entity + '/' + n.clip : '';
      openNotes.set(k, (openNotes.get(k) || 0) + 1);
    }
    for (const u of units) {
      const { s, v, stale, vs } = current(u), ui = u.ui;
      u.state = s;
      u.node.dataset.state = s;
      ui.state.dataset.s = s; ui.state.textContent = LABEL[s];
      ui.fresh.hidden = !stale;
      if (stale) ui.fresh.textContent = 'New since your verdict (' + LABEL[stale.verdict].toLowerCase() + ', ' + when(stale.at) + ')';
      for (const [k, b] of Object.entries(ui.btn)) b.setAttribute('aria-pressed', String(s === k));
      ui.undo.hidden = !(v && v.author && v.author === meId) || !db || canWrite === false;
      const shown = v || stale;
      ui.last.hidden = !shown || u.compact && !v;
      if (shown) {
        ui.last.replaceChildren(el('span', 'who', LABEL[shown.verdict] + ' by ' + who(shown.author) + ' · ' + when(shown.at) + (shown === stale ? ' · on an earlier shot' : '')));
        if (shown.note) ui.last.append(el('p', null, shown.note));
      }
      if (u.chip) u.chip.dataset.state = s;
      if (ui.meta) {
        const parts = [];
        if (u.checklist.length) parts.push(el('span', null, 'Checklist: ' + u.checklist.join(', ')));
        let k = openNotes.get(u.key) || 0;
        if (u.kind === 'model') for (const c of units) if (c.parent === u.key) k += openNotes.get(c.key) || 0;
        if (k) { const n = el('span'); n.append(el('b', null, plural(k, 'open note')), u.kind === 'subject' ? ' under its shots' : ' on it and its clips'); parts.push(n); }
        if (u.judged?.log) { const a = el('a', null, 'Round log'); a.href = u.judged.log; parts.push(a); }
        ui.meta.replaceChildren(...parts);
        ui.meta.hidden = !parts.length;
      }
      if (ui.past) {
        const older = vs.filter(x => x !== (v || stale));
        ui.past.hidden = !older.length;
        if (older.length) {
          const list = el('ol');
          for (const x of older) { const li = el('li', null, LABEL[x.verdict] + ' by ' + who(x.author) + ' · ' + when(x.at)); if (x.note) li.append(el('p', null, x.note)); list.append(li); }
          ui.past.replaceChildren(el('summary', null, plural(older.length, 'earlier verdict')), list);
        }
      }
      sync(u);
    }
    filter();
  }

  // Filters: Awaiting you / Changes asked / Redo / Approved / All, per panel, with their counts.
  const chosen = {};
  for (const bar of document.querySelectorAll('.vfilters')) {
    const panel = bar.dataset.panel;
    const saved = store.get('dt-look-filter:' + panel);
    chosen[panel] = ['awaiting', 'change', 'redo', 'approve', 'all'].includes(saved) ? saved : 'awaiting';
    for (const b of bar.querySelectorAll('button')) b.addEventListener('click', () => { chosen[panel] = b.dataset.f; store.set('dt-look-filter:' + panel, b.dataset.f); filter(); });
  }
  function filter() {
    for (const bar of document.querySelectorAll('.vfilters')) {
      const panel = bar.dataset.panel, f = chosen[panel];
      const cards = units.filter(u => u.panel === panel && u.kind !== 'clip');
      const n = { all: cards.length, awaiting: 0, change: 0, redo: 0, approve: 0 };
      for (const u of cards) n[u.state || 'awaiting']++;
      for (const b of bar.querySelectorAll('button')) {
        b.setAttribute('aria-pressed', String(b.dataset.f === f));
        b.querySelector('b').textContent = n[b.dataset.f];
      }
      let shown = 0;
      for (const u of cards) {
        const on = f === 'all' || (u.state || 'awaiting') === f;
        u.node.hidden = !on; if (on) shown++;
        if (u.chip) u.chip.closest('li').hidden = !on;
      }
      const empty = bar.parentElement.querySelector('.vfilters-empty');
      if (empty) {
        empty.hidden = shown > 0;
        empty.textContent = { awaiting: 'Nothing is waiting for you: every newest shot here has your verdict.', change: 'No changes asked right now.', redo: 'Nothing is marked for a redo.', approve: 'Nothing approved yet.', all: '' }[f];
      }
    }
  }
  // A link to something in a filtered-out card shows everything, so the link lands.
  const reveal = () => {
    const t = document.getElementById(decodeURIComponent(location.hash.slice(1)));
    const card = t?.closest('.card');
    if (card?.hidden) { const p = card.closest('.panel').id.replace(/^panel-/, ''); chosen[p] = 'all'; filter(); }
    let d = t?.parentElement?.closest('details');
    while (d) { d.open = true; d = d.parentElement?.closest('details'); }
  };
  addEventListener('hashchange', reveal);
  render();
  reveal();

  (async () => {
    const c = window.claude;
    [db, user] = c ? await Promise.all([c.use('db'), c.use('user')]) : [null, null];
    if (!db) { for (const u of units) if (!u.compact) say(u, 'Verdicts open in the claude.ai viewer.'); return; }
    if (user) [meId, canWrite] = await Promise.all([user.id(), user.can('data.write')]);
    if (canWrite === false) for (const u of units) { for (const b of Object.values(u.ui.btn)) b.hidden = true; if (!u.compact) say(u, 'You can read the verdicts; giving them needs Contributor access.'); }
    for (const u of units) sync(u);
    db.collection('verdicts').onSnapshot(snap => {
      verdicts = snap.docs.map(x => ({ id: x.id, ...x.data() })).filter(v => typeof v.subject === 'string' && LABEL[v.verdict] && v.verdict !== 'awaiting');
      live = true; render();
    }, e => { for (const u of units) if (!u.compact) say(u, 'Could not load the verdicts (' + (e?.code || 'error') + ').', true); });
    db.collection('notes').onSnapshot(snap => {
      notes = snap.docs.map(x => x.data()).filter(n => n && typeof n.kind === 'string');
      if (live) render();
    }, () => {});
  })();
})();
