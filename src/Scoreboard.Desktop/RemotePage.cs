namespace Scoreboard.Desktop;

/// <summary>
/// The pages the remote control serves: a password sign-in, and one console that carries every
/// value the on-screen control panel has — scores, clocks, down and distance, sport values, custom
/// stats, layouts, Update Output and Live. Values are edited in the page and commit on Enter, so a
/// phone keyboard never needs a pop-up dialog.
/// </summary>
public static class RemotePage
{
    public const string ProductName = "ScorePlus";

    // Assembled by replacement rather than string interpolation: the page is mostly braces
    // (CSS and JavaScript), and an interpolated literal would have to escape every one of them.
    const string Style = """
        *{box-sizing:border-box}
        html,body{margin:0;padding:0;background:#0b0f15;color:#f4f7fb;font-family:"Segoe UI",system-ui,sans-serif}
        body{padding:10px 10px 34px;max-width:820px;margin:0 auto}
        header{display:flex;align-items:center;gap:9px;padding:6px 2px 12px}
        header .mark{height:26px;width:auto;display:block}
        header h1{font-size:12px;letter-spacing:1.6px;margin:0;font-weight:800;color:#8fa0b5}
        header .sport{font-size:11px;color:#8fa0b5;margin-left:auto;text-align:right}
        header a{color:#8fa0b5;font-size:11px;text-decoration:none;border:1px solid #263241;border-radius:6px;padding:5px 8px;white-space:nowrap}
        .card{background:#131a24;border:1px solid #1f2a37;border-radius:12px;padding:10px;margin-bottom:9px}
        .title{font-size:10px;letter-spacing:1.4px;color:#8fa0b5;font-weight:800;margin:0 0 8px;display:flex;align-items:center;gap:6px}
        .title:before{content:"";width:3px;height:11px;border-radius:2px;background:#38d7a3}
        .title .right{margin-left:auto;font-weight:700;letter-spacing:.4px;color:#5c6b7e}
        #mediaNow{max-width:68%;text-align:right;white-space:normal;overflow-wrap:anywhere;line-height:1.35}
        .score{display:grid;grid-template-columns:1fr 132px 1fr;gap:8px;align-items:stretch}
        .team{background:#101720;border:1px solid #1f2a37;border-radius:10px;padding:8px;min-width:0}
        .team .row{flex-wrap:wrap}
        .team .chip{width:16px;height:16px;border-radius:4px;border:1px solid #2b394a;flex:0 0 16px;margin-top:7px}
        .team input.name{width:100%;font-size:12px;font-weight:700;letter-spacing:.6px;text-transform:uppercase;padding:7px 6px;min-height:36px}
        .team input.num{width:100%;font-size:40px;font-weight:800;line-height:1;text-align:center;padding:2px 4px;min-height:56px;background:#0b1118;font-variant-numeric:tabular-nums}
        .team input.abbr{width:64px;font-size:11px;font-weight:700;text-transform:uppercase;padding:7px 6px;min-height:34px}
        .team input.color{flex:1 1 74px;min-width:0;font-size:11px;padding:7px 6px;min-height:34px}
        .tos{font-size:10px;color:#7d8da1;display:flex;align-items:center;gap:6px;margin-top:6px}
        .tos b{font-size:17px;color:#f4f7fb}
        .center{display:flex;flex-direction:column;align-items:center;gap:5px}
        .center input.clock{width:100%;font-size:30px;font-weight:800;text-align:center;font-variant-numeric:tabular-nums;letter-spacing:.5px;padding:4px 2px;min-height:48px}
        .center .periodrow{display:flex;align-items:center;gap:4px;width:100%}
        .center input.period{width:100%;text-align:center;font-size:12px;font-weight:700;padding:6px 2px;min-height:34px}
        .badge{font-size:9px;font-weight:800;border-radius:5px;padding:3px 6px;letter-spacing:.6px}
        .badge.ok{color:#38d7a3;background:#0d2a22}
        .badge.staged{color:#ffd166;background:#2c2410}
        button{font-family:inherit;font-size:13px;font-weight:700;color:#eef3f9;background:#1b2531;border:1px solid #2b394a;border-radius:8px;padding:9px 6px;min-height:42px;cursor:pointer;touch-action:manipulation}
        button:active{background:#26415a}
        button:disabled{opacity:.4;cursor:default}
        button.small{font-size:11px;min-height:36px;padding:7px 5px}
        input,select{font-family:inherit;color:#f4f7fb;background:#0b1118;border:1px solid #2b394a;border-radius:8px;font-size:14px;min-height:38px}
        input:focus{outline:2px solid #38d7a3;outline-offset:1px}
        .row{display:flex;gap:6px;margin-top:6px}
        .row button{flex:1 1 46px}
        .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:7px}
        .value{display:grid;grid-template-columns:1fr 40px minmax(46px,62px) 40px;align-items:center;gap:4px;background:#101720;border:1px solid #1f2a37;border-radius:8px;padding:5px 6px;min-height:46px}
        .value .l{font-size:10px;letter-spacing:.4px;color:#9fb0c4;text-transform:uppercase;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .value input{text-align:center;font-weight:800;padding:4px 2px;min-height:38px;font-size:15px}
        .value.wide{grid-template-columns:1fr 40px minmax(46px,62px) 40px 34px}
        .value .x{font-size:14px;min-height:38px;padding:0;background:#2a1720;border-color:#5b2634;color:#ffd7df}
        .plus{background:#123c33;border-color:#1d6b56;color:#c9f7e8}
        .minus{background:#2a1720;border-color:#5b2634;color:#ffd7df}
        .flag{display:flex;align-items:center;justify-content:space-between;gap:6px;width:100%;background:#101720;border:1px solid #1f2a37;min-height:46px;padding:6px 10px}
        .flag .state{font-size:11px;font-weight:800;color:#7d8da1}
        .flag.on{border-color:#1d6b56;background:#0f2b24}
        .flag.on .state{color:#38d7a3}
        .muted{color:#5c6b7e;font-size:11px;padding:2px}
        .warn{background:#2c2410;border:1px solid #6b5620;color:#ffd166;font-size:10px;line-height:1.45;border-radius:8px;padding:7px 9px;margin:0 0 8px}
        .groupHead{grid-column:1/-1;color:#38d7a3;font-size:10px;font-weight:700;letter-spacing:.05em;padding:5px 2px 1px;border-bottom:1px solid #1d2632}
        .poss{display:grid;grid-template-columns:1fr 1fr;gap:6px}
        .poss button{min-height:44px}
        .poss button.on{background:#123c33;border-color:#14a67c;color:#c9f7e8}
        .layoutbar{display:flex;align-items:center;gap:6px}
        .layoutbar .label{flex:1 1 auto;text-align:center;font-size:12px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .publish{background:#0b6b50;border-color:#14a67c}
        .live-on{background:#123c33;border-color:#14a67c;color:#c9f7e8}
        .live-off{background:#2a1720;border-color:#5b2634;color:#ffd7df}
        .flash{position:fixed;left:0;right:0;bottom:0;background:#111a24;border-top:1px solid #2b394a;color:#9fb0c4;font-size:11px;padding:7px 12px;text-align:center;opacity:0;transition:opacity .2s}
        .flash.show{opacity:1}
        .statEdit{display:flex;align-items:center;gap:6px;margin-top:6px}
        .statEdit .l{flex:1 1 auto;font-size:11px;color:#9fb0c4;text-transform:uppercase;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .statEdit input{width:72px;text-align:center;font-weight:800}
        .statEdit button{min-width:42px}
        #lockScreen{position:fixed;inset:0;z-index:50;background:rgba(5,8,12,.94);display:flex;align-items:center;justify-content:center;padding:24px;text-align:center}
        #lockScreen .box{max-width:340px;background:#131a24;border:1px solid #5b2634;border-radius:14px;padding:22px}
        #lockScreen h2{font-size:15px;letter-spacing:1.4px;margin:0 0 8px;color:#ffd7df}
        #lockScreen p{color:#9fb0c4;font-size:12px;line-height:1.5;margin:0}
        .login{max-width:360px;margin:60px auto;padding:22px;background:#131a24;border:1px solid #1f2a37;border-radius:14px}
        .login .brand{height:54px;margin:0 auto 12px;display:block}
        .login h2{font-size:15px;letter-spacing:1.4px;margin:0 0 6px}
        .login p{color:#9fb0c4;font-size:12px;line-height:1.5;margin:0 0 14px}
        .login input{width:100%;padding:12px;font-size:15px}
        .login button{width:100%;margin-top:10px;background:#0b6b50;border-color:#14a67c;min-height:44px}
        .tos{flex-wrap:wrap}
        /* On a phone the three columns cannot share 390px without the clock buttons clipping, so
           the teams stack with the clock between them and each one gets the full width. */
        @media (max-width:640px){
          .score{grid-template-columns:1fr}
          .team input.num{font-size:38px;min-height:52px}
          .center input.clock{font-size:28px}
          .score .team:first-child{order:1}
          .score .center{order:2}
          .score .team:last-child{order:3}
        }
        """;

