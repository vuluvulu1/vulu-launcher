// Oyun çubuğu: masaüstünde her temada, her sayfada görünür. Yeni düğme yazılmaz; Oyna sayfasındaki alt çubuk ve radyo
// aynı öğeler olarak #m-bar'a taşınır, dar pencerede yerlerine döner (id'ler ve dinleyiciler korunur). Görünümü temanın
// kendi CSS'inde (css/mesale.css, css/gamebar.css). Dosyanın sonundaki yanarak açılış yalnızca Meşale'nindir.
const mesale = { bar: null, heads: null, membersLoaded: false };

/** Telefon yerleşiminde (css/mobile.css) çubuk yoktur; 900 px'ten geniş pencerelerde kurulur. */
function mesaleLayoutActive() {
    return innerWidth > 900;
}

function buildMesaleBar() {
    if (mesale.bar) return mesale.bar;
    mesale.bar = el('footer', { id: 'm-bar', 'aria-label': 'Oyun çubuğu' },
        el('div', { class: 'm-current' },
            el('div', { id: 'm-cover', class: 'm-cover', 'aria-hidden': 'true' }),
            el('div', { class: 'm-current-text' },
                el('b', { id: 'm-name' }))),
        el('button', { id: 'm-join', class: 'btn btn-play m-join', type: 'button', hidden: true, onclick: mesaleJoin }, 'Sunucuya bağlan'));
    mesale.heads = el('div', { id: 'm-heads', class: 'm-heads' });
    return mesale.bar;
}

function mesaleJoin() {
    if (state.packServer) play({ join: state.packServer });
}

/** Oyun çubuğunu kurar ya da söker. Yalnızca öğeleri taşır, durum bilgisi syncMesaleBar ile dolar. */
function applyMesaleLayout() {
    const main = $('view-main'), dock = document.querySelector('.dock'), music = document.querySelector('.music');
    if (!main || !dock || !music) return;
    if (mesaleLayoutActive()) {
        const bar = buildMesaleBar();
        if (bar.parentElement !== main) { main.append(bar); main.querySelector('.hero-row').append(mesale.heads); }
        const join = $('m-join');
        if (dock.parentElement !== bar) { bar.insertBefore(music, join); bar.insertBefore(dock, join); }
    } else {
        if (dock.parentElement !== $('page-home')) $('page-home').insertBefore(dock, $('console'));
        if (music.parentElement !== main.querySelector('.rail')) main.querySelector('.rail').append(music);
        if (mesale.bar) { mesale.bar.remove(); mesale.heads.remove(); }
    }
}

/** Çubuktaki paket adı, resmi, durum ve oyundaki kişiler; sayfa arka planı da seçili paketten gelir. */
function syncMesaleBar() {
    syncHeroInset();
    if (!mesaleLayoutActive() || !mesale.bar) return;
    const instance = state.selected, cover = $('m-cover');
    const picture = instance && (instance.thumbnail || instance.iconUrl || instance.background);
    cover.style.backgroundImage = picture ? `url("${picture}")` : '';
    cover.textContent = instance && !picture ? instance.name.slice(0, 1).toLocaleUpperCase('tr') : '';
    $('m-name').textContent = instance ? instance.name : 'Paket seçilmedi';
    const art = instance && instance.background;
    // Değişkendeki göreli adres CSS dosyasına göre çözülür; tam adres gerekir
    $('view-main').style.setProperty('--m-art', art ? `url("${new URL(art, location.href).href}")` : 'none');

    const here = instance ? playingHere(instance) : [];
    mesale.heads.title = here.map(p => p.username).join(', ');
    mesale.heads.replaceChildren(...here.slice(0, 3).map(p => avatarEl({ username: p.username, avatarVersion: memberAvatarVersion(p.username) }, 'avatar-md')));
    if (here.length) mesale.heads.append(el('span', { class: 'm-heads-text' }, `${here.length} kişi burada oynuyor`));
    // Çevrimiçi listesinde resim bilgisi yok; üye listesi gelince fotoğraflar çizilir
    if (here.length && !mesale.membersLoaded) {
        mesale.membersLoaded = true;
        ensureMembers().then(() => { mesale.membersLoaded = profile.members.length > 0; syncMesaleBar(); });
    }

    const idle = !launching && state.busy === 0 && !state.gameRunning && state.canLaunchGame;
    const canJoin = !!(instance && state.packServer && idle);
    $('m-join').hidden = !canJoin;
    mesale.bar.classList.toggle('has-join', canJoin);
}

