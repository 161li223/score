# score

A tiny personal Windows console tool that shows my **2 most recent ranked solo queue games** (League of Legends) and computes a performance score locally.

- KDA, CS/min, damage, gold, vision, game result
- A performance score for the ADC role, calculated from the match data (see [FORMULE.md](FORMULE.md))
- Very lightweight: a 16 KB executable, about 25 MB of RAM for about 1 second, then it exits

## Riot API usage

| API | Purpose |
|---|---|
| ACCOUNT-V1 `/riot/account/v1/accounts/by-riot-id` | Get my PUUID from my Riot ID |
| MATCH-V5 `/lol/match/v5/matches/by-puuid/{puuid}/ids` | Get my last 2 solo queue match IDs |
| MATCH-V5 `/lol/match/v5/matches/{matchId}` | Match details |
| MATCH-V5 `/lol/match/v5/matches/{matchId}/timeline` | Turret plate events |

- About 5 requests per refresh, for a single user.
- No data is stored, shared or published.
- The API key is kept locally in `riot_key.txt`, which is git-ignored and never committed.

## Build and run

The build uses the C# compiler included in Windows (.NET Framework 4.x), so there is nothing to install.

```
build.bat
score.exe                 # last 2 games of the default Riot ID
score.exe "Name#TAG"      # another Riot ID
score.exe --watch 120     # refresh automatically every 120 s
```

Put your Riot API key in `riot_key.txt`, next to the exe.

---

score isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games, and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
