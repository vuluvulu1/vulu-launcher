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
async function openScreenshots(instance) {
    let data;
    try { data = await bridge.invoke('screenshots.list', { id: instance.id }); }
    catch (err) { await alertDialog({ title: 'Ekran görüntüleri', message: err.message }); return; }

    await openModal(close => {
        const grid = el('div', { class: 'shot-grid' });
        const summary = el('span', { class: 'hint shot-summary' });
        const render = () => {
            summary.textContent = data.total
                ? `${data.total} ekran görüntüsü · ${formatSize(data.totalMb)}` + (data.total > data.shots.length ? ` · en yeni ${data.shots.length} tanesi gösteriliyor` : '')
                : '';
            grid.replaceChildren(...(data.shots.length ? data.shots.map((shot, index) =>
                el('button', { class: 'shot', title: formatDate(new Date(shot.takenAt)), onclick: () => viewShot(index) },
                    el('img', { src: shot.url, alt: shot.name, loading: 'lazy', decoding: 'async' })))
                : [el('div', { class: 'dm-empty' },
                    el('p', {}, 'Henüz ekran görüntüsü yok.'),
                    el('p', { class: 'hint' }, 'Oyunda F2 tuşuna basınca alınan görüntüler burada görünür.'))]));
        };

        async function viewShot(index) {
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
                const onKey = e => {
                    if (e.key === 'ArrowLeft') show(index - 1);
                    else if (e.key === 'ArrowRight') show(index + 1);
                };
                document.addEventListener('keydown', onKey);
                const done = value => { document.removeEventListener('keydown', onKey); closeView(value); };
                show(index);
                return [
                    el('div', { class: 'shot-view' },
                        el('button', { class: 'shot-nav prev', title: 'Önceki (←)', onclick: () => show(index - 1), disabled: data.shots.length < 2 }, '‹'),
                        img,
                        el('button', { class: 'shot-nav next', title: 'Sonraki (→)', onclick: () => show(index + 1), disabled: data.shots.length < 2 }, '›')),
                    el('div', { class: 'modal-foot shot-foot' },
                        caption, status,
                        el('button', { class: 'btn btn-sm', onclick: () => act('screenshots.copy', 'Kopyalandı ✓ Discord\'a ya da bir klasöre yapıştırabilirsin.') }, 'Kopyala'),
                        el('button', { class: 'btn btn-sm', onclick: () => act('screenshots.open') }, 'Aç'),
                        el('button', { class: 'btn btn-sm', onclick: () => act('screenshots.reveal') }, 'Klasörde göster'),
                        el('button', { class: 'btn btn-sm btn-danger', onclick: () => done({ remove: index }) }, 'Sil'),
                        el('button', { class: 'btn btn-sm', onclick: () => done(null) }, 'Kapat')),
                ];
            }, { wide: true });
            if (result && result.remove != null) await removeShot(result.remove);
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
            el('div', { class: 'modal-head' }, `${instance.name} · Ekran görüntüleri`),
            el('div', { class: 'modal-body shot-body' }, grid),
            el('div', { class: 'modal-foot' },
                summary,
                el('button', { class: 'btn', onclick: () => bridge.invoke('files.open', { target: 'screenshots', id: instance.id }).catch(() => {}) }, 'Klasörü aç'),
                el('button', { class: 'btn btn-primary', onclick: () => close() }, 'Kapat')),
        ];
    }, { wide: true });
}

// ── Bildirime tıklama ─────────────────────────────────────────────────
async function onNotificationClicked({ tag }) {
    if (!tag || !state.username) return;
    if (tag.startsWith('dm:')) {
        showPage('messages');
        openMessagesPage(tag.slice(3));
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
}
