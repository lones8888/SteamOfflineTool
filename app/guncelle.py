# -*- coding: utf-8 -*-
"""guncelle.py (02.11.2026): Steam Offline Tool guncelleyicisi. Exe internete cikmaz: surum denetimi, indirme ve
dosyalari degistirme isi burada (Python), Steam dosyalarindaki gibi. Cikti tek satir JSON (yalniz ASCII).

  --kontrol                     GitHub'daki son surum: {"surum": "1.5.0"}
  --indir [--zip <dosya>]       son surumun zip'ini indirir (--zip: indirmek yerine bu dosya, deneme icin), SHA-256'sini
                                GitHub'in verdigi ozetle karsilastirir, %TEMP%\\SteamOfflineTool_guncelleme\\yeni'ye
                                cikarir: {"surum": ..., "kaynak": "<cikarilan SteamOfflineTool klasoru>"}
  --uygula --pid N --hedef <klasor> --kaynak <klasor> [--baslatma]
                                yeni surumun kendi Python'uyla calisir: program kapanana kadar bekler, dosyalari hedefe
                                kopyalar (notlar ve resimler kalir; yarida kalirsa eski hali geri koyar), programi acar.
                                Sonuc hedefteki guncelleme_gunlugu.txt'ye yazilir.
"""
import ctypes
import datetime
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.request
import zipfile

DEPO = "lones8888/SteamOfflineTool"
API = "https://api.github.com/repos/%s/releases/latest" % DEPO
VARLIK = "SteamOfflineTool.zip"
GECICI = os.path.join(os.environ.get("TEMP") or os.path.expanduser("~"), "SteamOfflineTool_guncelleme")
KORUNAN = {"hesap_notlari.txt", "avatarlar", "islem_gunlugu.txt", "guncelleme_gunlugu.txt"}   # kullanicinin dosyalari
GEREKLI = ("SteamOfflineTool.exe", "steam_hesap.py", "guncelle.py", os.path.join("python", "python.exe"))


def _istek(url, sure=20):
    return urllib.request.urlopen(urllib.request.Request(url, headers={
        "User-Agent": "SteamOfflineTool-guncelleyici", "Accept": "application/vnd.github+json"}), timeout=sure)


def son_surum():
    """(surum, zip adresi, boyut, sha256) - GitHub'in son yayinlanan surumu."""
    with _istek(API) as y:
        d = json.loads(y.read().decode("utf-8"))
    surum = (d.get("tag_name") or "").strip().lstrip("vV")
    varlik = next((a for a in d.get("assets") or [] if a.get("name") == VARLIK), None)
    if not surum or varlik is None:
        raise ValueError("son surumde %s yok" % VARLIK)
    ozet = varlik.get("digest") or ""
    return surum, varlik["browser_download_url"], int(varlik.get("size") or 0), ozet[7:].lower() if ozet.startswith("sha256:") else ""


def _sil(yol):
    if os.path.isdir(yol):
        shutil.rmtree(yol, ignore_errors=True)
    elif os.path.exists(yol):
        try:
            os.remove(yol)
        except OSError:
            pass


def indir(yerel_zip=None):
    surum, url, boyut, ozet = son_surum()
    _sil(GECICI)                                    # onceki guncellemeden kalan
    os.makedirs(GECICI, exist_ok=True)
    zip_yolu = os.path.join(GECICI, VARLIK)
    h, toplam = hashlib.sha256(), 0
    with (open(yerel_zip, "rb") if yerel_zip else _istek(url, 60)) as y, open(zip_yolu, "wb") as f:
        while True:
            parca = y.read(1 << 16)
            if not parca:
                break
            h.update(parca)
            f.write(parca)
            toplam += len(parca)
    if not yerel_zip:
        if ozet and h.hexdigest() != ozet:
            raise ValueError("indirilen dosyanin SHA-256'si GitHub'dakiyle tutmuyor")
        if not ozet and boyut and toplam != boyut:
            raise ValueError("indirilen dosya eksik (%d / %d bayt)" % (toplam, boyut))
    yeni = os.path.join(GECICI, "yeni")
    kok = os.path.normcase(os.path.abspath(yeni)) + os.sep
    with zipfile.ZipFile(zip_yolu) as z:
        for ad in z.namelist():                     # zip disina yazan yol olmasin
            if not os.path.normcase(os.path.abspath(os.path.join(yeni, ad))).startswith(kok):
                raise ValueError("zip icinde gecersiz yol: %s" % ad)
        z.extractall(yeni)
    kaynak = os.path.join(yeni, "SteamOfflineTool")
    for g in GEREKLI:
        if not os.path.isfile(os.path.join(kaynak, g)):
            raise ValueError("yeni surumde %s yok" % g)
    return {"surum": surum, "kaynak": kaynak}


