<div align="center">

<img src="docs/logo.svg" width="112" height="112" alt="vulu Launcher" />

# vulu Launcher

Minecraft'ı kurmayı, modlamayı ve oynamayı tek bir uygulamada toplayan bir launcher.

<a href="https://github.com/vuluvulu1/vulu-launcher/releases/latest"><img src="https://img.shields.io/github/v/release/vuluvulu1/vulu-launcher?label=s%C3%BCr%C3%BCm&color=c9a992&labelColor=1a1410" alt="Sürüm"></a>
<a href="https://github.com/vuluvulu1/vulu-launcher/releases"><img src="https://img.shields.io/github/downloads/vuluvulu1/vulu-launcher/total?label=indirme&color=9c704c&labelColor=1a1410" alt="İndirme"></a>
<a href="LICENSE"><img src="https://img.shields.io/badge/lisans-AGPL--3.0-654127?labelColor=1a1410" alt="Lisans: AGPL-3.0"></a>
<img src="https://img.shields.io/badge/.NET-10-c9a992?logo=dotnet&logoColor=white&labelColor=1a1410" alt=".NET 10">
<img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-654127?labelColor=1a1410" alt="Windows, Android">

**[İndir (vulu-launcher-setup.exe)](https://vuluvulu.com.tr/releases/vulu-launcher-setup.exe)** · [vuluvulu.com.tr](https://vuluvulu.com.tr/launcher)

<br>

<img src="docs/screenshots/home.png" alt="Ana ekran" width="860" />

</div>

## Kurulum

`vulu-launcher-setup.exe` dosyasını indirip çalıştır. Launcher her açılışta kendini günceller. Kaldırmak için Windows'un "Uygulamalar" listesini kullanabilirsin.

Hesaplar davetle açılır: giriş ekranından ya da [siteden](https://vuluvulu.com.tr/kayit) katılma isteği gönderilir, bir admin onaylar.

Windows ilk seferde SmartScreen uyarısı verebilir: *Ek bilgi → Yine de çalıştır*.

### Android

**[APK'yı indir](https://vuluvulu.com.tr/releases/android/vulu-launcher.apk)** · Android 8 ve üstü. Aynı hesap, sosyal akış, mesajlar, paketler ve sunucular, telefon ekranına göre tasarlanmış haliyle. Minecraft da uygulamanın içinden açılır (oyun motoru: Amethyst); paketler ve modlar telefona kurulabilir. İlk açılışta oyun dosyaları ve Java bir kez iner (1.20.1 için yaklaşık 700 MB). Yeni sürüm çıkınca uygulama haber verir.

Telefonda henüz olmayanlar: Forge ve NeoForge paketleri açılmıyor; sunucuya doğrudan katılma ve oynama süresi çalışmıyor.

Geliştirme: Android uygulaması (`Android/`, .NET MAUI) Windows sürümüyle aynı arayüzü (`Launcher/wwwroot`) ve aynı komutları (`Core/`) kullanır. `tools/android-build.ps1` derler, `tools/publish-android.ps1` yayınlar.

## Neler var

- Vanilla ve kendi instance'ların (Fabric, Quilt, Forge, NeoForge); Java kendiliğinden indirilir
- CurseForge'dan mod, kaynak paketi, shader ve modpack kurma (bağımlılıklarıyla birlikte); Modrinth de alternatif kaynak olarak duruyor
- vulu Packs: paylaşılan paketlerin tek tıkla kurulumu ve güncellemesi; her güncellemede neyin değiştiği görünür
- Sunucular sekmesi: vulu sunucularının durumu ve tek tıkla bağlanma
- Sosyal: ekran görüntüsü paylaşma ve yorumlar, uçtan uca şifreli mesajlar, arkadaş listesi, profil bannerı
- Bildirimler: yeni mesaj, beğeni, yorum ve paket güncellemeleri için Windows bildirimi; adminden duyurular
- Çökme yardımcısı ve eksik mod denetimi
- Dünya yedekleri, oynama süresi, skinler, Discord durumu
- Masaüstü kısayolları: bir paketi ya da sunucuyu tek tıkla açan kısayol
- Üç tema: Classic, Neon, Atlas

## Teşekkürler

- **[Amethyst](https://github.com/AngelAuraMC/Amethyst-Android)** (AngelAuraMC) ve onun temeli **[PojavLauncher](https://github.com/PojavLauncherTeam/PojavLauncher)**: Android'de Minecraft'ı açan oyun motoru. GNU LGPL 3.0 ile dağıtılır; vulu için yapılan değişiklikler [vulu-amethyst](https://github.com/vuluvulu1/vulu-amethyst) deposunda.
- **[CmlLib.Core](https://github.com/CmlLib/CmlLib.Core)**: Windows'ta oyunu ve mod yükleyicilerini kuran ve başlatan kütüphane (MIT).
- **[authlib-injector](https://github.com/yushijinhun/authlib-injector)**: skinlerin vulu üzerinden çalışmasını sağlayan Java ajanı (AGPL 3.0). Launcher pakete gömmez; ilk ihtiyaçta resmî adresinden indirir.
- **[CurseForge](https://www.curseforge.com/minecraft)** ve **[Modrinth](https://modrinth.com)**: mod, kaynak paketi, shader ve modpack'lerin kaynağı. İçerikler yapımcılarına aittir; launcher onları bu platformların kendi sunucularından indirir.

Bütün bileşenlerin listesi ve lisansları: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Lisans

[AGPL-3.0](LICENSE). Launcher'ı özgürce kullanabilir, değiştirebilir ve dağıtabilirsiniz. Değiştirilmiş bir sürümü dağıtan ya da sunucuda çalıştırıp başkalarına sunan, kaynak kodunu da aynı lisansla açmalı ve bu projenin telif ve lisans bildirimlerini korumalıdır.

"vulu" adı ve VL logosu lisansa dahil değildir: kendi sürümünüzü dağıtırken farklı bir ad ve logo kullanın.

Kullanılan üçüncü taraf bileşenler ve lisansları: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
