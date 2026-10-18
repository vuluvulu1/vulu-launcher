// Kitaplık araçları: kurulu vulu pack güncellemeleri, oynama süresi, ekran görüntüsü galerisi
// ve Windows bildirimlerine tıklanınca ilgili yerin açılması.

// ── vulu pack güncellemeleri ──────────────────────────────────────────
/** state.packUpdates: instanceId → { packId, name, from, to } */
function onPackUpdates(result) {
    state.packUpdates = Object.fromEntries((result.updates || []).map(u => [u.instanceId, u]));
    const count = Object.keys(state.packUpdates).length;
    const badge = $('packs-badge');
    badge.textContent = String(count);
    badge.hidden = !count;
    renderInstanceLists();
    if (state.selected) renderPackActions(state.selected);
}

async function refreshPackUpdates() {
    try { onPackUpdates(await bridge.invoke('packs.updates')); } catch { }
}

/** Seçili instance'ın paketi Keşfet'te güncellendiyse Oyna sayfasından da güncellenebilir. */
async function updateInstancePack(instance) {
    const u = (state.packUpdates || {})[instance.id];
    if (!u) return;
    await updatePack({ id: u.packId, name: u.name, revision: u.to });
    refreshPackUpdates();
}

// ── Oynama süresi ─────────────────────────────────────────────────────
function formatPlayTime(seconds) {
    if (!(seconds >= 60)) return seconds > 0 ? '1 dk\'dan az' : 'Hiç';
    const minutes = Math.floor(seconds / 60), hours = Math.floor(minutes / 60);
    if (hours === 0) return minutes + ' dk';
    if (hours < 10) return `${hours} sa ${minutes % 60} dk`;
    return hours.toLocaleString('tr-TR') + ' sa';
}

