using System.Collections.ObjectModel;

namespace VuluLauncher.Views
{
    /// <summary>
    /// Görünümün süzülmüş listesini hedef sıraya en az değişiklikle getirir: duran öğelerin kartları (ve seçim, kaydırma)
    /// yerinde kalır; temizleyip baştan doldurmanın titremesi ve kart yeniden kurma maliyeti olmaz.
    /// </summary>
    internal static class ListSync
    {
        public static void SyncTo<T>(this ObservableCollection<T> list, IReadOnlyList<T> target)
        {
            var keep = new HashSet<T>(target);
            for (int i = list.Count - 1; i >= 0; i--)
                if (!keep.Contains(list[i])) list.RemoveAt(i);
            for (int i = 0; i < target.Count; i++)
            {
                if (i < list.Count && EqualityComparer<T>.Default.Equals(list[i], target[i])) continue;
                int at = list.IndexOf(target[i]);
                if (at >= 0) list.Move(at, i);
                else list.Insert(i, target[i]);
            }
        }
    }
}
