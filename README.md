# Temnorid Arena: Core

`temnorid-arena-core` is the Temnorid Arena gameplay core: rounds, teams, economy, and match state.

It is a game package: it holds Temnorid Arena's own rules and content, built on the **IT-Kamianets 3D Engine** packages.

### Logic

v0.1 is an **endless free-for-all deathmatch against bots**, run by a network host so real players can join the same match. (Teams, rounds, an economy and objectives are on the roadmap.)

The server owns the match through `3d-unity-network`; clients only show it. Defeats are sent as `3d-network-schema` game messages (`temnorid-arena.player-defeated`), so the kill feed, replays and stats can be read outside Unity.

Every other `temnorid-arena-*` package builds on this one.

### Status

Version 0.1.

* `Combatant` (players and bots): health, mana with regeneration, a damage shield, alive/dead, display name, kills and deaths, all as network variables changed by the server; `ApplyDamage` (shield first), `TrySpendMana`, `GiveShield`, `Respawn`; turns off listed components (controls, caster) while dead and hides the body unless `hideBodyWhenDead` is off (models with a death animation lie where they fell); `Defeated`, `Damaged`, `AliveChanged` events.
* `ArenaMatch` (a network prefab spawned by `ArenaMatchSpawner` when a session starts): spawns the bots, builds the scoreboard (`Scores`) from every combatant's kills and deaths (per-combatant variables rather than one shared network list, so a client joining mid-match never sees a row twice), respawns the defeated after 3 s at the spawn point farthest from enemies, defeats anyone who falls below the arena, sets the session running on the arena, and publishes `temnorid-arena.player-defeated`.
* `IArenaSpawns`: spawn points, provided by the map.

Checked in Play mode: "Play vs bots" starts a host; 5 bots spawn and fight; kills and deaths count; defeated combatants respawn at spawn points; the session is running on `first-arena` in `temnorid-arena.deathmatch` mode.

### Roadmap

* Teams and players
* Round loop and match state
* Win conditions: elimination and objective
* Economy: round rewards and buying
* Match settings (rounds, timers)
* Match events in 3d-network-schema
* Match results to Firebase stats
