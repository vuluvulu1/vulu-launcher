// Geri bildirimler. Herkes yazar, kendi yazdığını siler; admin "yapıldı" olarak işaretler ve kısa bir yanıt yazar.
// Bir paket verilirse o paketin, null ise pakete bağlı olmayan paket isteklerinin, 'launcher' ise launcher'ın kendisi
// hakkındaki hata bildirimleri ve önerilerin listesi açılır.
const FEEDBACK_KINDS = {
    feedback: { label: 'Geri bildirim', placeholder: 'Pakette neyi beğendin, ne sorun çıkardı? Çöken mod, eksik görev, performans…' },
    request: { label: 'İstek', placeholder: 'Eklenmesini ya da değişmesini istediğin şey: bir mod, bir ayar, yeni bir paket…' },
};
const LAUNCHER_KINDS = {
    bug: { label: 'Hata', placeholder: 'Ne oldu? Ne yaparken oldu, her seferinde oluyor mu? Ekranda bir hata yazdıysa onu da ekle.' },
    idea: { label: 'Öneri', placeholder: 'Launcher\'da neyin olmasını ya da değişmesini istersin?' },
};
const ALL_KINDS = { ...FEEDBACK_KINDS, ...LAUNCHER_KINDS };

function timeAgo(date) {
    const seconds = Math.max(0, (Date.now() - date) / 1000);
    if (seconds < 60) return 'şimdi';
    if (seconds < 3600) return Math.floor(seconds / 60) + ' dk önce';
    if (seconds < 86400) return Math.floor(seconds / 3600) + ' sa önce';
    return relativeDate(date);
}

