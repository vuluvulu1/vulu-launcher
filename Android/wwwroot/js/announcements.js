// Duyurular (ana ekrandaki şerit, admin'in duyuru sekmesi) ve admin'in veritabanı alanı göstergesi.

const ANNOUNCEMENT_REFRESH_MS = 10 * 60 * 1000;
let announcementTimer = 0;

async function refreshAnnouncement() {
    clearTimeout(announcementTimer);
    announcementTimer = setTimeout(refreshAnnouncement, ANNOUNCEMENT_REFRESH_MS);
    let a = null;
    try { ({ announcement: a } = await bridge.invoke('announcement.get')); } catch { return; }
    renderAnnouncement(a);
}

function renderAnnouncement(a) {
    const box = $('announcement');
    // Kapatılan duyuru, yenisi gelene kadar bir daha gösterilmez
    if (!a || store.get('announcementDismissed', '') === a.id) { box.hidden = true; box.replaceChildren(); return; }
    const at = parseDate(a.createdAt);
    box.replaceChildren(
        el('span', { class: 'announcement-icon', 'aria-hidden': 'true' },
            fromMarkup('<svg viewBox="0 0 24 24"><path d="M3 11v2a1 1 0 001 1h2l5 4V6L6 10H4a1 1 0 00-1 1z"/><path d="M15.5 8.5a5 5 0 010 7M18.5 5.5a9 9 0 010 13"/></svg>')),
        el('div', { class: 'announcement-body' },
            el('p', { class: 'announcement-text' }, a.text),
            el('div', { class: 'announcement-meta' }, [a.author, at && relativeDate(at)].filter(Boolean).join(' · '))),
        el('button', { class: 'announcement-close', title: 'Kapat', 'aria-label': 'Duyuruyu kapat', onclick: () => { store.set('announcementDismissed', a.id); box.hidden = true; } }, '×'));
    box.hidden = false;
}

// ── Admin: duyuru ─────────────────────────────────────────────────────
const ANNOUNCEMENT_DURATIONS = [[0, 'Kaldırılana kadar'], [3, '3 saat'], [24, '1 gün'], [72, '3 gün'], [168, '1 hafta']];

async function renderAnnouncementTab(body) {
    let current = null;
    try { ({ announcement: current } = await bridge.invoke('announcement.get')); } catch { }

    const text = el('textarea', { class: 'textarea', rows: 3, maxLength: 500, placeholder: 'Örneğin: Bu akşam 21:00\'de AGLR SMP\'de etkinlik var!' });
    const duration = el('select', {}, ANNOUNCEMENT_DURATIONS.map(([h, label]) => el('option', { value: h }, label)));
    const status = el('span', { class: 'hint' });
    const publish = el('button', { class: 'btn btn-primary', onclick: async () => {
        if (!text.value.trim()) { status.textContent = 'Önce bir şey yaz.'; return; }
        publish.disabled = true;
        try {
            const { announcement } = await bridge.invoke('announcement.post', { text: text.value, hours: Number(duration.value) });
            store.set('announcementDismissed', '');
            renderAnnouncement(announcement);
            renderAnnouncementTab(body);
        } catch (err) { status.textContent = err.message; publish.disabled = false; }
    } }, 'Yayınla');

    body.replaceChildren(
        el('div', { class: 'admin-form' },
            el('p', { class: 'hint' }, 'Duyuru herkesin launcher\'ında, Oyna sayfasının üstünde görünür. Yeni duyuru eskisinin yerine geçer.'),
            text,
            el('div', { class: 'row' }, el('span', {}, 'Ne kadar kalsın:'), duration, publish, status)),
        current
            ? el('div', { class: 'admin-current' },
                el('div', { class: 'hint' }, 'Şu anki duyuru' + (current.expiresAt ? ` · ${formatDate(new Date(current.expiresAt))} ${new Date(current.expiresAt).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })} tarihine kadar` : '')),
                el('p', { class: 'announcement-text' }, current.text),
                el('button', { class: 'btn btn-sm btn-danger', onclick: async () => {
                    try { await bridge.invoke('announcement.delete', { id: current.id }); renderAnnouncement(null); renderAnnouncementTab(body); }
                    catch (err) { await alertDialog({ title: 'Kaldırılamadı', message: err.message }); }
                } }, 'Kaldır'))
            : el('p', { class: 'hint admin-current' }, 'Şu an yayında bir duyuru yok.'));
}

// ── Admin: veritabanı alanı ───────────────────────────────────────────
const STORAGE_COLORS = { packs: '#c27e3e', shots: '#5b8def', images: '#9b6bd6', messages: '#4caf6a' };

async function renderStorageTab(body) {
    body.replaceChildren(el('p', { class: 'hint' }, 'Hesaplanıyor…'));
    let s;
    try { s = await bridge.invoke('admin.storage'); }
    catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, err.message)); return; }

    const mb = b => formatSize(b / 1048576);
    const known = s.parts.reduce((a, p) => a + p.bytes, 0);
    const used = s.usedBytes != null ? Math.max(s.usedBytes, known) : known;
    const ratio = used / s.limitBytes;
    const level = ratio >= 0.9 ? 'storage-full' : ratio >= 0.7 ? 'storage-warn' : '';
    const other = Math.max(0, used - known);
    const parts = [...s.parts, { key: 'other', label: 'Diğer (kullanıcılar, paket listesi, dizinler)', bytes: other }];
    const color = k => STORAGE_COLORS[k] || 'var(--dim)';

    body.replaceChildren(
        el('div', { class: 'storage-total' },
            el('div', { class: level }, el('b', {}, `${mb(used)} / ${mb(s.limitBytes)}`), ` kullanılıyor (%${Math.round(ratio * 100)})`),
            el('div', { class: 'storage-bar', title: `${mb(used)} / ${mb(s.limitBytes)}` },
                parts.filter(p => p.bytes > 0).map(p => el('span', { style: `width:${Math.min(100, p.bytes / s.limitBytes * 100)}%;background:${color(p.key)}`, title: `${p.label}: ${mb(p.bytes)}` }))),
            ratio >= 0.7 && el('p', { class: 'hint ' + level }, ratio >= 0.9
                ? 'Alan neredeyse dolu. Dolarsa paket yayınlama ve resim yüklemeleri hata vermeye başlar; kullanılmayan paketleri kaldırmayı düşün.'
                : 'Alanın çoğu doldu. Büyük paketleri ve eski ekran görüntülerini gözden geçirmek iyi olabilir.')),
        el('div', { class: 'storage-legend' }, parts.map(p =>
            el('div', {}, el('i', { style: `background:${color(p.key)}` }), el('span', {}, p.label), el('b', {}, mb(p.bytes))))),
        el('p', { class: 'hint' }, 'Ekran görüntüleri kendi sınırına (varsayılan 150 MB) ulaşınca en eskileri otomatik silinir.'));
}
