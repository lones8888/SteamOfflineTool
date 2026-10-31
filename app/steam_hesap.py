# -*- coding: utf-8 -*-
"""steam_hesap.py (31.10.2026): Steam hesap dosyalarini okuyan / yazan motor. STEAM_BASLAT.bat ve Steam Offline Tool
(SteamOfflineTool.exe) bunu cagirir: Steam dosyalarina exe degil Python dokunur. Neden: Kaspersky, Steam giris
dosyalarini okuyan imzasiz exe'yi "PDM:Trojan.Win32.Generic" diye karantinaya aliyor, python.exe'ye ise guveniyor.

1) Cevrimdisi acilis ayari: Steam\\config\\loginusers.vdf icinde WantsOfflineMode ve SkipOfflineModeWarning
   (1 = cevrimdisi ac ve sorma, 0 = normal). Secilmeyen hesaplara ve diger satirlara dokunmaz. Degisiklik varsa
   once tarihli yedek alir. Steam KAPALIYKEN calistirilir. Istenen hesap dosyada yoksa 6 ile cikar.
2) Hesap basina Steam ayarlari (Cloud kapali, Remote Play kapali, oyunlar yalnizca baslatilinca guncellenir):
   denetler, Steam kapaliyken duzeltir. Dosyanin ilk hali bir kez .yedek_steambaslat olarak saklanir.

Kullanim (bat): python steam_hesap.py <Steam klasoru> [--hepsi | --hesaplar a,b,c] [--deger 1|0] [--dene]
  --hepsi     : butun hesaplar
  --hesaplar  : virgulle hesap adlari (vermezsen asagidaki HESAPLAR listesi)
  --deger     : 1 = cevrimdisi + sorma (varsayilan), 0 = normal
  --dene      : sadece goster, dosyaya yazma
Exe icin (cikti JSON, yalniz ASCII):
  --liste                                          hesaplar ve ayar sorunlari
  --ac --hesap "<dosyadaki tam ad>" --deger 1|0 [--duzelt]
                                                   modu yazar, AutoLoginUser yapar, istenirse ayarlari duzeltir
  --duzelt [--hesaplar a,b]                        ayarlari duzeltir (hesap verilmezse butun hesaplar)
"""
import datetime
import json
import os
import re
import shutil
import sys

HESAPLAR = []   # --hesaplar verilmezse bunlar (bos: --hesaplar a,b ya da --hepsi gerekir)
ANAHTARLAR = ("WantsOfflineMode", "SkipOfflineModeWarning")
STEAMID_TABANI = 76561197960265728     # userdata klasor adi = SteamID64 - bu

# ad (istenen durum), sorun (yanlissa), genel (config.vdf: butun hesaplarda ortak), dosya, anahtar yolu, istenen
KURALLAR = (
    (u"Steam Cloud kapalı", u"Steam Cloud açık", False, os.path.join("7", "remote", "sharedconfig.vdf"),
     ("UserRoamingConfigStore", "Software", "Valve", "Steam", "CloudEnabled"), "0"),
    (u"Remote Play kapalı", u"Remote Play açık", False, os.path.join("config", "localconfig.vdf"),
     ("UserLocalConfigStore", "streaming_v2", "EnableStreaming"), "0"),
    (u"Oyunlar yalnızca başlatılınca güncellenir", u"Oyun güncelleme zamanı farklı", True, os.path.join("config", "config.vdf"),
     ("InstallConfigStore", "Software", "Valve", "Steam", "GlobalDefaultAppUpdateBehavior"), "1"),
)


class BicimHatasi(Exception):
    pass