async function openFeedback(pack, isAdmin) {
    const launcher = pack === 'launcher';
    const packKey = launcher ? 'launcher' : pack ? pack.id : 'general';
    const kindsOf = launcher ? LAUNCHER_KINDS : FEEDBACK_KINDS;
    let items = [], filter = 'open', kind = launcher ? 'bug' : pack ? 'feedback' : 'request', loading = true, loadError = '';

    await openModal(close => {
        const list = el('div', { class: 'fb-list' });
        const filters = el('div', { class: 'segmented fb-filter' });
        const kinds = el('div', { class: 'segmented' });
        const text = el('textarea', { class: 'fb-text', rows: 2, maxLength: 1000, spellcheck: false, autofocus: true });
        const counter = el('span', { class: 'fb-counter' });
        const error = el('span', { class: 'form-error fb-error' });
        const send = el('button', { class: 'btn btn-primary btn-sm', type: 'submit' }, 'Gönder');

        function renderKinds() {
            kinds.replaceChildren(...Object.entries(kindsOf).map(([key, info]) =>
                el('button', { type: 'button', class: key === kind ? 'active' : '', onclick: () => { kind = key; renderKinds(); text.focus(); } },
                    info.label)));
            text.placeholder = kindsOf[kind].placeholder;
        }

        function refreshComposer() {
            counter.textContent = text.value.length > 800 ? `${text.value.length}/1000` : '';
            send.disabled = text.value.trim().length < 3;
        }

        function renderFilters() {
            const open = items.filter(i => i.status === 'open').length;
            filters.replaceChildren(...[['open', 'Açık', open], ['done', 'Yapıldı', items.length - open], ['all', 'Tümü', items.length]]
                .map(([key, label, count]) => el('button', { type: 'button', class: key === filter ? 'active' : '', onclick: () => { filter = key; render(); } },
                    label, el('span', { class: 'fb-n' }, count))));
        }

        function entry(item) {
            const avatar = el('span', { class: 'avatar avatar-md', 'aria-hidden': 'true' });
            setAvatar(avatar, { username: item.author, avatarVersion: item.authorAvatarVersion });
            const created = parseDate(item.createdAt);
            const done = item.status === 'done';

            const row = el('article', { class: 'fb-item' + (done ? ' done' : '') + (item.mine ? ' mine' : '') });
            const main = el('div', { class: 'fb-main' },
                el('div', { class: 'fb-head' },
                    el('b', {}, item.author),
                    el('span', { class: 'fb-kind ' + item.kind }, (ALL_KINDS[item.kind] || FEEDBACK_KINDS.feedback).label),
                    done && el('span', { class: 'fb-done' }, '✓ Yapıldı'),
                    el('span', { class: 'fb-time', title: created ? created.toLocaleString('tr-TR') : '' }, created ? timeAgo(created) : ''),
                    el('div', { class: 'fb-actions' },
                        isAdmin && el('button', { class: 'link-btn', onclick: () => update(item, { status: done ? 'open' : 'done' }) },
                            done ? 'Yeniden aç' : 'Yapıldı'),
                        isAdmin && el('button', { class: 'link-btn', onclick: () => replyTo(item, main) }, item.reply ? 'Yanıtı düzenle' : 'Yanıtla'),
                        item.canDelete && el('button', { class: 'link-btn danger', onclick: () => remove(item) }, 'Sil'))),
                el('p', { class: 'fb-text-body' }, item.text),
                item.meta && (item.meta.version || item.meta.platform) && el('p', { class: 'fb-meta' },
                    [item.meta.version && 'v' + item.meta.version, item.meta.platform].filter(Boolean).join(' · ')),
                item.reply && el('div', { class: 'fb-reply' }, el('b', {}, item.repliedBy || 'admin'), item.reply));
            row.append(avatar, main);
            return row;
        }

        function render() {
            renderFilters();
            if (loading) { list.replaceChildren(el('p', { class: 'fb-empty' }, 'Yükleniyor…')); return; }
            if (loadError) { list.replaceChildren(el('p', { class: 'fb-empty error' }, loadError)); return; }
            const shown = items.filter(i => filter === 'all' || i.status === filter);
            list.replaceChildren(...(shown.length ? shown.map(entry) : [el('p', { class: 'fb-empty' },
                items.length ? 'Bu filtrede bir şey yok.'
                    : launcher ? 'Henüz bildirim yok. Bir hata gördüysen ya da aklında bir fikir varsa yaz.'
                    : pack ? 'Henüz kimse yazmadı. İlk yazan sen ol.'
                        : 'Henüz istek yok. Eklenmesini istediğin bir paket ya da özellik varsa yaz.')]));
        }

        async function load() {
            try { items = await bridge.invoke('feedback.list', { pack: packKey }); }
            catch (err) { loadError = 'Yüklenemedi: ' + err.message; }
            loading = false;
            render();
        }

        async function submit(event) {
            event.preventDefault();
            error.textContent = '';
            send.disabled = true;
            try {
                items.unshift(await bridge.invoke('feedback.post', { pack: packKey, kind, text: text.value }));
                text.value = '';
                filter = 'open';
                render();
            } catch (err) { error.textContent = err.message; }
            refreshComposer();
        }

        async function update(item, change) {
            try {
                const updated = await bridge.invoke('feedback.update', { id: item.id, ...change });
                items = items.map(i => i.id === item.id ? updated : i);
                render();
            } catch (err) { await alertDialog({ title: 'Kaydedilemedi', message: err.message }); }
        }

        function replyTo(item, main) {
            if (main.querySelector('.fb-reply-form')) return;
            const input = el('textarea', { class: 'textarea', rows: 2, maxLength: 500, spellcheck: false, placeholder: 'Kısa bir yanıt, ör. "rev 4\'te eklendi"' });
            input.value = item.reply || '';
            const form = el('form', { class: 'fb-reply-form', onsubmit: event => { event.preventDefault(); update(item, { reply: input.value }); } },
                input,
                el('div', { class: 'fb-reply-actions' },
                    el('button', { class: 'btn btn-sm', type: 'button', onclick: () => form.remove() }, 'Vazgeç'),
                    el('button', { class: 'btn btn-sm btn-primary', type: 'submit' }, 'Kaydet')));
            main.append(form);
            input.focus();
        }

        async function remove(item) {
            const accepted = await confirmDialog({
                title: 'Silinsin mi?', message: 'Bu yazı kalıcı olarak silinir.', okText: 'Sil', cancelText: 'Vazgeç', danger: true,
            });
            if (!accepted) return;
            try {
                await bridge.invoke('feedback.delete', { id: item.id });
                items = items.filter(i => i.id !== item.id);
                render();
            } catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
        }

        const compose = el('form', { class: 'fb-compose', onsubmit: submit },
            el('div', { class: 'fb-compose-top' }, kinds, el('span', { class: 'fb-hint' }, 'Ctrl+Enter ile gönder')),
            text,
            el('div', { class: 'fb-compose-foot' }, error, counter, send));
        text.addEventListener('input', refreshComposer);
        text.addEventListener('keydown', event => {
            if (event.key === 'Enter' && event.ctrlKey && !send.disabled) compose.requestSubmit();
        });

        renderKinds();
        refreshComposer();
        render();
        load();

        return [
            el('div', { class: 'modal-head' },
                el('span', { class: 'fb-title' }, launcher ? 'vulu Launcher' : pack ? pack.name : 'Paket istekleri',
                    el('span', { class: 'fb-sub' }, launcher ? 'Hata bildir ya da öneri yaz' : pack ? 'Geri bildirim ve istekler' : 'Pakete bağlı olmayan istekler')),
                el('button', { class: 'icon-close', type: 'button', 'aria-label': 'Kapat', onclick: () => close() }, '✕')),
            el('div', { class: 'modal-body fb-body' }, compose, el('div', { class: 'fb-list-head' }, filters), list),
        ];
    }, { wide: true });

    // Kartlardaki ve üst çubuktaki açık sayıları yenilensin
    if (state.page === 'packs') openPacksPage();
    refreshFeedbackBadge();
}

/** Üst çubuktaki geri bildirim düğmesi: adminlere açık launcher bildirimlerinin sayısı. */
async function refreshFeedbackBadge() {
    const badge = $('feedback-badge');
    if (!badge) return;
    let count = 0;
    if (state.isAdmin) {
        try { count = ((await bridge.invoke('feedback.counts')) || {}).launcher || 0; } catch { }
    }
    badge.textContent = count || '';
    badge.hidden = !count;
}
