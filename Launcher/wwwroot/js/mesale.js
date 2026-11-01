// Meşale teması: masaüstünde oyun çubuğu her sayfada görünür. Yeni düğme yazılmaz; Oyna sayfasındaki alt çubuk ve radyo
// tema seçiliyken aynı öğeler olarak #m-bar'a taşınır, tema değişince yerlerine döner (id'ler ve dinleyiciler korunur).
const mesale = { bar: null, heads: null, membersLoaded: false };

/** Telefon yerleşimi (css/mobile.css) yalnızca renkleri alır; çubuk 900 px'ten geniş pencerelerde kurulur. */
function mesaleLayoutActive() {
    return document.documentElement.dataset.ui === 'mesale' && innerWidth > 900;
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

addEventListener('resize', applyMesaleLayout);
applyMesaleLayout();
