# DrakesTestLabServer

**Server only. Install it on a dedicated server, never on a game client.** It is the server half of [DrakesTestLab](https://github.com/drakethos/DrakesTestLab): it gives a dedicated Valheim server a fresh lab world on every start, and tells two lab clients apart so one can test while the other follows.

On a game client it logs one line and does nothing (it checks for the dedicated server's batch mode).

## What it does

- **A fresh lab world on every start.** Before the server loads its world, the mod replaces it with the shipped lab world: a levelled arena with a house holding every crafting station. The world it replaces is moved to a backup folder first. Every restart is a clean lab for testing mods against a real server.
- **Player 1 and Player 2.** DrakesTestLab clients (version 0.4.0 and up) say hello to the server with the role they want. The server gives `Player1` and `Player2` to one client each and refuses a second client asking for a taken role. Player 1 runs the test cases; steps marked `"on": "player2"` go through the server to Player 2, and the answer comes back the same way. Clients never address each other directly: the server decides, and it only accepts commands from Player 1. It rides on the game's own routed RPC, so it needs no extra port.

## Config (`BepInEx/config/com.drakemods.testlab.server.cfg`)

| Key | Default | |
| --- | --- | --- |
| `World.Mode` | `FreshEveryStart` | `FreshEveryStart`: a clean lab on every start. `CreateIfMissing`: the lab world only if the server has none, then the server keeps its own saves. `Off`: never touch the world. |
| `World.WorldName` | empty | World to replace. Empty = the server's own `-world` name. |
| `World.KeepBackups` | `3` | Replaced worlds kept in `testlab_world_backups` beside the save folders. `0` = keep none. |

The replaced world is moved to the backup folder, never deleted outright (unless `KeepBackups` is `0`). If preparing the lab world fails, the server starts with the world it already has.

## Install

Copy `DrakeMods-DrakesTestLabServer` into the server's `BepInEx/plugins`. For the Docker setup (`lloesche/valheim-server`): `config/bepinex/plugins`. Do not install the client DrakesTestLab on the server.

## Connecting clients

- **Same game version and same mods everywhere.** The server and every client need the same Valheim version and the same mods (for example Jotunn, DrakeModsLibs and whatever you are testing), or the game refuses the client with "incompatible version".
- **Run the server with crossplay on** (Docker: `CROSSPLAY=true`). A Game Pass / Xbox client only speaks PlayFab, and a Steam client has to join through PlayFab too. The lab does that for you.
- **Use the server's public `IP:port`** in the client's `[Connect] Address`. The server prints it at start ("serverIP used to register the server"). The lab looks the server up on PlayFab by that IP.
- On the client set `[Bootstrap] Target = Server`, `[Connect] Address`, `[Connect] Password` and `[Connect] Role` (`Player1` or `Player2`). See the DrakesTestLab README.

## Version

0.1.0. See the [changelog](CHANGELOG.md) and the [design notes](https://github.com/drakethos/DrakesTestLab/blob/main/docs/MULTIPLAYER.md).
