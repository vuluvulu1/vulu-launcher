using System.Text.Json;

namespace VuluMobile.Services
{
    internal static class JsonDates
    {
        /// <summary>Tarih alanı; sunucu null gönderdiyse (ör. hiç görülmemiş hesap) hata vermeden false.</summary>
        public static bool TryDate(this JsonElement e, out DateTime value)
        {
            value = default;
            return e.ValueKind == JsonValueKind.String && e.TryGetDateTime(out value);
        }
    }
}
