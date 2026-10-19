using System.Security.Cryptography;
using VuluShared;

// Release imzalama aracı. Özel anahtar repoda değil, yalnızca release çıkaran bilgisayarda durur:
//   %APPDATA%\vulu-release\release-signing-key.pem
// Komutlar:
//   keygen            yeni anahtar çifti üretir (varsa üzerine yazmaz), açık anahtarı yazdırır
//   pubkey            açık anahtarı yazdırır (Shared/Releases.cs'e konacak değer)
//   sign <dosya>      <dosya>.sig üretir ve hemen gömülü açık anahtarla doğrular
//   verify <dosya>    <dosya>.sig'i gömülü açık anahtarla doğrular

string keyPath = Environment.GetEnvironmentVariable("VULU_SIGNING_KEY") is { Length: > 0 } custom
    ? custom
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vulu-release", "release-signing-key.pem");

try
{
    switch (args.FirstOrDefault())
    {
        case "keygen":
            if (File.Exists(keyPath))
                return Fail($"Anahtar zaten var: {keyPath}\nYenisini üretmek bütün kullanıcıların güncelleme almasını keser; bilerek yapıyorsan önce eskisini taşı.");
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
                File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
                Console.WriteLine($"Özel anahtar: {keyPath}  (YEDEKLE, kimseyle paylaşma)");
                Console.WriteLine($"Açık anahtar: {Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}");
            }
            return 0;

        case "pubkey":
            using (var key = LoadKey(keyPath))
                Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
            return 0;

        case "sign" when args.Length == 2:
            {
                byte[] data = File.ReadAllBytes(args[1]);
                string signature;
                using (var key = LoadKey(keyPath))
                    signature = Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));

                // Gömülü açık anahtarla eşleşmeyen bir anahtarla imzalamak, kullanıcılarda güncellemeyi bozar
                if (!Releases.VerifySignature(data, signature))
                    return Fail("İmza, programlara gömülü açık anahtarla doğrulanamadı. Anahtar ile Shared/Releases.cs uyuşmuyor.");

                File.WriteAllText(args[1] + ".sig", signature);
                Console.WriteLine($"İmzalandı: {args[1]}.sig");
                return 0;
            }

        case "verify" when args.Length == 2:
            {
                bool ok = Releases.VerifySignature(File.ReadAllBytes(args[1]), File.ReadAllText(args[1] + ".sig"));
                Console.WriteLine(ok ? "İmza geçerli." : "İMZA GEÇERSİZ.");
                return ok ? 0 : 1;
            }

        // Updater ve kurulum programının kullandığı yolun aynısı: indirir, imzayı doğrular, ayrıştırır.
        // Adres verilmezse sunucudaki en son sürüm denetlenir.
        case "check":
            {
                if (args.Length == 2) Environment.SetEnvironmentVariable("VULU_UPDATE_FEED", args[1]);
                using var http = Releases.CreateHttpClient(TimeSpan.FromSeconds(30));
                try
                {
                    var latest = await Releases.GetLatestAsync(http);
                    Console.WriteLine($"Doğrulandı: v{latest.Manifest.Version}, {latest.Manifest.Files.Count} dosya, zip {latest.ZipUrl}");
                    return 0;
                }
                catch (ReleaseSignatureException ex)
                {
                    Console.WriteLine("REDDEDİLDİ: " + ex.Message);
                    return 2;
                }
            }

        default:
            return Fail("Kullanım: ReleaseSigner keygen | pubkey | sign <dosya> | verify <dosya> | check [release.json adresi]");
    }
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static ECDsa LoadKey(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"İmzalama anahtarı bulunamadı: {path}\nYedeğini bu konuma geri koy (yeni anahtar üretmek güncellemeleri bozar).");
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(path));
    return key;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
