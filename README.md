# Steam Offline Tool

Open Steam with **the account you choose**, in **offline or online mode**, from one small window — no Steam "Switch Account" screen (that one always signs in online).

Made for playing single-player games offline without Steam starting big updates, and for people who use several Steam accounts on one PC.

> **[Türkçe açıklama aşağıda ↓](#türkçe)**

## Features

- **Pick an account → Open Offline / Open Online.** If Steam is running, it is closed first (with your OK) and started again in the chosen mode.
- **Desktop shortcut per account** – right-click an account → *Desktop shortcut: open offline/online*. One double-click opens Steam with that account, without the window.
- **Tray quick switch** – right-click the tray icon → *Open offline / Open online* → pick an account. An account with a note is shown there by its note.
- **Notes** next to each account (stored only on your PC).
- **Settings check** (✔ / ✖ column): Steam Cloud off, Remote Play off, games update only when launched. Wrong settings are fixed while Steam is closed (optional checkbox).
- **Windows Update off / back to default** (Chris Titus WinUtil "Disable Updates" steps, asks for administrator permission).
- **English / Türkçe** interface (English by default).
- No installer, no internet, no account passwords: it only edits Steam's own local files while Steam is closed.

## Download and run

Direct download (always the latest version):

```text
https://github.com/lones8888/SteamOfflineTool/releases/latest/download/SteamOfflineTool.zip
```

1. Download **SteamOfflineTool.zip** (link above, or from **[Releases](../../releases/latest)**).
2. Extract it, for example to your **Desktop** (do not run it from inside the zip).
3. Run **SteamOfflineTool.exe** from the extracted folder.
4. Optional: click **Desktop shortcut** in the window, and tick **Start with Windows** if you want it in the tray at startup.

### One-line install (PowerShell)

Open a normal PowerShell window (administrator is not needed) and paste:

```powershell
irm https://raw.githubusercontent.com/lones8888/SteamOfflineTool/main/install.ps1 | iex
```

It downloads the latest release, extracts it to `Desktop\SteamOfflineTool` (your notes are kept when updating) and starts the program. Run the same line again to update.
Some antivirus products dislike `irm … | iex`; if yours blocks it, use the zip above.

## How it works

Steam remembers, per account, whether to start offline (`WantsOfflineMode` in `Steam\config\loginusers.vdf`) and which account to sign in automatically (`AutoLoginUser` in the registry). Steam writes these back when it exits, so the tool sets them **while Steam is closed** and then starts Steam normally.

For offline mode the account must have signed in once on this PC with **Remember me**. Games with DRM such as Denuvo may still need to go online from time to time.

## Antivirus notes

- The window (`SteamOfflineTool.exe`) never touches Steam's files. All Steam file reading/writing is done by `steam_hesap.py` with the bundled, **PSF-signed** embedded Python. Some antivirus products flag unsigned programs that read Steam login files; this split avoids that.
- There is **no installer** on purpose: unsigned installers that copy programs and add themselves to startup are often flagged. Just extract and run.
- Nothing is sent anywhere; the program has no network code.

## Settings column (✔ / ✖)

| Setting | Why |
|---|---|
| Steam Cloud off | Avoids cloud sync conflicts when you play offline. **Your saves are then not backed up to the cloud** – untick *Fix Steam settings if wrong* if you want Cloud on. |
| Remote Play off | Not needed for offline play. |
| Games update only when launched | Steam does not download updates in the background. |

## Requirements

Windows 10 / 11 with .NET Framework 4.6.2 or newer (built in). Python is bundled in the release zip.

## Build from source

`src\derle.bat` builds `app\SteamOfflineTool.exe` with the C# compiler that ships with Windows (.NET Framework 4, C# 5, no download). For running from source, put the `python` folder from the release zip next to it, or install Python 3.

---

## Türkçe

Steam'i **seçtiğin hesapla**, **çevrimdışı ya da çevrimiçi** açar. Steam'in "Hesap Değiştir" ekranı her zaman çevrimiçi giriş yaptığı için bu pencere kullanılır.

**Özellikler:** hesap seç → Çevrimdışı Aç / Çevrimiçi Aç; hesaba sağ tık → o hesabı doğrudan açan **masaüstü kısayolu**; saat yanındaki simgeye sağ tık → hesap listesinden **hızlı açma** (not yazdığın hesap orada notuyla görünür); hesap notları; Steam ayar denetimi (Cloud kapalı, Remote Play kapalı, oyunlar yalnızca başlatılınca güncellenir); Windows Update kapat / varsayılana döndür; **İngilizce / Türkçe** arayüz (pencerenin altından seçilir).

**İndirme linki (her zaman son sürüm):**

```text
https://github.com/lones8888/SteamOfflineTool/releases/latest/download/SteamOfflineTool.zip
```

**Kullanım:** zip'i indir → masaüstüne çıkar → klasördeki **SteamOfflineTool.exe**'yi çalıştır. Kurulum programı yok (imzasız kurulumları antivirüsler yanlışlıkla engelliyor). İstersen penceredeki **Masaüstüne kısayol** düğmesine bas, **Windows açılınca başlat**'ı işaretle.

**Tek satırla kurulum:** normal PowerShell aç (yönetici gerekmez) ve yapıştır:

```powershell
irm https://raw.githubusercontent.com/lones8888/SteamOfflineTool/main/install.ps1 | iex
```

Son sürümü masaüstündeki `SteamOfflineTool` klasörüne çıkarır ve açar (notların korunur); güncellemek için aynı satırı tekrar çalıştır.

**Not:** "Steam ayarları yanlışsa düzelt" açıkken Steam Cloud kapatılır, oyun kayıtların buluta yedeklenmez. Çevrimdışı açılış için hesapla bu bilgisayarda bir kez "Beni hatırla" işaretli giriş yapılmış olmalı.

## License

MIT – see [LICENSE](LICENSE). The release zip includes the official embedded Python distribution (PSF License, `python\LICENSE.txt`). Windows Update steps follow Chris Titus Tech's WinUtil.
