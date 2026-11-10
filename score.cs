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

            string last = null;
            do
            {
                var ids = GetList(Region + "/lol/match/v5/matches/by-puuid/" + puuid + "/ids?queue=420&start=0&count=2");
                string top = ids.Count > 0 ? (string)ids[0] : "";
                if (top != last)
                {
                    last = top;
                    Console.Clear();
                    Console.WriteLine("=== " + riotId + " - 2 dernieres SoloQ - " + DateTime.Now.ToString("HH:mm") + " ===\n");
                    foreach (object id in ids) Print(Analyse((string)id, puuid));
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
    }

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
        r.Remake = dur < 300;
        if (r.Remake) return r;

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
        b += Math.Min(2.5, 0.3 * BotPlatesTaken(matchId, team));
        b = Clamp(b, -5, 20);

        r.Global = g; r.LaneS = l; r.Obj = o; r.Team = tm; r.State = st; r.Role = b;
        r.Score = Math.Round(Clamp(15 + g + l + o + tm + st + b, 0, 100), 2);
        return r;
    }

    // Plaques detruites sur la tourelle bot adverse (timeline). Lecture par regex pour ne pas desérialiser ~1 Mo.
    static int BotPlatesTaken(string matchId, int myTeam)
    {
        string txt = GetText(Region + "/lol/match/v5/matches/" + matchId + "/timeline");
        int n = 0;
        foreach (Match m in Regex.Matches(txt, "\\{[^{}]*\"type\":\"TURRET_PLATE_DESTROYED\"[^{}]*\\}"))
        {
            string e = m.Value;
            if (!e.Contains("\"laneType\":\"BOT_LANE\"")) continue;
            var t = Regex.Match(e, "\"teamId\":(\\d+)");
            if (t.Success && int.Parse(t.Groups[1].Value) != myTeam) n++; // teamId = equipe proprietaire de la tourelle
        }
        return n;
    }

    // ---------------------------------------------------------------- affichage

    static void Print(Result r)
    {
        Console.ForegroundColor = r.Remake ? ConsoleColor.Gray : r.Win ? ConsoleColor.Cyan : ConsoleColor.Red;
        Console.WriteLine((r.Remake ? "REMAKE" : r.Win ? "VICTOIRE" : "DEFAITE") + "  " + r.Champ + " vs " + r.Opp + "  (" + (int)r.Minutes + ":" + ((int)(r.Minutes * 60) % 60).ToString("00") + ")");
        Console.ResetColor();
        Console.WriteLine("  KDA " + r.K + "/" + r.D + "/" + r.A + "   CS " + r.Cs + " (" + r.Csm.ToString("0.0") + "/min)   Degats " + r.Dmg + "   Gold " + r.Gold + "   Vision " + r.Vision);
        if (!r.Remake)
        {
            Console.ForegroundColor = r.Score >= 70 ? ConsoleColor.Green : r.Score >= 45 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.WriteLine("  DPM SCORE " + r.Score.ToString("0.00"));
            Console.ResetColor();
            Console.WriteLine("    global " + r.Global.ToString("0.0") + " | lane " + r.LaneS.ToString("0.0") + " | objectifs " + r.Obj.ToString("0.0") + " | equipe " + r.Team.ToString("0.0") + " | bot " + r.Role.ToString("0.0") + " | resultat " + r.State.ToString("0"));
            if (r.Lane != "BOTTOM") Console.WriteLine("    (formule calibree pour ADC : score approximatif sur " + r.Lane + ")");
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
