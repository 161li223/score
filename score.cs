// score - mini client ultra-leger : 2 derniers matchs SoloQ + recreation du "DPM Score" (ADC) de dpm.lol
// Compile avec le csc.exe integre a Windows (.NET Framework 4.x), voir build.bat
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

static class App
{
    static string Key;
    const string Region = "https://europe.api.riotgames.com"; // EUNE/EUW -> europe
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    // Valeurs reelles dpm.lol (relevees le 10/11/2026) pour verifier la formule : --check
    static readonly Dictionary<string, double> Reference = new Dictionary<string, double> {
        {"EUN1_4026500416", 70.84}, {"EUN1_4026291198", 42.36}, {"EUN1_4026259660", 30.99},
        {"EUN1_4026239750", 69.90}, {"EUN1_4026222602", 76.03},
    };

    static int Main(string[] args)
    {
        int code = Run(args);
        // Lance par double-clic : garde la fenetre ouverte pour lire le resultat
        if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
        {
            Console.WriteLine("Appuie sur une touche pour fermer...");
            Console.ReadKey(true);
        }
        return code;
    }

    static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        string riotId = "Sfinxo#EUNE";
        int watch = 0; bool check = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--watch") watch = (i + 1 < args.Length) ? int.Parse(args[++i]) : 120;
            else if (args[i] == "--check") check = true;
            else if (args[i].Contains("#")) riotId = args[i];
        }

        Key = Environment.GetEnvironmentVariable("RIOT_API_KEY");
        string keyFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "riot_key.txt");
        if (string.IsNullOrEmpty(Key) && File.Exists(keyFile)) Key = File.ReadAllText(keyFile).Trim();
        if (string.IsNullOrEmpty(Key))
        {
            Console.WriteLine("Cle API Riot manquante : mets-la dans riot_key.txt (a cote de l'exe) ou dans RIOT_API_KEY.");
            Console.WriteLine("Cle gratuite : https://developer.riotgames.com");
            return 1;
        }

        try
        {
            string[] parts = riotId.Split('#');
            var acc = Get(Region + "/riot/account/v1/accounts/by-riot-id/" + Uri.EscapeDataString(parts[0]) + "/" + Uri.EscapeDataString(parts[1]));
            string puuid = (string)acc["puuid"];

            if (check) { Check(puuid); return 0; }
            string exe = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location).ToLowerInvariant();
            if (exe.StartsWith("score_tot") || Array.IndexOf(args, "--all") >= 0) { RunAll(puuid, riotId); return 0; }

            string last = null;
            var cache = new Dictionary<string, Result>(); // evite de retelecharger les games deja analysees (mode --watch)
            do
            {
                // Toutes les SoloQ depuis minuit (heure locale)
                long since = (long)(DateTime.Today.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                var ids = GetList(Region + "/lol/match/v5/matches/by-puuid/" + puuid + "/ids?queue=420&startTime=" + since + "&start=0&count=100");
                bool today = ids.Count > 0;
                if (!today) ids = GetList(Region + "/lol/match/v5/matches/by-puuid/" + puuid + "/ids?queue=420&start=0&count=1");
                string top = (ids.Count > 0 ? (string)ids[0] : "") + DateTime.Today.Day;
                if (top != last)
                {
                    last = top;
                    var results = new List<Result>();
                    foreach (object o in ids)
                    {
                        string id = (string)o; Result r;
                        if (!cache.TryGetValue(id, out r)) { r = Analyse(id, puuid); cache[id] = r; }
                        results.Add(r);
                    }
                    try { Console.Clear(); } catch (IOException) { }
                    Console.WriteLine("=== " + riotId + " - " + (today ? "SoloQ du jour (" + ids.Count + ")" : "aucune SoloQ aujourd'hui, derniere game") + " - " + DateTime.Now.ToString("HH:mm") + " ===\n");
                    foreach (var r in results) Print(r);
                    if (today)
                    {
                        Summary(results);
                        // compteur : joueurs des games du jour, comptes sur TOUT l'historique (cache partage avec score_tot)
                        var hist = History(puuid, AllIds(puuid), cache, false);
                        var todayPlayers = new HashSet<string>();
                        foreach (var r in results) { todayPlayers.UnionWith(r.Allies); todayPlayers.UnionWith(r.Enemies); }
                        Players(hist, todayPlayers);
                    }
                    if (watch > 0) Console.WriteLine("(surveillance toutes les " + watch + " s - Ctrl+C pour quitter)");
                }
                ids = null;
                if (watch > 0) { GC.Collect(); Thread.Sleep(watch * 1000); }
            } while (watch > 0);
        }
        catch (WebException e)
        {
            var resp = e.Response as HttpWebResponse;
            int code = resp != null ? (int)resp.StatusCode : 0;
            Console.WriteLine("Erreur API Riot (" + code + ")" + (code == 401 || code == 403 ? " : cle invalide ou expiree (les cles dev expirent apres 24 h)." : code == 404 ? " : joueur introuvable." : code == 429 ? " : trop de requetes, reessaie dans 1 min." : " : " + e.Message));
            return 2;
        }
        return 0;
    }

    // ---------------------------------------------------------------- calcul

    class Result
    {
        public string Champ, Opp, Lane; public bool Win, Remake; public int K, D, A, Cs, Gold, Dmg, Vision; public double Minutes, Csm;
        public double Score, Global, LaneS, Obj, Team, State, Role; public string Id;
        // infos detaillees
        public int SoloKills, MultiKill, Outnumbered, Picks, Saves, Cs10, MaxCsAdv, Plates, GoldDiff, XpDiff, Towers;
        public double Kp, DmgShare, TankShare, Dpm;
        public bool AllyAfk, EnemyAfk; // AFK detecte (wasAfk / surrender IGNB) -> defaite a LP proteges
        public bool Afk { get { return !Remake && !Win && AllyAfk; } } // defaite avec allie AFK = pas de perte de LP
        public bool Excluded { get { return Remake || Afk; } } // exclue des calculs
        public string OppId = ""; public List<string> Allies = new List<string>(), Enemies = new List<string>(); // Riot IDs pour le compteur de joueurs
    }

    static string Rid(Dictionary<string, object> p)
    {
        object n, t; p.TryGetValue("riotIdGameName", out n); p.TryGetValue("riotIdTagline", out t);
        return (n as string ?? "?") + "#" + (t as string ?? "?");
    }

    static readonly string[] MultiName = { "-", "-", "Double", "Triple", "Quadra", "PENTA" };

    static double Clamp(double x, double lo, double hi) { return Math.Max(lo, Math.Min(hi, x)); }
    static double N(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToDouble(v) : 0; }
    static Dictionary<string, object> O(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }

    static Result Analyse(string matchId, string puuid)
    {
        var match = Get(Region + "/lol/match/v5/matches/" + matchId);
        var info = O(match, "info");
        var parts = (ArrayList)info["participants"];
        Dictionary<string, object> me = null, opp = null;
        foreach (Dictionary<string, object> p in parts) if ((string)p["puuid"] == puuid) me = p;
        string pos = (string)me["teamPosition"];
        int team = (int)N(me, "teamId");
        foreach (Dictionary<string, object> p in parts) if ((string)p["teamPosition"] == pos && (int)N(p, "teamId") != team) opp = p;
        var ch = O(me, "challenges"); var och = O(opp, "challenges");

        double dur = N(info, "gameDuration");
        if (dur > 100000) dur /= 1000; // anciens matchs en ms
        double min = dur / 60.0;
        bool shortGame = dur < 1200; // < 20 min : autres references
        double f = shortGame ? 1.3 : 1.0;

        Dictionary<string, object> myTeamObj = null;
        foreach (Dictionary<string, object> t in (ArrayList)info["teams"]) if ((int)N(t, "teamId") == team) myTeamObj = O(t, "objectives");
        Func<string, double> teamKills = k => N(O(myTeamObj, k), "kills");

        var r = new Result();
        r.Id = matchId; r.Champ = (string)me["championName"]; r.Opp = opp != null ? (string)opp["championName"] : "?"; r.Lane = pos;
        r.Win = (bool)me["win"]; r.K = (int)N(me, "kills"); r.D = (int)N(me, "deaths"); r.A = (int)N(me, "assists");
        r.Cs = (int)(N(me, "totalMinionsKilled") + N(me, "neutralMinionsKilled")); r.Gold = (int)N(me, "goldEarned");
        r.Dmg = (int)N(me, "totalDamageDealtToChampions"); r.Vision = (int)N(me, "visionScore"); r.Minutes = min; r.Csm = r.Cs / min;
        if (opp != null) r.OppId = Rid(opp);
        foreach (Dictionary<string, object> p in parts)
        {
            bool afk = p.ContainsKey("wasAfk") && (bool)p["wasAfk"];
            if ((int)N(p, "teamId") == team) { if (afk) r.AllyAfk = true; } else if (afk) r.EnemyAfk = true;
            if (p == me) continue;
            if ((int)N(p, "teamId") == team) r.Allies.Add(Rid(p)); else r.Enemies.Add(Rid(p));
        }
        if (me.ContainsKey("teamIGNBSurrendered") && (bool)me["teamIGNBSurrendered"]) r.AllyAfk = true;
        r.Remake = dur < 300;
        if (r.Remake) return r;

        r.SoloKills = (int)N(ch, "soloKills"); r.MultiKill = Math.Min(5, (int)N(me, "largestMultiKill"));
        r.Outnumbered = (int)N(ch, "outnumberedKills"); r.Picks = (int)N(ch, "pickKillWithAlly"); r.Saves = (int)N(ch, "saveAllyFromDeath");
        r.Kp = 100 * N(ch, "killParticipation"); r.DmgShare = 100 * N(ch, "teamDamagePercentage"); r.TankShare = 100 * N(ch, "damageTakenOnTeamPercentage");
        r.Dpm = N(ch, "damagePerMinute"); r.Cs10 = (int)N(ch, "laneMinionsFirst10Minutes"); r.MaxCsAdv = (int)N(ch, "maxCsAdvantageOnLaneOpponent");
        r.Plates = (int)N(ch, "turretPlatesTaken"); r.Towers = (int)N(me, "turretTakedowns");
        if (opp != null) { r.GoldDiff = (int)(N(me, "goldEarned") - N(opp, "goldEarned")); r.XpDiff = (int)(N(me, "champExperience") - N(opp, "champExperience")); }

        // --- global
        double g = 0.75 * r.K + (r.D == 0 ? 4 : -1.5 * r.D) + 0.75 * r.A;
        g += Clamp(1.8 * (r.Csm - 6.1 / f), -10, 10);
        g += Clamp(0.04 * (N(ch, "goldPerMinute") - 350 / f), -10, 10);
        g += Clamp(N(ch, "damagePerMinute") * (shortGame ? 0.01125 : 0.00675), -10, 10);
        g += Clamp(7 * (N(ch, "visionScorePerMinute") - (shortGame ? 0.25 : 0.55)), -10, 10);
        if ((bool)me["firstBloodKill"]) g += 5;

        // --- lane (vs adversaire direct)
        double l = 0;
        if (opp != null)
        {
            l += 0.001 * (N(me, "champExperience") - N(opp, "champExperience"));
            l += 0.0015 * (N(me, "goldEarned") - N(opp, "goldEarned"));
            double ov = N(och, "visionScorePerMinute");
            l += ov > 0 ? 2 * (N(ch, "visionScorePerMinute") / ov - 1) : 0;
        }
        l += 0.08 * N(ch, "maxCsAdvantageOnLaneOpponent");
        l = Clamp(l, -5, 10);

        // --- objectifs (equipe)
        double o = 3 * teamKills("riftHerald") + (shortGame ? 4 : 2) * teamKills("dragon") + 2 * teamKills("baron") + 0.5 * teamKills("horde");
        o += Math.Min(4, 0.00012 * N(me, "damageDealtToObjectives")) + 3.5 * N(ch, "epicMonsterSteals");
        o = Math.Min(o, 20);

        // --- equipe
        double tm = 0.12 * 100 * N(ch, "killParticipation") - 3 + 0.03 * 100 * N(ch, "damageTakenOnTeamPercentage") + 0.1 * 100 * N(ch, "teamDamagePercentage");
        tm = Math.Min(tm, 10);

        double st = r.Win ? 3 : -3;

        // --- role bot
        double b = Math.Min(10, 1.5 * N(ch, "soloKills")) + 0.37 * (N(ch, "laneMinionsFirst10Minutes") - 51) + 0.75 * N(me, "turretTakedowns");
        if ((bool)O(myTeamObj, "tower")["first"]) b += 5;
        b += Math.Min(2.5, 0.3 * N(ch, "turretPlatesTaken"));
        b = Clamp(b, -5, 20);

        r.Global = g; r.LaneS = l; r.Obj = o; r.Team = tm; r.State = st; r.Role = b;
        r.Score = Math.Round(Clamp(15 + g + l + o + tm + st + b, 0, 100), 2);
        return r;
    }

    // ---------------------------------------------------------------- affichage

    static void Print(Result r)
    {
        Console.ForegroundColor = r.Remake ? ConsoleColor.Gray : r.Win ? ConsoleColor.Cyan : ConsoleColor.Red;
        Console.Write((r.Remake ? "REMAKE" : r.Win ? "VICTOIRE" : "DEFAITE") + "  " + r.Champ + " vs " + r.Opp + "  (" + (int)r.Minutes + ":" + ((int)(r.Minutes * 60) % 60).ToString("00") + ")");
        if (r.Afk) { Console.ForegroundColor = ConsoleColor.Red; Console.Write("   AFK"); }
        Console.ResetColor(); Console.WriteLine();
        Console.WriteLine("  KDA " + r.K + "/" + r.D + "/" + r.A + " (" + ((r.K + r.A) / Math.Max(1.0, r.D)).ToString("0.00") + ")   CS " + r.Cs + " (" + r.Csm.ToString("0.0") + "/min)   Degats " + r.Dmg + "   Gold " + r.Gold + "   Vision " + r.Vision);
        if (!r.Remake)
        {
            ScoreLine("DPM SCORE", r.Score);

            // notes par categorie (/10, a partir de la plage de chaque categorie du score)
            Console.Write("  ");
            Grade("Combat", (r.Global + 10) / 50 * 10);
            Grade("Lane", (r.LaneS + 5) / 15 * 10);
            Grade("Teamfight", r.Team);
            Grade("Objectifs", r.Obj / 15 * 10);
            Grade("Role bot", (r.Role + 5) / 25 * 10);
            Console.WriteLine();

            // detail : titre en blanc, valeur coloree (vert = bien, jaune = moyen, rouge = faible, gris = neutre)
            Line("Combat   ",
                Stat("solo kills", r.SoloKills.ToString(), Lvl(r.SoloKills, 2, 1)),
                Stat("meilleur multi-kill", MultiName[r.MultiKill], r.MultiKill >= 3 ? 2 : r.MultiKill == 2 ? 1 : -1),
                Stat("kills en inferiorite", r.Outnumbered.ToString(), r.Outnumbered > 0 ? 2 : -1),
                Stat("degats/min", r.Dpm.ToString("0"), Lvl(r.Dpm, 700, 450)));
            Line("Teamfight",
                Stat("KP", r.Kp.ToString("0") + "%", Lvl(r.Kp, 55, 40)),
                Stat("part des degats", r.DmgShare.ToString("0") + "%", Lvl(r.DmgShare, 25, 18)),
                Stat("degats encaisses", r.TankShare.ToString("0") + "%", r.TankShare <= 15 ? 2 : r.TankShare <= 22 ? 1 : 0), // ADC : peu encaisser = bien
                Stat("kills en equipe",r.Picks.ToString(), Lvl(r.Picks, 10, 5)),
                Stat("allies sauves", r.Saves.ToString(), r.Saves > 0 ? 2 : -1));
            Line("Lane     ",
                Stat("CS a 10 min", r.Cs10.ToString(), Lvl(r.Cs10, 70, 51)),
                Stat("avance max CS", r.MaxCsAdv.ToString(), Lvl(r.MaxCsAdv, 20, 8)),
                Stat("gold vs adv", r.GoldDiff.ToString("+0;-0;0"), Lvl(r.GoldDiff, 500, -500)),
                Stat("XP vs adv", r.XpDiff.ToString("+0;-0;0"), Lvl(r.XpDiff, 500, -500)),
                Stat("plates", r.Plates.ToString(), Lvl(r.Plates, 4, 2)),
                Stat("tourelles", r.Towers.ToString(), Lvl(r.Towers, 2, 1)));
            if (r.Lane != "BOTTOM") Console.WriteLine("  (formule calibree pour ADC : score approximatif sur " + r.Lane + ")");
        }
        Console.WriteLine();
    }

    static ConsoleColor ScoreColor(double s) { return s >= 70 ? ConsoleColor.Green : s >= 45 ? ConsoleColor.Yellow : ConsoleColor.Red; }
    static ConsoleColor LvlColor(int lvl) { return lvl == 2 ? ConsoleColor.Green : lvl == 1 ? ConsoleColor.Yellow : lvl == 0 ? ConsoleColor.Red : ConsoleColor.Gray; }
    static int Lvl(double v, double good, double mid) { return v >= good ? 2 : v >= mid ? 1 : 0; }

    struct StatItem { public string Name, Value; public int Level; }
    static StatItem Stat(string name, string value, int level) { return new StatItem { Name = name, Value = value, Level = level }; }

    static void Line(string title, params StatItem[] items)
    {
        Console.ForegroundColor = ConsoleColor.White; Console.Write("  " + title + " : ");
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write(" | "); }
            Console.ForegroundColor = ConsoleColor.Gray; Console.Write(items[i].Name + " ");
            Console.ForegroundColor = LvlColor(items[i].Level); Console.Write(items[i].Value);
        }
        Console.ResetColor(); Console.WriteLine();
    }

    static void Grade(string name, double note)
    {
        note = Clamp(note, 0, 10);
        string letter = note >= 8.5 ? "S" : note >= 7 ? "A" : note >= 5.5 ? "B" : note >= 4 ? "C" : "D";
        Console.ForegroundColor = ConsoleColor.White; Console.Write(name + " : ");
        Console.ForegroundColor = note >= 7 ? ConsoleColor.Green : note >= 4 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.Write(letter + " (" + note.ToString("0.0") + ")");
        Console.ResetColor(); Console.Write("   ");
    }

    // Ligne "DPM SCORE : 64.2" - libelle blanc, note coloree
    static void ScoreLine(string label, double score)
    {
        Console.ForegroundColor = ConsoleColor.White; Console.Write("  " + label + " : ");
        Console.ForegroundColor = ScoreColor(score); Console.WriteLine(score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Console.ResetColor();
    }

    // Compteur de joueurs recroises : "3(1)" = 3 fois avec moi, 1 fois contre moi
    class Met { public int Vs, VsWin, With, WithWin; }

    // rs = games sur lesquelles on compte ; only = si fourni, n'affiche que ces joueurs (ex. ceux des games du jour)
    static void Players(List<Result> rs, HashSet<string> only = null)
    {
        var m = new Dictionary<string, Met>();
        Func<string, Met> get = id => { Met x; if (!m.TryGetValue(id, out x)) { x = new Met(); m[id] = x; } return x; };
        foreach (var r in rs)
        {
            if (r.Remake) continue; // une rencontre reste une rencontre, meme avec un AFK
            foreach (string e in r.Enemies) { var x = get(e); x.Vs++; if (r.Win) x.VsWin++; }
            foreach (string a in r.Allies) { var x = get(a); x.With++; if (r.Win) x.WithWin++; }
        }
        var list = new List<KeyValuePair<string, Met>>();
        foreach (var kv in m) if (kv.Value.Vs + kv.Value.With >= 2 && (only == null || only.Contains(kv.Key))) list.Add(kv);
        list.Sort((a, b) => (b.Value.Vs + b.Value.With).CompareTo(a.Value.Vs + a.Value.With));

        Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("  Joueurs recroises  (avec moi (contre moi))");
        Console.ResetColor();
        if (list.Count == 0) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine("    aucun joueur croise 2 fois ou plus"); Console.ResetColor(); Console.WriteLine(); return; }
        for (int i = 0; i < list.Count && i < 15; i++)
        {
            string id = list[i].Key; Met x = list[i].Value;
            int games = x.Vs + x.With, wins = x.VsWin + x.WithWin;
            Console.Write("    " + (id.Length > 24 ? id.Substring(0, 24) : id).PadRight(25) + (x.With + "(" + x.Vs + ")").PadRight(7));
            WrPart("WR ", wins, games);
            if (x.With > 0) WrPart("   avec ", x.WithWin, x.With);
            if (x.Vs > 0) WrPart("   contre ", x.VsWin, x.Vs);
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    static void WrPart(string label, int wins, int games)
    {
        int wr = 100 * wins / games;
        Console.ForegroundColor = ConsoleColor.Gray; Console.Write(label);
        Console.ForegroundColor = wr >= 55 ? ConsoleColor.Green : wr >= 45 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.Write(wr + "%"); Console.ResetColor();
    }

    // " (60% WR)" ou, s'il y a des defaites avec AFK allie : " dont 1 AFK (55% WR, 60% hors AFK)"
    static string WrText(List<Result> rs)
    {
        int n = 0, w = 0, afk = 0;
        foreach (var r in rs) { if (r.Afk) afk++; if (r.Excluded) continue; n++; if (r.Win) w++; }
        string s = n > 0 ? " (" + (100 * w / n) + "% WR)" : "";
        return afk > 0 ? s + "   [" + afk + " game" + (afk > 1 ? "s" : "") + " AFK exclue" + (afk > 1 ? "s" : "") + "]" : s;
    }

    static void Summary(List<Result> rs)
    {
        int w = 0, l = 0, k = 0, d = 0, a = 0, n = 0; double sum = 0; Result best = null, worst = null;
        foreach (var r in rs)
        {
            if (r.Excluded) continue;
            if (r.Win) w++; else l++;
            k += r.K; d += r.D; a += r.A; sum += r.Score; n++;
            if (best == null || r.Score > best.Score) best = r;
            if (worst == null || r.Score < worst.Score) worst = r;
        }
        if (n == 0) return;
        double avg = sum / n;
        Console.WriteLine("------------------------------------------------------------");
        Console.Write("JOURNEE  " + w + "V - " + l + "D" + WrText(rs) + "   KDA " + k + "/" + d + "/" + a + " (" + ((k + a) / Math.Max(1.0, d)).ToString("0.00") + ")   ");
        Console.ForegroundColor = avg >= 70 ? ConsoleColor.Green : avg >= 45 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine("DPM moyen " + avg.ToString("0.0"));
        Console.ResetColor();
        if (n > 1) Console.WriteLine("  meilleure : " + best.Champ + " " + best.Score.ToString("0.0") + "   pire : " + worst.Champ + " " + worst.Score.ToString("0.0"));
        Console.WriteLine();
    }

    static string Clean(string s) { return (s ?? "").Replace(";", " ").Replace("|", " ").Replace("\r", "").Replace("\n", ""); }

    // ---------------------------------------------------------------- cache partage (score + score_tot)
    // une ligne par game : resultat + pseudos des 9 autres joueurs -> chaque game n'est telechargee qu'une fois

    static string CacheFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "score_tot_cache.csv"); } }
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    static Dictionary<string, Result> LoadCache(string puuid)
    {
        var cache = new Dictionary<string, Result>();
        if (!File.Exists(CacheFile)) return cache;
        foreach (string line in File.ReadAllLines(CacheFile, Encoding.UTF8))
        {
            string[] f = line.Split(';');
            if (f.Length < 16 || f[0] != puuid.Substring(0, 8)) continue;
            try
            {
                var c = new Result { Id = f[1], Champ = f[2], Win = f[3] == "1", Remake = f[4] == "1", K = int.Parse(f[5]), D = int.Parse(f[6]), A = int.Parse(f[7]), Cs = int.Parse(f[8]), Minutes = double.Parse(f[9], Inv), Score = double.Parse(f[10], Inv), Lane = f[11], OppId = f[12] };
                if (f[13] != "") c.Allies.AddRange(f[13].Split('|'));
                if (f[14] != "") c.Enemies.AddRange(f[14].Split('|'));
                c.AllyAfk = f[15].Contains("A"); c.EnemyAfk = f[15].Contains("E");
                cache[c.Id] = c;
            }
            catch (FormatException) { } // ligne abimee -> la game sera re-analysee
        }
        return cache;
    }

    static void AppendCache(StreamWriter w, string puuid, Result r)
    {
        w.WriteLine(string.Join(";", new[] { puuid.Substring(0, 8), r.Id, Clean(r.Champ), r.Win ? "1" : "0", r.Remake ? "1" : "0", r.K.ToString(), r.D.ToString(), r.A.ToString(), r.Cs.ToString(),
            r.Minutes.ToString("0.00", Inv), r.Score.ToString("0.00", Inv), r.Lane ?? "", Clean(r.OppId), string.Join("|", r.Allies.ConvertAll(Clean)), string.Join("|", r.Enemies.ConvertAll(Clean)), (r.AllyAfk ? "A" : "") + (r.EnemyAfk ? "E" : "") }));
        w.Flush();
    }

    // toutes les SoloQ (par paquets de 100)
    static List<string> AllIds(string puuid)
    {
        var ids = new List<string>();
        for (int start = 0; ; start += 100)
        {
            var page = GetList(Region + "/lol/match/v5/matches/by-puuid/" + puuid + "/ids?queue=420&start=" + start + "&count=100");
            foreach (object o in page) ids.Add((string)o);
            if (page.Count < 100) break;
        }
        return ids;
    }

    // resultats de toutes les games : depuis le cache, sinon depuis "known" (deja analysees en memoire), sinon telechargees
    static List<Result> History(string puuid, List<string> ids, Dictionary<string, Result> known, bool progress)
    {
        var cache = LoadCache(puuid);
        var results = new List<Result>();
        int fresh = 0;
        using (var w = new StreamWriter(CacheFile, true, Encoding.UTF8))
            for (int i = 0; i < ids.Count; i++)
            {
                Result r;
                if (!cache.TryGetValue(ids[i], out r))
                {
                    if (known == null || !known.TryGetValue(ids[i], out r))
                    {
                        if (progress || ids.Count - i > 3) Console.Write("\rAnalyse des nouvelles games... " + (i + 1) + "/" + ids.Count + "   ");
                        r = Analyse(ids[i], puuid); fresh++;
                    }
                    AppendCache(w, puuid, r);
                }
                results.Add(r);
            }
        if (fresh > 0) Console.Write("\r" + new string(' ', 60) + "\r");
        return results;
    }

    // ---------------------------------------------------------------- score_tot : toutes les SoloQ

    static void RunAll(string puuid, string riotId)
    {
        var ids = AllIds(puuid);
        var results = History(puuid, ids, null, true);

        Console.WriteLine("=== " + riotId + " - toutes les SoloQ (" + ids.Count + ") - " + DateTime.Now.ToString("dd/MM HH:mm") + " ===\n");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  #    Res  Champion       KDA        Ratio  CS/min  Duree   DPM");
        Console.ResetColor();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            Console.Write("  " + (results.Count - i).ToString().PadRight(4) + " ");
            Console.ForegroundColor = r.Remake ? ConsoleColor.Gray : r.Win ? ConsoleColor.Cyan : ConsoleColor.Red;
            Console.Write((r.Remake ? "RMK" : r.Win ? "V  " : "D  ") + "  ");
            Console.ResetColor();
            double ratio = (r.K + r.A) / Math.Max(1.0, r.D);
            Console.Write(r.Champ.PadRight(14) + " " + (r.K + "/" + r.D + "/" + r.A).PadRight(10) + " ");
            Console.ForegroundColor = ratio >= 3 ? ConsoleColor.Green : ratio >= 2 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.Write(ratio.ToString("0.00").PadLeft(5));
            Console.ResetColor();
            Console.Write("  " + (r.Cs / r.Minutes).ToString("0.0").PadLeft(6) + "  " + ((int)r.Minutes + ":" + ((int)(r.Minutes * 60) % 60).ToString("00")).PadLeft(5) + "  ");
            if (!r.Remake) { Console.ForegroundColor = ScoreColor(r.Score); Console.Write(r.Score.ToString("0.0").PadLeft(5)); Console.ResetColor(); }
            if (r.Afk) { Console.ForegroundColor = ConsoleColor.Red; Console.Write("  AFK"); Console.ResetColor(); }
            Console.WriteLine();
        }
        Console.WriteLine();

        // bilan global
        var played = results.FindAll(x => !x.Excluded);
        if (played.Count == 0) return;
        int wins = played.FindAll(x => x.Win).Count; double avg = 0; int k = 0, d = 0, a = 0;
        foreach (var r in played) { avg += r.Score; k += r.K; d += r.D; a += r.A; }
        avg /= played.Count;
        Console.WriteLine("------------------------------------------------------------");
        ScoreLine("DPM SCORE MOYEN", avg);
        Console.WriteLine("  " + played.Count + " games   " + wins + "V - " + (played.Count - wins) + "D" + WrText(results) + "   KDA moyen " + ((double)k / played.Count).ToString("0.0") + "/" + ((double)d / played.Count).ToString("0.0") + "/" + ((double)a / played.Count).ToString("0.0") + " (" + ((k + a) / Math.Max(1.0, d)).ToString("0.00") + ")");
        if (played.Count >= 10)
        {
            double last10 = 0; for (int i = 0; i < 10; i++) last10 += played[i].Score; last10 /= 10;
            Console.Write("  10 dernieres games : DPM moyen ");
            Console.ForegroundColor = ScoreColor(last10); Console.Write(last10.ToString("0.0")); Console.ResetColor();
            Console.WriteLine(last10 >= avg ? "  (en progression)" : "  (en baisse)");
        }

        Console.WriteLine();
        Players(results); // toutes les games (AFK compris) : une rencontre reste une rencontre

        // par champion
        Console.WriteLine("  Par champion :");
        var byChamp = new Dictionary<string, List<Result>>();
        foreach (var r in played) { if (!byChamp.ContainsKey(r.Champ)) byChamp[r.Champ] = new List<Result>(); byChamp[r.Champ].Add(r); }
        var champs = new List<string>(byChamp.Keys);
        champs.Sort((x, y) => byChamp[y].Count.CompareTo(byChamp[x].Count));
        foreach (string c in champs)
        {
            var l = byChamp[c]; int cw = l.FindAll(x => x.Win).Count; double cs = 0; int ck = 0, cd = 0, ca = 0;
            foreach (var r in l) { cs += r.Score; ck += r.K; cd += r.D; ca += r.A; }
            cs /= l.Count;
            double cr = (ck + ca) / Math.Max(1.0, cd);
            Console.Write("    " + c.PadRight(14) + (l.Count + " games").PadRight(10) + ((100 * cw / l.Count) + "% WR").PadRight(9));
            Console.Write(("KDA " + ((double)ck / l.Count).ToString("0.0") + "/" + ((double)cd / l.Count).ToString("0.0") + "/" + ((double)ca / l.Count).ToString("0.0")).PadRight(19) + "(");
            Console.ForegroundColor = cr >= 3 ? ConsoleColor.Green : cr >= 2 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.Write(cr.ToString("0.00"));
            Console.ResetColor();
            Console.Write(")   DPM ");
            Console.ForegroundColor = ScoreColor(cs); Console.WriteLine(cs.ToString("0.0")); Console.ResetColor();
        }
        Console.WriteLine();
    }

    static void Check(string puuid)
    {
        Console.WriteLine("Verification de la formule vs dpm.lol :\n");
        foreach (var kv in Reference)
        {
            var r = Analyse(kv.Key, puuid);
            Console.WriteLine(kv.Key + "  " + r.Champ.PadRight(12) + " calcule " + r.Score.ToString("0.00").PadLeft(6) + "   dpm.lol " + kv.Value.ToString("0.00").PadLeft(6) + "   ecart " + (r.Score - kv.Value).ToString("+0.00;-0.00"));
            Console.WriteLine("     global " + r.Global.ToString("0.00") + " lane " + r.LaneS.ToString("0.00") + " obj " + r.Obj.ToString("0.00") + " team " + r.Team.ToString("0.00") + " bot " + r.Role.ToString("0.00"));
        }
    }

    // ---------------------------------------------------------------- HTTP

    static string GetText(string url)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return GetTextOnce(url); }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                if (resp == null || (int)resp.StatusCode != 429 || attempt >= 10) throw;
                int wait; if (!int.TryParse(resp.Headers["Retry-After"], out wait)) wait = 10;
                Console.Write("\r(limite Riot atteinte, pause " + wait + " s...)          ");
                Thread.Sleep((wait + 1) * 1000);
            }
        }
    }

    static string GetTextOnce(string url)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Headers["X-Riot-Token"] = Key;
        req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        req.UserAgent = "score-light";
        using (var resp = req.GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            return sr.ReadToEnd();
    }
    static Dictionary<string, object> Get(string url) { return Json.Deserialize<Dictionary<string, object>>(GetText(url)); }
    static ArrayList GetList(string url) { return Json.Deserialize<ArrayList>(GetText(url)); }
}