/** Meşale'de kurulum rafı paket bilgisine ulaşmıyorsa bilgi sola yaslanır; raf uzayınca ya da pencere alçalınca geri çekilir. */
function syncHeroInset() {
    const page = $('page-home'), hero = page && page.querySelector('.stage-hero'), last = document.querySelector('.rail .new-instance');
    if (!hero || !last) return;
    const free = document.documentElement.dataset.ui === 'mesale' && mesaleLayoutActive() && hero.offsetHeight > 0
        && last.getBoundingClientRect().bottom + 14 < hero.getBoundingClientRect().top;
    page.classList.toggle('m-hero-left', free);
}

addEventListener('resize', applyMesaleLayout);
applyMesaleLayout();
if ('ResizeObserver' in window) {
    const watcher = new ResizeObserver(syncHeroInset);
    for (const part of document.querySelectorAll('.stage-hero, .rail-scroll, #instance-list')) watcher.observe(part);
}

// ── Açılış: yanma ─────────────────────────────────────────────────────
// Karanlık bir WebGL katmanıdır: gürültülü bir yanma cephesi yazıdan dışa yürür, geçtiği yer saydamlaşır ve launcher görünür.
// Kıvılcımlar ayrı bir 2B katmanda. Süre, css/mesale.css'teki perde geçişiyle aynı.
const BURN_MS = 1500;
const BURN_SHADER = `precision mediump float;
uniform vec2 size, word; uniform float front, time;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p), u = f * f * (3. - 2. * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) { return noise(p) * .5 + noise(p * 2.1 + 3.) * .27 + noise(p * 4.3 + 7.) * .15 + noise(p * 9.1 + 1.) * .08; }
void main() {
    vec2 p = (gl_FragCoord.xy - word) / size.y;
    float e = length(p * vec2(.55, 1.25)) + (fbm(p * 3.4 + vec2(0., -time * .25)) - .5) * .42 - front;
    vec3 hot = mix(vec3(1., .93, .68), vec3(1., .46, .1), smoothstep(.0, .022, e));
    hot = mix(hot, vec3(.34, .08, .02), smoothstep(.022, .07, e));
    vec3 sheet = mix(hot, vec3(.031, .027, .024), smoothstep(.05, .17, e));
    float a = smoothstep(-.004, .004, e);
    // Yanmış tarafta cepheye yakın sıcak ışık: saydamlığı değiştirmeden launcher'ın üstüne eklenir
    gl_FragColor = vec4(sheet * a + vec3(1., .42, .08) * .34 * exp(min(e, 0.) * 24.) * (1. - a), a);
}`;
const burn = { sheet: null, sparks: null, gl: null, ctx: null, uniforms: null, raf: 0, startedAt: 0, openedAt: 0, list: [], word: null };

/** Yanma katmanını kurar; WebGL yoksa null döner ve açılış karanlığın çekilmesine düşer. */
function createBurnSheet() {
    const canvas = el('canvas', { class: 'm-sheet', 'aria-hidden': 'true' });
    const gl = canvas.getContext('webgl', { alpha: true, premultipliedAlpha: true, antialias: false });
    if (!gl) return null;
    const program = gl.createProgram();
    const add = (type, source) => {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        gl.attachShader(program, shader);
    };
    add(gl.VERTEX_SHADER, 'attribute vec2 pos; void main() { gl_Position = vec4(pos, 0., 1.); }');
    add(gl.FRAGMENT_SHADER, BURN_SHADER);
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return null;
    gl.useProgram(program);
    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.bindAttribLocation(program, 0, 'pos');
    gl.linkProgram(program);
    gl.useProgram(program);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    gl.clearColor(.031, .027, .024, 1);
    burn.gl = gl;
    burn.uniforms = Object.fromEntries(['size', 'word', 'front', 'time'].map(n => [n, gl.getUniformLocation(program, n)]));
    return canvas;
}

function addSpark(x, y, spread, lift) {
    if (burn.list.length > 280) return;
    const life = 500 + Math.random() * 900;
    burn.list.push({ x, y, vx: (Math.random() - .5) * spread, vy: -lift * (.4 + Math.random()), life, left: life, size: .7 + Math.random() * 1.3 });
}

/** Cephenin yarıçapı (pencere yüksekliği cinsinden): yavaş tutuşur, hızlanarak yayılır. */
function burnFront(progress) {
    const end = Math.hypot(innerWidth / innerHeight * .42, .85) + .4;
    return -.22 + Math.pow(progress, 1.7) * (end + .22);
}