// ── Ekran görüntüleri ─────────────────────────────────────────────────
/** Galeri. { pick: true } ile açılırsa tıklanan görüntünün adını döndürür (paylaşmak için seçim). */
async function openScreenshots(instance, { pick = false } = {}) {
    let data;
    try { data = await bridge.invoke('screenshots.list', { id: instance.id }); }
    catch (err) { await alertDialog({ title: 'Ekran görüntüleri', message: err.message }); return; }

    return openModal(close => {
        const grid = el('div', { class: 'shot-grid' });
        const summary = el('span', { class: 'hint shot-summary' });
        const render = () => {
            summary.textContent = data.total
                ? `${data.total} ekran görüntüsü · ${formatSize(data.totalMb)}` + (data.total > data.shots.length ? ` · en yeni ${data.shots.length} tanesi gösteriliyor` : '')
                : '';
            grid.replaceChildren(...(data.shots.length ? data.shots.map((shot, index) =>
                el('button', { class: 'shot', title: formatDate(new Date(shot.takenAt)), onclick: () => (pick ? close(shot.name) : viewShot(index)) },
                    el('img', { src: shot.url, alt: shot.name, loading: 'lazy', decoding: 'async' })))
                : [el('div', { class: 'dm-empty' },
                    el('p', {}, 'Henüz ekran görüntüsü yok.'),
                    el('p', { class: 'hint' }, 'Oyunda F2 tuşuna basınca alınan görüntüler burada görünür.'))]));
        };

        async function viewShot(index) {
            let onKey;
            const result = await openModal(closeView => {
                const img = el('img', { class: 'shot-full', alt: '' });
                const caption = el('span', { class: 'shot-caption' });
                const status = el('span', { class: 'hint shot-status', 'aria-live': 'polite' });
                const show = i => {
                    index = (i + data.shots.length) % data.shots.length;
                    const shot = data.shots[index];
                    img.src = shot.url;
                    img.alt = shot.name;
                    const at = new Date(shot.takenAt);
                    caption.textContent = `${formatDate(at)} ${at.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })} · ${formatSize(shot.sizeKb / 1024)} · ${index + 1}/${data.shots.length}`;
                    status.textContent = '';
                };
                const act = async (command, done) => {
                    try { await bridge.invoke(command, { id: instance.id, name: data.shots[index].name }); if (done) status.textContent = done; }
                    catch (err) { status.textContent = err.message; }
                };
                onKey = e => {
                    if (e.key === 'ArrowLeft') show(index - 1);
                    else if (e.key === 'ArrowRight') show(index + 1);
                };
                document.addEventListener('keydown', onKey);
                const done = value => closeView(value);
                show(index);
                return [
                    el('div', { class: 'shot-view' },
                        el('button', { class: 'shot-nav prev', title: 'Önceki (←)', onclick: () => show(index - 1), disabled: data.shots.length < 2 }, '‹'),
                        img,
                        el('button', { class: 'shot-nav next', title: 'Sonraki (→)', onclick: () => show(index + 1), disabled: data.shots.length < 2 }, '›')),
                    el('div', { class: 'modal-foot shot-foot' },
                        caption, status,
                        el('button', { class: 'btn btn-sm btn-primary', title: 'Sosyal\'de bütün vulu üyeleriyle paylaş', onclick: () => done({ share: index }) }, 'Paylaş'),
                        el('button', { class: 'btn btn-sm', onclick: () => act('screenshots.copy', 'Kopyalandı ✓ Discord\'a ya da bir klasöre yapıştırabilirsin.') }, 'Kopyala'),
                        el('button', { class: 'btn btn-sm', onclick: () => act('screenshots.open') }, 'Aç'),
                        el('button', { class: 'btn btn-sm', onclick: () => act('screenshots.reveal') }, 'Klasörde göster'),
                        el('button', { class: 'btn btn-sm btn-danger', onclick: () => done({ remove: index }) }, 'Sil'),
                        el('button', { class: 'btn btn-sm', onclick: () => done(null) }, 'Kapat')),
                ];
            }, { wide: true });
            document.removeEventListener('keydown', onKey);   // Esc ya da arka plan tıklamasıyla kapansa da
            if (result && result.remove != null) await removeShot(result.remove);
            if (result && result.share != null && await shareScreenshot(instance, data.shots[result.share].name)) close();
        }

        async function removeShot(index) {
            const shot = data.shots[index];
            if (!await confirmDialog({ title: 'Ekran görüntüsünü sil', message: `${shot.name} geri dönüşüm kutusuna taşınsın mı?`, okText: 'Sil', cancelText: 'Vazgeç', danger: true })) return;
            try {
                await bridge.invoke('screenshots.delete', { id: instance.id, name: shot.name });
                data.shots.splice(index, 1);
                data.total--;
                data.totalMb = Math.max(0, data.totalMb - shot.sizeKb / 1024);
                render();
            } catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
        }

        render();
        return [
            el('div', { class: 'modal-head' }, pick ? 'Paylaşılacak ekran görüntüsünü seç' : `${instance.name} · Ekran görüntüleri`),
            el('div', { class: 'modal-body shot-body' }, grid),
            el('div', { class: 'modal-foot' },
                summary,
                el('button', { class: 'btn', onclick: () => bridge.invoke('files.open', { target: 'screenshots', id: instance.id }).catch(() => {}) }, 'Klasörü aç'),
                el('button', { class: 'btn btn-primary', onclick: () => close() }, pick ? 'Vazgeç' : 'Kapat')),
        ];
    }, { wide: true }).then(v => (typeof v === 'string' ? v : null));
}

