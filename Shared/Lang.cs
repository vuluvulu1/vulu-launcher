using System.Globalization;
using System.Text.Json;

namespace VuluShared
{
    /// <summary>
    /// Arayüz dili. Metinlerin Türkçesi anahtardır (kodda ve axaml'da Türkçe yazılır); İngilizce seçiliyse
    /// i18n/en.json'daki karşılığı gösterilir, karşılığı yoksa Türkçesi kalır. Değişken içeren metinler
    /// <see cref="F"/> ile biçimlenir: "{0} kuruldu" → "{0} installed".
    /// Dil açılışta bir kez seçilir (ayardan ya da sistem dilinden); değişince launcher yeniden başlatılır.
    /// </summary>
    public static class L
    {
        public static string Language { get; private set; } = "tr";
        public static bool IsEnglish => Language == "en";

        /// <summary>Tarih ve sayı biçimi: Türkçede tr-TR, İngilizcede en-US (ay adları, ondalık ayırıcı).</summary>
        public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "tr-TR");

        private static Dictionary<string, string> _en = new(StringComparer.Ordinal);

        /// <summary>"" ya da null: sistem dili Türkçeyse Türkçe, değilse İngilizce.</summary>
        public static string Resolve(string? setting) =>
            setting is "tr" or "en" ? setting
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

        public static void Init(string? setting)
        {
            Language = Resolve(setting);
            _en = Language == "en" ? Load() : new(StringComparer.Ordinal);
        }

        private static Dictionary<string, string> Load()
        {
            try
            {
                using var stream = typeof(L).Assembly.GetManifestResourceStream("i18n.en.json");
                if (stream == null) return new(StringComparer.Ordinal);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
                return new Dictionary<string, string>(map.Where(kv => !string.IsNullOrEmpty(kv.Value)), StringComparer.Ordinal);
            }
            catch { return new(StringComparer.Ordinal); }
        }

        /// <summary>Metnin seçili dildeki hali.</summary>
        public static string T(string text) => TryTranslate(text, out var result) ? result : text;

        /// <summary>Biçimli metin: önce kalıp çevrilir, sonra değerler yerleştirilir.</summary>
        public static string F(string format, params object?[] args) => string.Format(Culture, T(format), args);

        /// <summary>Sözlükte varsa çevirisi; baştaki ve sondaki boşluklar korunur (" · yeni" gibi parçalar).</summary>
        public static bool TryTranslate(string? text, out string result)
        {
            result = text ?? "";
            if (!IsEnglish || string.IsNullOrWhiteSpace(text)) return false;
            if (_en.TryGetValue(text, out var exact)) { result = exact; return true; }
            string trimmed = text.Trim();
            if (trimmed.Length == text.Length || !_en.TryGetValue(trimmed, out var inner)) return false;
            int start = text.IndexOf(trimmed, StringComparison.Ordinal);
            result = text[..start] + inner + text[(start + trimmed.Length)..];
            return true;
        }
    }
}