def _bekle(pid, saniye=60):
    """Program (pid) kapanana kadar bekler. True: kapandi."""
    k32 = ctypes.windll.kernel32
    h = k32.OpenProcess(0x00100000, False, pid)     # SYNCHRONIZE
    if not h:
        return True                                 # zaten kapanmis
    try:
        return k32.WaitForSingleObject(h, saniye * 1000) == 0
    finally:
        k32.CloseHandle(h)


def _tasi(eski, yeni):
    """Kilit (antivirus, kapanmakta olan program) birkac saniye surebilir: tekrar dener."""
    for _ in range(40):
        try:
            os.replace(eski, yeni)
            return
        except PermissionError:
            time.sleep(0.5)
    os.replace(eski, yeni)


def uygula(pid, hedef, kaynak, baslat):
    if not _bekle(pid):
        raise TimeoutError("program 60 saniyede kapanmadi")
    eskiler, kopyalanan, yeniler = [], [], []
    try:
        for ad in sorted(os.listdir(kaynak)):
            if ad in KORUNAN:
                continue
            k, h = os.path.join(kaynak, ad), os.path.join(hedef, ad)
            if os.path.exists(h):                   # once eskisini kenara al: yarida kalirsa geri konur
                eski = h + ".eski_guncelleme"
                _sil(eski)
                _tasi(h, eski)
                eskiler.append((h, eski))
            else:
                yeniler.append(h)                   # eski surumde yoktu: yarida kalirsa silinir
            if os.path.isdir(k):
                shutil.copytree(k, h)
            else:
                shutil.copy2(k, h)
            kopyalanan.append(ad)
    except Exception:
        for h in yeniler:
            _sil(h)
        for h, eski in reversed(eskiler):
            _sil(h)
            try:
                os.replace(eski, h)
            except OSError:
                pass
        raise
    for _, eski in eskiler:
        _sil(eski)
    return kopyalanan


def _arguman(ad, varsayilan=None):
    if ad in sys.argv:
        i = sys.argv.index(ad)
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    return varsayilan


def _gunluk(hedef, satir):
    try:
        with open(os.path.join(hedef, "guncelleme_gunlugu.txt"), "a", encoding="utf-8") as f:
            f.write(datetime.datetime.now().strftime("%d.%m.%Y %H:%M:%S") + "  " + satir + "\n")
    except OSError:
        pass


def _ac(hedef, arguman):
    exe = os.path.join(hedef, "SteamOfflineTool.exe")
    subprocess.Popen([exe] + arguman, cwd=hedef, close_fds=True, creationflags=0x00000008 | 0x00000200)


def main():
    if "--uygula" in sys.argv:
        hedef, kaynak, baslat = _arguman("--hedef"), _arguman("--kaynak"), "--baslatma" not in sys.argv
        try:
            yeni = uygula(int(_arguman("--pid", "0")), hedef, kaynak, baslat)
            _gunluk(hedef, "TAMAM: guncellendi (%s)" % ", ".join(yeni))
            if baslat:
                _ac(hedef, ["--guncellendi"])
            return 0
        except Exception as e:
            _gunluk(hedef, "HATA: %s: %s (eski surum yerinde)" % (type(e).__name__, e))
            if baslat:
                _ac(hedef, ["--guncelleme-hatasi"])
            return 1
    try:
        if "--kontrol" in sys.argv:
            sonuc = {"surum": son_surum()[0]}
        elif "--indir" in sys.argv:
            sonuc = indir(_arguman("--zip"))
        else:
            print(__doc__)
            return 2
        kod = 0
    except Exception as e:
        sonuc, kod = {"hata": "%s: %s" % (type(e).__name__, e)}, 1
    print(json.dumps(sonuc, ensure_ascii=True))
    return kod


if __name__ == "__main__":
    sys.exit(main())