def steam_klasoru():
    """Kayit defterinden Steam klasoru (HKCU SteamPath, sonra HKLM InstallPath), yoksa varsayilan."""
    try:
        import winreg
        for kok, anahtar, deger in ((winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath"),
                                   (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")):
            try:
                with winreg.OpenKey(kok, anahtar) as k:
                    v = winreg.QueryValueEx(k, deger)[0]
                    if v:
                        return os.path.normpath(v.replace("/", "\\"))
            except OSError:
                pass
    except ImportError:
        pass
    return r"C:\Program Files (x86)\Steam"


def _atomik_yaz(yol, veri):
    gecici = yol + ".yaziliyor"
    with open(gecici, "wb") as fh:
        fh.write(veri)
    os.replace(gecici, yol)


# ------------------------------------------------------------------ loginusers.vdf

def _oku(steam):
    yol = os.path.join(steam, "config", "loginusers.vdf")
    if not os.path.isfile(yol):
        raise FileNotFoundError(yol)
    metin = open(yol, "rb").read().decode("utf-8")
    nl = "\r\n" if "\r\n" in metin else "\n"
    satirlar = metin.split(nl)
    derinlik, bas, bloklar = 0, None, []
    for i, s in enumerate(satirlar):
        t = s.strip()
        if t == "{":
            derinlik += 1
            if derinlik == 2:
                bas = i
        elif t == "}":
            if derinlik == 2:
                bloklar.append((bas, i))
            derinlik -= 1
    if derinlik != 0 or not bloklar:
        raise BicimHatasi("loginusers.vdf beklenen bicimde degil")
    return yol, nl, satirlar, bloklar


def _alan(satirlar, bas, son, ad):
    r = re.compile(r'^\s*"' + re.escape(ad) + r'"\s+"([^"]*)"\s*$', re.IGNORECASE)
    for i in range(bas + 1, son):
        m = r.match(satirlar[i])
        if m:
            return m.group(1)
    return None


def hesaplari_oku(steam):
    """[{ad, ham, persona, wants, skip, zaman, no}] - ham: dosyadaki tam ad (bastaki bosluk dahil, AutoLoginUser'a
    bu yazilir); wants/skip: '1', '0' ya da None (satir yok); no: userdata klasor numarasi (0 = bilinmiyor)."""
    _, _, satirlar, bloklar = _oku(steam)
    sonuc = []
    for bas, son in bloklar:
        ad = _alan(satirlar, bas, son, "AccountName")
        if ad is None:
            continue
        zaman = _alan(satirlar, bas, son, "Timestamp")
        m = re.match(r'^\s*"(\d+)"\s*$', satirlar[bas - 1]) if bas > 0 else None
        no = int(m.group(1)) - STEAMID_TABANI if m and int(m.group(1)) > STEAMID_TABANI else 0
        sonuc.append({"ad": ad.strip(), "ham": ad, "persona": _alan(satirlar, bas, son, "PersonaName") or "",
                      "wants": _alan(satirlar, bas, son, "WantsOfflineMode"),
                      "skip": _alan(satirlar, bas, son, "SkipOfflineModeWarning"),
                      "zaman": int(zaman) if zaman and zaman.isdigit() else 0, "no": no})
    return sonuc


def ayarla(steam, hesaplar=None, deger="1", dene=False):
    """hesaplar: ad listesi (None = hepsi). Donus: (bulunan, eksik, degisen, yedek_yolu ya da None)."""
    if deger not in ("0", "1"):
        raise ValueError("deger 0 ya da 1 olmali")
    yol, nl, satirlar, bloklar = _oku(steam)
    hedef = None if hesaplar is None else {h.strip().lower() for h in hesaplar if h.strip()}
    bulunan, degisen = [], 0
    for bas, son in reversed(bloklar):              # sondan basa: eklenen satirlar kaymasin
        ad = _alan(satirlar, bas, son, "AccountName")
        if ad is None or (hedef is not None and ad.strip().lower() not in hedef):
            continue
        bulunan.append(ad.strip())
        girinti = (re.match(r"^(\s*)", satirlar[bas + 1]).group(1) if bas + 1 < son else "") or "\t\t"
        gorulen = set()
        for i in range(bas + 1, son):
            m = re.match(r'^(\s*)"(WantsOfflineMode|SkipOfflineModeWarning)"(\s+)"([^"]*)"\s*$', satirlar[i], re.IGNORECASE)
            if m:
                gorulen.add(m.group(2).lower())
                if m.group(4) != deger:
                    satirlar[i] = f'{m.group(1)}"{m.group(2)}"{m.group(3)}"{deger}"'
                    degisen += 1
        ekle = [f'{girinti}"{k}"\t\t"{deger}"' for k in ANAHTARLAR if k.lower() not in gorulen]
        if ekle:
            satirlar[son:son] = ekle
            degisen += len(ekle)
    eksik = [] if hedef is None else sorted(hedef - {b.lower() for b in bulunan})
    yedek = None
    if degisen and not dene:
        yedek = yol + ".yedek_" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        n = 2
        while os.path.exists(yedek):               # ayni saniyede ikinci calistirma yedegin ustune yazmasin
            yedek = yol + ".yedek_" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S") + f"_{n}"
            n += 1
        shutil.copy2(yol, yedek)
        _atomik_yaz(yol, nl.join(satirlar).encode("utf-8"))
    return sorted(bulunan, key=str.lower), eksik, degisen, yedek


def _oto_giris_yaz(ham):
    """HKCU\\Software\\Valve\\Steam\\AutoLoginUser: Steam bu hesapla acilir (tam ad, bastaki bosluk dahil)."""
    import winreg
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
        winreg.SetValueEx(k, "AutoLoginUser", 0, winreg.REG_SZ, ham)


# ------------------------------------------------------------------ Valve KeyValues metni (config.vdf, localconfig.vdf ...)
# Metni agaca ayirir ama degisikligi metnin icinde yapar: latin-1 ile her bayt tek karakter, dosyanin geri kalani
# bayt bayt aynen yazilir (UTF-8 adlar bozulmaz).

_KV_SIMGE = re.compile(r'\s+|//[^\n]*|[{}]|"[^"\\]*(?:\\.[^"\\]*)*"|[^\s{}"]+', re.S)


class _Dugum(object):
    __slots__ = ("anahtar", "blok", "bas", "son", "kapanis", "cocuklar")

    def __init__(self, anahtar, blok, bas=-1, son=-1):
        self.anahtar, self.blok, self.bas, self.son = anahtar, blok, bas, son      # deger: tirnak ici [bas, son)
        self.kapanis = -1                                                          # blok: '}' konumu
        self.cocuklar = []


def _coz(t):
    if "\\" not in t:
        return t
    cikti, i = [], 0
    while i < len(t):
        c = t[i]
        if c == "\\" and i + 1 < len(t):
            i += 1
            c = t[i]
            cikti.append("\n" if c == "n" else "\t" if c == "t" else c)
        else:
            cikti.append(c)
        i += 1
    return "".join(cikti)


def _kv_ayristir(s):
    kok = _Dugum(None, True)
    yigin = [kok]
    anahtar = None
    konum, n = 0, len(s)
    while konum < n:
        m = _KV_SIMGE.match(s, konum)
        if not m:
            raise BicimHatasi("kapanmayan tirnak (konum %d)" % konum)
        bas, konum = m.start(), m.end()
        c = s[bas]
        if c.isspace() or s.startswith("//", bas):
            continue
        if c == "{":
            if anahtar is None:
                raise BicimHatasi("anahtarsiz blok (konum %d)" % bas)
            b = _Dugum(anahtar, True)
            yigin[-1].cocuklar.append(b)
            yigin.append(b)
            anahtar = None
            continue
        if c == "}":
            if anahtar is not None or len(yigin) == 1:
                raise BicimHatasi("beklenmeyen } (konum %d)" % bas)
            yigin.pop().kapanis = bas
            continue
        if c == '"':
            ib, ison = bas + 1, konum - 1
        elif c == "[":
            continue                                    # [$WIN32] gibi kosul: yok say
        else:
            ib, ison = bas, konum
        if anahtar is None:
            anahtar = _coz(s[ib:ison])
        else:
            yigin[-1].cocuklar.append(_Dugum(anahtar, False, ib, ison))
            anahtar = None
    if len(yigin) != 1 or anahtar is not None:
        raise BicimHatasi("eksik }")
    return kok


class KvMetin(object):
    def __init__(self, metin):
        self.metin = metin
        self._kok = _kv_ayristir(metin)

    @classmethod
    def oku(cls, yol):
        with open(yol, "rb") as fh:
            return cls(fh.read().decode("latin-1"))

    @staticmethod
    def _cocuk(d, ad):
        ad = ad.lower()
        for c in d.cocuklar:
            if c.anahtar.lower() == ad:
                return c
        return None

    def deger(self, yol):
        d = self._kok
        for a in yol:
            if d is None or not d.blok:
                return None
            d = self._cocuk(d, a)
        return None if d is None or d.blok else self.metin[d.bas:d.son]

    def ayarla(self, yol, deger):
        """Degeri yazar; anahtar ya da ara bloklar yoksa ekler. Degisiklik olduysa True."""
        d, i = self._kok, 0
        while i < len(yol) - 1:
            c = self._cocuk(d, yol[i])
            if c is None:
                break
            if not c.blok:
                raise BicimHatasi('"%s" blok degil' % yol[i])
            d = c
            i += 1
        if i == len(yol) - 1:
            v = self._cocuk(d, yol[i])
            if v is not None:
                if v.blok:
                    raise BicimHatasi('"%s" deger degil' % yol[i])
                if self.metin[v.bas:v.son] == deger:
                    return False
                self.metin = self.metin[:v.bas] + deger + self.metin[v.son:]
                self._kok = _kv_ayristir(self.metin)
                return True
        if d is self._kok:
            raise BicimHatasi('"%s" blogu yok' % yol[0])
        # eksik kismi, blogun kapanis satirindan hemen once Steam'in bicimiyle (sekmeyle girintili) ekle
        nl = "\r\n" if "\r\n" in self.metin else "\n"
        satir_bas = self.metin.rfind("\n", 0, d.kapanis) + 1
        onu = self.metin[satir_bas:d.kapanis]
        ayri_satir = onu.strip() == ""
        g = (onu if ayri_satir else "") + "\t"
        parca = [] if ayri_satir else [nl]
        for k in range(i, len(yol) - 1):
            parca += [g, '"', yol[k], '"', nl, g, "{", nl]
            g += "\t"
        parca += [g, '"', yol[-1], '"\t\t"', deger, '"', nl]
        for k in range(len(yol) - 2, i - 1, -1):
            g = g[:-1]
            parca += [g, "}", nl]
        yer = satir_bas if ayri_satir else d.kapanis
        self.metin = self.metin[:yer] + "".join(parca) + self.metin[yer:]
        self._kok = _kv_ayristir(self.metin)
        return True


# ------------------------------------------------------------------ hesap basina Steam ayarlari

def _kural_dosyasi(steam, no, kural):
    return os.path.join(steam, kural[3]) if kural[2] else os.path.join(steam, "userdata", str(no), kural[3])


def _hesap_var(steam, no):
    return bool(no) and os.path.isdir(os.path.join(steam, "userdata", str(no)))


def ayar_sorunlari(steam, no):
    """no None: ortak kurallar (config.vdf tek sefer okunur; giris anahtarlari da bu dosyada oldugu icin gereksiz yere
    tekrar okunmaz). Hesap numarasi verilirse yalniz o hesabin kendi kurallari. Yanlis olan kurallari dondurur;
    None: bu hesapla bu bilgisayarda Steam hic acilmamis (userdata klasoru yok)."""
    if no is not None and not _hesap_var(steam, no):
        return None
    sorunlar = []
    for kural in KURALLAR:
        if kural[2] != (no is None):
            continue
        yol = _kural_dosyasi(steam, no, kural)
        if os.path.isfile(yol) and KvMetin.oku(yol).deger(kural[4]) != kural[5]:
            sorunlar.append(kural)
    return sorunlar


def ayar_duzelt(steam, no):
    """Steam KAPALIYKEN (no None: ortak kurallar). Yanlis degerleri yazar; duzeltilen kurallarin adlarini dondurur."""
    duzelen = []
    if no is not None and not _hesap_var(steam, no):
        return duzelen
    for kural in KURALLAR:
        if kural[2] != (no is None):
            continue
        yol = _kural_dosyasi(steam, no, kural)
        if not os.path.isfile(yol):
            continue
        kv = KvMetin.oku(yol)
        if not kv.ayarla(kural[4], kural[5]):
            continue
        if kv.deger(kural[4]) != kural[5]:
            raise BicimHatasi(os.path.basename(yol) + " yazilamadi")
        yedek = yol + ".yedek_steambaslat"
        if not os.path.exists(yedek):
            shutil.copy2(yol, yedek)
        _atomik_yaz(yol, kv.metin.encode("latin-1"))
        duzelen.append(kural[0])
    return duzelen


# ------------------------------------------------------------------ exe icin komutlar (JSON)

def liste(steam):
    sonuc = {"hesaplar": [], "ortak_hata": None}
    try:
        ortak = ayar_sorunlari(steam, None)
    except Exception as e:
        ortak, sonuc["ortak_hata"] = [], "config.vdf okunamadi: %s" % e
    for h in hesaplari_oku(steam):
        h["ayar_hata"] = None
        try:
            s = ayar_sorunlari(steam, h["no"])
            h["sorunlar"] = None if s is None else [k[1] for k in s + ortak]
        except Exception as e:
            h["sorunlar"], h["ayar_hata"] = None, "Ayar dosyasi okunamadi: %s" % e
        sonuc["hesaplar"].append(h)
    return sonuc


def ac(steam, ham, deger, duzelt):
    bulunan = ayarla(steam, [ham], deger)[0]
    if not bulunan:
        return {"tamam": False, "hata": "%s hesap dosyasinda yok" % ham.strip()}
    duzeltilen, hatalar = [], []
    if duzelt:
        no = next((h["no"] for h in hesaplari_oku(steam) if h["ad"].lower() == ham.strip().lower()), 0)
        for n in (None, no):
            try:
                duzeltilen += ayar_duzelt(steam, n)
            except Exception as e:
                hatalar.append(str(e))
    _oto_giris_yaz(ham)
    return {"tamam": True, "duzeltilen": duzeltilen, "hatalar": hatalar}


def duzelt(steam, hesaplar=None):
    sonuc = {"duzeltilen": [], "hatalar": []}
    try:
        d = ayar_duzelt(steam, None)
        if d:
            sonuc["duzeltilen"].append({"hesap": u"tüm hesaplar", "kurallar": d})
    except Exception as e:
        sonuc["hatalar"].append("config.vdf: %s" % e)
    hedef = None if hesaplar is None else {h.strip().lower() for h in hesaplar if h.strip()}
    for h in hesaplari_oku(steam):
        if hedef is not None and h["ad"].lower() not in hedef:
            continue
        try:
            d = ayar_duzelt(steam, h["no"])
            if d:
                sonuc["duzeltilen"].append({"hesap": h["ad"], "kurallar": d})
        except Exception as e:
            sonuc["hatalar"].append("%s: %s" % (h["ad"], e))
    return sonuc


def _arguman(ad, varsayilan=None):
    if ad in sys.argv:
        i = sys.argv.index(ad)
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    return varsayilan


def _json_komutu(steam):
    """--liste / --ac / --duzelt: sonucu JSON (yalniz ASCII) olarak yazar. None: JSON komutu degil."""
    try:
        if "--liste" in sys.argv:
            sonuc, kod = liste(steam), 0
        elif "--ac" in sys.argv:
            deger = _arguman("--deger", "1")
            if deger not in ("0", "1") or _arguman("--hesap") is None:
                raise ValueError("--hesap ve --deger 0|1 gerekli")
            sonuc = ac(steam, _arguman("--hesap"), deger, "--duzelt" in sys.argv)
            kod = 0 if sonuc["tamam"] else 6
        elif "--duzelt" in sys.argv:
            liste_arg = _arguman("--hesaplar")
            sonuc, kod = duzelt(steam, liste_arg.split(",") if liste_arg else None), 0
        else:
            return None
    except Exception as e:
        sonuc, kod = {"hata": "%s: %s" % (type(e).__name__, e)}, 1
    print(json.dumps(sonuc, ensure_ascii=True))
    return kod


def main():
    if len(sys.argv) < 2 or sys.argv[1].startswith("--"):
        print(__doc__)
        return 2
    steam = sys.argv[1]
    kod = _json_komutu(steam)
    if kod is not None:
        return kod
    dene, hepsi = "--dene" in sys.argv, "--hepsi" in sys.argv
    deger = _arguman("--deger", "1")
    if deger not in ("0", "1"):
        print("HATA: --deger 0 ya da 1 olmali.")
        return 2
    liste_arg = _arguman("--hesaplar")
    hesaplar = None if hepsi else (liste_arg.split(",") if liste_arg else HESAPLAR)
    if hesaplar is not None and not hesaplar:
        print("HATA: --hesaplar a,b ya da --hepsi ver.")
        return 2
    try:
        bulunan, eksik, degisen, yedek = ayarla(steam, hesaplar, deger, dene)
    except FileNotFoundError as e:
        print(f"NOT: {e} yok, hesap ayari yapilmadi.")
        return 3
    except PermissionError as e:
        print(f"HATA: Yazma izni yok: {e.filename or e}")
        print("Hesap ayari degistirilmedi.")
        return 5
    except BicimHatasi as e:
        print(f"UYARI: {e}, dokunulmadi.")
        return 4
    durum = "cevrimdisi ac + sorma" if deger == "1" else "normal (cevrimdisi acilis kapali)"
    for b in bulunan:
        print(f"  {b}: {durum}")
    for e in eksik:
        print(f"  UYARI: {e} hesabi dosyada yok (once bu hesapla bir kez Steam'e girilmeli).")
    if not bulunan:
        print("HATA: istenen hesap dosyada yok, hicbir sey yazilmadi.")
        return 6
    if degisen == 0:
        print("Degisiklik gerekmedi: ayarlar zaten istenen degerde.")
    elif dene:
        print(f"DENEME: {degisen} deger degisecekti, dosyaya YAZILMADI.")
    else:
        print(f"TAMAM: {len(bulunan)} hesap, {degisen} deger yazildi. Yedek: {yedek}")
    return 6 if eksik else 0


if __name__ == "__main__":
    sys.exit(main())
