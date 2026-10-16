// İndirme paneli: C#'taki DownloadJob'un gönderdiği anlık durumları gösterir. Hangi dosyaların indiği,
// ne kadarı kaldığı, hız ve kalan süre. Tamamlanan iş birkaç saniye sonra kendiliğinden kapanır.
const downloads = {
    job: null,           // son gelen durum
    expanded: true,
    hideTimer: 0,
    hovered: false,
};

function formatBytes(bytes) {
    if (!(bytes > 0)) return '0 MB';
    const mb = bytes / 1024 / 1024;
    if (mb < 1) return Math.max(1, Math.round(bytes / 1024)) + ' KB';
    if (mb < 100) return mb.toFixed(1).replace('.', ',') + ' MB';
    if (mb < 1024) return Math.round(mb) + ' MB';
    return (mb / 1024).toFixed(2).replace('.', ',') + ' GB';
}

function formatDuration(seconds) {
    if (seconds < 60) return Math.max(1, Math.round(seconds)) + ' sn';
    const minutes = Math.floor(seconds / 60), rest = Math.round(seconds % 60);
    return rest ? `${minutes} dk ${rest} sn` : `${minutes} dk`;
}

function downloadPercent(job) {
    if (job.state === 'done') return 100;
    if (job.bytes.total > 0) return Math.min(100, job.bytes.done * 100 / job.bytes.total);
    if (job.files.total > 0) return job.files.done * 100 / job.files.total;
    return null;
}

const DL_ICONS = {
    done: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>',
    failed: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 7v6M12 16.5v.5"/></svg>',
    chevron: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 15l6-6 6 6"/></svg>',
    close: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7l10 10M17 7L7 17"/></svg>',
};

function renderDownloads() {
    const root = $('downloads'), job = downloads.job;
    root.hidden = !job;
    if (!job) return;

    const running = job.state === 'running';
    const pct = downloadPercent(job);
    root.className = 'dl ' + job.state + (downloads.expanded ? ' expanded' : '') + (state.page === 'home' ? ' at-top' : '');

    const icon = running ? el('span', { class: 'dl-icon' }, el('span', { class: 'spinner' }))
        : el('span', { class: 'dl-icon' }, fromMarkup(DL_ICONS[job.state === 'done' ? 'done' : 'failed']));

    const head = el('div', { class: 'dl-head' },
        icon,
        el('div', { class: 'dl-titles' },
            el('b', { title: job.title }, running ? job.title : (job.message || job.title)),
            el('span', { class: 'dl-phase' }, running ? (job.phase || 'Hazırlanıyor') : job.state === 'done' ? job.title.replace(/ (kuruluyor|güncelleniyor|hazırlanıyor)$/, '') : 'Başarısız')),
        el('button', { class: 'dl-btn', 'data-action': 'toggle', title: downloads.expanded ? 'Ayrıntıları gizle' : 'Ayrıntıları göster' },
            fromMarkup(DL_ICONS.chevron)),
        !running && el('button', { class: 'dl-btn', 'data-action': 'close', title: 'Kapat' }, fromMarkup(DL_ICONS.close)));

    const bar = el('div', { class: 'dl-bar' + (pct === null ? ' indeterminate' : '') }, el('i', { style: pct === null ? '' : `width:${pct.toFixed(1)}%` }));

    const stats = [];
    if (job.files.total > 0) stats.push(el('span', {}, el('b', {}, job.files.done), ' / ' + job.files.total + ' dosya'));
    if (job.bytes.total > 0) stats.push(el('span', {}, el('b', {}, formatBytes(job.bytes.done)), ' / ' + formatBytes(job.bytes.total)));
    else if (job.bytes.done > 0) stats.push(el('span', {}, el('b', {}, formatBytes(job.bytes.done))));
    if (running && job.speed > 0) stats.push(el('span', {}, el('b', {}, formatBytes(job.speed) + '/sn')));
    if (running && job.eta) stats.push(el('span', { class: 'dl-eta' }, '~' + formatDuration(job.eta) + ' kaldı'));
    if (!running && job.elapsed > 0) stats.push(el('span', {}, formatDuration(job.elapsed) + ' sürdü'));

    const parts = [head,
        el('div', { class: 'dl-progress' }, bar, el('span', { class: 'dl-pct' }, pct === null ? '' : Math.floor(pct) + '%')),
        stats.length && el('div', { class: 'dl-stats' }, stats)];

    if (downloads.expanded) parts.push(downloadDetails(job));
    root.replaceChildren(el('div', { class: 'dl-card' }, parts));
}

