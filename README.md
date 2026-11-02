# Steam Offline Tool

Open Steam with **the account you choose**, in **offline or online mode**, from one small window — no Steam "Switch Account" screen (that one always signs in online).

Made for playing single-player games offline without Steam starting big updates, and for people who use several Steam accounts on one PC.

> **Note:** Steam's **“Ask which account to use each time Steam starts”** (*Settings → Interface*) must stay off, otherwise Steam shows its account chooser instead of opening the account you picked. **The tool turns it off automatically** every time it opens an account.

> **[Türkçe açıklama aşağıda ↓](#türkçe)**

## Features

- **Account cards with the Steam profile picture**, dark theme (light theme button at the top right), compact window. Accounts without a picture show their initial.
- **Click an account → "Open account: Offline / Online" appears at the bottom.** If Steam is running, it is closed first (with your OK) and started again in the chosen mode.
- **Desktop shortcut per account** – right-click an account → *Desktop shortcut: open offline/online*. One double-click opens Steam with that account, without the window. The shortcut's icon is the account's picture (gray dot = offline, green dot = online).
- **Tray quick switch** – right-click the tray icon → *Open offline / Open online* → pick an account (with pictures). An account with a note is shown there by its note.
- **Notes** on each account card – click the card's bottom line or right-click → *Write note* (stored only on your PC).
- **Settings check** (✔ / ✖ mark on each card): Steam Cloud off, Remote Play off, games update only when launched, account chooser off. Wrong settings are always fixed while Steam is closed.
- **Status buttons:** Steam at the top (green running, gray closed; click it to close Steam); Windows Update at the bottom right (green off, orange partly off, gray on).
- **Windows Update off / back to default** – click the Windows Update button to turn updates off, ↻ restores the default (Chris Titus WinUtil "Disable Updates" steps, asks for administrator permission).
- **Shortcut button** at the bottom: desktop shortcut for the program itself or the selected account (offline / online).
- **Update check** – at startup the tool asks GitHub once for the latest version. If there is a newer one, the version at the top right turns yellow; click it and the tool downloads it, closes, installs it into its own folder and opens again (notes and settings are kept).
- **English / Türkçe** interface (English by default, the language button at the top right switches). Help texts are on the ⓘ next to the title. The window size and position are remembered.
- No installer, no account passwords: it only edits Steam's own local files while Steam is closed.

## Download and run

Direct download (always the latest version):

```text
https://github.com/lones8888/SteamOfflineTool/releases/latest/download/SteamOfflineTool.zip
```

1. Download **SteamOfflineTool.zip** (link above, or from **[Releases](../../releases/latest)**).
2. Extract it, for example to your **Desktop** (do not run it from inside the zip).
3. Run **SteamOfflineTool.exe** from the extracted folder.
4. Optional: click **Shortcut** in the window for a desktop shortcut, and click the **Start with Windows** button (green = on) if you want it in the tray at startup.

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

Profile pictures come from Steam's own local cache (`Steam\config\avatarcache\<SteamID64>.png`). `steam_hesap.py` copies them to the `avatarlar` folder next to the exe, so the window still never touches Steam's files.

Updates are done by `guncelle.py` (also Python): it reads the latest release from the GitHub API, downloads `SteamOfflineTool.zip`, checks its SHA-256 against the one GitHub reports, waits for the tool to close and copies the new files over the old ones. Your notes (`hesap_notlari.txt`), pictures and settings stay; if something fails halfway, the old version is put back.

## Antivirus notes

- The window (`SteamOfflineTool.exe`) never touches Steam's files. All Steam file reading/writing is done by `steam_hesap.py` with the bundled, **PSF-signed** embedded Python. Some antivirus products flag unsigned programs that read Steam login files; this split avoids that.
- There is **no installer** on purpose: unsigned installers that copy programs and add themselves to startup are often flagged. Just extract and run.
- The window itself has no network code. The only internet use is `guncelle.py` checking GitHub for a new version at startup (and downloading it when you click update); nothing about you or your accounts is sent.

## Settings mark (✔ / ✖)

| Setting | Why |
|---|---|
| Steam Cloud off | Avoids cloud sync conflicts when you play offline. **Your saves are then not backed up to the cloud.** |
| Remote Play off | Not needed for offline play. |
| Games update only when launched | Steam does not download updates in the background. |
| Account chooser off | Steam's *Ask which account to use each time Steam starts*. If it is on, Steam shows its account chooser instead of the account you picked, so the tool always turns it off when it opens an account (even with the checkbox unticked). |

## Requirements

Windows 10 / 11 with .NET Framework 4.6.2 or newer (built in). Python is bundled in the release zip.

## Build from source

`src\derle.bat` builds `app\SteamOfflineTool.exe` with the C# compiler that ships with Windows (.NET Framework 4, C# 5, no download). For running from source, put the `python` folder from the release zip next to it, or install Python 3.

---

## Türkçe

Steam'i **seçtiğin hesapla**, **çevrimdışı ya da çevrimiçi** açar. Steam'in "Hesap Değiştir" ekranı her zaman çevrimiçi giriş yaptığı için bu pencere kullanılır.

> **Not:** Steam'in **“Steam her başlatıldığında hangi hesabın kullanılacağını sor”** ayarı (*Ayarlar → Arayüz*) kapalı olmalı; açıksa Steam seçtiğin hesap yerine hesap seçme ekranını açar. **Program her hesap açışında bunu kendisi kapatır.**

**Özellikler:** hesaplar kart kart, **Steam profil resmiyle** (koyu tema, küçük pencere; resmi olmayan hesapta baş harf); hesaba tıkla → altta "Hesabı aç: Offline / Online"; alttaki Kısayol → programın ya da seçili hesabın masaüstü kısayolu; üstteki Steam düğmesine bas → Steam'i kapat; hesaba sağ tık → o hesabı doğrudan açan **masaüstü kısayolu** (simgesi hesabın resmi: gri nokta çevrimdışı, yeşil nokta çevrimiçi); saat yanındaki simgeye sağ tık → hesap listesinden **hızlı açma** (not yazdığın hesap orada notuyla görünür); hesap notları (kartın alt satırına tıkla ya da sağ tık → Not yaz); Steam ayarları (Cloud kapalı, Remote Play kapalı, oyunlar yalnızca başlatılınca güncellenir, hesap seçme ekranı kapalı) Steam kapalıyken her zaman düzeltilir; üstte Steam ışığı (yeşil açık, gri kapalı); sağ altta Windows Update ışığı (yeşil kapalı, turuncu kısmen kapalı, gri açık; basınca kapatır, ↻ varsayılana döndürür); açık / koyu tema düğmesi; **İngilizce / Türkçe** arayüz (pencerenin sağ üstünden seçilir); açıklamalar başlığın yanındaki ⓘ'de; pencere boyutu hatırlanır.

**Güncelleme:** program açılınca GitHub'da yeni sürüm var mı bir kez bakar. Varsa sağ üstteki sürüm yazısı sarı olur; tıklayınca yeni sürümü indirir, kapanır, kendi klasörüne kurar ve yeniden açılır. Notların ve ayarların korunur.

**İndirme linki (her zaman son sürüm):**

```text
https://github.com/lones8888/SteamOfflineTool/releases/latest/download/SteamOfflineTool.zip
```

**Kullanım:** zip'i indir → masaüstüne çıkar → klasördeki **SteamOfflineTool.exe**'yi çalıştır. Kurulum programı yok (imzasız kurulumları antivirüsler yanlışlıkla engelliyor). İstersen penceredeki **Kısayol** düğmesine bas, **Windows açılınca başlat** düğmesine bas (yeşil = açık).

**Tek satırla kurulum:** normal PowerShell aç (yönetici gerekmez) ve yapıştır:

```powershell
irm https://raw.githubusercontent.com/lones8888/SteamOfflineTool/main/install.ps1 | iex
```

Son sürümü masaüstündeki `SteamOfflineTool` klasörüne çıkarır ve açar (notların korunur); güncellemek için aynı satırı tekrar çalıştır.

**Not:** Steam Cloud her zaman kapatılır, oyun kayıtların buluta yedeklenmez. Çevrimdışı açılış için hesapla bu bilgisayarda bir kez "Beni hatırla" işaretli giriş yapılmış olmalı.

## License

MIT – see [LICENSE](LICENSE). The release zip includes the official embedded Python distribution (PSF License, `python\LICENSE.txt`). Windows Update steps follow Chris Titus Tech's WinUtil.
