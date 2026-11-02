// SteamOfflineTool.cs (31.10.2026): Steam Offline Tool - STEAM_BASLAT.bat'in pencereli (exe) hali.
// - Steam'i SECILEN hesapla ve SECILEN modda (cevrimdisi / cevrimici) acar; hesaplarin yanina not yazilir
//   (exe'nin yanindaki hesap_notlari.txt).
// - Steam dosyalarina exe DOKUNMAZ: okuma ve yazma isini yanindaki steam_hesap.py (Python) yapar. Neden: Kaspersky,
//   Steam giris dosyalarini (loginusers.vdf, config.vdf) okuyan imzasiz exe'yi "PDM:Trojan.Win32.Generic" diye
//   karantinaya aliyor; python.exe'ye guveniyor (bat + py halinde hic sorun cikmadi).
// - Neden mod yazilir: Steam kapanirken o anki modunu loginusers.vdf'e (WantsOfflineMode) yazar ve sonraki acilista
//   o moda doner. Bu yuzden secilen hesabin degeri Steam KAPALIYKEN, acmadan hemen once yazilir, hesap otomatik giris
//   hesabi yapilir (AutoLoginUser) ve Steam normal acilir. Steam'in "Hesap Degistir" ekrani her zaman cevrimici giris
//   yapar, kullanilmaz.
// - Her hesabin Steam ayarlarini gosterir (Cloud kapali, Remote Play kapali, oyunlar yalnizca baslatilinca
//   guncellenir): dogruysa yesil tik, degilse Steam kapaliyken Python'a duzelttirir.
// - Windows Update'i kapatir / varsayilana dondurur (Chris Titus WinUtil "Disable Updates" adimlari); bu, ayni exe'nin
//   yonetici izniyle (UAC) acilan ayri kopyasinda calisir, Steam hicbir zaman yonetici olarak acilmaz. Windows'un
//   onarim servisi (WaaSMedicSvc, SYSTEM) BITS ve wuauserv'i birkac dakika sonra geri acabiliyor (31.10.2026'da
//   9 dk sonra acti); politika (NoAutoUpdate) ve UsoSvc kapali kalir, pencere bu durumu "KISMEN KAPALI" gosterir.
// - Kucultunce gorev cubugundan cekilip saat yanindaki simgelere (tepsi) iner.
// - Kurulum yok: zip'i cikar, exe'yi calistir (kendini kopyalayip baslangica ekleyen kurulumu Kaspersky virus sandi).
// - 1.3 (31.10.2026): arayuz dili Ingilizce (varsayilan) / Turkce; hesaba sag tik -> masaustu kisayolu
//   (SteamOfflineTool.exe --offline "<hesap>" / --online "<hesap>": pencere acmadan Steam'i o hesapla acar);
//   tepsi simgesine sag tik -> hesap listesinden hizli cevrimdisi / cevrimici acma.
// - 1.3.1 (31.10.2026): tepsi menusunde notu yazilan hesap notuyla gorunur; not yoksa hesap (profil adi).
// - 1.3.2 (31.10.2026): pencerenin en ustunde not.
// - 1.3.3 (31.10.2026): not duzeltildi: Steam'in "Ask which account to use on startup" tiki (hesap secme ekrani,
//   Ayarlar > Arayuz) kapali olmali; aciksa Steam secilen hesap yerine hesap secme ekranini acar.
// - 1.3.4 (31.10.2026): bu ayari (config.vdf AlwaysShowUserChooser) motor her hesap acilisinda kendisi kapatir;
//   ayarlar sutununda da gorunur ("Hesap secme ekrani acik").
// - 1.4 (02.11.2026): hesaplar tablo yerine kart kart (TcNo Account Switcher gibi): Steam profil resmi, profil adi,
//   hesap, not; koyu tema, daha kucuk pencere. Profil resmini steam_hesap.py Steam'in onbelleginden
//   (config\avatarcache\<SteamID64>.png) exe'nin yanindaki avatarlar klasorune kopyalar (exe Steam klasorune yine
//   dokunmaz); resmi olmayan hesapta bas harf. Hesap kisayollarinin simgesi o hesabin resmi (sag altta son mod
//   noktasi: cevrimdisi gri, cevrimici yesil). Not, kartin alt satirina tiklayinca (ya da sag tik > Not yaz) yazilir.
// Derleme: derle.bat (Windows'taki .NET Framework 4 derleyicisi, indirme gerekmez). Dil C# 5 ile sinirli.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Steam Offline Tool")]
[assembly: AssemblyDescription("Opens Steam with the selected account in offline or online mode")]
[assembly: AssemblyProduct("Steam Offline Tool")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

namespace SteamOfflineTool
{
    public static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int surec);

        [STAThread]
        static int Main(string[] args)
        {
            DpiAyarla();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new KoyuMenuCizici();     // 1.4: sag tik ve tepsi menuleri koyu
            string islem = args.FirstOrDefault(a => Yonetici.Islemler.Contains(a));
            if (islem != null)
            {
                Dil.Turkce = args.Contains("--tr");     // yonetici kopyasi ana pencerenin dilini argumanla alir
                return Yonetici.Calistir(islem, args.Contains("--dene"), Arguman(args, "--log"), Arguman(args, "--canli"));
            }
            // masaustu kisayolu: pencere acmadan Steam'i o hesapla acar (tek pencere kuralina takilmaz)
            if (args.Contains("--offline")) return KisayolAc.Calistir(Arguman(args, "--offline"), "1");
            if (args.Contains("--online")) return KisayolAc.Calistir(Arguman(args, "--online"), "0");
            bool ilk;
            using (new Mutex(true, @"Local\SteamOfflineTool.TekPencere", out ilk))
            using (var goster = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SteamOfflineTool.Goster"))
            {
                // ikinci kez acilirsa yeni pencere acmaz: acik olani (tepside gizli olsa da) one getirir
                if (!ilk)
                {
                    AllowSetForegroundWindow(-1);
                    goster.Set();
                    return 0;
                }
                var pencere = new AnaPencere(false);
                var dinleyici = new Thread(() =>
                {
                    try
                    {
                        while (goster.WaitOne())
                            if (pencere.IsHandleCreated) pencere.BeginInvoke(new Action(pencere.PencereyiGoster));
                    }
                    catch (Exception) { }       // pencere kapandi
                }) { IsBackground = true };
                dinleyici.Start();
                Application.Run(pencere);
            }
            return 0;
        }

        public static void DpiAyarla()
        {
            try { SetProcessDPIAware(); } catch (Exception) { }
        }

        static string Arguman(string[] args, string ad)
        {
            int i = Array.IndexOf(args, ad);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }

    // ------------------------------------------------------------------ yardimcilar

    public static class Kayit
    {
        public static readonly RegistryKey Makine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        public static object Oku(RegistryKey kok, string yol, string ad)
        {
            try
            {
                using (RegistryKey k = kok.OpenSubKey(yol))
                    return k == null ? null : k.GetValue(ad);
            }
            catch (Exception) { return null; }
        }
    }

    public static class Dosya
    {
        public static void AtomikYaz(string yol, byte[] veri)
        {
            string gecici = yol + ".yaziliyor";
            File.WriteAllBytes(gecici, veri);
            try
            {
                if (File.Exists(yol)) File.Replace(gecici, yol, null, true);
                else File.Move(gecici, yol);
            }
            finally
            {
                if (File.Exists(gecici)) File.Delete(gecici);
            }
        }
    }

    public static class Olcek
    {
        public static float Al()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }
    }

    public static class UygulamaAyari
    {
        const string Yol = @"Software\SteamOfflineTool";

        public static bool Oku(string ad, bool varsayilan)
        {
            object v = Kayit.Oku(Registry.CurrentUser, Yol, ad);
            return v is int ? (int)v != 0 : varsayilan;
        }

        public static void Yaz(string ad, bool deger)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Yol))
                k.SetValue(ad, deger ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    // ------------------------------------------------------------------ dil: varsayilan Ingilizce, pencereden Turkce secilir

    public static class Dil
    {
        public static bool Turkce = UygulamaAyari.Oku("Turkce", false);

        public static string T(string en, string tr) { return Turkce ? tr : en; }

        // steam_hesap.py Turkce kural adlari dondurur; motor degismesin diye Ingilizcesi burada
        static readonly Dictionary<string, string> MotorIngilizce = new Dictionary<string, string>
        {
            { "Steam Cloud kapalı", "Steam Cloud off" }, { "Steam Cloud açık", "Steam Cloud on" },
            { "Remote Play kapalı", "Remote Play off" }, { "Remote Play açık", "Remote Play on" },
            { "Oyunlar yalnızca başlatılınca güncellenir", "Games update only when launched" },
            { "Oyun güncelleme zamanı farklı", "Game update timing is different" },
            { "Hesap seçme ekranı kapalı", "Account chooser off" }, { "Hesap seçme ekranı açık", "Account chooser on" },
            { "tüm hesaplar", "all accounts" }
        };

        public static string Motor(string s)
        {
            string en;
            return !Turkce && s != null && MotorIngilizce.TryGetValue(s, out en) ? en : s;
        }
    }

    // ------------------------------------------------------------------ masaustu kisayolu (pencere acmadan)

    // SteamOfflineTool.exe --offline "<hesap>" / --online "<hesap>": Steam aciksa sorup kapatir, modu yazar, Steam'i acar.
    public static class KisayolAc
    {
        const string Baslik = "Steam Offline Tool";

        public static int Calistir(string ad, string deger)
        {
            string mod = deger == "1" ? Dil.T("offline", "çevrimdışı") : Dil.T("online", "çevrimiçi");
            try
            {
                if (string.IsNullOrWhiteSpace(ad)) throw new Exception(Dil.T("The shortcut has no account name.", "Kısayolda hesap adı yok."));
                string steam = Steam.Klasor();
                string ortakHata;
                Hesap h = Motor.Liste(steam, out ortakHata).FirstOrDefault(x => x.Ad.Equals(ad.Trim(), StringComparison.OrdinalIgnoreCase));
                if (h == null) throw new Exception(Dil.T("Account not found on this PC: ", "Hesap bu bilgisayarda bulunamadı: ") + ad.Trim());
                if (Steam.Acik())
                {
                    if (MessageBox.Show(Dil.T("Steam is running. It must close first, otherwise it writes the old mode back when it exits.\n\nClose Steam and open ",
                                              "Steam açık. Ayar için Steam kapanmalı, yoksa kapanırken eski modu geri yazar.\n\nSteam kapatılıp ")
                                        + h.Ad + " " + mod + Dil.T("?", " açılsın mı?"), Baslik, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return 1;
                    bool kapandi = Steam.Kapat(steam, 30);
                    if (!kapandi && MessageBox.Show(Dil.T("Steam did not close in 30 seconds. Force close it?\n(Steam may not save its last settings.)",
                                                          "Steam 30 saniyede kapanmadı. Zorla kapatılsın mı?\n(Steam son ayarlarını kaydedemeyebilir.)"),
                                                    Baslik, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        kapandi = Steam.ZorlaKapat();
                    if (!kapandi) throw new Exception(Dil.T("Steam did not close. Nothing was changed.", "Steam kapanmadı. Hiçbir şey değişmedi."));
                }
                Motor.Ac(steam, h.HamAd, deger, UygulamaAyari.Oku("AyarlariDuzelt", true));
                Steam.Baslat(steam);
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show(Dil.T("ERROR: ", "HATA: ") + e.Message, Baslik, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // masaustune "<hesap> - Offline.lnk" / "<hesap> - Online.lnk" (Turkcede Cevrimdisi / Cevrimici).
        // 1.4: simgesi hesabin profil resmi (avatarlar klasorunde .ico); yazilamazsa programin simgesi.
        public static string Olustur(Hesap h, string deger)
        {
            string mod = deger == "1" ? Dil.T("Offline", "Çevrimdışı") : Dil.T("Online", "Çevrimiçi");
            string ad = h.Ad + " - " + mod;
            foreach (char c in Path.GetInvalidFileNameChars()) ad = ad.Replace(c, '_');
            string simge = null;
            try { simge = HesapResmi.KisayolSimgesi(h, deger == "1", ad); } catch (Exception) { }
            Yaz(Path.Combine(Masaustu, ad + ".lnk"), (deger == "1" ? "--offline " : "--online ") + "\"" + h.Ad + "\"",
                "Steam Offline Tool: " + h.Ad + " (" + mod + ")", simge);
            return ad;
        }

        // programin kendisi icin masaustu kisayolu (kurulum programi yok: Kaspersky kuruluma takiliyor)
        public static void UygulamaKisayolu()
        {
            Yaz(Path.Combine(Masaustu, "Steam Offline Tool.lnk"), "", "Steam Offline Tool", null);
        }

        static string Masaustu { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }

        static void Yaz(string yol, string argumanlar, string aciklama, string simge)
        {
            string exe = Application.ExecutablePath;
            dynamic kabuk = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic lnk = kabuk.CreateShortcut(yol);
            lnk.TargetPath = exe;
            lnk.Arguments = argumanlar;
            lnk.WorkingDirectory = Path.GetDirectoryName(exe);
            lnk.IconLocation = (simge ?? exe) + ",0";
            lnk.Description = aciklama;
            lnk.Save();
        }
    }

    // ------------------------------------------------------------------ Steam (surec ve kayit defteri; dosyalara dokunmaz)

    public static class Steam
    {
        public static string Klasor()
        {
            string v = Kayit.Oku(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath") as string;
            if (string.IsNullOrEmpty(v)) v = Kayit.Oku(Kayit.Makine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") as string;
            if (string.IsNullOrEmpty(v)) v = @"C:\Program Files (x86)\Steam";
            try { return Path.GetFullPath(v.Replace('/', '\\')); }
            catch (Exception) { return v; }
        }

        public static string Exe(string klasor) { return Path.Combine(klasor, "steam.exe"); }

        public static bool Acik()
        {
            Process[] p = Process.GetProcessesByName("steam");
            foreach (Process x in p) x.Dispose();
            return p.Length > 0;
        }

        // Steam'in o an girisli hesabi (userdata numarasi), 0 = yok
        public static uint AktifHesapNo()
        {
            object v = Kayit.Oku(Registry.CurrentUser, @"Software\Valve\Steam\ActiveProcess", "ActiveUser");
            return v is int ? unchecked((uint)(int)v) : 0u;
        }

        public static string SonHesap()
        {
            return Kayit.Oku(Registry.CurrentUser, @"Software\Valve\Steam", "AutoLoginUser") as string;
        }

        public static void Baslat(string klasor) { Calistir(klasor, ""); }

        static void Calistir(string klasor, string argumanlar)
        {
            using (Process.Start(new ProcessStartInfo(Exe(klasor), argumanlar) { WorkingDirectory = klasor, UseShellExecute = true })) { }
        }

        // -shutdown ile kapatir, en fazla 'saniye' bekler; kapandiysa Steam dosyalarini birakmasi icin 2 sn daha bekler
        public static bool Kapat(string klasor, int saniye)
        {
            if (!Acik()) return true;
            Calistir(klasor, "-shutdown");
            for (int i = 0; i < saniye; i++)
            {
                Thread.Sleep(1000);
                if (!Acik()) { Thread.Sleep(2000); return true; }
            }
            return false;
        }

        public static bool ZorlaKapat()
        {
            foreach (string ad in new[] { "steam", "steamwebhelper" })
                foreach (Process p in Process.GetProcessesByName(ad))
                    using (p)
                        try { p.Kill(); } catch (Exception) { }
            for (int i = 0; i < 10 && Acik(); i++) Thread.Sleep(1000);
            if (Acik()) return false;
            Thread.Sleep(1000);
            return true;
        }
    }

    // ------------------------------------------------------------------ hesaplar (Steam dosyalari Python'da)

    public class Hesap
    {
        public string HamAd;            // dosyadaki tam ad: " ornekhesap" gibi bastaki bosluk korunur (AutoLoginUser'a bu yazilir)
        public string Ad;               // bosluksuz
        public string Persona = "";
        public string Wants;            // "1", "0" ya da null (satir yok)
        public long Zaman;              // son giris (unix saniye), 0 = bilinmiyor
        public uint HesapNo;            // userdata klasor adi, 0 = bilinmiyor
        public List<string> Sorunlar;   // yanlis Steam ayarlari; null = bilinmiyor (bu hesapla Steam hic acilmamis)
        public string AyarHata;
        public string Avatar;           // 1.4: avatarlar klasorundeki profil resmi (steam_hesap.py kopyalar), null = yok
    }

    // Steam dosyalarini okuyan/yazan steam_hesap.py'yi (exe'nin yaninda) gorunmez calistirir; cikti tek satir JSON.
    public static class Motor
    {
        public static string Betik
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "steam_hesap.py"); }
        }

        static string python;

        // en yeni Python 3: kayit defteri (PythonCore), %LOCALAPPDATA%\Programs\Python, PATH (Microsoft Store kisayolu haric)
        public static string PythonBul()
        {
            if (python != null && File.Exists(python)) return python;
            string yerel = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "python", "python.exe");
            if (File.Exists(yerel)) { python = yerel; return yerel; }    // paketle gelen gömülü Python: önce bunu kullan
            var adaylar = new List<KeyValuePair<Version, string>>();
            foreach (RegistryKey kok in new[] { Registry.CurrentUser, Kayit.Makine })
                try
                {
                    using (RegistryKey pc = kok.OpenSubKey(@"Software\Python\PythonCore"))
                        if (pc != null)
                            foreach (string surum in pc.GetSubKeyNames())
                                using (RegistryKey ip = pc.OpenSubKey(surum + @"\InstallPath"))
                                {
                                    if (ip == null) continue;
                                    string exe = ip.GetValue("ExecutablePath") as string;
                                    string klasor = ip.GetValue(null) as string;
                                    if (string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(klasor)) exe = Path.Combine(klasor, "python.exe");
                                    Ekle(adaylar, surum, exe);
                                }
                }
                catch (Exception) { }
            string programlar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
            if (Directory.Exists(programlar))
                foreach (string klasor in Directory.GetDirectories(programlar, "Python3*"))
                    Ekle(adaylar, "3." + Path.GetFileName(klasor).Substring(7), Path.Combine(klasor, "python.exe"));
            foreach (string klasor in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try { Ekle(adaylar, "3.0", Path.Combine(klasor.Trim().Trim('"'), "python.exe")); } catch (Exception) { }
            python = adaylar.OrderByDescending(a => a.Key).Select(a => a.Value).FirstOrDefault();
            return python;
        }

        static void Ekle(List<KeyValuePair<Version, string>> adaylar, string surum, string exe)
        {
            Version v;
            if (string.IsNullOrEmpty(exe) || exe.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0 || !File.Exists(exe)) return;
            if (!Version.TryParse(surum.Split('-')[0], out v) || v.Major != 3) return;
            adaylar.Add(new KeyValuePair<Version, string>(v, exe));
        }

        static string Tirnak(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        // "hata" anahtari varsa istisna atar
        static Dictionary<string, object> Calistir(string steam, params string[] argumanlar)
        {
            string py = PythonBul();
            if (py == null) throw new Exception(Dil.T("Python 3 was not found. Reinstall the tool or install Python 3 from python.org.",
                                                      "Python 3 bulunamadı. Programı yeniden kur ya da python.org'dan Python 3 kur."));
            if (!File.Exists(Betik)) throw new Exception(Dil.T("steam_hesap.py not found (it must be next to the exe): ",
                                                               "steam_hesap.py bulunamadı (exe'nin yanında olmalı): ") + Betik);
            var bilgi = new ProcessStartInfo(py, "-I " + Tirnak(Betik) + " " + Tirnak(steam) + " " + string.Join(" ", argumanlar.Select(Tirnak)))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(Betik)
            };
            string cikti, hata;
            using (Process p = Process.Start(bilgi))
            {
                Task<string> hataOku = p.StandardError.ReadToEndAsync();
                cikti = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                hata = hataOku.Result;
            }
            string satir = cikti.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            Dictionary<string, object> sonuc = null;
            try { sonuc = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(satir); }
            catch (Exception) { }
            if (sonuc == null) throw new Exception(Dil.T("steam_hesap.py did not respond. ", "steam_hesap.py yanıt vermedi. ")
                                                   + (hata.Trim().Length > 0 ? hata.Trim() : cikti.Trim()));
            object h;
            if (sonuc.TryGetValue("hata", out h) && h != null) throw new Exception(Metin(h));
            return sonuc;
        }

        public static List<Hesap> Liste(string steam, out string ortakHata)
        {
            Dictionary<string, object> s = Calistir(steam, "--liste");
            ortakHata = Metin(Al(s, "ortak_hata"));
            var liste = new List<Hesap>();
            foreach (object o in Dizi(Al(s, "hesaplar")))
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                liste.Add(new Hesap
                {
                    HamAd = Metin(Al(d, "ham")) ?? "",
                    Ad = Metin(Al(d, "ad")) ?? "",
                    Persona = Metin(Al(d, "persona")) ?? "",
                    Wants = Metin(Al(d, "wants")),
                    Zaman = Sayi(Al(d, "zaman")),
                    HesapNo = (uint)Sayi(Al(d, "no")),
                    Sorunlar = Al(d, "sorunlar") == null ? null : Dizi(Al(d, "sorunlar")).Select(x => Metin(x)).ToList(),
                    AyarHata = Metin(Al(d, "ayar_hata")),
                    Avatar = Metin(Al(d, "avatar"))
                });
            }
            return liste;
        }

        // Steam KAPALIYKEN: modu yazar, hesabi AutoLoginUser yapar, istenirse ayarlari duzeltir. Doner: duzeltme mesaji ya da null
        public static string Ac(string steam, string hamAd, string deger, bool duzelt)
        {
            var a = new List<string> { "--ac", "--hesap", hamAd, "--deger", deger };
            if (duzelt) a.Add("--duzelt");
            Dictionary<string, object> s = Calistir(steam, a.ToArray());
            var parca = new List<string>();
            List<string> d = Dizi(Al(s, "duzeltilen")).Select(x => Dil.Motor(Metin(x))).ToList();
            if (d.Count > 0) parca.Add(Dil.T("Steam settings fixed: ", "Steam ayarları düzeltildi: ") + string.Join(", ", d) + ".");
            List<string> h = Dizi(Al(s, "hatalar")).Select(x => Metin(x)).ToList();
            if (h.Count > 0) parca.Add(Dil.T("Could not fix: ", "Düzeltilemedi: ") + string.Join("; ", h));
            return parca.Count > 0 ? string.Join(" ", parca) : null;
        }

        // Steam KAPALIYKEN butun hesaplarin ayarlarini duzeltir. Doner: mesaj ya da null (degisiklik gerekmedi)
        public static string Duzelt(string steam)
        {
            Dictionary<string, object> s = Calistir(steam, "--duzelt");
            var parca = new List<string>();
            var duzelen = new List<string>();
            foreach (object o in Dizi(Al(s, "duzeltilen")))
            {
                var d = o as Dictionary<string, object>;
                if (d != null) duzelen.Add(Dil.Motor(Metin(Al(d, "hesap"))) + " (" + string.Join(", ", Dizi(Al(d, "kurallar")).Select(x => Dil.Motor(Metin(x)))) + ")");
            }
            if (duzelen.Count > 0) parca.Add(Dil.T("Steam settings fixed: ", "Steam ayarları düzeltildi: ") + string.Join("; ", duzelen) + ".");
            List<string> h = Dizi(Al(s, "hatalar")).Select(x => Metin(x)).ToList();
            if (h.Count > 0) parca.Add(Dil.T("Could not fix: ", "Düzeltilemedi: ") + string.Join("; ", h));
            return parca.Count > 0 ? string.Join(" ", parca) : null;
        }

        static object Al(Dictionary<string, object> d, string ad)
        {
            object o;
            return d != null && d.TryGetValue(ad, out o) ? o : null;
        }

        static string Metin(object o) { return o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture); }

        static long Sayi(object o)
        {
            try { return o == null ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }

        static List<object> Dizi(object o)
        {
            var l = new List<object>();
            var e = o as System.Collections.IEnumerable;
            if (e != null && !(o is string))
                foreach (object x in e) l.Add(x);
            return l;
        }
    }

    // ------------------------------------------------------------------ notlar ve baslangic

    public static class Notlar
    {
        public static string Yol
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "hesap_notlari.txt"); }
        }

        // satir basina: hesap<TAB>not
        public static Dictionary<string, string> Oku()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(Yol))
                    foreach (string satir in File.ReadAllLines(Yol, Encoding.UTF8))
                    {
                        int t = satir.IndexOf('\t');
                        if (t > 0) d[satir.Substring(0, t).Trim()] = satir.Substring(t + 1);
                    }
            }
            catch (Exception) { }
            return d;
        }

        public static void Yaz(Dictionary<string, string> notlar)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> n in notlar.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                if (!string.IsNullOrWhiteSpace(n.Value))
                    sb.Append(n.Key.Trim()).Append('\t').Append(n.Value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim()).Append("\r\n");
            Dosya.AtomikYaz(Yol, new UTF8Encoding(false).GetBytes(sb.ToString()));
        }
    }

    public static class Baslangic
    {
        const string RunYolu = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string OnayYolu = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string Ad = "SteamOfflineTool";

        static string Komut(string exe) { return "\"" + exe + "\""; }

        public static bool Acik()
        {
            string v = Kayit.Oku(Registry.CurrentUser, RunYolu, Ad) as string;
            if (v == null || !string.Equals(v.Trim(), Komut(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase)) return false;
            byte[] onay = Kayit.Oku(Registry.CurrentUser, OnayYolu, Ad) as byte[];
            return onay == null || onay.Length == 0 || (onay[0] & 1) == 0;     // tek sayi (03) = Gorev Yoneticisi'nde devre disi
        }

        public static void Ayarla(bool ac)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunYolu))
                if (ac) k.SetValue(Ad, Komut(Application.ExecutablePath), RegistryValueKind.String);
                else k.DeleteValue(Ad, false);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(OnayYolu, true))
                if (k != null) k.DeleteValue(Ad, false);
        }
    }

    // ------------------------------------------------------------------ Windows Update (Chris Titus WinUtil adimlari)

    public static class Servis
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string makine, string veritabani, uint erisim);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenService(IntPtr scm, string ad, uint erisim);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool ChangeServiceConfig(IntPtr servis, uint tur, uint baslangic, uint hata, string ikili, string grup,
                                               IntPtr etiket, string bagimliliklar, string hesap, string parola, string gorunenAd);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool ChangeServiceConfig2(IntPtr servis, uint seviye, ref GecikmeliBaslatma bilgi);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CloseServiceHandle(IntPtr tutamac);

        [StructLayout(LayoutKind.Sequential)]
        struct GecikmeliBaslatma { public int Gecikmeli; }

        const uint SC_MANAGER_CONNECT = 0x1, SERVICE_QUERY_CONFIG = 0x1, SERVICE_CHANGE_CONFIG = 0x2;
        const uint SERVICE_NO_CHANGE = 0xFFFFFFFF, SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

        // tur: 2 otomatik, 3 el ile, 4 devre disi
        public static void BaslangicAyarla(string ad, int tur, bool gecikmeli)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) throw new Win32Exception();
            try
            {
                IntPtr s = OpenService(scm, ad, SERVICE_CHANGE_CONFIG | SERVICE_QUERY_CONFIG);
                if (s == IntPtr.Zero) throw new Win32Exception();
                try
                {
                    if (!ChangeServiceConfig(s, SERVICE_NO_CHANGE, (uint)tur, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                        throw new Win32Exception();
                    if (tur == 2)
                    {
                        var g = new GecikmeliBaslatma { Gecikmeli = gecikmeli ? 1 : 0 };
                        if (!ChangeServiceConfig2(s, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref g)) throw new Win32Exception();
                    }
                }
                finally { CloseServiceHandle(s); }
            }
            finally { CloseServiceHandle(scm); }
        }

        public static string Durdur(string ad)
        {
            try
            {
                using (var sc = new ServiceController(ad))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) return Dil.T("already stopped", "zaten durmuş");
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    return Dil.T("stopped", "durduruldu");
                }
            }
            catch (Exception e) { return Dil.T("could not stop (", "durdurulamadı (") + e.Message + ")"; }
        }

        public static void Baslat(string ad)
        {
            using (var sc = new ServiceController(ad))
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                }
        }
    }

    public static class Guncelleme
    {
        public static readonly string[] Servisler = { "BITS", "wuauserv", "UsoSvc" };
        const string AuYolu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        const string WuYolu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        const string DoYolu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
        const string ExplorerYolu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        static readonly string[] GorevKlasorleri =
        {
            @"\Microsoft\Windows\InstallService", @"\Microsoft\Windows\UpdateOrchestrator", @"\Microsoft\Windows\UpdateAssistant",
            @"\Microsoft\Windows\WaaSMedic", @"\Microsoft\Windows\WindowsUpdate", @"\Microsoft\WindowsUpdate"
        };

        public static int BaslangicTuru(string servis)
        {
            object v = Kayit.Oku(Kayit.Makine, @"SYSTEM\CurrentControlSet\Services\" + servis, "Start");
            return v is int ? (int)v : -1;
        }

        static string TurAdi(int t)
        {
            switch (t)
            {
                case 0: case 1: return Dil.T("system", "sistem");
                case 2: return Dil.T("automatic", "otomatik");
                case 3: return Dil.T("manual", "el ile");
                case 4: return Dil.T("disabled", "devre dışı");
                default: return Dil.T("missing", "yok");
            }
        }

        // 1 = tamamen kapali, 0 = acik (Windows varsayilani), -1 = kismen. Yonetici izni gerekmez.
        public static int Durum(out string ayrinti)
        {
            object p = Kayit.Oku(Kayit.Makine, AuYolu, "NoAutoUpdate");
            bool politika = p is int && (int)p == 1;
            int kapali = Servisler.Count(s => BaslangicTuru(s) == 4);
            ayrinti = string.Join(", ", Servisler.Select(s => s + ": " + TurAdi(BaslangicTuru(s))))
                    + Dil.T(", automatic update policy: ", ", otomatik güncelleme politikası: ")
                    + (politika ? Dil.T("off", "kapalı") : Dil.T("on", "açık"));
            if (politika && kapali == Servisler.Length) return 1;
            if (!politika && kapali == 0) return 0;
            return -1;
        }

        // WinUtil Invoke-WPFUpdatesdisable. SoftwareDistribution klasorunun silinmesi bilerek yok (gerekmez, geri alinamaz).
        public static bool Kapat(Action<string> log, bool dene)
        {
            bool tamam = true;
            tamam &= Adim(log, dene, Dil.T("Turning off the automatic update policy (NoAutoUpdate=1, AUOptions=1)",
                                           "Otomatik güncelleme politikası kapatılıyor (NoAutoUpdate=1, AUOptions=1)"), () =>
            {
                using (RegistryKey k = Kayit.Makine.CreateSubKey(AuYolu))
                {
                    k.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                    k.SetValue("AUOptions", 1, RegistryValueKind.DWord);
                }
            });
            tamam &= Adim(log, dene, Dil.T("Turning off Delivery Optimization downloads (DODownloadMode=0)",
                                           "Teslim İyileştirme indirmeleri kapatılıyor (DODownloadMode=0)"), () =>
            {
                using (RegistryKey k = Kayit.Makine.CreateSubKey(DoYolu))
                    k.SetValue("DODownloadMode", 0, RegistryValueKind.DWord);
            });
            foreach (string s in Servisler)
            {
                string ad = s;
                tamam &= Adim(log, dene, Dil.T("Stopping and disabling the " + ad + " service", ad + " servisi durdurulup devre dışı bırakılıyor"), () =>
                {
                    log("    " + Servis.Durdur(ad));
                    Servis.BaslangicAyarla(ad, 4, false);
                });
            }
            tamam &= Gorevler(log, false, dene);
            Sonuc(log, dene, tamam, Dil.T("DONE: Windows Update is turned off.", "TAMAM: Windows Update kapatıldı."));
            return tamam;
        }

        // WinUtil Invoke-WPFUpdatesdefault
        public static bool Ac(Action<string> log, bool dene)
        {
            bool tamam = true;
            tamam &= Adim(log, dene, Dil.T("Removing update policies", "Güncelleme politikaları kaldırılıyor"), () =>
            {
                DegerSil(AuYolu, "NoAutoUpdate", "AUOptions", "NoAutoRebootWithLoggedOnUsers", "AUPowerManagement");
                DegerSil(WuYolu, "ExcludeWUDriversInQualityUpdate", "DeferFeatureUpdates", "DeferFeatureUpdatesPeriodInDays",
                         "DeferQualityUpdates", "DeferQualityUpdatesPeriodInDays");
                DegerSil(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "BranchReadinessLevel", "DeferFeatureUpdatesPeriodInDays",
                         "DeferQualityUpdatesPeriodInDays");
                DegerSil(@"SOFTWARE\Policies\Microsoft\Windows\Device Metadata", "PreventDeviceMetadataFromNetwork");
                DegerSil(@"SOFTWARE\Policies\Microsoft\Windows\DriverSearching", "DontPromptForWindowsUpdate", "DontSearchWindowsUpdate",
                         "DriverUpdateWizardWuSearchEnabled");
                DegerSil(DoYolu, "DODownloadMode");
                string sayfa = Kayit.Oku(Kayit.Makine, ExplorerYolu, "SettingsPageVisibility") as string;
                if (sayfa != null && sayfa.Equals("hide:windowsupdate", StringComparison.OrdinalIgnoreCase))
                    DegerSil(ExplorerYolu, "SettingsPageVisibility");
            });
            tamam &= Adim(log, dene, Dil.T("BITS and wuauserv: manual", "BITS ve wuauserv: el ile"), () =>
            {
                Servis.BaslangicAyarla("BITS", 3, false);
                Servis.BaslangicAyarla("wuauserv", 3, false);
            });
            tamam &= Adim(log, dene, Dil.T("UsoSvc: automatic (delayed) and starting", "UsoSvc: otomatik (gecikmeli) ve başlatılıyor"), () =>
            {
                Servis.BaslangicAyarla("UsoSvc", 2, true);
                Servis.Baslat("UsoSvc");
            });
            tamam &= Gorevler(log, true, dene);
            Sonuc(log, dene, tamam, Dil.T("DONE: Windows Update is back to default.", "TAMAM: Windows Update varsayılana döndü."));
            return tamam;
        }

        static void DegerSil(string yol, params string[] adlar)
        {
            using (RegistryKey k = Kayit.Makine.OpenSubKey(yol, true))
                if (k != null)
                    foreach (string a in adlar) k.DeleteValue(a, false);
        }

        static bool Adim(Action<string> log, bool dene, string aciklama, Action is_)
        {
            log("• " + aciklama);
            if (dene) { log(Dil.T("    (test run: not done)", "    (deneme: yapılmadı)")); return true; }
            try { is_(); log(Dil.T("    done", "    tamam")); return true; }
            catch (Exception e) { log(Dil.T("    ERROR: ", "    HATA: ") + e.Message); return false; }
        }

        static bool Gorevler(Action<string> log, bool etkin, bool dene)
        {
            log("• " + (etkin ? Dil.T("Turning on update scheduled tasks", "Güncelleme zamanlanmış görevleri açılıyor")
                              : Dil.T("Turning off update scheduled tasks", "Güncelleme zamanlanmış görevleri kapatılıyor")));
            dynamic ts;
            try
            {
                ts = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                ts.Connect();
            }
            catch (Exception e) { log(Dil.T("    ERROR: could not open Task Scheduler: ", "    HATA: Görev Zamanlayıcı açılamadı: ") + e.Message); return false; }
            bool tamam = true;
            foreach (string yol in GorevKlasorleri)
            {
                object klasor;
                try { klasor = ts.GetFolder(yol); }
                catch (Exception e)
                {
                    bool yok = e is FileNotFoundException || e is DirectoryNotFoundException
                            || e.HResult == unchecked((int)0x80070002) || e.HResult == unchecked((int)0x80070003);
                    log(yok ? "    " + yol + Dil.T(": missing", ": yok")
                            : Dil.T("    ERROR: could not open ", "    HATA: ") + yol + Dil.T(" (", " açılamadı (") + e.Message + ")");
                    if (!yok && !dene) tamam = false;
                    continue;
                }
                int degisen = 0, zaten = 0;
                var korumali = new List<string>();
                GorevKlasoru(klasor, etkin, dene, ref degisen, ref zaten, korumali);
                log(string.Format(Dil.T("    {0}: {1} {2}{3}, {4} already so{5}", "    {0}: {1} {2}{3}, {4} zaten öyle{5}"), yol, degisen,
                    etkin ? Dil.T("turned on", "açıldı") : Dil.T("turned off", "kapatıldı"),
                    dene ? Dil.T(" (test run)", " (deneme)") : "", zaten,
                    korumali.Count > 0 ? ", " + korumali.Count + Dil.T(" protected, Windows does not allow it (", " korumalı, Windows izin vermiyor (")
                                         + string.Join(", ", korumali) + ")" : ""));
            }
            return tamam;   // korumali tek tek gorevler WinUtil'de de sessizce atlanir, onlar hata sayilmaz
        }

        static void Sonuc(Action<string> log, bool dene, bool tamam, string basari)
        {
            log("");
            if (dene) log(Dil.T("TEST RUN finished: nothing was changed.", "DENEME bitti: hiçbir şey değiştirilmedi."));
            else log(tamam ? basari + Dil.T(" Restart the PC so everything settles.", " Hepsinin oturması için bilgisayarı yeniden başlat.")
                           : Dil.T("Some steps could not be done (lines with ERROR).", "Bazı adımlar yapılamadı (HATA yazan satırlar)."));
        }

        static void GorevKlasoru(object k, bool etkin, bool dene, ref int degisen, ref int zaten, List<string> korumali)
        {
            dynamic klasor = k;
            foreach (dynamic g in klasor.GetTasks(1))      // 1 = gizli gorevler dahil
            {
                string ad = g.Name;
                try
                {
                    if ((bool)g.Enabled == etkin) { zaten++; continue; }
                    if (!dene) g.Enabled = etkin;
                    degisen++;
                }
                catch (Exception) { korumali.Add(ad); }
            }
            foreach (dynamic alt in klasor.GetFolders(0))
                GorevKlasoru((object)alt, etkin, dene, ref degisen, ref zaten, korumali);
        }
    }

    // ------------------------------------------------------------------ yonetici izniyle calisan kopya

    public static class Yonetici
    {
        public static readonly string[] Islemler = { "--wu-kapat", "--wu-ac" };

        public static bool YoneticiMi()
        {
            using (WindowsIdentity k = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(k).IsInRole(WindowsBuiltInRole.Administrator);
        }

        // --dene: hicbir sey degistirmez, yapilacaklari yazar. --log <dosya>: pencere yerine dosyaya yazar.
        // --canli <dosya>: her satir aninda bu dosyaya da yazilir; ana pencere hangi adimda olundugunu (ve Kaspersky
        // isi yarida keserse son adimi) buradan gorur. Sonda "SONUÇ:" satiri yoksa is yarida kesilmistir.
        public static int Calistir(string islem, bool dene, string logYolu, string canliYol)
        {
            var sb = new StringBuilder();
            StreamWriter canli = null;
            if (canliYol != null)
                try
                {
                    canli = new StreamWriter(new FileStream(canliYol, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                                             new UTF8Encoding(false)) { AutoFlush = true };
                }
                catch (Exception) { }
            Action<string> log = s =>
            {
                sb.AppendLine(s);
                if (canli != null)
                    try { canli.WriteLine(s); } catch (Exception) { }
            };
            string baslik = islem == "--wu-kapat" ? Dil.T("Turning off Windows Update", "Windows Update kapatılıyor")
                                                  : Dil.T("Returning Windows Update to default", "Windows Update varsayılana döndürülüyor");
            log(baslik + (dene ? Dil.T(" (TEST RUN: nothing is changed)", " (DENEME: hiçbir şey değiştirilmez)") : ""));
            log("");
            bool tamam;
            try
            {
                if (!dene && !YoneticiMi()) { log(Dil.T("ERROR: no administrator permission.", "HATA: Yönetici izni yok.")); tamam = false; }
                else if (islem == "--wu-kapat") tamam = Guncelleme.Kapat(log, dene);
                else tamam = Guncelleme.Ac(log, dene);
            }
            catch (Exception e) { log(Dil.T("ERROR: ", "HATA: ") + e.Message); tamam = false; }
            log(Dil.T("RESULT: ", "SONUÇ: ") + (tamam ? Dil.T("done", "tamam") : Dil.T("some steps could not be done", "bazı adımlar yapılamadı")));
            if (canli != null) canli.Dispose();
            if (logYolu != null) File.WriteAllText(logYolu, sb.ToString(), new UTF8Encoding(false));
            else KayitPenceresi.Goster("Steam Offline Tool - " + baslik, sb.ToString());
            return tamam ? 0 : 1;
        }

        // ayni exe'yi yonetici izniyle (UAC) baslatir. null = kullanici UAC'de Hayir dedi
        public static Process Baslat(string islem, string canliYol)
        {
            string argumanlar = islem + (canliYol != null ? " --canli \"" + canliYol + "\"" : "") + (Dil.Turkce ? " --tr" : "");
            try
            {
                return Process.Start(new ProcessStartInfo(Application.ExecutablePath, argumanlar) { Verb = "runas", UseShellExecute = true });
            }
            catch (Win32Exception e)
            {
                if (e.NativeErrorCode == 1223) return null;
                throw;
            }
        }
    }

    public class KayitPenceresi : Form
    {
        public static void Goster(string baslik, string metin)
        {
            using (var p = new KayitPenceresi(baslik, metin)) p.ShowDialog();
        }

        KayitPenceresi(string baslik, string metin)
        {
            float o = Olcek.Al();
            Text = baslik;
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(780 * o), (int)(460 * o));
            MinimizeBox = false;
            BackColor = Tema.Arka;
            ForeColor = Tema.Metin;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            var kutu = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true,
                Font = new Font("Consolas", 10f), BackColor = Tema.Kart, ForeColor = Tema.Metin, BorderStyle = BorderStyle.None, Text = metin
            };
            kutu.HandleCreated += delegate { Tema.KoyuKaydirma(kutu.Handle); };
            var ic = new Panel { Dock = DockStyle.Fill, Padding = new Padding((int)(12 * o)) };
            ic.Controls.Add(kutu);
            var tamam = new KoyuButon { Text = Dil.T("OK", "Tamam"), DialogResult = DialogResult.OK, AutoSize = true, Ana = true,
                                        MinimumSize = new Size((int)(110 * o), (int)(32 * o)) };
            var alt = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding((int)(8 * o)) };
            alt.Controls.Add(tamam);
            Controls.Add(ic);
            Controls.Add(alt);
            AcceptButton = tamam;
            Shown += delegate { kutu.SelectionLength = 0; tamam.Focus(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.KoyuBaslik(Handle);
        }
    }

    // ------------------------------------------------------------------ koyu tema (1.4)

    public static class Tema
    {
        public static readonly Color Arka = Color.FromArgb(24, 25, 28);             // pencere
        public static readonly Color Kart = Color.FromArgb(34, 36, 41);             // hesap karti, Windows Update karti
        public static readonly Color KartUstunde = Color.FromArgb(43, 46, 52);
        public static readonly Color KartSecili = Color.FromArgb(36, 48, 68);
        public static readonly Color Kenar = Color.FromArgb(55, 59, 66);
        public static readonly Color Metin = Color.FromArgb(232, 234, 237);
        public static readonly Color Soluk = Color.FromArgb(150, 157, 167);         // ikincil yazi
        public static readonly Color Vurgu = Color.FromArgb(56, 132, 255);          // secili kart, ana buton
        public static readonly Color Buton = Color.FromArgb(46, 49, 56);
        public static readonly Color ButonUstunde = Color.FromArgb(57, 61, 69);
        public static readonly Color NotRengi = Color.FromArgb(125, 180, 255);
        public static readonly Color Yesil = Color.FromArgb(72, 199, 116);
        public static readonly Color Kirmizi = Color.FromArgb(244, 99, 99);
        public static readonly Color Turuncu = Color.FromArgb(240, 150, 70);
        public static readonly Color Cevrimdisi = Color.FromArgb(128, 134, 144);    // son mod noktasi

        static float oran;

        // ekran olcegi (SetProcessDPIAware sonrasi)
        public static float Oran
        {
            get
            {
                if (oran <= 0) oran = Olcek.Al();
                return oran;
            }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr pencere, int ozellik, ref int deger, int boyut);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr pencere, string uygulama, string liste);

        // koyu baslik cubugu: Windows 11 / 10 20H1+ (20), eski Windows 10 (19); desteklenmezse hicbir sey olmaz
        public static void KoyuBaslik(IntPtr pencere)
        {
            int bir = 1;
            try
            {
                if (DwmSetWindowAttribute(pencere, 20, ref bir, 4) != 0) DwmSetWindowAttribute(pencere, 19, ref bir, 4);
            }
            catch (Exception) { }
        }

        // koyu kaydirma cubugu (Windows 10 1809+)
        public static void KoyuKaydirma(IntPtr pencere)
        {
            try { SetWindowTheme(pencere, "DarkMode_Explorer", null); } catch (Exception) { }
        }

        public static GraphicsPath Yuvarlak(RectangleF r, float yaricap)
        {
            var yol = new GraphicsPath();
            float d = Math.Min(yaricap * 2f, Math.Min(r.Width, r.Height));
            if (d < 1f)
            {
                yol.AddRectangle(r);
                return yol;
            }
            yol.AddArc(r.X, r.Y, d, d, 180, 90);
            yol.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            yol.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            yol.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            yol.CloseFigure();
            return yol;
        }
    }

    // duz, koyu buton (Ana: mavi). Windows'un acik renkli buton cizimi yerine kendisi cizer.
    public class KoyuButon : Button
    {
        bool ana, ustunde, basili;

        public KoyuButon()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;         // AutoSize olcusu duz stile gore
            FlatAppearance.BorderSize = 1;
            ForeColor = Tema.Metin;
            BackColor = Tema.Buton;
            UseVisualStyleBackColor = false;
        }

        public bool Ana
        {
            get { return ana; }
            set { ana = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { ustunde = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { ustunde = basili = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { basili = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { basili = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Tema.Arka);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color zemin = !Enabled ? Color.FromArgb(36, 38, 43)
                        : ana ? (basili ? Color.FromArgb(38, 104, 214) : ustunde ? Color.FromArgb(82, 150, 255) : Tema.Vurgu)
                        : (basili ? Color.FromArgb(68, 73, 82) : ustunde ? Tema.ButonUstunde : Tema.Buton);
            Color kenar = !Enabled ? Color.FromArgb(46, 49, 55) : Focused && ShowFocusCues ? Color.FromArgb(150, 190, 255) : ana ? zemin : Tema.Kenar;
            using (GraphicsPath yol = Tema.Yuvarlak(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4f * Tema.Oran))
            {
                using (var f = new SolidBrush(zemin)) g.FillPath(f, yol);
                using (var k = new Pen(kenar)) g.DrawPath(k, yol);
            }
            Color yazi = !Enabled ? Color.FromArgb(105, 110, 119) : ana ? Color.White : ForeColor;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, yazi, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // koyu onay kutusu: kutu ve tik kendisi cizilir
    public class KoyuOnay : CheckBox
    {
        public KoyuOnay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ForeColor = Tema.Metin;
        }

        int Kutu { get { return (int)Math.Round(14 * Tema.Oran); } }
        int Bosluk { get { return (int)Math.Round(6 * Tema.Oran); } }

        public override Size GetPreferredSize(Size istenen)
        {
            Size y = TextRenderer.MeasureText(Text, Font);
            return new Size(Kutu + Bosluk + y.Width + 2, Math.Max(Kutu, y.Height) + (int)Math.Round(4 * Tema.Oran));
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Tema.Arka);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int k = Kutu, y = (Height - k) / 2;
            var kutu = new RectangleF(0.5f, y + 0.5f, k - 1f, k - 1f);
            using (GraphicsPath yol = Tema.Yuvarlak(kutu, 3f * Tema.Oran))
            {
                using (var f = new SolidBrush(Checked ? Tema.Vurgu : Tema.Buton)) g.FillPath(f, yol);
                using (var p = new Pen(Checked ? Tema.Vurgu : Focused && ShowFocusCues ? Color.FromArgb(150, 190, 255) : Color.FromArgb(88, 94, 104)))
                    g.DrawPath(p, yol);
            }
            if (Checked)
                using (var p = new Pen(Color.White, Math.Max(1.6f, 1.8f * Tema.Oran)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] { new PointF(k * 0.25f, y + k * 0.52f), new PointF(k * 0.43f, y + k * 0.71f), new PointF(k * 0.76f, y + k * 0.31f) });
            var yazi = new Rectangle(k + Bosluk, 0, Width - k - Bosluk, Height);
            TextRenderer.DrawText(g, Text, Font, yazi, Enabled ? ForeColor : Tema.Soluk, TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                                  | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    // koyu ipucu. ElleGoster: kartlar gibi tek kontrolun icindeki parcalar icin (her kartin kendi ipucu var).
    public class KoyuIpucu : ToolTip
    {
        readonly Font yazi = new Font("Segoe UI", 9f);
        string elle;

        public KoyuIpucu()
        {
            OwnerDraw = true;
            AutoPopDelay = 20000;
            InitialDelay = 500;
            ReshowDelay = 100;
            Popup += (s, e) =>
            {
                string t = elle ?? GetToolTip(e.AssociatedControl) ?? "";
                Size o = TextRenderer.MeasureText(t, yazi, new Size(Olcu(420), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                e.ToolTipSize = new Size(o.Width + Olcu(16), o.Height + Olcu(10));
            };
            Draw += (s, e) =>
            {
                e.Graphics.Clear(Color.FromArgb(40, 43, 49));
                using (var p = new Pen(Tema.Kenar)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, yazi, Rectangle.Inflate(e.Bounds, -Olcu(8), -Olcu(5)), Tema.Metin,
                                      TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
            };
        }

        static int Olcu(float px) { return (int)Math.Round(px * Tema.Oran); }

        public void ElleGoster(string metin, IWin32Window pencere, Point yer)
        {
            if (string.IsNullOrEmpty(metin)) return;
            elle = metin;
            Show(metin, pencere, yer, AutoPopDelay);
        }

        public void ElleGizle(IWin32Window pencere)
        {
            if (elle == null) return;
            elle = null;
            try { Hide(pencere); } catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) yazi.Dispose();
            base.Dispose(disposing);
        }
    }

    public class KoyuMenuRenkleri : ProfessionalColorTable
    {
        static readonly Color Zemin = Color.FromArgb(36, 38, 43);
        static readonly Color Secili = Color.FromArgb(52, 57, 66);
        public override Color ToolStripDropDownBackground { get { return Zemin; } }
        public override Color ImageMarginGradientBegin { get { return Zemin; } }
        public override Color ImageMarginGradientMiddle { get { return Zemin; } }
        public override Color ImageMarginGradientEnd { get { return Zemin; } }
        public override Color MenuBorder { get { return Tema.Kenar; } }
        public override Color MenuItemBorder { get { return Secili; } }
        public override Color MenuItemSelected { get { return Secili; } }
        public override Color MenuItemSelectedGradientBegin { get { return Secili; } }
        public override Color MenuItemSelectedGradientEnd { get { return Secili; } }
        public override Color MenuItemPressedGradientBegin { get { return Secili; } }
        public override Color MenuItemPressedGradientMiddle { get { return Secili; } }
        public override Color MenuItemPressedGradientEnd { get { return Secili; } }
        public override Color SeparatorDark { get { return Tema.Kenar; } }
        public override Color SeparatorLight { get { return Zemin; } }
    }

    public class KoyuMenuCizici : ToolStripProfessionalRenderer
    {
        public KoyuMenuCizici() : base(new KoyuMenuRenkleri()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Tema.Metin : Color.FromArgb(105, 110, 119);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && !e.Item.Enabled ? Color.FromArgb(105, 110, 119) : Tema.Metin;
            base.OnRenderArrow(e);
        }
    }

    // ------------------------------------------------------------------ hesap resimleri (1.4)

    // Profil resmini Steam'in onbelleginden steam_hesap.py kopyalar (exe Steam klasorune dokunmaz); resim yoksa bas harfli
    // renkli kare. Masaustu kisayolunun simgesi (.ico) da buradan yazilir.
    public static class HesapResmi
    {
        public static string Klasor
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "avatarlar"); }
        }

        static readonly Color[] Renkler =
        {
            Color.FromArgb(88, 101, 242), Color.FromArgb(37, 99, 235), Color.FromArgb(13, 148, 136), Color.FromArgb(22, 163, 74),
            Color.FromArgb(217, 119, 6), Color.FromArgb(220, 38, 38), Color.FromArgb(219, 39, 119), Color.FromArgb(124, 58, 237)
        };

        // avatarlar klasorundeki profil resmi (dosyayi acik tutmaz: steam_hesap.py sonra ustune yazabilsin); yoksa null
        public static Bitmap Yukle(Hesap h)
        {
            if (string.IsNullOrEmpty(h.Avatar) || h.Avatar.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            try
            {
                string yol = Path.Combine(Klasor, h.Avatar);
                if (!File.Exists(yol)) return null;
                using (var ms = new MemoryStream(File.ReadAllBytes(yol)))
                using (Image r = Image.FromStream(ms))
                    return new Bitmap(r);
            }
            catch (Exception) { return null; }
        }

        // resmi olmayan hesap: profil adinin (yoksa hesap adinin) bas harfi, renk hesap adindan (hep ayni renk)
        public static Bitmap Harf(Hesap h, int s)
        {
            string ad = h.Persona.Trim().Length > 0 ? h.Persona.Trim() : h.Ad.Trim();
            string harf = ad.Length > 0 ? StringInfo.GetNextTextElement(ad).ToUpper(CultureInfo.CurrentCulture) : "?";
            int iz = 0;
            foreach (char c in h.Ad.ToLowerInvariant()) iz = unchecked(iz * 31 + c);
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            using (var f = new Font("Segoe UI Semibold", s * 0.44f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Renkler[(iz & 0x7fffffff) % Renkler.Length]);
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.DrawString(harf, f, Brushes.White, new RectangleF(0, s * 0.03f, s, s), sf);
            }
            return b;
        }

        // kartta gosterilen resim: s x s, kenarlari yuvarlatilmis
        public static Bitmap Kart(Hesap h, int s)
        {
            using (Bitmap kaynak = Yukle(h) ?? Harf(h, 184))
                return Yuvarlat(kaynak, s, s * 0.16f);
        }

        static Bitmap Yuvarlat(Bitmap kaynak, int s, float yaricap)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var olcekli = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(olcekli))
                using (var ia = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);        // kenarlarda yari saydam cizgi olmasin
                    g.DrawImage(kaynak, new Rectangle(0, 0, s, s), 0, 0, kaynak.Width, kaynak.Height, GraphicsUnit.Pixel, ia);
                }
                using (Graphics g = Graphics.FromImage(b))
                using (var firca = new TextureBrush(olcekli, WrapMode.Clamp))
                using (GraphicsPath yol = Tema.Yuvarlak(new RectangleF(0, 0, s, s), yaricap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.FillPath(firca, yol);
                }
            }
            return b;
        }

        // masaustu kisayolu simgesi: profil resmi + sag altta son mod noktasi (cevrimdisi gri, cevrimici yesil). Dosya adi
        // icerige gore degisir (Windows simge onbellegi eski resmi gostermesin); ayni kisayolun eski simgesi silinir.
        public static string KisayolSimgesi(Hesap h, bool cevrimdisi, string ad)
        {
            byte[] ico;
            using (Bitmap kaynak = Yukle(h) ?? Harf(h, 256))
                ico = Ico(kaynak, cevrimdisi);
            uint iz = 2166136261;
            foreach (byte x in ico) iz = unchecked((iz ^ x) * 16777619);
            Directory.CreateDirectory(Klasor);
            string yol = Path.Combine(Klasor, ad + "." + iz.ToString("x8") + ".ico");
            foreach (string eski in Directory.GetFiles(Klasor, ad + ".*.ico"))
                if (!eski.Equals(yol, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(eski); } catch (Exception) { }
            if (!File.Exists(yol)) File.WriteAllBytes(yol, ico);
            return yol;
        }

        static Bitmap SimgeResmi(Bitmap kaynak, int s, bool cevrimdisi)
        {
            Bitmap b = Yuvarlat(kaynak, s, s * 0.18f);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float d = Math.Max(6f, s * 0.32f), x = s - d, y = s - d, ic = d * 0.66f;
                using (var halka = new SolidBrush(Color.FromArgb(32, 34, 38))) g.FillEllipse(halka, x, y, d, d);
                using (var nokta = new SolidBrush(cevrimdisi ? Tema.Cevrimdisi : Tema.Yesil))
                    g.FillEllipse(nokta, x + (d - ic) / 2f, y + (d - ic) / 2f, ic, ic);
            }
            return b;
        }

        // .ico: 256 piksel PNG, kucukler 32 bit BMP (her Windows surumu okur)
        static byte[] Ico(Bitmap kaynak, bool cevrimdisi)
        {
            int[] boylar = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var parcalar = new List<byte[]>();
            foreach (int s in boylar)
                using (Bitmap b = SimgeResmi(kaynak, s, cevrimdisi))
                    parcalar.Add(s >= 256 ? Png(b) : Dib(b));
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)boylar.Length);
                int konum = 6 + 16 * boylar.Length;
                for (int i = 0; i < boylar.Length; i++)
                {
                    byte boy = (byte)(boylar[i] >= 256 ? 0 : boylar[i]);
                    w.Write(boy);
                    w.Write(boy);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(parcalar[i].Length);
                    w.Write(konum);
                    konum += parcalar[i].Length;
                }
                foreach (byte[] p in parcalar) w.Write(p);
                w.Flush();
                return ms.ToArray();
            }
        }

        static byte[] Png(Bitmap b)
        {
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        // BITMAPINFOHEADER + 32 bit BGRA satirlar (alttan uste) + bos AND maskesi (saydamlik alfa kanalinda)
        static byte[] Dib(Bitmap b)
        {
            int s = b.Width, maske = (s + 31) / 32 * 4 * s;
            var satir = new byte[s * 4];
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40);
                w.Write(s);
                w.Write(s * 2);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(0);
                w.Write(s * s * 4 + maske);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                BitmapData veri = b.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = s - 1; y >= 0; y--)
                    {
                        Marshal.Copy(veri.Scan0 + y * veri.Stride, satir, 0, satir.Length);
                        w.Write(satir);
                    }
                }
                finally { b.UnlockBits(veri); }
                w.Write(new byte[maske]);
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------ hesap kartlari (1.4)

    // Hesaplar kart kart (TcNo Account Switcher gibi): profil resmi, profil adi, hesap, not, ayar isareti (sag ust) ve son mod
    // noktasi (resmin sag alti). Tek kontrol, kartlari kendisi cizer; not, kartin alt satirina tiklayinca yerinde yazilir.
    public class HesapKartlari : Panel
    {
        public const int ResimBoyu = 68;
        const int KartEni = 116, KartBoyu = 142, Aralik = 8;
        static readonly int[] SatirUstu = { 0, 18, 34 };     // isim, hesap, not satirlari (resmin altindan)

        public class Oge
        {
            public Hesap Hesap;
            public Bitmap Resim;
            public string Isim = "", Alt = "", Not = "", Rozet = "", RozetIpucu = "", Ipucu = "";
            public Color RozetRengi;
            public bool SonKullanilan;
            public bool? Cevrimdisi;            // son mod: true cevrimdisi, false cevrimici, null bilinmiyor
        }

        readonly float olcek;
        readonly Font kalin, kucuk, rozetYazisi;
        readonly KoyuIpucu ipucu;
        readonly TextBox notKutusu;
        readonly System.Windows.Forms.Timer ipucuZamani;
        List<Oge> ogeler = new List<Oge>();
        int secili = -1, ustunde = -1, duzenlenen = -1;
        string ipucuAnahtari, bekleyenIpucu;
        Point ipucuYeri;
        public string NotIpucu = "", NotYeri = "";
        public event Action<Hesap, string> NotDegisti;      // not yazildi ya da vazgecildi (yazi degismemis olabilir)

        public HesapKartlari(float olcek, Font kalin, Font kucuk, KoyuIpucu ipucu)
        {
            this.olcek = olcek;
            this.kalin = kalin;
            this.kucuk = kucuk;
            this.ipucu = ipucu;
            rozetYazisi = new Font("Segoe UI Symbol", 10f, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Tema.Arka;
            AutoScroll = true;
            TabStop = true;
            notKutusu = new TextBox { Visible = false, BorderStyle = BorderStyle.FixedSingle, MaxLength = 300,
                                      BackColor = Color.FromArgb(18, 19, 22), ForeColor = Tema.NotRengi, Font = kucuk };
            notKutusu.KeyDown += NotTusu;
            notKutusu.Leave += delegate { NotuBitir(); };
            Controls.Add(notKutusu);
            ipucuZamani = new System.Windows.Forms.Timer { Interval = 550 };
            ipucuZamani.Tick += delegate
            {
                ipucuZamani.Stop();
                if (bekleyenIpucu != null) ipucu.ElleGoster(bekleyenIpucu, this, ipucuYeri);
            };
        }

        int P(float px) { return (int)Math.Round(px * olcek); }

        public Hesap SeciliHesap { get { return secili >= 0 && secili < ogeler.Count ? ogeler[secili].Hesap : null; } }

        public bool NotDuzenleniyor { get { return duzenlenen >= 0; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.KoyuKaydirma(Handle);
        }

        // kartlari yeniler; secAd: secili kalacak hesap (yoksa secim yok)
        public void Doldur(List<Oge> yeni, string secAd)
        {
            NotuBitir();
            foreach (Oge o in ogeler)
                if (o.Resim != null) o.Resim.Dispose();
            ogeler = yeni;
            secili = secAd == null ? -1 : ogeler.FindIndex(o => o.Hesap.Ad.Equals(secAd, StringComparison.OrdinalIgnoreCase));
            ustunde = -1;
            IpucuKapat();
            Yerlestir();
            Invalidate();
            if (secili >= 0) Gorunur(secili);
        }

        public void Sec(string ad)
        {
            int i = ogeler.FindIndex(o => o.Hesap.Ad.Equals(ad, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) SeciliYap(i);
        }

        void SeciliYap(int i)
        {
            if (i == secili) return;
            NotuBitir();
            secili = i;
            Invalidate();
            if (i >= 0) Gorunur(i);
        }

        // ---------------------------------------------------------- yerlesim

        int Sutun { get { return Math.Max(1, (ClientSize.Width - P(4) + P(Aralik)) / (P(KartEni) + P(Aralik))); } }

        // kartlar ortalanir (az hesapta tek sira ortada durur)
        int SolBosluk
        {
            get
            {
                int n = Math.Min(Sutun, Math.Max(1, ogeler.Count));
                return Math.Max(P(2), (ClientSize.Width - (n * P(KartEni) + (n - 1) * P(Aralik))) / 2);
            }
        }

        // istemci koordinatlari (kaydirma dahil)
        Rectangle KartAlani(int i)
        {
            int s = Sutun;
            return new Rectangle(SolBosluk + i % s * (P(KartEni) + P(Aralik)), P(2) + i / s * (P(KartBoyu) + P(Aralik)) + AutoScrollPosition.Y,
                                 P(KartEni), P(KartBoyu));
        }

        Rectangle ResimAlani(Rectangle r)
        {
            int a = P(ResimBoyu);
            return new Rectangle(r.X + (r.Width - a) / 2, r.Y + P(10), a, a);
        }

        Rectangle SatirAlani(Rectangle r, int sira)
        {
            return new Rectangle(r.X + P(6), r.Y + P(10 + ResimBoyu + 5 + SatirUstu[sira]), r.Width - P(12), P(sira == 0 ? 17 : 15));
        }

        Rectangle RozetAlani(Rectangle r) { return new Rectangle(r.Right - P(25), r.Y + P(4), P(21), P(21)); }

        Rectangle NotAlani(Rectangle r)
        {
            Rectangle n = SatirAlani(r, 2);
            n.Inflate(0, P(2));
            return n;
        }

        void Yerlestir()
        {
            int satir = (ogeler.Count + Sutun - 1) / Sutun;
            var istenen = new Size(0, satir == 0 ? 0 : P(2) + satir * P(KartBoyu) + (satir - 1) * P(Aralik) + P(2));
            if (AutoScrollMinSize != istenen) AutoScrollMinSize = istenen;
        }

        void Gorunur(int i)
        {
            Rectangle r = KartAlani(i);
            int y = -AutoScrollPosition.Y;
            if (r.Top < 0) AutoScrollPosition = new Point(0, y + r.Top - P(2));
            else if (r.Bottom > ClientSize.Height) AutoScrollPosition = new Point(0, y + r.Bottom - ClientSize.Height + P(2));
            Invalidate();
        }

        int Bul(Point p)
        {
            for (int i = 0; i < ogeler.Count; i++)
                if (KartAlani(i).Contains(p)) return i;
            return -1;
        }

        // kartin neresi: 1 ayar isareti, 2 not satiri, 0 diger
        int Bolum(int i, Point p)
        {
            Rectangle r = KartAlani(i);
            if (RozetAlani(r).Contains(p)) return 1;
            return NotAlani(r).Contains(p) ? 2 : 0;
        }

        protected override void OnResize(EventArgs e)
        {
            NotuBitir();
            base.OnResize(e);
            Yerlestir();
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            NotuBitir();
            IpucuKapat();
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            NotuBitir();
            IpucuKapat();
            base.OnMouseWheel(e);
            Invalidate();
        }

        // ---------------------------------------------------------- cizim

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < ogeler.Count; i++)
            {
                Rectangle r = KartAlani(i);
                if (r.IntersectsWith(e.ClipRectangle)) Ciz(g, ogeler[i], r, i == secili, i == ustunde);
            }
        }

        void Ciz(Graphics g, Oge o, Rectangle r, bool sec, bool ust)
        {
            Color zemin = sec ? Tema.KartSecili : ust ? Tema.KartUstunde : Tema.Kart;
            float kalinlik = sec ? Math.Max(2f, 2f * olcek) : 1f;
            var dis = new RectangleF(r.X + kalinlik / 2f, r.Y + kalinlik / 2f, r.Width - kalinlik, r.Height - kalinlik);
            using (GraphicsPath yol = Tema.Yuvarlak(dis, P(8)))
            {
                using (var f = new SolidBrush(zemin)) g.FillPath(f, yol);
                Color kenar = sec ? Tema.Vurgu : o.SonKullanilan ? Color.FromArgb(120, Tema.Vurgu) : Tema.Kenar;
                using (var k = new Pen(kenar, kalinlik)) g.DrawPath(k, yol);
            }
            Rectangle a = ResimAlani(r);
            if (o.Resim != null) g.DrawImage(o.Resim, a.X, a.Y, a.Width, a.Height);
            if (o.Cevrimdisi.HasValue)
            {
                float d = P(16), x = a.Right - d + P(4), y = a.Bottom - d + P(4), ic = d - P(5);
                using (var f = new SolidBrush(zemin)) g.FillEllipse(f, x, y, d, d);
                using (var f = new SolidBrush(o.Cevrimdisi.Value ? Tema.Cevrimdisi : Tema.Yesil)) g.FillEllipse(f, x + (d - ic) / 2f, y + (d - ic) / 2f, ic, ic);
            }
            const TextFormatFlags orta = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                                       | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (o.Rozet.Length > 0) TextRenderer.DrawText(g, o.Rozet, rozetYazisi, RozetAlani(r), o.RozetRengi, orta);
            TextRenderer.DrawText(g, o.Isim, kalin, SatirAlani(r, 0), Tema.Metin, orta);
            if (o.Alt.Length > 0) TextRenderer.DrawText(g, o.Alt, kucuk, SatirAlani(r, 1), Tema.Soluk, orta);
            if (o.Not.Length > 0) TextRenderer.DrawText(g, o.Not, kucuk, SatirAlani(r, 2), Tema.NotRengi, orta);
            else if (sec || ust) TextRenderer.DrawText(g, NotYeri, kucuk, SatirAlani(r, 2), Color.FromArgb(98, 105, 116), orta);
        }

        // ---------------------------------------------------------- fare, klavye, ipucu

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = Bul(e.Location);
            if (i != ustunde)
            {
                ustunde = i;
                Invalidate();
            }
            int bolum = i >= 0 ? Bolum(i, e.Location) : -1;
            Cursor = bolum == 2 ? Cursors.IBeam : Cursors.Default;
            IpucuAyarla(i, bolum, e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (ustunde != -1)
            {
                ustunde = -1;
                Invalidate();
            }
            IpucuKapat();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            NotuBitir();
            IpucuKapat();
            Focus();
            int i = Bul(e.Location);
            if (i >= 0 && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                SeciliYap(i);
                if (e.Button == MouseButtons.Left && Bolum(i, e.Location) == 2) NotuDuzenle();
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys tus)
        {
            switch (tus & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(tus);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (ogeler.Count == 0) return;
            int s = Sutun, i = secili < 0 ? 0 : secili, yeni;
            switch (e.KeyCode)
            {
                case Keys.Left: yeni = i - 1; break;
                case Keys.Right: yeni = i + 1; break;
                case Keys.Up: yeni = i - s; break;
                case Keys.Down: yeni = i + s; break;
                case Keys.Home: yeni = 0; break;
                case Keys.End: yeni = ogeler.Count - 1; break;
                case Keys.F2:
                    NotuDuzenle();
                    e.Handled = true;
                    return;
                default: return;
            }
            SeciliYap(secili < 0 ? 0 : Math.Max(0, Math.Min(ogeler.Count - 1, yeni)));
            e.Handled = true;
        }

        void IpucuAyarla(int i, int bolum, Point p)
        {
            string anahtar = i < 0 ? null : i + ":" + bolum;
            if (anahtar == ipucuAnahtari) return;
            IpucuKapat();
            ipucuAnahtari = anahtar;
            if (i < 0) return;
            Oge o = ogeler[i];
            bekleyenIpucu = bolum == 1 ? o.RozetIpucu : bolum == 2 ? NotIpucu + (o.Not.Length > 0 ? "\n" + o.Not : "") : o.Ipucu;
            ipucuYeri = new Point(p.X + P(12), p.Y + P(18));
            ipucuZamani.Start();
        }

        void IpucuKapat()
        {
            ipucuZamani.Stop();
            bekleyenIpucu = null;
            ipucuAnahtari = null;
            ipucu.ElleGizle(this);
        }

        // ---------------------------------------------------------- not (kartin alt satiri)

        public void NotuDuzenle()
        {
            if (secili < 0 || duzenlenen >= 0) return;
            IpucuKapat();
            Gorunur(secili);
            Rectangle n = NotAlani(KartAlani(secili));
            n.Inflate(P(2), 0);
            duzenlenen = secili;
            notKutusu.Text = ogeler[secili].Not;
            notKutusu.SetBounds(n.X, n.Y + (n.Height - notKutusu.PreferredHeight) / 2, n.Width, notKutusu.PreferredHeight);
            notKutusu.Visible = true;
            notKutusu.Focus();
            notKutusu.SelectionStart = notKutusu.Text.Length;
        }

        public void NotuBitir() { NotKapat(true); }

        void NotKapat(bool kaydet)
        {
            if (duzenlenen < 0) return;
            int i = duzenlenen;
            duzenlenen = -1;
            bool odak = notKutusu.Focused;
            string yeni = notKutusu.Text.Trim();
            notKutusu.Visible = false;
            if (odak && CanFocus) Focus();
            if (i >= ogeler.Count) return;
            Oge o = ogeler[i];
            if (kaydet) o.Not = yeni;
            Invalidate();
            if (NotDegisti != null) NotDegisti(o.Hesap, o.Not);
        }

        void NotTusu(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                NotKapat(e.KeyCode == Keys.Enter);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Oge o in ogeler)
                    if (o.Resim != null) o.Resim.Dispose();
                ogeler.Clear();
                rozetYazisi.Dispose();
                ipucuZamani.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ ana pencere

    public class AnaPencere : Form
    {
        static readonly Color Yesil = Tema.Yesil;          // 1.4: koyu tema renkleri
        static readonly Color Kirmizi = Tema.Kirmizi;
        static readonly Color Turuncu = Tema.Turuncu;
        static readonly Color Gri = Tema.Soluk;
        static readonly Color KartRengi = Tema.Kart;
        static string[] KuralAdlari()
        {
            return new[] { Dil.T("Steam Cloud off", "Steam Cloud kapalı"), Dil.T("Remote Play off", "Remote Play kapalı"),
                           Dil.T("Games update only when launched", "Oyunlar yalnızca başlatılınca güncellenir") };
        }

        static string WuKapatSorusu
        {
            get
            {
                return Dil.T("Turn off Windows Update?\n\n" +
                             "Chris Titus WinUtil's \"Disable Updates\" steps are applied:\n" +
                             "• BITS, wuauserv and UsoSvc services are stopped and disabled\n" +
                             "• The automatic update policy is turned off (NoAutoUpdate)\n" +
                             "• Delivery Optimization downloads are turned off\n" +
                             "• Update scheduled tasks are disabled\n\n" +
                             "Security updates will not arrive either. To undo: \"Restore Default\".\n" +
                             "Administrator permission (UAC) will be asked.",
                             "Windows Update kapatılsın mı?\n\n" +
                             "Chris Titus WinUtil'in \"Disable Updates\" adımları uygulanır:\n" +
                             "• BITS, wuauserv ve UsoSvc servisleri durdurulup devre dışı bırakılır\n" +
                             "• Otomatik güncelleme politikası kapatılır (NoAutoUpdate)\n" +
                             "• Teslim İyileştirme indirmeleri kapatılır\n" +
                             "• Güncelleme zamanlanmış görevleri devre dışı bırakılır\n\n" +
                             "Güvenlik güncellemeleri de gelmez. Geri almak için \"Varsayılana Döndür\".\n" +
                             "Yönetici izni (UAC) istenecek.");
            }
        }

        static string WuAcSorusu
        {
            get
            {
                return Dil.T("Return Windows Update to default?\n\n" +
                             "• Update policies are removed\n" +
                             "• BITS and wuauserv set to \"manual\", UsoSvc to \"automatic\"\n" +
                             "• Update scheduled tasks are turned back on\n\n" +
                             "Administrator permission (UAC) will be asked.",
                             "Windows Update varsayılana döndürülsün mü?\n\n" +
                             "• Güncelleme politikaları kaldırılır\n" +
                             "• BITS ve wuauserv \"el ile\", UsoSvc \"otomatik\" yapılır\n" +
                             "• Güncelleme zamanlanmış görevleri yeniden açılır\n\n" +
                             "Yönetici izni (UAC) istenecek.");
            }
        }

        readonly bool onizleme;         // true: hicbir sey yazmaz, zamanlayici ve tepsi yok (deneme icin)
        readonly float olcek;
        readonly string steam;
        readonly Dictionary<string, string> notlar;
        List<Hesap> hesaplar = new List<Hesap>();
        string ortakHata;
        bool mesgul, steamAcikti, bekleyenYenileme, wuCalisiyor;

        HesapKartlari kartlar;
        KoyuButon btnCevrimdisi, btnCevrimici, btnKapat, btnWuKapat, btnWuAc;
        Label lblSteam, lblMesaj, lblWu, lblWuBaslik;
        KoyuOnay chkBaslangic, chkDuzelt;
        Label lblBaslik, lblAciklama, lblDil, lblNot;
        KoyuButon btnIngilizce, btnTurkce;
        KoyuButon btnKisayol;
        ContextMenuStrip satirMenu, tepsiMenu;
        ToolStripMenuItem mnuKisayolCevrimdisi, mnuKisayolCevrimici, mnuNot;
        Font kalin, kucuk;
        KoyuIpucu ipucu;
        System.Windows.Forms.Timer zamanlayici;
        NotifyIcon tepsi;

        public AnaPencere(bool onizleme)
        {
            this.onizleme = onizleme;
            olcek = Olcek.Al();
            steam = Steam.Klasor();
            notlar = Notlar.Oku();
            Text = "Steam Offline Tool";
            Font = new Font("Segoe UI", 9f);
            kalin = new Font(Font, FontStyle.Bold);
            kucuk = new Font("Segoe UI", 8.25f);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Tema.Arka;
            ForeColor = Tema.Metin;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(P(780), P(600));
            MinimumSize = new Size(P(640), P(500));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Olustur();
            HesaplariYukle();
            SteamDurumu(Steam.Acik());
            WuDurumu();
            if (!onizleme) TepsiOlustur();
        }

        int P(float px) { return (int)Math.Round(px * olcek); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Tema.KoyuBaslik(Handle);
        }

        // ---------------------------------------------------------- yerlesim

        void Olustur()
        {
            SuspendLayout();
            ipucu = new KoyuIpucu();
            var kok = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(P(12), P(10), P(12), P(10)) };
            kok.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // ust satir: baslik ve Steam durumu solda, dil sagda
            var ust = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, P(4)) };
            ust.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            ust.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ust.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var sol = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            lblBaslik = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), Margin = new Padding(0, 0, P(10), 0) };
            sol.Controls.Add(lblBaslik);
            lblSteam = new Label { AutoSize = true, Font = kalin, Margin = new Padding(0, P(4), 0, 0) };
            sol.Controls.Add(lblSteam);
            var dil = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            lblDil = new Label { AutoSize = true, Margin = new Padding(0, P(5), P(6), 0) };
            btnIngilizce = DilButonu("English", false);         // 1.4: acilir liste yerine iki dugme (koyu temada duzgun gorunur)
            btnTurkce = DilButonu("Türkçe", true);
            dil.Controls.AddRange(new Control[] { lblDil, btnIngilizce, btnTurkce });
            ust.Controls.Add(sol, 0, 0);
            ust.Controls.Add(dil, 1, 0);
            Ekle(kok, ust, false);

            lblNot = Yazi();                // en ustte: Steam'in "acilista hesap sor" tiki kapali olmali
            lblNot.Font = kucuk;
            lblNot.ForeColor = Gri;
            lblNot.Margin = new Padding(0, 0, 0, P(6));
            Ekle(kok, lblNot, false);

            kartlar = KartlarOlustur();
            Ekle(kok, kartlar, true);

            var butonlar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, P(8), 0, 0) };
            btnCevrimdisi = Buton("", true);
            btnCevrimici = Buton("", false);
            btnKapat = Buton("", false);
            btnCevrimdisi.Click += delegate { Ac("1"); };
            btnCevrimici.Click += delegate { Ac("0"); };
            btnKapat.Click += delegate { SteamiKapat(); };
            butonlar.Controls.AddRange(new Control[] { btnCevrimdisi, btnCevrimici, btnKapat });
            Ekle(kok, butonlar, false);

            lblMesaj = Yazi();
            lblMesaj.Margin = new Padding(0, P(6), 0, 0);
            Ekle(kok, lblMesaj, false);

            lblAciklama = Yazi();
            lblAciklama.Font = kucuk;
            lblAciklama.Margin = new Padding(0, P(3), 0, 0);
            lblAciklama.ForeColor = Gri;
            Ekle(kok, lblAciklama, false);

            lblWu = new Label { AutoSize = true, Font = kalin };
            btnWuKapat = Buton("", false);
            btnWuAc = Buton("", false);
            btnWuKapat.Click += delegate { YoneticiIslemi("--wu-kapat", WuKapatSorusu); };
            btnWuAc.Click += delegate { YoneticiIslemi("--wu-ac", WuAcSorusu); };
            TableLayoutPanel wu = Kart("Windows Update", lblWu, btnWuKapat, btnWuAc);
            wu.Margin = new Padding(0, P(8), 0, 0);
            Ekle(kok, wu, false);

            var alt = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, P(8), 0, 0) };
            chkBaslangic = new KoyuOnay { AutoSize = true, Checked = Baslangic.Acik(), Margin = new Padding(0, P(4), P(16), 0) };
            chkDuzelt = new KoyuOnay { AutoSize = true, Checked = UygulamaAyari.Oku("AyarlariDuzelt", true), Margin = new Padding(0, P(4), P(16), 0) };
            btnKisayol = Buton("", false);
            btnKisayol.MinimumSize = new Size(0, P(28));
            btnKisayol.Margin = new Padding(0, 0, P(16), 0);
            chkBaslangic.CheckedChanged += delegate { BaslangicDegisti(); };
            chkDuzelt.CheckedChanged += delegate { DuzeltDegisti(); };
            btnKisayol.Click += delegate { UygulamaKisayolu(); };
            alt.Controls.AddRange(new Control[] { chkBaslangic, chkDuzelt, btnKisayol });
            Ekle(kok, alt, false);

            Controls.Add(kok);
            MetinleriUygula();
            Mesaj(VarsayilanMesaj, Gri);
            ResumeLayout(true);
        }

        string VarsayilanMesaj
        {
            get
            {
                return Dil.T("Use this window to switch accounts: Steam's own \"Switch Account\" screen always signs in online.",
                             "Hesap değiştirmek için bu pencereyi kullan: Steam'in \"Hesap Değiştir\" ekranı her zaman çevrimiçi giriş yapar.");
            }
        }

        // butun yazilar secili dilde (dil degisince yeniden cagrilir)
        void MetinleriUygula()
        {
            lblBaslik.Text = Dil.T("Accounts", "Hesaplar");
            lblNot.Text = Dil.T("Steam's \u201cAsk which account to use each time Steam starts\u201d must stay off (otherwise Steam shows its account chooser "
                                + "instead of the account you picked). The tool turns it off every time it opens an account.",
                                "Steam'in \u201cSteam her başlatıldığında hangi hesabın kullanılacağını sor\u201d ayarı kapalı olmalı (açıksa Steam seçtiğin hesap "
                                + "yerine hesap seçme ekranını açar). Program her hesap açışında bunu kendisi kapatır.");
            btnCevrimdisi.Text = Dil.T("Open Offline", "Çevrimdışı Aç");
            btnCevrimici.Text = Dil.T("Open Online", "Çevrimiçi Aç");
            btnKapat.Text = Dil.T("Close Steam", "Steam'i Kapat");
            // 1.4: kisa aciklama; uzun hali ipucunda
            lblAciklama.Text = Dil.T("✔ settings right · ✖ fixed while Steam is closed · dot on the picture: last mode (gray offline, green online) · "
                                   + "click a card's bottom line: note · right-click: desktop shortcut",
                                     "✔ ayarlar doğru · ✖ Steam kapalıyken düzeltilir · resimdeki nokta: son mod (gri çevrimdışı, yeşil çevrimiçi) · "
                                   + "kartın alt satırına tıkla: not · sağ tık: masaüstü kısayolu");
            ipucu.SetToolTip(lblAciklama, Dil.T("✔ = Steam Cloud off, Remote Play off, games update only when launched, account chooser off.\n"
                                              + "An account with ✖ is fixed while Steam is closed (hover over the mark for details).\n"
                                              + "Right-click an account for a desktop shortcut that opens it directly, with the account's picture.",
                                                "✔ = Steam Cloud kapalı, Remote Play kapalı, oyunlar yalnızca başlatılınca güncellenir, hesap seçme ekranı kapalı.\n"
                                              + "✖ olan hesap Steam kapalıyken düzeltilir (ayrıntı için işaretin üzerine gel).\n"
                                              + "Hesabı doğrudan açan, hesabın resmiyle masaüstü kısayolu için hesaba sağ tıkla."));
            btnWuKapat.Text = Dil.T("Turn Off Updates", "Güncellemeleri Kapat");
            btnWuAc.Text = Dil.T("Restore Default", "Varsayılana Döndür");
            chkBaslangic.Text = Dil.T("Start with Windows", "Windows açılınca başlat");
            chkDuzelt.Text = Dil.T("Fix Steam settings if wrong", "Steam ayarları yanlışsa düzelt");
            btnKisayol.Text = Dil.T("Desktop shortcut", "Masaüstüne kısayol");
            lblDil.Text = Dil.T("Language:", "Dil:");
            btnIngilizce.Ana = !Dil.Turkce;
            btnTurkce.Ana = Dil.Turkce;
            mnuKisayolCevrimdisi.Text = Dil.T("Desktop shortcut: open offline", "Masaüstü kısayolu: çevrimdışı aç");
            mnuKisayolCevrimici.Text = Dil.T("Desktop shortcut: open online", "Masaüstü kısayolu: çevrimiçi aç");
            mnuNot.Text = Dil.T("Write note", "Not yaz");
            kartlar.NotIpucu = Dil.T("Click to write a note", "Not yazmak için tıkla");
            kartlar.NotYeri = Dil.T("+ note", "+ not");
        }

        static void Ekle(TableLayoutPanel t, Control c, bool doldur)
        {
            t.RowStyles.Add(doldur ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.AutoSize));
            t.RowCount = t.RowStyles.Count;
            t.Controls.Add(c, 0, t.RowCount - 1);
        }

        Label Yazi()
        {
            return new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        }

        KoyuButon Buton(string yazi, bool ana)
        {
            return new KoyuButon
            {
                Text = yazi, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(P(112), P(30)),
                Padding = new Padding(P(8), P(1), P(8), P(1)), Margin = new Padding(0, 0, P(6), 0), Ana = ana, Font = ana ? kalin : Font
            };
        }

        // dil dugmesi: secili dil mavi
        KoyuButon DilButonu(string yazi, bool tr)
        {
            var b = new KoyuButon
            {
                Text = yazi, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(P(64), P(28)),
                Padding = new Padding(P(4), 0, P(4), 0), Margin = new Padding(0, 0, P(4), 0)
            };
            b.Click += delegate { DilDegisti(tr); };
            return b;
        }

        // Windows Update karti (1.4: tek satir; servis ayrintisi ipucunda)
        TableLayoutPanel Kart(string baslik, Label durum, params Button[] butonlar)
        {
            var t = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, BackColor = KartRengi, Padding = new Padding(P(10), P(6), P(6), P(6)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lblWuBaslik = new Label { Text = baslik, AutoSize = true, Font = kalin, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, P(10), 0) };
            durum.Anchor = AnchorStyles.Left;
            durum.Margin = new Padding(0);
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            f.Controls.AddRange(butonlar);
            t.Controls.Add(lblWuBaslik, 0, 0);
            t.Controls.Add(durum, 1, 0);
            t.Controls.Add(f, 2, 0);
            return t;
        }

        HesapKartlari KartlarOlustur()
        {
            var k = new HesapKartlari(olcek, kalin, kucuk, ipucu) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            k.NotDegisti += NotKaydet;

            // hesaba sag tik: o hesabi dogrudan acan masaustu kisayolu (1.4: ve not)
            satirMenu = new ContextMenuStrip();
            mnuKisayolCevrimdisi = new ToolStripMenuItem("", null, delegate { HesapKisayolu("1"); });
            mnuKisayolCevrimici = new ToolStripMenuItem("", null, delegate { HesapKisayolu("0"); });
            mnuNot = new ToolStripMenuItem("", null, delegate { kartlar.NotuDuzenle(); });
            satirMenu.Items.AddRange(new ToolStripItem[] { mnuKisayolCevrimdisi, mnuKisayolCevrimici, new ToolStripSeparator(), mnuNot });
            satirMenu.Opening += (s, e) => { if (SeciliHesap() == null) e.Cancel = true; };
            k.ContextMenuStrip = satirMenu;
            return k;
        }

        // ---------------------------------------------------------- durumlar

        void Mesaj(string metin, Color renk)
        {
            lblMesaj.Text = metin;
            lblMesaj.ForeColor = renk;
        }

        void Mesgul(bool m)
        {
            mesgul = m;
            btnCevrimdisi.Enabled = btnCevrimici.Enabled = !m && hesaplar.Count > 0;
            btnKapat.Enabled = !m && Steam.Acik();
            UseWaitCursor = m;
        }

        Hesap SeciliHesap()
        {
            return kartlar.SeciliHesap;
        }

        static string ModAdi(string wants)
        {
            return wants == "1" ? Dil.T("Offline", "Çevrimdışı") : wants == "0" ? Dil.T("Online", "Çevrimiçi") : "-";
        }

        static string Tarih(long zaman)
        {
            if (zaman <= 0) return "-";
            try { return DateTimeOffset.FromUnixTimeSeconds(zaman).LocalDateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture); }
            catch (Exception) { return "-"; }
        }

        void HesaplariYukle()
        {
            kartlar.NotuBitir();
            Hesap secili = SeciliHesap();
            string secAd = secili != null ? secili.Ad : null;
            try { hesaplar = Motor.Liste(steam, out ortakHata); }
            catch (Exception e)
            {
                hesaplar = new List<Hesap>();
                ortakHata = null;
                Mesaj(Dil.T("ERROR: ", "HATA: ") + e.Message, Kirmizi);
            }
            KartlariDoldur(secAd);
            Mesgul(mesgul);
        }

        // 1.4: tablo satirlari yerine kartlar (profil resmi, ad, not, ayar isareti, ipuclari)
        void KartlariDoldur(string secAd)
        {
            string son = Steam.SonHesap();
            son = son == null ? null : son.Trim();
            if (secAd == null) secAd = son;
            var ogeler = new List<HesapKartlari.Oge>();
            foreach (Hesap h in hesaplar)
            {
                string not;
                notlar.TryGetValue(h.Ad, out not);
                bool sonKullanilan = son != null && h.Ad.Equals(son, StringComparison.OrdinalIgnoreCase);
                var o = new HesapKartlari.Oge
                {
                    Hesap = h,
                    Isim = h.Persona.Length > 0 ? h.Persona : h.Ad,
                    Alt = h.Persona.Length > 0 && !h.Persona.Equals(h.Ad, StringComparison.OrdinalIgnoreCase) ? h.Ad : "",
                    Not = not ?? "",
                    SonKullanilan = sonKullanilan,
                    Cevrimdisi = h.Wants == "1" ? true : h.Wants == "0" ? false : (bool?)null,
                    Resim = HesapResmi.Kart(h, P(HesapKartlari.ResimBoyu)),
                    Ipucu = Dil.T("Account: ", "Hesap: ") + h.Ad
                            + (h.Persona.Length > 0 ? "\n" + Dil.T("Profile name: ", "Profil adı: ") + h.Persona : "")
                            + "\n" + Dil.T("Last mode: ", "Son mod: ") + ModAdi(h.Wants)
                            + "\n" + Dil.T("Last sign-in: ", "Son giriş: ") + Tarih(h.Zaman)
                            + (sonKullanilan ? "\n" + Dil.T("Last used account", "Son kullanılan hesap") : "")
                };
                AyarIsareti(h, o);
                ogeler.Add(o);
            }
            kartlar.Doldur(ogeler, secAd);
        }

        void AyarIsareti(Hesap h, HesapKartlari.Oge o)
        {
            string neden = ortakHata ?? h.AyarHata;
            if (neden != null || h.Sorunlar == null)
            {
                o.Rozet = "?";
                o.RozetRengi = Gri;
                o.RozetIpucu = neden ?? Dil.T("Steam was never opened with this account on this PC; there is no settings file.",
                                              "Bu hesapla bu bilgisayarda Steam hiç açılmamış; ayar dosyası yok.");
            }
            else if (h.Sorunlar.Count == 0)
            {
                o.Rozet = "✔";
                o.RozetRengi = Yesil;
                o.RozetIpucu = string.Join("\n", KuralAdlari().Select(k => "✔ " + k));
            }
            else
            {
                o.Rozet = "✖";
                o.RozetRengi = Kirmizi;
                o.RozetIpucu = Dil.T("Will be fixed while Steam is closed:\n", "Steam kapalıyken düzeltilecek:\n")
                             + string.Join("\n", h.Sorunlar.Select(k => "✖ " + Dil.Motor(k)));
            }
        }

        // butun hesaplarin ayarlarini Python'a duzelttirir; Steam acikken null doner (kapanirken eski degerleri geri yazardi)
        string AyarlariDuzelt()
        {
            if (onizleme || Steam.Acik()) return null;
            string m;
            try { m = Motor.Duzelt(steam); }
            catch (Exception e) { return Dil.T("Could not fix Steam settings: ", "Steam ayarları düzeltilemedi: ") + e.Message; }
            if (m != null) HesaplariYukle();        // isaretler tazelensin
            return m;
        }

        void SteamDurumu(bool acik)
        {
            if (!mesgul) btnKapat.Enabled = acik;
            if (!acik)
            {
                lblSteam.Text = Dil.T("Steam closed", "Steam kapalı");
                lblSteam.ForeColor = Gri;
                return;
            }
            uint no = Steam.AktifHesapNo();
            Hesap h = no == 0 ? null : hesaplar.FirstOrDefault(x => x.HesapNo == no);
            string ad = h != null ? h.Ad : (Steam.SonHesap() ?? "").Trim();     // cevrimdisi modda ActiveUser 0 kalir: son acilan hesap
            lblSteam.Text = Dil.T("Steam running", "Steam açık") + (ad.Length > 0 ? ": " + ad : "");
            lblSteam.ForeColor = Yesil;
        }

        void WuDurumu()
        {
            string ayrinti;
            int d = Guncelleme.Durum(out ayrinti);
            lblWu.Text = d == 1 ? Dil.T("✔ OFF", "✔ KAPALI") : d == 0 ? Dil.T("ON (Windows default)", "AÇIK (Windows varsayılanı)")
                                                              : Dil.T("PARTLY OFF", "KISMEN KAPALI");
            lblWu.ForeColor = d == 1 ? Yesil : d == 0 ? Gri : Turuncu;
            ipucu.SetToolTip(lblWu, ayrinti);       // 1.4: servis ayrintisi ipucunda (pencere daha kucuk)
            ipucu.SetToolTip(lblWuBaslik, ayrinti);
            if (!wuCalisiyor)
            {
                btnWuKapat.Enabled = d != 1;        // zaten tamamen kapaliysa "Güncellemeleri Kapat" pasif
                btnWuAc.Enabled = d != 0;           // zaten varsayilansa "Varsayılana Döndür" pasif
            }
        }

        // ---------------------------------------------------------- tepsi (saat yanindaki simgeler)

        void TepsiOlustur()
        {
            tepsiMenu = new ContextMenuStrip();
            tepsiMenu.Opening += delegate { TepsiMenusuKur(); };     // her acilista hesaplar ve dil tazelenir
            TepsiMenusuKur();
            tepsi = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Steam Offline Tool", ContextMenuStrip = tepsiMenu, Visible = true };
            tepsi.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) PencereyiGoster(); };
        }

        void TepsiMenusuKur()
        {
            tepsiMenu.Items.Clear();
            tepsiMenu.Items.Add(Dil.T("Open", "Aç"), null, delegate { PencereyiGoster(); });
            tepsiMenu.Items.Add(new ToolStripSeparator());
            var cevrimdisi = new ToolStripMenuItem(Dil.T("Open offline", "Çevrimdışı aç"));
            var cevrimici = new ToolStripMenuItem(Dil.T("Open online", "Çevrimiçi aç"));
            foreach (Hesap h in hesaplar)
            {
                Hesap hesap = h;
                string yazi = HesapYazisi(h);
                cevrimdisi.DropDownItems.Add(yazi, null, delegate { Ac("1", hesap); });
                cevrimici.DropDownItems.Add(yazi, null, delegate { Ac("0", hesap); });
            }
            cevrimdisi.Enabled = cevrimici.Enabled = hesaplar.Count > 0 && !mesgul;
            tepsiMenu.Items.Add(cevrimdisi);
            tepsiMenu.Items.Add(cevrimici);
            tepsiMenu.Items.Add(new ToolStripMenuItem(Dil.T("Close Steam", "Steam'i Kapat"), null, delegate { SteamiKapat(); })
                                { Enabled = !mesgul && Steam.Acik() });
            tepsiMenu.Items.Add(new ToolStripSeparator());
            tepsiMenu.Items.Add(Dil.T("Exit", "Çıkış"), null, delegate { Close(); });
        }

        // tepsi menusundeki yazi: not yazildiysa not, yoksa hesap (profil adi)
        string HesapYazisi(Hesap h)
        {
            string not;
            notlar.TryGetValue(h.Ad, out not);
            string s = h.Ad;
            if (!string.IsNullOrEmpty(h.Persona) && !h.Persona.Equals(h.Ad, StringComparison.OrdinalIgnoreCase)) s += " (" + h.Persona + ")";
            if (!string.IsNullOrWhiteSpace(not)) s = not.Trim();
            if (s.Length > 70) s = s.Substring(0, 67) + "...";
            return s.Replace("&", "&&");    // & menude kisayol harfi sayilmasin
        }

        // pencere gizliyken (tepside) sorular sahipsiz acilir, sonuc balon olarak da gosterilir
        IWin32Window Sahip { get { return Visible ? this : null; } }

        void Bildir(string metin, Color renk)
        {
            Mesaj(metin, renk);
            if (tepsi != null && !Visible)
                try { tepsi.ShowBalloonTip(6000, "Steam Offline Tool", metin.Length > 250 ? metin.Substring(0, 247) + "..." : metin,
                                           renk == Kirmizi ? ToolTipIcon.Error : ToolTipIcon.Info); }
                catch (Exception) { }
        }

        public void PencereyiGoster()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // kucultunce gorev cubugundan cekil, yalniz tepside kal
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tepsi != null && WindowState == FormWindowState.Minimized) Hide();
        }

        // pencereye donunce guncelleme durumunu tazele (Windows servisleri kendisi geri acabiliyor)
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (lblWu != null) WuDurumu();
        }

        // ---------------------------------------------------------- islemler

        async Task<bool> SteamKapanana()
        {
            Mesaj(Dil.T("Closing Steam...", "Steam kapatılıyor..."), Gri);
            bool kapandi = await Task.Run(() => Steam.Kapat(steam, 30));
            if (!kapandi && MessageBox.Show(Sahip, Dil.T("Steam did not close in 30 seconds. Force close it?\n(Steam may not save its last settings.)",
                                                         "Steam 30 saniyede kapanmadı. Zorla kapatılsın mı?\n(Steam son ayarlarını kaydedemeyebilir.)"),
                                            "Steam Offline Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                kapandi = await Task.Run(() => Steam.ZorlaKapat());
            steamAcikti = !kapandi;
            return kapandi;
        }

        // secilen: tepsi menusunden gelen hesap; yoksa listede secili olan
        async void Ac(string deger, Hesap secilen = null)
        {
            if (mesgul) return;
            Hesap h = secilen ?? SeciliHesap();
            if (h == null) { Mesaj(Dil.T("Select an account from the list first.", "Önce listeden bir hesap seç."), Kirmizi); return; }
            kartlar.NotuBitir();
            if (secilen != null) kartlar.Sec(h.Ad);
            string mod = deger == "1" ? Dil.T("offline", "çevrimdışı") : Dil.T("online", "çevrimiçi");
            bool duzelt = chkDuzelt.Checked;
            Mesgul(true);
            try
            {
                if (Steam.Acik())
                {
                    if (MessageBox.Show(Sahip, Dil.T("Steam is running. It must close first, otherwise it writes the old mode back when it exits.\n\n"
                                                     + "Close Steam and open " + h.Ad + " " + mod + "?",
                                                     "Steam açık. Ayar için Steam kapanmalı, yoksa kapanırken eski modu geri yazar.\n\n"
                                                     + "Steam kapatılıp " + h.Ad + " " + mod + " açılsın mı?"), "Steam Offline Tool",
                                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        Mesaj(Dil.T("Cancelled, nothing changed.", "Vazgeçildi, hiçbir şey değişmedi."), Gri);
                        return;
                    }
                    if (!await SteamKapanana())
                    {
                        Bildir(Dil.T("ERROR: Steam did not close. Close Steam yourself and try again. Nothing changed.",
                                     "HATA: Steam kapanmadı. Steam'i kendin kapatıp tekrar dene. Hiçbir şey değişmedi."), Kirmizi);
                        return;
                    }
                }
                Mesaj(Dil.T("Setting up the account...", "Hesap ayarlanıyor..."), Gri);
                string duzeltme = await Task.Run(() => Motor.Ac(steam, h.HamAd, deger, duzelt));
                Steam.Baslat(steam);
                steamAcikti = true;
                HesaplariYukle();
                string m = Dil.T("DONE: Steam is opening " + mod + " (" + h.Ad + ").", "TAMAM: Steam " + mod + " açılıyor (" + h.Ad + ").");
                if (deger == "1")
                    m += Dil.T(" If the Steam menu shows \"Go Online...\", it is offline. If a sign-in screen appears, this account has no saved login or it expired.",
                               " Steam menüsünde \"Çevrimiçi Ol...\" görünürse çevrimdışıdır. Giriş ekranı açılırsa bu hesabın kayıtlı girişi yok ya da süresi dolmuş.");
                if (duzeltme != null) m += " " + duzeltme;
                Bildir(m, Yesil);
            }
            catch (Exception e) { Bildir(Dil.T("ERROR: ", "HATA: ") + e.Message + Dil.T(" Steam was not started.", " Steam başlatılmadı."), Kirmizi); }
            finally { if (!IsDisposed) Mesgul(false); }
        }

        async void SteamiKapat()
        {
            if (mesgul) return;
            if (!Steam.Acik()) { Bildir(Dil.T("Steam is already closed.", "Steam zaten kapalı."), Gri); return; }
            Mesgul(true);
            try
            {
                if (!await SteamKapanana())
                {
                    Bildir(Dil.T("ERROR: Steam did not close.", "HATA: Steam kapanmadı."), Kirmizi);
                    return;
                }
                SteamDurumu(false);
                HesaplariYukle();
                string m = chkDuzelt.Checked ? AyarlariDuzelt() : null;
                Bildir(Dil.T("Steam closed.", "Steam kapatıldı.") + (m != null ? " " + m : ""), Yesil);
            }
            finally { if (!IsDisposed) Mesgul(false); }
        }

        // ---------------------------------------------------------- kisayollar ve dil

        void HesapKisayolu(string deger)
        {
            Hesap h = SeciliHesap();
            if (h == null) return;
            try { Mesaj(Dil.T("Desktop shortcut created: ", "Masaüstü kısayolu oluşturuldu: ") + KisayolAc.Olustur(h, deger), Yesil); }
            catch (Exception e) { Mesaj(Dil.T("ERROR: shortcut could not be created: ", "HATA: kısayol oluşturulamadı: ") + e.Message, Kirmizi); }
        }

        void UygulamaKisayolu()
        {
            try
            {
                KisayolAc.UygulamaKisayolu();
                Mesaj(Dil.T("Desktop shortcut created: Steam Offline Tool", "Masaüstü kısayolu oluşturuldu: Steam Offline Tool"), Yesil);
            }
            catch (Exception e) { Mesaj(Dil.T("ERROR: shortcut could not be created: ", "HATA: kısayol oluşturulamadı: ") + e.Message, Kirmizi); }
        }

        void DilDegisti(bool tr)
        {
            if (tr == Dil.Turkce) return;
            Dil.Turkce = tr;
            try { UygulamaAyari.Yaz("Turkce", tr); } catch (Exception) { }
            SuspendLayout();
            MetinleriUygula();
            ResumeLayout(true);
            HesaplariYukle();               // mod adlari ve ipuclari yeni dilde
            SteamDurumu(Steam.Acik());
            WuDurumu();
            Mesaj(VarsayilanMesaj, Gri);
        }

        async void YoneticiIslemi(string islem, string soru)
        {
            if (MessageBox.Show(this, soru, "Steam Offline Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            // yonetici kopyasi her adimi aninda bu dosyaya yazar: takilir ya da yarida kesilirse son adim gorunur
            string gunluk = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "islem_gunlugu.txt");
            try { File.Delete(gunluk); } catch (Exception) { }
            Process p;
            try { p = Yonetici.Baslat(islem, gunluk); }
            catch (Exception e) { Mesaj(Dil.T("ERROR: ", "HATA: ") + e.Message, Kirmizi); return; }
            if (p == null) { Mesaj(Dil.T("Cancelled: administrator permission was not given.", "İptal edildi: yönetici izni verilmedi."), Gri); return; }
            wuCalisiyor = true;
            btnWuKapat.Enabled = btnWuAc.Enabled = false;
            DateTime basla = DateTime.Now;
            using (p)
                while (!p.HasExited)
                {
                    await Task.Delay(500);
                    if (IsDisposed) return;
                    string adim = SonAdim(GunlukSatirlari(gunluk)) ?? Dil.T("starting...", "başlıyor...");
                    if ((DateTime.Now - basla).TotalSeconds < 60) Mesaj(Dil.T("Working: ", "Çalışıyor: ") + adim, Gri);
                    else Mesaj(Dil.T("Taking long, it may be stuck (is an antivirus warning open?). Current step: ",
                                     "Uzun sürüyor, takılmış olabilir (Kaspersky uyarısı açık mı?). Şu adımda: ") + adim, Turuncu);
                }
            if (IsDisposed) return;
            wuCalisiyor = false;
            WuDurumu();
            List<string> satirlar = GunlukSatirlari(gunluk);
            List<string> hatali = HataliAdimlar(satirlar);
            if (!satirlar.Any(s => s.StartsWith("SONUÇ:") || s.StartsWith("RESULT:")))
                Mesaj(Dil.T("Interrupted (an antivirus may have stopped it). Last step: ", "Yarıda kesildi (Kaspersky durdurmuş olabilir). Son adım: ")
                      + (SonAdim(satirlar) ?? "-") + Dil.T(". Details: islem_gunlugu.txt", ". Ayrıntı: islem_gunlugu.txt"), Kirmizi);
            else if (hatali.Count == 0)
                Mesaj(Dil.T("Finished: all steps done. Details: islem_gunlugu.txt", "Bitti: bütün adımlar tamam. Ayrıntı: islem_gunlugu.txt"), Yesil);
            else
                Mesaj(Dil.T("Finished, but these step(s) failed: ", "Bitti ama şu adım(lar) yapılamadı: ") + string.Join("; ", hatali)
                      + Dil.T(". Details: islem_gunlugu.txt", ". Ayrıntı: islem_gunlugu.txt"), Kirmizi);
        }

        static List<string> GunlukSatirlari(string yol)
        {
            try
            {
                using (var fs = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                    return sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        // gunlukte adimlar "• " ile baslar, basarisiz olanin altinda "HATA" satiri olur
        static string SonAdim(List<string> satirlar)
        {
            string adim = null;
            foreach (string s in satirlar)
                if (s.StartsWith("• ")) adim = s.Substring(2);
            return adim;
        }

        static List<string> HataliAdimlar(List<string> satirlar)
        {
            var hatali = new List<string>();
            string adim = null;
            foreach (string s in satirlar)
            {
                if (s.StartsWith("• ")) adim = s.Substring(2);
                else if (s.TrimStart().StartsWith("HATA") || s.TrimStart().StartsWith("ERROR") || s.StartsWith("UYARI") || s.StartsWith("WARNING"))
                {
                    string a = adim ?? s.Trim();
                    if (!hatali.Contains(a)) hatali.Add(a);
                }
            }
            return hatali;
        }

        void BaslangicDegisti()
        {
            try
            {
                Baslangic.Ayarla(chkBaslangic.Checked);
                Mesaj(chkBaslangic.Checked ? Dil.T("Steam Offline Tool will start with Windows.", "Windows açılınca Steam Offline Tool açılacak.")
                                           : Dil.T("It will no longer start with Windows.", "Windows açılınca artık açılmayacak."), Gri);
            }
            catch (Exception e) { Mesaj(Dil.T("ERROR: could not write the startup setting: ", "HATA: başlangıç ayarı yazılamadı: ") + e.Message, Kirmizi); }
        }

        void DuzeltDegisti()
        {
            UygulamaAyari.Yaz("AyarlariDuzelt", chkDuzelt.Checked);
            if (!chkDuzelt.Checked) return;
            if (Steam.Acik()) { Mesaj(Dil.T("Steam is running: settings will be fixed when Steam closes.", "Steam açık: ayarlar Steam kapanınca düzeltilecek."), Gri); return; }
            string m = AyarlariDuzelt();
            Mesaj(m ?? Dil.T("Steam settings are already correct.", "Steam ayarları zaten doğru."), Yesil);
        }

        // kartta not yazildi (ya da vazgecildi): degistiyse hesap_notlari.txt'ye yazilir
        void NotKaydet(Hesap h, string yeni)
        {
            yeni = (yeni ?? "").Trim();
            string eski;
            notlar.TryGetValue(h.Ad, out eski);
            if (yeni != (eski ?? ""))
            {
                notlar[h.Ad] = yeni;
                try { Notlar.Yaz(notlar); }
                catch (Exception ex) { Mesaj(Dil.T("Could not save the note: ", "Not kaydedilemedi: ") + ex.Message, Kirmizi); }
            }
            if (bekleyenYenileme)
            {
                bekleyenYenileme = false;
                BeginInvoke(new Action(HesaplariYukle));
            }
        }

        void Zamanlayici_Tick(object sender, EventArgs e)
        {
            bool acik = Steam.Acik();
            SteamDurumu(acik);
            if (mesgul) return;
            if (steamAcikti && !acik)
            {
                // Steam kendi kapandi: modlari yeniden oku, yanlis ayarlari duzelt
                if (kartlar.NotDuzenleniyor) bekleyenYenileme = true;
                else HesaplariYukle();
                string m = chkDuzelt.Checked ? AyarlariDuzelt() : null;
                if (m != null) Mesaj(m, Yesil);
            }
            steamAcikti = acik;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (onizleme) return;
            steamAcikti = Steam.Acik();
            if (!steamAcikti && chkDuzelt.Checked)
            {
                string m = AyarlariDuzelt();
                if (m != null) Mesaj(m, Yesil);
            }
            zamanlayici = new System.Windows.Forms.Timer { Interval = 2000 };
            zamanlayici.Tick += Zamanlayici_Tick;
            zamanlayici.Start();
            kartlar.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            kartlar.NotuBitir();
            if (zamanlayici != null) zamanlayici.Stop();
            if (tepsi != null)
            {
                tepsi.Visible = false;      // yoksa simge fare ustune gelene kadar tepside kalir
                tepsi.Dispose();
                tepsi = null;
            }
            base.OnFormClosing(e);
        }
    }
}