function downloadDetails(job) {
    const rows = [];
    const activeRow = item => {
        // Oyun dosyaları tek tek izlenmez; yalnızca son denetlenen dosyaların adı gelir
        const tracked = item.size > 0 || item.done > 0;
        const pct = item.size > 0 ? Math.min(100, item.done * 100 / item.size) : null;
        const name = tracked ? item.name : item.name.split(/[\\/]/).pop();
        return el('div', { class: 'dl-row active' + (tracked ? '' : ' plain') },
            el('span', { class: 'dl-name', title: item.name }, name),
            el('span', { class: 'dl-size' }, item.size > 0 ? `${formatBytes(item.done)} / ${formatBytes(item.size)}` : ''),
            tracked && el('div', { class: 'dl-mini' + (pct === null ? ' indeterminate' : '') }, el('i', { style: pct === null ? '' : `width:${pct.toFixed(1)}%` })));
    };

    if (job.failed.length) {
        rows.push(el('div', { class: 'dl-section' }, 'Hata'));
        rows.push(...job.failed.map(item => el('div', { class: 'dl-row failed' },
            el('span', { class: 'dl-name', title: item.name }, item.name),
            el('span', { class: 'dl-error', title: item.error }, item.error))));
    }
    if (job.active.length) {
        rows.push(el('div', { class: 'dl-section' }, job.state === 'running' ? 'Şu an' : 'Son işlenen',
            job.queued > 0 && el('span', {}, `sırada ${job.queued}`)));
        rows.push(...job.active.map(activeRow));
    }
    if (job.recent.length) {
        rows.push(el('div', { class: 'dl-section' }, 'Tamamlanan'));
        rows.push(...job.recent.map(item => el('div', { class: 'dl-row done' },
            fromMarkup(DL_ICONS.done),
            el('span', { class: 'dl-name', title: item.name }, item.name),
            el('span', { class: 'dl-size' }, item.size > 0 ? formatBytes(item.size) : ''))));
    }
    if (!rows.length) rows.push(el('div', { class: 'dl-empty' }, job.state === 'running' ? 'Dosyalar hazırlanıyor…' : 'İndirilecek dosya yoktu.'));
    return el('div', { class: 'dl-details' }, rows);
}

function closeDownloads() {
    clearTimeout(downloads.hideTimer);
    downloads.job = null;
    renderDownloads();
    refreshActivity();
}

function scheduleDownloadsHide() {
    clearTimeout(downloads.hideTimer);
    if (!downloads.job || downloads.job.state !== 'done') return;
    downloads.hideTimer = setTimeout(() => {
        if (downloads.hovered) { scheduleDownloadsHide(); return; }
        closeDownloads();
    }, 6000);
}

function setupDownloads() {
    downloads.expanded = store.get('downloadsExpanded', 'true') !== 'false';
    const root = $('downloads');
    root.addEventListener('mouseenter', () => { downloads.hovered = true; });
    root.addEventListener('mouseleave', () => { downloads.hovered = false; });

    // Panel saniyede birkaç kez yeniden çizildiği için düğmeler basılıp bırakılana kadar değişebilir;
    // tıklama yerine basma anında işlenir. Klavyeyle (Enter/Boşluk) gelen tıklama ayrıca karşılanır.
    const act = action => {
        if (action === 'toggle') {
            downloads.expanded = !downloads.expanded;
            store.set('downloadsExpanded', downloads.expanded);
            renderDownloads();
        } else if (action === 'close') closeDownloads();
    };
    root.addEventListener('pointerdown', event => {
        const button = event.button === 0 && event.target.closest('[data-action]');
        if (!button) return;
        event.preventDefault();
        act(button.dataset.action);
    });
    root.addEventListener('click', event => {
        const button = event.detail === 0 && event.target.closest('[data-action]');
        if (button) act(button.dataset.action);
    });

    let framePending = false;
    bridge.on('download', job => {
        // Yeni bir iş öncekinin yerini alır; aynı işin eski bir durumu sonra gelirse yok sayılır
        if (downloads.job && downloads.job.id === job.id && downloads.job.state !== 'running') return;
        downloads.job = job;
        if (job.state !== 'running') scheduleDownloadsHide(); else clearTimeout(downloads.hideTimer);
        refreshActivity();
        if (framePending) return;
        framePending = true;
        requestAnimationFrame(() => { framePending = false; renderDownloads(); });
    });
}
