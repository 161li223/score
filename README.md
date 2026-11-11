# score

A tiny personal Windows console tool for my League of Legends ranked solo queue games. It computes a performance score locally, for my own use only.

Two programs are built from the same source:

- **`score.exe`** shows all my solo queue games of the day, plus a daily summary.
- **`score_tot.exe`** shows all my solo queue games since the beginning, plus overall and per-champion stats.

Features:

- KDA and ratio, CS/min, damage, gold, vision, and the game result
- A performance score for the ADC role, calculated from the match data (see [FORMULE.md](FORMULE.md))
- Grades per category: combat, lane, teamfight, objectives, bot role
- Detailed stats, colour-coded from good to bad: solo kills, multi-kills, kill participation, damage share, CS at 10 min, gold and XP difference with the lane opponent, and so on
- A counter of players met more than once, in the format `with me (against me)`, with my winrate for each
- Losses with an AFK teammate (no LP loss) are still listed, marked **AFK**, and excluded from all stats
- Very lightweight: a ~25 KB executable, about 35 MB of RAM for 1 to 2 seconds, then it exits

## Riot API usage

| API | Purpose |
|---|---|
| ACCOUNT-V1 `/riot/account/v1/accounts/by-riot-id` | Get my PUUID from my Riot ID |
| MATCH-V5 `/lol/match/v5/matches/by-puuid/{puuid}/ids` | Get my solo queue match IDs |
| MATCH-V5 `/lol/match/v5/matches/{matchId}` | Match details |

- Each match is downloaded **only once**. A small local cache (`score_tot_cache.csv`) stores the result and the Riot IDs of the other players, so a normal launch makes only a few requests.
- If the rate limit is reached (HTTP 429), the tool waits for `Retry-After` and retries.
- Single user. Nothing is shared or published: the cache and the API key stay on my computer.
- The API key is kept locally in `riot_key.txt`. That file and the cache are git-ignored and never committed.

## Build and run

The build uses the C# compiler included in Windows (.NET Framework 4.x), so there is nothing to install.

```
build.bat                 # builds score.exe and score_tot.exe
score.exe                 # today's games
score.exe "Name#TAG"      # another Riot ID
score.exe --watch 120     # refresh automatically every 120 s (or double-click score-auto.bat)
score_tot.exe             # all solo queue games since the beginning
score.exe --check         # compare the computed score with reference values
```

Put your Riot API key in `riot_key.txt`, next to the exe.

---

score isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games, and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
