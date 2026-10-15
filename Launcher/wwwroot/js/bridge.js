// C# tarafıyla konuşan katman.
//   bridge.invoke(cmd, args)  → Promise (komutun cevabı)
//   bridge.send(cmd, args)    → cevap beklemeyen komut (pencere işlemleri)
//   bridge.on(event, handler) → C#'ın gönderdiği olayları dinler
const bridge = (() => {
    const isNative = !!(window.external && typeof window.external.sendMessage === 'function');
    const pending = new Map();
    const listeners = new Map();
    let nextId = 1;

    function dispatch(message) {
        if (message.event) {
            (listeners.get(message.event) || []).forEach(handler => handler(message.data));
            return;
        }
        const request = pending.get(message.id);
        if (!request) return;
        pending.delete(message.id);
        if (message.ok) request.resolve(message.result);
        else request.reject(new Error(message.error || 'Bilinmeyen hata'));
    }

    if (isNative) {
        window.external.receiveMessage(raw => {
            try { dispatch(JSON.parse(raw)); }
            catch (err) { console.error('Geçersiz mesaj', err); }
        });
    }

    function invoke(cmd, args) {
        if (!isNative) return mockBackend.invoke(cmd, args || {});
        return new Promise((resolve, reject) => {
            const id = String(nextId++);
            pending.set(id, { resolve, reject });
            window.external.sendMessage(JSON.stringify({ id, cmd, args: args || {} }));
        });
    }

    function send(cmd, args) {
        if (!isNative) return;
        window.external.sendMessage(JSON.stringify({ cmd, args: args || {} }));
    }

    function on(event, handler) {
        if (!listeners.has(event)) listeners.set(event, []);
        listeners.get(event).push(handler);
    }

    return { invoke, send, on, isNative, _dispatch: dispatch };
})();

