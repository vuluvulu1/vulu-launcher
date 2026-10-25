// Admin → Radyo: vulu Radyo şarkılarını yükleme, adlandırma ve silme. Şarkılar sunucu diskinde durur; launcher'lar
// bir kez indirip önbellekten çalar.

async function renderRadioTab(body) {
    body.replaceChildren(el('p', { class: 'hint' }, 'Yükleniyor…'));
    let data;
    try { data = await bridge.invoke('radio.adminList'); }
    catch (err) { body.replaceChildren(el('p', { class: 'form-error' }, err.message)); return; }

    const mb = b => formatSize(b / 1048576);
    const note = el('p', { class: 'form-error', hidden: true });
    const uploadButton = el('button', { class: 'btn btn-primary btn-sm', onclick: upload }, 'Şarkı yükle');

    async function upload() {
        note.hidden = true;
        uploadButton.disabled = true;
        uploadButton.textContent = 'Yükleniyor…';
        try {
            const result = await bridge.invoke('radio.upload');
            if (result.uploaded) { radio.stations = []; await renderRadioTab(body); return; }   // oynatıcı listeyi yeniden alsın
        } catch (err) {
            note.textContent = err.message;
            note.hidden = false;
        }
        uploadButton.disabled = false;
        uploadButton.textContent = 'Şarkı yükle';
    }

    const row = song => {
        const title = el('input', { type: 'text', value: song.title, maxlength: 100, placeholder: 'Şarkı adı', 'aria-label': 'Şarkı adı' });
        const artist = el('input', { type: 'text', value: song.artist, maxlength: 100, placeholder: 'Sanatçı', 'aria-label': 'Sanatçı' });
        const save = async () => {
            if (title.value.trim() === song.title && artist.value.trim() === song.artist) return;
            try {
                const updated = await bridge.invoke('radio.updateSong', { id: song.id, title: title.value, artist: artist.value });
                Object.assign(song, updated);
                radio.stations = [];
            } catch (err) {
                title.value = song.title; artist.value = song.artist;
                await alertDialog({ title: 'Kaydedilemedi', message: err.message });
            }
        };
        title.addEventListener('change', save);
        artist.addEventListener('change', save);
        return el('div', { class: 'user-row radio-song' },
            el('div', { class: 'radio-song-fields' }, title, artist),
            el('span', { class: 'meta' }, mb(song.size)),
            el('button', {
                class: 'btn btn-sm btn-danger', onclick: async () => {
                    const ok = await confirmDialog({ title: 'Şarkıyı sil', message: `"${song.title}" vulu Radyo'dan silinsin mi?`, okText: 'Sil', cancelText: 'Vazgeç', danger: true });
                    if (!ok) return;
                    try { await bridge.invoke('radio.deleteSong', { id: song.id }); radio.stations = []; await renderRadioTab(body); }
                    catch (err) { await alertDialog({ title: 'Silinemedi', message: err.message }); }
                },
            }, 'Sil'));
    };

    const ratio = data.capBytes ? data.usedBytes / data.capBytes : 0;
    body.replaceChildren(
        el('div', { class: 'admin-toolbar' },
            el('span', {}, `${data.songs.length} şarkı · ${mb(data.usedBytes)} / ${mb(data.capBytes)}`),
            uploadButton),
        !data.ready && el('p', { class: 'form-error' }, 'Sunucuda şarkı klasörü henüz ayarlı değil; yükleme yapılamaz (RADIO_DIR).'),
        note,
        el('div', { class: 'storage-bar radio-storage' }, el('span', { style: `width:${Math.min(100, ratio * 100)}%;background:var(--accent)` })),
        el('p', { class: 'hint' },
            'MP3, OGG ya da M4A; en fazla 30 MB. Dosya adı "Sanatçı - Şarkı" ise ikisi otomatik ayrılır. ',
            el('b', {}, 'Yalnızca hakkına sahip olduğun ya da telifsiz şarkıları yükle'),
            ' — buradaki şarkılar bütün üyelere dağıtılır.'),
        data.songs.length
            ? el('div', {}, data.songs.map(row))
            : el('p', { class: 'hint' }, 'Henüz şarkı yok. İlk şarkıyı yükleyince vulu Radyo herkesin çalarında ilk tür olarak görünür.'));
}