// ── Masaüstü kısayolları ──────────────────────────────────────────────
/** Paketin logosundan 256 px PNG simge; logo yoksa null (kısayol launcher simgesini kullanır). */
async function shortcutIcon(instanceId) {
    if (!instanceId) return null;
    let source;
    try { source = await bridge.invoke('shortcuts.icon', { id: instanceId }); } catch { return null; }
    if (!source) return null;
    try {
        const img = await loadImage(source.dataUrl);
        const N = 256, c = el('canvas', { width: N, height: N }), g = c.getContext('2d');
        const ratio = img.naturalWidth / img.naturalHeight;
        g.beginPath(); g.roundRect(0, 0, N, N, 44); g.clip();
        if (ratio > 0.8 && ratio < 1.25) {
            // Kareye yakın logo: simgenin tamamını kaplar
            const s = Math.max(N / img.naturalWidth, N / img.naturalHeight);
            g.drawImage(img, (N - img.naturalWidth * s) / 2, (N - img.naturalHeight * s) / 2, img.naturalWidth * s, img.naturalHeight * s);
        } else {
            // Geniş/uzun logo: koyu bir kutunun ortasına sığdırılır
            g.fillStyle = '#121010'; g.fillRect(0, 0, N, N);
            const pad = 22, s = Math.min((N - pad * 2) / img.naturalWidth, (N - pad * 2) / img.naturalHeight);
            g.drawImage(img, (N - img.naturalWidth * s) / 2, (N - img.naturalHeight * s) / 2, img.naturalWidth * s, img.naturalHeight * s);
        }
        return c.toDataURL('image/png');
    } catch { return null; }
}

async function createShortcut(kind, id, name, iconInstanceId) {
    try {
        const iconDataUrl = await shortcutIcon(iconInstanceId);
        await bridge.invoke('shortcuts.create', { kind, id, name, iconDataUrl: iconDataUrl || '' });
        await alertDialog({
            title: 'Kısayol eklendi',
            message: kind === 'play'
                ? `Masaüstündeki "${name}" kısayolu launcher'ı açıp ${name} paketini doğrudan başlatır.`
                : `Masaüstündeki "${name}" kısayolu launcher'ı açıp doğrudan ${name} sunucusuna bağlanır.`,
        });
    } catch (err) { await alertDialog({ title: 'Kısayol oluşturulamadı', message: err.message }); }
}

/** Kısayoldan (ya da açıkken başka bir kısayoldan) gelen istek: paketi oyna / sunucuya bağlan. */
async function runStartupAction() {
    if (!state.username) return;   // giriş yapılınca enterLauncher yeniden çağırır
    let action;
    try { action = await bridge.invoke('app.takeAction'); } catch { return; }
    if (!action) return;
    if (state.gameRunning) { appendLog({ text: 'Oyun zaten açık.', level: 'warn' }); return; }
    if (action.kind === 'play') {
        await reloadInstances(action.id);
        const instance = state.instances.find(i => i.id === action.id);
        if (!instance) { await alertDialog({ title: 'Kısayol', message: 'Bu kısayolun paketi artık yok. Kısayolu silebilirsin.' }); return; }
        showPage('home');
        play();
    } else if (action.kind === 'join') {
        let servers = [];
        try { servers = await bridge.invoke('community.servers'); } catch { }
        const server = servers.find(s => s.id === action.id);
        if (!server) { await alertDialog({ title: 'Kısayol', message: 'Bu kısayolun sunucusu artık vulu listesinde yok.' }); return; }
        joinServer(server);
    }
}

// ── Bildirime tıklama ─────────────────────────────────────────────────
async function onNotificationClicked({ tag }) {
    if (!tag || !state.username) return;
    if (tag.startsWith('dm:')) {
        showPage('messages');
        openMessagesPage(tag.slice(3));
    } else if (tag.startsWith('shot:')) {
        openSharedShot(tag.slice(5));
    } else if (tag.startsWith('pack:')) {
        const instance = state.instances.find(i => i.id === tag.slice(5));
        if (!instance) return;
        selectInstance(instance);
        showPage('home');
        await refreshPackUpdates();
        updateInstancePack(instance);
    }
}

function setupLibrary() {
    bridge.on('packUpdates', onPackUpdates);
    bridge.on('notificationClicked', onNotificationClicked);
    bridge.on('startupAction', runStartupAction);
    bridge.on('shotEvent', e => appendLog({ text: e.kind === 'like' ? `♥ ${e.from} ekran görüntünü beğendi.` : `💬 ${e.from}: ${e.text}`, level: 'accent' }));
}