    const string Script = """
        const $ = id => document.getElementById(id);
        let signature = "";
        let statsSignature = "";
        let live = true;
        let clock = "0:00";
        let timeoutClock = "0:00";
        let penaltySide = "home";

        function flash(message) {
          const box = $('flash');
          if (!box) return;
          box.textContent = message;
          box.classList.add('show');
          clearTimeout(flash.timer);
          flash.timer = setTimeout(() => box.classList.remove('show'), 1600);
        }

        function post(body) {
          return fetch('/api/action', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams(body)
          }).then(r => {
            if (r.status === 401) { location = '/'; return null; }
            if (r.status === 423) { poll(); return null; }
            return r.json();
          }).then(result => {
            if (!result) return;
            if (result.ok === false && result.message) flash(result.message);
            return poll();
          }).catch(() => flash('Lost the console — check the app is running.'));
        }

        function act(id, extra) {
          const body = Object.assign({ a: id }, extra || {});
          return post(body);
        }

        // M:SS or plain seconds, so the operator can type either one anywhere.
        function toSeconds(text) {
          const clean = String(text || '').trim();
          if (clean.indexOf(':') >= 0) {
            const parts = clean.split(':');
            return Math.max(0, (parseInt(parts[0], 10) || 0) * 60 + (parseInt(parts[1], 10) || 0));
          }
          return Math.max(0, parseInt(clean, 10) || 0);
        }

        function commit(field) {
          const action = field.dataset.set;
          if (!action) return;
          const extra = Object.assign({}, field.dataset);
          delete extra.set;
          const raw = field.value;
          extra.value = field.dataset.kind === 'clock' ? String(toSeconds(raw)) : String(raw).trim();
          field.value = raw.trim();
          post(Object.assign({ a: action }, extra));
        }

        document.addEventListener('keydown', event => {
          if (event.key !== 'Enter') return;
          const field = event.target;
          if (field && field.id === 'statName') {
            event.preventDefault();
            const add = document.querySelector('[data-act="stat.add"]');
            if (add) add.click();
            return;
          }
          if (!field || !field.dataset || !field.dataset.set) return;
          event.preventDefault();
          commit(field);
          field.blur();
        });

        document.addEventListener('focusout', event => {
          const field = event.target;
          if (!field || !field.dataset || !field.dataset.set) return;
          if (field.value !== field.dataset.was) commit(field);
        });

        document.addEventListener('focusin', event => {
          const field = event.target;
          if (field && field.dataset && field.dataset.set) field.dataset.was = field.value;
        });

        document.addEventListener('click', event => {
          const button = event.target.closest('[data-act]');
          if (!button) return;
          const action = button.dataset.act;
          if (action === 'stat.add') {
            const name = ($('statName').value || '').trim();
            if (!name) { flash('Type a name for the new stat first.'); return; }
            $('statName').value = '';
            return act('stat.add', { value: name });
          }
          if (action === 'live.toggle') return act(live ? 'live.off' : 'live.on');
          if (action.startsWith('stat.plus.')) return act('stat.adjust', { id: action.slice(10), value: 1 });
          if (action.startsWith('stat.minus.')) return act('stat.adjust', { id: action.slice(11), value: -1 });
          if (action.startsWith('stat.remove.')) return act('stat.remove', { id: action.slice(12) });
          return act(action);
        });

        function valueRow(control, prefix, setAction, extra) {
          const wrap = document.createElement('div');
          wrap.className = 'value';
          const label = document.createElement('span');
          label.className = 'l';
          label.textContent = control.label;
          const minus = document.createElement('button');
          minus.className = 'minus';
          minus.textContent = '−';
          minus.dataset.act = prefix + '.minus';
          const input = document.createElement('input');
          input.inputMode = 'numeric';
          input.value = control.display;
          input.dataset.stat = control.id;
          input.dataset.set = setAction || 'sport.set';
          if (!setAction) input.dataset.control = control.id;
          Object.keys(extra || {}).forEach(key => { input.dataset[key] = extra[key]; });
          const plus = document.createElement('button');
          plus.className = 'plus';
          plus.textContent = '+';
          plus.dataset.act = prefix + '.plus';
          wrap.append(label, minus, input, plus);
          return wrap;
        }

        function flagRow(control, prefix) {
          const button = document.createElement('button');
          button.className = 'flag';
          button.dataset.act = prefix + '.toggle';
          const name = document.createElement('span');
          name.textContent = control.label;
          const state = document.createElement('span');
          state.className = 'state';
          state.textContent = control.display || 'OFF';
          button.append(name, state);
          button.dataset.flag = control.id;
          return button;
        }

        function renderControls(state) {
          const key = state.sportId + '|' + state.controls.map(c => c.id + c.label + c.flag + c.min + c.max).join(',');
          if (key !== signature) {
            signature = key;
            const host = $('controls');
            host.innerHTML = '';
            state.controls.forEach(control => {
              host.append(control.flag ? flagRow(control, 'sport.' + control.id) : valueRow(control, 'sport.' + control.id));
            });
            // An empty card says nothing: the sport simply has no extra values to offer.
            const football = $('footballValues');
            football.innerHTML = '';
            const down = valueRow({ id: 'down', label: 'Down', display: String(state.down) }, 'football.down', 'football.down.set');
            const toGo = valueRow({ id: 'togo', label: 'To go', display: String(state.toGo) }, 'football.distance', 'football.distance.set');
            const ball = valueRow({ id: 'ball', label: 'Ball on', display: String(state.ballOn) }, 'football.ball', 'football.ball.set');
            football.append(down, toGo, ball);
          }
          state.controls.forEach(control => {
            if (control.flag) {
              const button = document.querySelector('[data-flag="' + control.id + '"]');
              if (!button) return;
              button.classList.toggle('on', control.value !== 0);
              button.querySelector('.state').textContent = control.display || 'OFF';
            } else {
              const box = document.querySelector('[data-stat="' + control.id + '"]');
              if (box && document.activeElement !== box) box.value = control.display;
            }
          });
          ['down', 'togo', 'ball'].forEach((id, index) => {
            const box = $('footballValues').querySelectorAll('.value input')[index];
            if (!box || document.activeElement === box) return;
            box.value = String(id === 'down' ? state.down : id === 'togo' ? state.toGo : state.ballOn);
          });
          $('footballCard').style.display = state.football ? '' : 'none';
        }

        function renderScoreButtons(state) {
          ['away', 'home'].forEach(side => {
            const host = $(side + 'ScoreButtons');
            const key = side + state.scoring.join(',');
            if (host.dataset.key === key) return;
            host.dataset.key = key;
            host.innerHTML = '';
            state.scoring.forEach(amount => {
              const plus = document.createElement('button');
              plus.className = 'plus small';
              plus.textContent = '+' + amount;
              plus.dataset.act = side + '.score.plus' + amount;
              host.append(plus);
            });
            state.scoring.forEach(amount => {
              const minus = document.createElement('button');
              minus.className = 'minus small';
              minus.textContent = '−' + amount;
              minus.dataset.act = side + '.score.minus' + amount;
              host.append(minus);
            });
          });
        }

        function renderStats(state) {
          const key = state.stats.map(s => s.id + s.name).join(',');
          if (key !== statsSignature) {
            statsSignature = key;
            const host = $('stats');
            host.innerHTML = '';
            state.stats.forEach(stat => {
              const row = valueRow({ id: stat.id, label: stat.name, display: stat.display }, 'stat', 'stat.set');
              row.classList.add('wide');
              row.querySelector('input').dataset.id = stat.id;
              // The steppers must name the stat they belong to: a bare "stat.plus" reached the
              // console as an unknown action, so the + and − beside a stat did nothing at all.
              const steppers = row.querySelectorAll('button');
              steppers[0].dataset.act = 'stat.minus.' + stat.id;
              steppers[1].dataset.act = 'stat.plus.' + stat.id;
              const remove = document.createElement('button');
              remove.className = 'x';
              remove.textContent = '×';
              remove.title = 'Remove ' + stat.name;
              remove.dataset.act = 'stat.remove.' + stat.id;
              row.append(remove);
              host.append(row);
            });
            // Nothing to say when the list is empty: the field below is the way to add one.
          }
          state.stats.forEach(stat => {
            const box = document.querySelector('[data-stat="' + stat.id + '"]');
            if (box && document.activeElement !== box) box.value = stat.display;
          });
        }

        function setField(id, value) {
          const field = $(id);
          if (!field || document.activeElement === field) return;
          field.value = value;
        }

        // The flag card: raise or clear a penalty and say what it was for and who it was on.
        // Choosing a type or a side leaves the flag itself alone, so a correction never blinks it off.
        function penalty(body) {
          return fetch('/api/penalty/action', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams(body)
          }).then(() => poll());
        }

        function renderPenalty(state) {
          const card = $('penaltyCard');
          if (card) card.style.display = (state.penaltyTypes || []).length ? '' : 'none';
          if (!state.penalty) return;
          penaltySide = state.penalty.home ? 'home' : 'away';
          const flag = $('penaltyFlag');
          flag.textContent = state.penalty.active ? 'Flag: ' + state.penalty.type : 'Flag down';
          flag.classList.toggle('plus', !!state.penalty.active);
          $('penaltyHome').classList.toggle('plus', penaltySide === 'home');
          $('penaltyAway').classList.toggle('plus', penaltySide === 'away');
          const select = $('penaltyType');
          const key = (state.penaltyTypes || []).join('|');
          if (select.dataset.keys !== key) {
            select.innerHTML = '';
            (state.penaltyTypes || []).forEach(type => {
              const option = document.createElement('option');
              option.value = type;
              option.textContent = type;
              select.append(option);
            });
            select.dataset.keys = key;
          }
          if (document.activeElement !== select) select.value = state.penalty.type;
        }

        $('penaltyFlag').onclick = () => penalty({ a: 'flag', type: $('penaltyType').value, side: penaltySide });
        $('penaltyHome').onclick = () => { penaltySide = 'home'; penalty({ a: 'team', type: $('penaltyType').value, side: 'home' }); };
        $('penaltyAway').onclick = () => { penaltySide = 'away'; penalty({ a: 'team', type: $('penaltyType').value, side: 'away' }); };
        $('penaltyType').onchange = () => penalty({ a: 'type', type: $('penaltyType').value, side: penaltySide });

        function render(state) {
          $('sport').textContent = state.sportName;
          setField('awayName', state.away.name);
          setField('homeName', state.home.name);
          setField('awayScore', String(state.away.score));
          setField('homeScore', String(state.home.score));
          setField('awayAbbr', state.away.abbr);
          setField('homeAbbr', state.home.abbr);
          setField('awayColor', state.away.color);
          setField('homeColor', state.home.color);
          $('awayChip').style.background = state.away.color;
          $('homeChip').style.background = state.home.color;
          $('awayTimeouts').textContent = state.away.timeouts;
          $('homeTimeouts').textContent = state.home.timeouts;
          clock = state.clock;
          setField('clock', state.clock);
          setField('period', String(state.period));
          $('periodsTotal').textContent = state.periodLabel + ' · ' + state.periods + ' in the game';
          $('clockToggle').textContent = state.clockRunning ? 'Stop' : 'Start';
          $('clockToggle').classList.toggle('plus', !state.clockRunning);
          timeoutClock = state.timeoutClock;
          setField('timeoutClock', state.timeoutClock);
          $('timeoutClockToggle').textContent = state.timeoutRunning ? 'Stop' : 'Start';
          setField('playClock', String(state.playClock));
          $('playClockToggle').textContent = state.playRunning ? 'Stop' : 'Start';
          $('playCard').style.display = state.football ? '' : 'none';
          setField('shotClock', String(state.shotClock));
          $('shotClockToggle').textContent = state.shotRunning ? 'Stop' : 'Start';
          $('shotCard').style.display = state.hasShotClock ? '' : 'none';
          $('possAway').classList.toggle('on', state.possession === 'away');
          $('possHome').classList.toggle('on', state.possession === 'home');
          $('ballSide').textContent = state.ballHome ? 'HOME ▶' : '◀ VISITORS';
          $('ballSide').dataset.act = 'football.ball.side';
          $('layout').textContent = state.layout;
          const badge = $('liveBadge');
          badge.textContent = state.staged ? 'NOT ON AIR' : 'LIVE';
          badge.className = 'badge ' + (state.staged ? 'staged' : 'ok');
          live = state.live;
          const liveButton = $('liveButton');
          liveButton.textContent = live ? 'Live: ON' : 'Live: OFF';
          liveButton.className = live ? 'live-on' : 'live-off';
          renderScoreButtons(state);
          renderControls(state);
          renderStats(state);
          renderPenalty(state);
          var lock = $('lockScreen');
          if (lock) lock.hidden = !state.locked;
        }

        async function poll() {
          try {
            const response = await fetch('/api/state', { cache: 'no-store' });
            if (response.status === 401) { location = '/'; return; }
            render(await response.json());
          } catch (_) { /* the next poll picks the console back up */ }
        }

        poll();
        setInterval(poll, 900);
        """;