function burnFrame(now, last) {
    const splash = $('view-splash');
    if (splash.hidden || !splash.classList.contains('play')) { stopMesaleSplash(); return; }
    const dt = Math.min(50, now - last), { word, ctx } = burn;
    const progress = burn.openedAt ? Math.min(1, (now - burn.openedAt) / BURN_MS) : 0;
    const front = burnFront(progress);

    if (burn.gl) {
        const gl = burn.gl, u = burn.uniforms, c = burn.sheet;
        if (progress === 0) gl.clear(gl.COLOR_BUFFER_BIT);
        else {
            const scale = c.width / innerWidth;
            gl.uniform2f(u.size, c.width, c.height);
            gl.uniform2f(u.word, word.x * scale, c.height - word.y * scale);
            gl.uniform1f(u.front, front);
            gl.uniform1f(u.time, (now - burn.startedAt) / 1000);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
        }
    }

    // Kıvılcımlar: önce yazıda yürüyen alevden, sonra yanma cephesinden çıkar
    const age = (now - burn.startedAt) / 1000;
    if (!burn.openedAt && age > .25 && age < 1.5) {
        const x = word.left + word.width * Math.min(1, (age - .25) / 1.05);
        for (let i = 0; i < 2; i++) addSpark(x, word.y + (Math.random() - .5) * word.height * .6, .1, .16);
    } else if (!burn.openedAt && Math.random() < .12) addSpark(word.left + Math.random() * word.width, word.y, .06, .07);
    if (burn.openedAt && progress < .9 && front > 0) {
        for (let i = 0; i < 7; i++) {
            const a = Math.random() * Math.PI * 2;
            addSpark(word.x + Math.cos(a) * front / .55 * innerHeight, word.y + Math.sin(a) * front / 1.25 * innerHeight, .22, .3);
        }
    }
    ctx.clearRect(0, 0, innerWidth, innerHeight);
    ctx.globalCompositeOperation = 'lighter';
    burn.list = burn.list.filter(s => (s.left -= dt) > 0);
    for (const s of burn.list) {
        s.x += (s.vx + Math.sin(s.left / 90) * .025) * dt;
        s.y += s.vy * dt;
        const heat = s.left / s.life;
        ctx.fillStyle = `rgba(255, ${Math.round(90 + heat * 130)}, ${Math.round(heat * 70)}, ${Math.min(1, heat * 1.6)})`;
        ctx.beginPath();
        ctx.arc(s.x, s.y, s.size, 0, Math.PI * 2);
        ctx.fill();
    }
    burn.raf = requestAnimationFrame(next => burnFrame(next, now));
}

/** beginSplash çağırır: Meşale'de karanlığı ve kıvılcımları kurar. */
function startMesaleSplash() {
    stopMesaleSplash();
    if (document.documentElement.dataset.ui !== 'mesale') return;
    const splash = $('view-splash');
    const rect = splash.querySelector('.stage-name').getBoundingClientRect();
    burn.word = { left: rect.left, width: rect.width, height: rect.height, x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
    burn.startedAt = performance.now();
    burn.sheet = createBurnSheet();
    burn.sparks = el('canvas', { class: 'm-sparks', 'aria-hidden': 'true' });
    burn.sparks.width = innerWidth;
    burn.sparks.height = innerHeight;
    burn.ctx = burn.sparks.getContext('2d');
    if (burn.sheet) {
        const scale = Math.min(1, 1280 / innerWidth); // gürültü yumuşak; tam çözünürlük boşa GPU harcar
        burn.sheet.width = Math.round(innerWidth * scale);
        burn.sheet.height = Math.round(innerHeight * scale);
        burn.gl.viewport(0, 0, burn.sheet.width, burn.sheet.height);
        splash.prepend(burn.sheet);
        splash.classList.add('m-burn');
    }
    splash.append(burn.sparks);
    burn.raf = requestAnimationFrame(now => burnFrame(now, now));
}

/** openCurtainsTo çağırır: yanma başlar. */
function openMesaleSplash() {
    if (burn.sparks) burn.openedAt = performance.now();
}

function stopMesaleSplash() {
    cancelAnimationFrame(burn.raf);
    burn.gl?.getExtension('WEBGL_lose_context')?.loseContext();
    burn.sheet?.remove();
    burn.sparks?.remove();
    Object.assign(burn, { sheet: null, sparks: null, gl: null, ctx: null, openedAt: 0, list: [] });
    $('view-splash').classList.remove('m-burn');
}
