"""
Arayüz çevirisi yardımcı aracı.

  python tools/i18n.py missing [klasör...]   çevrilmemiş metinleri tools/i18n-missing.json'a yazar (Türkçe → "")
  python tools/i18n.py merge                 tools/i18n-missing.json'da doldurulan çevirileri Shared/i18n/en.json'a ekler
  python tools/i18n.py dynamic [klasör...]   içine değişken giren ($"...") metinleri listeler (L.F'ye çevrilmeli)

Türkçe metin anahtardır; arayüz İngilizce açılınca UiTranslator sabit metinleri, L.T/L.F kodu çevirir.
"""
import json, os, re, sys, html
sys.stdout.reconfigure(encoding='utf-8'); sys.stderr.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EN = os.path.join(ROOT, 'Shared', 'i18n', 'en.json')
MISSING = os.path.join(ROOT, 'tools', 'i18n-missing.json')
DEFAULT_DIRS = ['Launcher']

ATTR = re.compile(r'\b(?:Text|Content|Header|Watermark|PlaceholderText|ToolTip\.Tip|Title|Label)="([^"{][^"]*)"')
TEXT_NODE = re.compile(r'>([^<>{}]*[A-Za-zçğıöşüÇĞİÖŞÜ][^<>{}]*)<')
CS_STRING = re.compile(r'(?<![$@\w])"((?:[^"\\\n]|\\.)*)"')
CS_INTERP = re.compile(r'\$@?"((?:[^"\\\n]|\\.)*)"')
TURKISH = re.compile(r'[çğıöşüÇĞİÖŞÜ]')
CODEISH = re.compile(r'^(?:[a-z0-9_.:/\\-]+|M[\d.\s,-]|https?://|#[0-9A-Fa-f]{3,8}$|[A-Z][A-Za-z]+\.[A-Za-z.]+$)')


def files(dirs, ext):
    for d in dirs:
        for base, subdirs, names in os.walk(os.path.join(ROOT, d)):
            subdirs[:] = [s for s in subdirs if s not in ('bin', 'obj')]
            for n in names:
                if n.endswith(ext):
                    yield os.path.join(base, n)


def unescape_cs(s):
    return s.encode('utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8') if '\\' in s else s


def visible(s):
    s = s.strip()
    if len(s) < 2 or not re.search(r'[A-Za-zçğıöşüÇĞİÖŞÜ]', s):
        return False
    if CODEISH.match(s):
        return False
    return ' ' in s or bool(TURKISH.search(s)) or s[:1].isupper()


def collect(dirs):
    keys = {}
    for path in files(dirs, '.axaml'):
        text = open(path, encoding='utf-8-sig').read()
        text = re.sub(r'<!--.*?-->', '', text, flags=re.S)
        for m in ATTR.finditer(text):
            v = html.unescape(m.group(1))
            if visible(v):
                keys.setdefault(v.strip(), os.path.relpath(path, ROOT))
        for m in TEXT_NODE.finditer(text):
            v = html.unescape(m.group(1)).strip()
            if visible(v):
                keys.setdefault(v, os.path.relpath(path, ROOT))
    for path in files(dirs, '.cs'):
        for line in open(path, encoding='utf-8-sig'):
            stripped = line.strip()
            if stripped.startswith('//') or stripped.startswith('///') or stripped.startswith('['):
                continue
            code = line.split('//')[0] if '"' not in line.split('//')[0][-1:] else line
            line = CS_INTERP.sub('""', line)   # değişkenli metinler ayrı (dynamic)
            for m in CS_STRING.finditer(line):
                try:
                    v = unescape_cs(m.group(1))
                except Exception:
                    v = m.group(1)
                # Yalnızca Türkçe görünen metinler: Türkçe harf ya da boşluklu cümle
                if visible(v) and (TURKISH.search(v) or (' ' in v.strip() and re.search(r'[a-z]{3}', v))):
                    if any(t in v for t in ('=>', 'MainWindow', '.axaml', 'Segoe', 'Consolas')):
                        continue
                    keys.setdefault(v.strip(), os.path.relpath(path, ROOT))
    return keys


def load_en():
    return json.load(open(EN, encoding='utf-8')) if os.path.exists(EN) else {}


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'missing'
    dirs = sys.argv[2:] or DEFAULT_DIRS
    if cmd == 'missing':
        en = load_en()
        keys = collect(dirs)
        missing = {k: '' for k in sorted(keys) if k not in en}
        json.dump(missing, open(MISSING, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        print(f'{len(keys)} metin, {len(missing)} çevrilmemiş → {os.path.relpath(MISSING, ROOT)}')
    elif cmd == 'merge':
        en = load_en()
        new = json.load(open(MISSING, encoding='utf-8'))
        added = {k: v for k, v in new.items() if v}
        en.update(added)
        json.dump(dict(sorted(en.items())), open(EN, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        left = {k: v for k, v in new.items() if not v}
        json.dump(left, open(MISSING, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        print(f'{len(added)} çeviri eklendi, toplam {len(en)}; {len(left)} kaldı')
    elif cmd == 'apply':
        # python tools/i18n.py apply <dosya> <başlangıç>: dosyadaki her satır i18n-missing.json'daki sıradaki
        # metnin çevirisi ("-" = atla); doldurulanlar en.json'a eklenir
        lines = open(sys.argv[2], encoding='utf-8').read().splitlines()
        start = int(sys.argv[3])
        keys = list(json.load(open(MISSING, encoding='utf-8')))
        en = load_en()
        added = 0
        for i, line in enumerate(lines):
            if not line or line == '-' or start + i >= len(keys):
                continue
            en[keys[start + i]] = line.replace('\\n', '\n')
            added += 1
        json.dump(dict(sorted(en.items())), open(EN, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        print(f'{added} çeviri eklendi, toplam {len(en)}')
    elif cmd == 'dynamic':
        n = 0
        for path in files(dirs, '.cs'):
            for i, line in enumerate(open(path, encoding='utf-8-sig'), 1):
                if line.strip().startswith('//'):
                    continue
                for m in CS_INTERP.finditer(line):
                    if TURKISH.search(m.group(1)) or re.search(r'[a-z]{3} [a-z]{3}', m.group(1)):
                        n += 1
                        print(f'{os.path.relpath(path, ROOT)}:{i}: {m.group(0)[:110]}')
        print(f'{n} değişkenli metin', file=sys.stderr)


if __name__ == '__main__':
    main()