    const string Body = """
        <header>
          <img class="mark" src="/logo.png" alt="ScorePlus"/>
          <h1>REMOTE</h1>
          <span class="sport" id="sport">—</span>
          <a href="/logout">Sign out</a>
        </header>

        <div class="tabs">
          <button class="tab active" data-tab="console">Console</button>
          <button class="tab" data-tab="media">Media</button>
          <button class="tab" data-tab="roster">Roster</button>
        </div>

        <div class="card">
          <div class="title">SCORE <span class="right" id="liveBadge">LIVE</span></div>
          <div class="score">
            <div class="team">
              <input class="name" id="awayName" data-set="team.name" data-side="away" title="Visitors name"/>
              <input class="num" id="awayScore" data-set="score.set" data-side="away" inputmode="numeric" title="Visitors score — type it and press Enter"/>
              <div class="row" id="awayScoreButtons"></div>
              <div class="tos">
                <button class="small plus" data-act="away.timeout.plus">+ TO</button>
                <span><b id="awayTimeouts">3</b> timeouts</span>
                <button class="small" data-act="away.timeout">Timeout</button>
              </div>
              <div class="row">
                <input class="abbr" id="awayAbbr" data-set="team.abbr" data-side="away" title="Visitors abbreviation"/>
                <span class="chip" id="awayChip"></span>
                <input class="color" id="awayColor" data-set="team.color" data-side="away" title="Visitors colour, for example #4EA8FF"/>
              </div>
            </div>

            <div class="center">
              <input class="clock" id="clock" data-set="clock.set" data-kind="clock" inputmode="numeric" title="Game clock M:SS — press Enter to set"/>
              <div class="row" style="width:100%">
                <button class="small minus" data-act="clock.minus60">−1:00</button>
                <button class="small minus" data-act="clock.minus1">−0:01</button>
                <button class="small plus" data-act="clock.plus1">+0:01</button>
                <button class="small plus" data-act="clock.plus60">+1:00</button>
              </div>
              <button id="clockToggle" data-act="clock.toggle" style="width:100%">Start</button>
              <div class="periodrow">
                <button class="small" data-act="period.previous" style="flex:0 0 38px">◀</button>
                <input class="period" id="period" data-set="period.set" inputmode="numeric" title="Period — type it and press Enter"/>
                <button class="small" data-act="period.next" style="flex:0 0 38px">▶</button>
              </div>
              <span class="muted" id="periodsTotal"></span>
            </div>

            <div class="team">
              <input class="name" id="homeName" data-set="team.name" data-side="home" title="Home name"/>
              <input class="num" id="homeScore" data-set="score.set" data-side="home" inputmode="numeric" title="Home score — type it and press Enter"/>
              <div class="row" id="homeScoreButtons"></div>
              <div class="tos">
                <button class="small plus" data-act="home.timeout.plus">+ TO</button>
                <span><b id="homeTimeouts">3</b> timeouts</span>
                <button class="small" data-act="home.timeout">Timeout</button>
              </div>
              <div class="row">
                <input class="abbr" id="homeAbbr" data-set="team.abbr" data-side="home" title="Home abbreviation"/>
                <span class="chip" id="homeChip"></span>
                <input class="color" id="homeColor" data-set="team.color" data-side="home" title="Home colour, for example #38D7A3"/>
              </div>
            </div>
          </div>
        </div>

        <div class="card">
          <div class="title">CLOCKS</div>
          <div class="grid">
            <div class="value" id="playCard">
              <span class="l">PLAY CLOCK</span>
              <button class="small" id="playClockToggle" data-act="football.playclock.toggle">Start</button>
              <input id="playClock" data-set="playclock.set" inputmode="numeric" title="Play clock — type it and press Enter"/>
              <button class="minus" data-act="football.playclock.minus">−</button>
            </div>
            <div class="value" id="shotCard">
              <span class="l">SHOT CLOCK</span>
              <button class="small" id="shotClockToggle" data-act="basketball.shotclock.toggle">Start</button>
              <input id="shotClock" data-set="shotclock.set" inputmode="numeric" title="Shot clock — type it and press Enter"/>
              <button class="minus" data-act="basketball.shotclock.minus">−</button>
            </div>
            <div class="value">
              <span class="l">TIMEOUT CLOCK</span>
              <button class="small" id="timeoutClockToggle" data-act="timeoutclock.toggle">Start</button>
              <input id="timeoutClock" data-set="timeoutclock.set" data-kind="clock" title="Timeout clock M:SS — press Enter to set"/>
              <button class="minus" data-act="timeoutclock.minus1">−</button>
            </div>
          </div>
          <div class="row">
            <button class="small plus" data-act="football.playclock.plus">Play +1</button>
            <button class="small" data-act="football.playclock.reset40">:40</button>
            <button class="small" data-act="football.playclock.reset25">:25</button>
            <button class="small plus" data-act="basketball.shotclock.plus">Shot +1</button>
            <button class="small" data-act="basketball.shotclock.reset">24</button>
            <button class="small" data-act="timeoutclock.plus1">TO +1</button>
            <button class="small" data-act="timeoutclock.reset">↺ 1:00</button>
          </div>
        </div>

        <div class="card" id="footballCard">
          <div class="title">DOWN &amp; DISTANCE</div>
          <div class="grid" id="footballValues"></div>
          <div class="row">
            <button class="small" data-act="football.down.plus">Next down</button>
            <button class="small" data-act="football.down.previous">Previous down</button>
            <button class="small" data-act="football.series.new">New series</button>
            <button class="small" data-act="football.down.recall">Last down</button>
            <button class="small" id="ballSide" data-act="football.ball.side">◀ VISITORS</button>
          </div>
        </div>

        <div class="card">
          <div class="title">POSSESSION</div>
          <div class="poss">
            <button id="possAway" data-act="possession.away">◀ Visitors</button>
            <button id="possHome" data-act="possession.home">Home ▶</button>
          </div>
        </div>

        <div class="card" id="penaltyCard">
          <div class="title">FLAG</div>
          <div class="row">
            <button class="small" id="penaltyFlag">Flag down</button>
            <button class="small" id="penaltyHome">Home</button>
            <button class="small" id="penaltyAway">Visitors</button>
          </div>
          <select id="penaltyType" title="What the flag was for" style="width:100%;margin-top:6px;padding:8px"></select>
        </div>

        <div class="card">
          <div class="title">SPORT VALUES</div>
          <div class="grid" id="controls"></div>
        </div>

        <div class="card">
          <div class="title">CUSTOM VALUES</div>
          <div class="grid" id="stats"></div>
          <div class="row">
            <input id="statName" placeholder="New stat name" style="flex:1 1 auto;padding:8px" />
            <button class="small plus" data-act="stat.add" style="flex:0 0 82px">Add</button>
          </div>
        </div>

        <div class="card">
          <div class="title">OUTPUT</div>
          <div class="layoutbar">
            <button class="small" data-act="layout.prev" style="flex:0 0 44px">◀</button>
            <span class="label" id="layout">—</span>
            <button class="small" data-act="layout.next" style="flex:0 0 44px">▶</button>
          </div>
          <div class="row">
            <button class="publish" data-act="output.publish" style="flex:2 1 120px">Update Output</button>
            <button id="liveButton" data-act="live.toggle" style="flex:1 1 90px">Live: ON</button>
          </div>
          <div class="row">
            <button class="small" data-act="global.undo">Undo</button>
            <button class="small" data-act="panel.new">New panel</button>
          </div>
        </div>

        <div class="card" id="mediaCard" hidden>
          <div class="title">MEDIA <span class="right" id="mediaNow">—</span></div>
          <div class="warn">Experimental: remote media controls are still being refined. Paused clips and speaker buses are shown separately from playing media; check the desktop before going live.</div>
          <div class="muted" id="speakerNow"></div>
          <div class="row">
            <button class="small" id="mediaToggle">Play / Pause</button>
            <button class="small" id="mediaRefresh">Refresh</button>
          </div>
          <div class="grid" id="mediaList"></div>
        </div>

        <div class="card" id="rosterCard" hidden>
          <div class="title">ROSTER</div>
          <div class="row">
            <button class="small" id="rosterAway" data-rside="away">◀ Visitors</button>
            <button class="small" id="rosterHome" data-rside="home">Home ▶</button>
          </div>
          <div class="muted" id="rosterName">—</div>
          <button class="small" id="rosterOffAir" style="margin:6px 0 8px">TAKE PLAYER OFF AIR</button>
          <div class="grid" id="rosterList"></div>
          <div id="playerStats"></div>
        </div>

        <div id="lockScreen" hidden><div class="box"><h2>SCOREPLUS LOCKED</h2><p>The operator has locked the console, media, rosters and remote. Ask them to unlock ScorePlus on the desktop.</p></div></div>
        <div class="flash" id="flash"></div>
        """;