// Sayfa launcher dışında (normal tarayıcıda) açılırsa arayüz üzerinde çalışabilmek için
// sahte bir arka uç. Gerçek launcher'da hiç kullanılmaz.
const mockBackend = (() => {
    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    const log = (text, level = 'info', replace = false) =>
        bridge._dispatch({ event: 'log', data: [{ time: new Date().toTimeString().slice(0, 8), text, level, replace }] });

    let users = [
        { id: '1', username: 'vulu', allowedPacks: ['Vanilla', 'AGLR', 'R'], isActive: true, isAdmin: true },
        { id: '2', username: 'misafir', allowedPacks: ['Vanilla'], isActive: true, isAdmin: false },
        { id: '3', username: 'eskiuye', allowedPacks: ['Vanilla', 'AGLR'], isActive: false, isAdmin: false },
    ];

    let settings = {
        defaultRamGb: 0, javaMode: 'auto', customJavaPath: '', jvmArgs: '', gameWidth: 0, gameHeight: 0, fullscreen: false,
        onGameStart: 'keep', skipSplash: false, consoleExpanded: false, musicEnabled: false, musicVolume: 40, accentColor: '#c27e3e',
    };

    let instances = [
        { id: 'pack:vanilla', kind: 'server', name: 'Vanilla', description: 'Modsuz, sade Minecraft.', mcVersion: '1.20.1', loader: 'vanilla', loaderVersion: null, recommendedRamGb: 4, logo: 'assets/minecraftlogo.png', background: 'assets/launchervanillabg.jpg', backgroundStyle: 'cover', thumbnail: 'assets/launchervanillabg.jpg', iconUrl: null },
        { id: 'pack:aglr', kind: 'server', name: 'AGLR', description: 'Forge tabanlı ana mod paketi.', mcVersion: '1.20.1', loader: 'forge', loaderVersion: '47.4.10', recommendedRamGb: 8, logo: 'assets/aglr.png', background: 'assets/mainmenu.png', backgroundStyle: 'side', thumbnail: 'assets/mainmenu.png', iconUrl: null },
        { id: 'inst:fabric-121', kind: 'custom', name: 'Fabric 1.21', description: 'Kişisel instance', mcVersion: '1.21.1', loader: 'fabric', loaderVersion: '0.16.14', recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null },
    ];

    let content = [
        { file: 'mods/sodium-fabric-0.6.jar', type: 'mod', title: 'Sodium', version: 'mc1.21.1-0.6.13', iconUrl: null, sizeMb: 1.2 },
        { file: 'mods/elle-eklenen.jar', type: 'mod', title: 'elle-eklenen.jar', version: null, iconUrl: null, sizeMb: 0.4 },
        { file: 'shaderpacks/ComplementaryReimagined.zip', type: 'shader', title: 'Complementary Reimagined', version: 'r5.5', iconUrl: null, sizeMb: 0.6 },
    ];

    const sampleHits = type => ['Sodium', 'Iris Shaders', 'Lithium', 'Mod Menu', 'Fabric API', 'Entity Culling'].map((title, index) => ({
        id: 'p' + index, slug: title.toLowerCase(), type, title, author: 'yazar' + index, iconUrl: null,
        description: 'Örnek açıklama: oyunun performansını artıran, çok kullanılan bir içerik. Uzun açıklamalar iki satırda kesilir ve sonuna üç nokta konur ki liste düzenli kalsın.',
        downloads: 242793522 / (index + 1), follows: 41673 / (index + 1), updated: new Date(Date.now() - index * 40 * 86400000).toISOString(),
        categories: ['optimization', 'utility'].slice(0, 1 + index % 2), loaders: ['fabric', 'neoforge', 'quilt'].slice(0, 1 + index % 3), installed: index === 0 && type === 'mod',
    }));

    const handlers = {
        'app.init': async () => ({ version: '0.7', savedUsername: 'vulu', hasSavedPassword: true, ramOptionsGb: [4, 8, 12, 16], settings }),
        'app.wake': async () => { await wait(600); return { reachable: true }; },
        'auth.login': async args => {
            await wait(500);
            if (args.username === 'hata') return { success: false, error: 'Kullanıcı adı veya şifre hatalı.' };
            return { success: true, username: args.username || 'vulu', isAdmin: true };
        },
        'auth.logout': async () => ({ success: true }),
        'auth.forgetPassword': async () => ({ success: true }),
        'updates.check': async () => { await wait(300); return []; },
        'updates.apply': async () => ({ success: true }),
        'launcher.check': async () => { log('Launcher güncel.', 'muted'); return { status: 'upToDate' }; },
        'game.play': async args => {
            log(`${args.instanceId} hazırlanıyor...`, 'accent');
            await wait(700);
            log('Java 21 bulunamadı, Adoptium\'dan indiriliyor...', 'accent');
            for (let mb = 0; mb <= 45; mb += 15) { log(`Java 21 indiriliyor... ${mb} MB`, 'accent', true); await wait(200); }
            log('Java 21 kuruldu.', 'ok');
            log('Oyun başlatıldı! İyi oyunlar.', 'ok');
            log('[Render thread/INFO]: Setting user: vulu', 'game');
            setTimeout(() => { log('Oyun kapatıldı. İyi günler!', 'warn'); bridge._dispatch({ event: 'gameExited' }); }, 4000);
            return { started: true };
        },

        'instances.list': async () => instances,
        'instances.options': async () => { await wait(300); return { minecraftVersions: ['1.21.11', '1.21.1', '1.20.1', '1.19.2', '1.16.5', '1.12.2'], loaders: ['vanilla', 'fabric', 'quilt', 'forge', 'neoforge'] }; },
        'instances.loaderVersions': async args => { await wait(300); return args.loader === 'neoforge' && args.mcVersion === '1.20.1' ? [] : ['0.16.14', '0.16.13', '0.16.10']; },
        'instances.create': async args => {
            const id = 'inst:' + args.name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
            instances = instances.concat({ id, kind: 'custom', name: args.name, description: 'Kişisel instance', mcVersion: args.mcVersion, loader: args.loader, loaderVersion: args.loaderVersion || null, recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null });
            return { id };
        },
        'instances.update': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, name: args.name, mcVersion: args.mcVersion, loader: args.loader, loaderVersion: args.loaderVersion || null } : i);
            return { id: args.id };
        },
        'instances.delete': async args => { instances = instances.filter(i => i.id !== args.id); return { success: true }; },
        'instances.content': async () => ({ name: 'Fabric 1.21', mcVersion: '1.21.1', loader: 'fabric', items: content }),
        'instances.removeContent': async args => { content = content.filter(c => c.file !== args.file); return { success: true }; },

        'modrinth.search': async args => { await wait(350); return { total: 38, offset: args.offset, hits: sampleHits(args.type) }; },
        'modrinth.filters': async args => {
            await wait(200);
            const byType = {
                mod: [{ header: 'categories', names: ['adventure', 'decoration', 'equipment', 'library', 'magic', 'optimization', 'storage', 'technology', 'utility', 'worldgen'] }],
                modpack: [{ header: 'categories', names: ['adventure', 'challenging', 'combat', 'kitchen-sink', 'lightweight', 'magic', 'optimization', 'quests', 'technology'] }],
                resourcepack: [{ header: 'categories', names: ['realistic', 'simplistic', 'themed'] }, { header: 'resolutions', names: ['16x', '32x', '64x'] }],
                shader: [{ header: 'categories', names: ['cartoon', 'fantasy', 'realistic'] }, { header: 'features', names: ['bloom', 'shadows', 'reflections'] }, { header: 'performance impact', names: ['low', 'medium', 'high'] }],
            };
            return { categories: byType[args.type] || [], sorts: ['relevance', 'downloads', 'follows', 'newest', 'updated'], minecraftVersions: ['1.21.11', '1.21.1', '1.20.1', '1.19.2'], loaders: ['fabric', 'quilt', 'forge', 'neoforge'] };
        },
        'modrinth.versions': async () => {
            await wait(250);
            return [
                { id: 'v3', name: 'Sodium 0.6.13', number: 'mc1.21.1-0.6.13', type: 'release', published: new Date(Date.now() - 20 * 86400000).toISOString(), gameVersions: ['1.21', '1.21.1'], loaders: ['fabric', 'quilt'] },
                { id: 'v2', name: 'Sodium 0.6.12 Beta 2', number: 'mc1.21.1-0.6.12-beta.2', type: 'beta', published: new Date(Date.now() - 90 * 86400000).toISOString(), gameVersions: ['1.21.1'], loaders: ['fabric'] },
                { id: 'v1', name: 'Sodium 0.6.0', number: 'mc1.21.1-0.6.0', type: 'release', published: new Date(Date.now() - 400 * 86400000).toISOString(), gameVersions: ['1.21.1'], loaders: ['fabric'] },
            ];
        },
        'modrinth.openPage': async () => ({ opened: true }),
        'instances.pickBackground': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, background: 'assets/launchervanillabg.jpg', backgroundStyle: 'cover', hasCustomBackground: true, thumbnail: i.kind === 'custom' ? 'assets/launchervanillabg.jpg' : i.thumbnail } : i);
            return { changed: true };
        },
        'instances.clearBackground': async args => {
            instances = instances.map(i => i.id === args.id ? { ...i, background: i.kind === 'custom' ? null : i.background, hasCustomBackground: false, thumbnail: i.kind === 'custom' ? null : i.thumbnail } : i);
            return { changed: true };
        },
        'modrinth.install': async () => {
            log('Iris Shaders indiriliyor...', 'accent'); await wait(900);
            log('Sodium indiriliyor...', 'accent'); await wait(900);
            return { success: true, installed: ['Iris Shaders', 'Sodium'] };
        },
        'modrinth.installModpack': async () => {
            for (let n = 0; n <= 120; n += 30) { log(`Modpack dosyaları indiriliyor... ${n}/120`, 'accent', true); await wait(400); }
            instances = instances.concat({ id: 'inst:fabulously', kind: 'custom', name: 'Fabulously Optimized', description: 'Modrinth modpack · 6.1.0', mcVersion: '1.21.1', loader: 'fabric', loaderVersion: '0.16.14', recommendedRamGb: 8, logo: null, background: null, backgroundStyle: 'cover', thumbnail: null, iconUrl: null });
            return { success: true, id: 'inst:fabulously', name: 'Fabulously Optimized' };
        },

        'settings.get': async () => ({ settings, ramOptionsGb: [4, 8, 12, 16], installedJava: [17, 21], version: '0.7', gameFolder: 'D:\\Launchers\\vulu Launcher\\', hasSavedPassword: true, savedUsername: 'vulu' }),
        'settings.save': async args => { settings = { ...args }; return { settings }; },
        'settings.pickJava': async () => ({ path: 'C:\\Program Files\\Java\\jdk-21\\bin\\java.exe' }),
        'settings.removeJava': async () => ({ removed: 2 }),
        'files.open': async () => ({ opened: true }),

        'admin.connect': async args => { await wait(400); return args.password ? { success: true } : { success: false, error: 'Bağlantı başarısız.' }; },
        'admin.users': async () => users,
        'admin.saveUser': async args => {
            if (args.id) users = users.map(u => u.id === args.id ? { ...u, ...args } : u);
            else users.push({ ...args, id: String(Date.now()) });
            return { success: true };
        },
        'admin.deleteUser': async args => { users = users.filter(u => u.id !== args.id); return { success: true }; },
        'admin.instances': async () => ({ trackedFolders: ['config', 'kubejs', 'defaultconfigs', 'fancymenu_data'], instances: ['Vanilla', 'AGLR'] }),
        'admin.scanPush': async () => { await wait(800); return { success: true, fileCount: 214, deletedCount: 3 }; },
    };

    return {
        invoke: (cmd, args) => handlers[cmd] ? handlers[cmd](args) : Promise.reject(new Error('Bilinmeyen komut: ' + cmd)),
    };
})();
