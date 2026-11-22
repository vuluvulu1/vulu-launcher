"""
Değişkenli Türkçe metinleri ($"...{x}...") L.F("...{0}...", x) biçimine çevirir; böylece kalıp çevrilebilir.
  python tools/i18n_interp.py <klasör...>        değişiklikleri yazar ve sayısını söyler
Günlük satırları ("[ui] ..." gibi köşeli parantezle başlayanlar) ve PerfProbe dokunulmadan kalır.
"""
import os, re, sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TURKISH = re.compile(r'[çğıöşüÇĞİÖŞÜ]')
WORDS = re.compile(r'\b(ve|bir|bu|için|ile|olan|kişi|gün|önce|sürüm|paket|dosya|silinsin|kaldırılsın|mı|mi|var|yok)\b')
SKIP_FILES = {'PerfProbe.cs'}


def parse_interp(s, i):
    """s[i] == '$' ve s[i+1] == '"'. (bitiş, kalıp, ifadeler) ya da None döner."""
    j = i + 2
    fmt, exprs = [], []
    while j < len(s):
        c = s[j]
        if c == '\\':
            fmt.append(s[j:j + 2]); j += 2; continue
        if c == '"':
            return j + 1, ''.join(fmt), exprs
        if c == '{':
            if s[j + 1:j + 2] == '{':
                fmt.append('{{'); j += 2; continue
            # delik: ifade [,hizalama] [:biçim]
            k = j + 1
            depth = 0
            expr_end = None
            spec = ''
            while k < len(s):
                ch = s[k]
                if ch == '"':
                    k += 1
                    while k < len(s) and s[k] != '"':
                        k += 2 if s[k] == '\\' else 1
                    k += 1; continue
                if ch == "'":
                    k += 1
                    while k < len(s) and s[k] != "'":
                        k += 2 if s[k] == '\\' else 1
                    k += 1; continue
                if ch in '([':
                    depth += 1
                elif ch in ')]':
                    depth -= 1
                elif depth == 0 and ch in ',:' and expr_end is None:
                    # ?: üçlüsü parantezsiz olamaz; ?. ve ?? için ':' değil
                    expr_end = k
                elif depth == 0 and ch == '}':
                    if expr_end is None:
                        expr_end = k
                    else:
                        spec = s[expr_end:k]
                    break
                k += 1
            else:
                return None
            expr = s[j + 1:expr_end].strip()
            if not expr:
                return None
            fmt.append('{' + str(len(exprs)) + spec + '}')
            exprs.append(expr)
            j = k + 1
            continue
        if c == '}':
            if s[j + 1:j + 2] == '}':
                fmt.append('}}'); j += 2; continue
            return None
        fmt.append(c); j += 1
    return None


def convert_line(line):
    out, i, changed = [], 0, 0
    while i < len(line):
        if line.startswith('$"', i) and (i == 0 or line[i - 1] not in '@$'):
            r = parse_interp(line, i)
            if r:
                end, fmt, exprs = r
                text = re.sub(r'\{\d+[^}]*\}', '', fmt)
                if exprs and (TURKISH.search(text) or WORDS.search(text) or re.search(r'[a-zçğıöşü]{3,} [a-zçğıöşü]{2,}', text)) and not fmt.lstrip().startswith('['):
                    out.append('L.F("' + fmt + '", ' + ', '.join(exprs) + ')')
                    i = end; changed += 1; continue
        out.append(line[i]); i += 1
    return ''.join(out), changed


def main():
    total = 0
    for d in sys.argv[1:]:
        for base, subdirs, names in os.walk(os.path.join(ROOT, d)):
            subdirs[:] = [x for x in subdirs if x not in ('bin', 'obj')]
            for n in names:
                if not n.endswith('.cs') or n in SKIP_FILES:
                    continue
                path = os.path.join(base, n)
                raw = open(path, encoding='utf-8-sig', newline='').read()
                bom = open(path, 'rb').read(3) == b'\xef\xbb\xbf'
                lines = raw.split('\n')
                count = 0
                for idx, line in enumerate(lines):
                    if line.strip().startswith('//'):
                        continue
                    new, c = convert_line(line)
                    if c:
                        lines[idx] = new; count += c
                if count:
                    open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write('\n'.join(lines))
                    print(f'{os.path.relpath(path, ROOT)}: {count}')
                    total += count
    print(f'toplam {total}')


if __name__ == '__main__':
    main()