    static string Shell(string title, string body, bool script)
    {
        var page = """
            <!doctype html>
            <html lang="en"><head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no">
            <meta name="theme-color" content="#0b0f15">
            <title>{{TITLE}} — {{PRODUCT}} Remote</title>
            <style>{{STYLE}}</style>
            </head><body>
            {{BODY}}
            {{SCRIPT}}
            </body></html>
            """;
        page = page.Replace("{{TITLE}}", title)
                   .Replace("{{PRODUCT}}", ProductName)
                   .Replace("{{STYLE}}", Style)
                   .Replace("{{BODY}}", body);
        page = page.Replace("{{SCRIPT}}", script ? "<script>\n" + Script + "\n</script>" : string.Empty);
        return page;
    }

    public static string Login(string product, string message)
    {
        var body = """
            <div class="login">
              <img class="brand" src="/logo.png" alt="ScorePlus"/>
              <h2>{{PRODUCT}} · REMOTE</h2>
              <p>{{MESSAGE}}</p>
              <form method="post" action="/login">
                <input type="password" name="password" autocomplete="current-password" autofocus placeholder="Remote password">
                <button type="submit">Sign in</button>
              </form>
              <p style="margin:14px 0 0">Turn the remote on and choose its password in ScorePlus → Preferences.</p>
            </div>
            """;
        body = body.Replace("{{PRODUCT}}", product).Replace("{{MESSAGE}}", System.Net.WebUtility.HtmlEncode(message));
        return Shell("Sign in", body, script: false);
    }

