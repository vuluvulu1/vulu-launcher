<div align="center">

<img src="docs/logo.svg" width="112" height="112" alt="vulu Launcher" />

# vulu Launcher

Minecraft'ı kurmayı, modlamayı ve oynamayı tek bir uygulamada toplayan bir launcher.

[![Sürüm](https://img.shields.io/github/v/release/vuluvulu1/vulu-launcher?style=for-the-badge&label=s%C3%BCr%C3%BCm&color=c27e3e)](https://github.com/vuluvulu1/vulu-launcher/releases/latest)
[![İndirme](https://img.shields.io/github/downloads/vuluvulu1/vulu-launcher/total?style=for-the-badge&label=indirme&color=3d6b4f)](https://github.com/vuluvulu1/vulu-launcher/releases)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-2b5797?style=for-the-badge&logo=windows11&logoColor=white)
![Android](https://img.shields.io/badge/Android-8%2B-3d8b5a?style=for-the-badge&logo=android&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?style=for-the-badge&logo=dotnet&logoColor=white)

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

## Lisans

[AGPL-3.0](LICENSE). Launcher'ı özgürce kullanabilir, değiştirebilir ve dağıtabilirsiniz. Değiştirilmiş bir sürümü dağıtan ya da sunucuda çalıştırıp başkalarına sunan, kaynak kodunu da aynı lisansla açmalı ve bu projenin telif ve lisans bildirimlerini korumalıdır.

"vulu" adı ve VL logosu lisansa dahil değildir: kendi sürümünüzü dağıtırken farklı bir ad ve logo kullanın.

Kullanılan üçüncü taraf bileşenler ve lisansları: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