    public static string Panel()
    {
        var page = Shell("Remote", Body, script: true);
        return page.Replace("</body>", Tabs + "</body>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The console tab keeps the live scoreboard and clock cards; Media and Roster are their own tabs
    /// with their own lists, so the phone offers the whole desk instead of only the score.
    /// </summary>
    const string Tabs = """
        <style>
        .tabs{position:sticky;top:0;z-index:5;display:flex;gap:6px;padding:8px 12px;background:#0b0f15;border-bottom:1px solid #223}
        .tab{flex:1 1 0;padding:10px 6px;border-radius:8px;border:1px solid #2a3546;background:#131a24;color:#9fb0c6;font:inherit;font-size:14px}
        .tab.active{background:#16382f;border-color:#38d7a3;color:#eaf7f2}
        [hidden]{display:none!important}
        .mediaRow{display:flex;align-items:center;gap:8px;padding:6px;border-bottom:1px solid #1d2632}
        /* A long clip or player name gets the room and ends in an ellipsis; the Air button keeps its
           own width instead of squeezing the name off the row. */
        .mediaRow button{flex:1 1 auto;text-align:left;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .mediaRow button:last-child{flex:0 0 auto}
        .mediaRow button.editing{color:#ffd766;border-color:#ffd766;font-weight:800}
        </style>
        <script>
        (function(){
          var tabs=[].slice.call(document.querySelectorAll('.tab'));
          var cards=[].slice.call(document.querySelectorAll('.card'));
          var consoleCards=cards.filter(function(c){return c.id!=='mediaCard'&&c.id!=='rosterCard';});
          var side='home';
          var editPlayer='';
          function el(tag,cls,text){var n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=text;return n;}
          function post(url,body){
            return fetch(url,{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},credentials:'same-origin',body:new URLSearchParams(body)});
          }
          function getJson(url){return fetch(url,{cache:'no-store',credentials:'same-origin'}).then(function(r){return r.ok?r.json():null;}).catch(function(){return null;});}
          function show(name){
            tabs.forEach(function(t){t.classList.toggle('active',t.dataset.tab===name);});
            consoleCards.forEach(function(c){c.hidden=name!=='console';});
            document.getElementById('mediaCard').hidden=name!=='media';
            document.getElementById('rosterCard').hidden=name!=='roster';
            if(name==='media')loadMedia();
            if(name==='roster')loadRoster();
          }
          function loadMedia(){
            getJson('/api/media').then(function(d){
              if(!d)return;
              var live=(d.live||[]).filter(function(x){return x;});
              var paused=(d.paused||[]).filter(function(x){return x;});
              var speakers=(d.speakers||[]).filter(function(x){return x;});
              var status=live.length?live.join(' + '):(paused.length?'PAUSED · '+paused.join(' + '):'nothing playing');
              document.getElementById('mediaNow').textContent=status;
              document.getElementById('speakerNow').textContent=speakers.length?'Speaker bus on air: '+speakers.join(' + '):'';
              document.getElementById('mediaToggle').textContent=paused.length?'Resume':'Pause';
              var list=document.getElementById('mediaList');
              list.innerHTML='';
              (d.clips||[]).slice(0,80).forEach(function(c){
                var row=el('div','mediaRow');
                var play=el('button','small',(c.kind?c.kind+' · ':'')+c.label+(c.length?'  ·  '+c.length.toFixed(1)+'s':''));
                play.onclick=function(){post('/api/media/action',{a:'play',id:c.id}).then(loadMedia);};
                var onair=el('button','small'+(c.onAir?' plus':''),c.onAir?'ON AIR':'Air');
                onair.onclick=function(){post('/api/media/action',{a:'onair',id:c.id}).then(loadMedia);};
                row.appendChild(play);row.appendChild(onair);
                list.appendChild(row);
              });
              if(!(d.clips||[]).length)list.appendChild(el('div','muted','No clips in the library yet.'));
            });
          }
          function loadRoster(){
            getJson('/api/rosters').then(function(d){
              if(!d)return;
              var sideData=(side==='home'?d.home:d.away)||{};
              document.getElementById('rosterName').textContent=(sideData.name||'No roster')+(sideData.rosterName?'  ·  '+sideData.rosterName:'');
              var list=document.getElementById('rosterList');
              list.innerHTML='';
              (d.rosters||[]).forEach(function(r){
                var b=el('button','small'+(r.id===sideData.rosterId?' plus':''),r.name);
                b.onclick=function(){post('/api/roster/action',{a:'roster',side:side,id:r.id}).then(loadRoster);};
                list.appendChild(b);
              });
              var players=(sideData.players||[]).slice().sort(function(a,b){
                return String(a.group||'').localeCompare(String(b.group||''));
              });
              // The player whose stats are on screen is lit up from the first paint, not only after
              // a row is tapped; with nothing chosen the on-air player is the one being edited.
              if(!editPlayer||!players.some(function(p){return p.id===editPlayer;})){
                var initial=players.find(function(p){return p.selected;})||players[0];
                editPlayer=initial?initial.id:'';
              }
              var lastGroup=null;
              players.forEach(function(p){
                var group=(p.group||'').trim();
                if(group!==lastGroup){list.appendChild(el('div','groupHead',group?group.toUpperCase():'UNGROUPED'));lastGroup=group;}
                var row=el('div','mediaRow');
                var b=el('button','small'+(p.id===editPlayer?' editing':''),(p.number?p.number+'  ':'')+p.name);
                b.onclick=function(){editPlayer=p.id;loadRoster();};
                var air=el('button','small'+(p.selected?' plus':''),p.selected?'ON AIR':'Air');
                air.title=p.selected?'Take this player off air':'Put this player on air';
                air.onclick=function(){post('/api/roster/action',p.selected?{a:'clear',side:side,id:p.id}:{a:'player',side:side,id:p.id}).then(loadRoster);};
                row.appendChild(b);row.appendChild(air);list.appendChild(row);
              });
              if(!(sideData.players||[]).length)list.appendChild(el('div','muted','Pick a roster to list its players.'));
              var editing=(sideData.players||[]).find(function(p){return p.id===editPlayer;})
                ||(sideData.players||[]).find(function(p){return p.selected;})
                ||(sideData.players||[])[0];
              if(editing)editPlayer=editing.id;
              renderPlayerStats(side,editing);
            });
          }
          function renderPlayerStats(side,player){
            var host=document.getElementById('playerStats');
            if(!host)return;
            host.innerHTML='';
            if(!player){host.appendChild(el('div','muted','Pick a player to edit their stats.'));return;}
            host.appendChild(el('div','muted','EDIT STATS — '+((player.number?player.number+'  ':'')+player.name)));
            (player.stats||[]).forEach(function(s){
              var row=el('div','statEdit');
              row.appendChild(el('span','l',s.name));
              var minus=el('button','small minus','−');
              var input=el('input');input.type='number';input.inputMode='numeric';input.value=s.value;
              var plus=el('button','small plus','+');
              function save(value){
                post('/api/roster/action',{a:'stat',side:side,id:player.id,stat:s.name,value:value}).then(loadRoster);
              }
              minus.onclick=function(){save(Math.max(0,(parseInt(s.value,10)||0)-1));};
              plus.onclick=function(){save((parseInt(s.value,10)||0)+1);};
              input.onchange=function(){var value=parseInt(input.value,10);if(!isNaN(value))save(value);};
              row.appendChild(minus);row.appendChild(input);row.appendChild(plus);
              host.appendChild(row);
            });
            if(!(player.stats||[]).length)host.appendChild(el('div','muted','This player has no stat columns yet.'));
          }
          tabs.forEach(function(t){t.addEventListener('click',function(){show(t.dataset.tab);});});
          document.getElementById('mediaToggle').onclick=function(){post('/api/media/action',{a:'toggle'}).then(loadMedia);};
          document.getElementById('mediaRefresh').onclick=loadMedia;
          document.getElementById('rosterHome').onclick=function(){side='home';loadRoster();};
          document.getElementById('rosterAway').onclick=function(){side='away';loadRoster();};
          document.getElementById('rosterOffAir').onclick=function(){post('/api/roster/action',{a:'clear',side:side,id:''}).then(loadRoster);};
          show('console');
        })();
        </script>
        """;
}
