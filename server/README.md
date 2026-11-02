# DrakesTestLabServer

**Server only. Install it on a dedicated server, never on a game client.**

This is the server half of [DrakesTestLab](https://github.com/drakethos/DrakesTestLab), the QA bench for Valheim modders. With it, you can test your mod the way players actually meet it: **on a dedicated server, with two real players**.

It does two things:

1. **A fresh lab world on every start.** Every server start is the same clean, levelled arena with a house of every crafting station, so a test run always begins from a known state.
2. **Player 1 and Player 2.** It tells two lab clients apart and passes commands between them, so one client runs the tests and the other acts as a real second player.

On a game client it logs one line and does nothing (it checks for the dedicated server).

## Why a server half?

The single-player lab already tests a lot, but some things only happen on a real server: who owns an object, which player's game answers a request, what the server refuses, what one player sees of another. A fake "virtual player" inside one game can imitate some of that, but not all of it. With a dedicated server and two real clients you test the real thing: the server decides, and each client sees only what a real client would.

## How it works with the client

```
 Player 1                    dedicated server                     Player 2
 (lab client,           (your mods + DrakesTestLabServer)        (lab client,
  Role = Player1)                                                 Role = Player2)
  runs the test   ──────►   gives out the roles    ──────────►   does what it is told,
  cases                     relays commands                       runs no tests
                  ◄──────   relays the answers     ◄──────────
```

- Both clients join the same server with **DrakesTestLab 0.4.0 or newer** in *server connect mode*. Each one asks the server for a role when it spawns. The server gives `Player1` to one client and `Player2` to another, and refuses a second client that asks for a role that is taken.
- **Player 1 runs the tests.** A test case is a JSON file on Player 1. Mark any step `"on": "player2"` and it does not run on Player 1: it goes to the server, the server forwards it to Player 2, Player 2 runs it with the same step engine, and the answer comes back the same way. The test fails with Player 2's reason if Player 2's step fails.
- **Player 2 runs no tests of its own.** It joins, spawns, says hello, and waits for commands.
- **The server decides.** Clients never talk to each other directly. The server accepts commands only from Player 1, and Player 2 accepts commands only from the server. It all rides on the game's own routed RPC, so it needs no extra port.

A test case looks like this:

```json
{
  "name": "Player 2 opens my chest",
  "steps": [
    { "do": "ping", "on": "player2", "connectTimeout": 120 },
    { "do": "spawn", "prefab": "piece_chest_wood", "dz": 3, "owner": "me", "as": "chest" },
    { "do": "wait", "seconds": 2 },
    { "do": "teleport", "on": "player2", "to": "chest", "distance": 2 },
    { "do": "interact", "on": "player2", "target": "chest" },
    { "do": "wait", "seconds": 1.5 },
    { "do": "check", "check": "zdoOwner", "target": "chest", "expect": "other" }
  ]
}
```

`ping` checks Player 2 is connected (it retries for `connectTimeout` seconds). An object Player 1 tagged (`"as": "chest"`) is sent to Player 2 as its prefab plus Player 1's position, so Player 2 finds the same object. See the DrakesTestLab README and `docs/MULTIPLAYER.md` for more.

## What you do with it

1. Set up the dedicated server (below) with this mod **and the mods you want to test**.
2. Install the same mods, and DrakesTestLab, on two game clients. Both need the same mod versions as the server.
3. Configure the clients (below), start **Player 2 first**, then start Player 1.
4. Run your tests from Player 1 (`F9` toolbox, or unattended: launch with `-testlab-run <suite> -testlab-quit`).
5. Read the results in Player 1's `BepInEx/config/DrakesTestLab/results/latest.html`.

## Set up the server

**Docker (`lloesche/valheim-server`)** is the easiest. A working `docker-compose.yml`:

```yaml
services:
  valheim:
    image: ghcr.io/lloesche/valheim-server
    container_name: valheim
    restart: unless-stopped
    cap_add: [sys_nice]
    stop_grace_period: 2m
    ports:
      - "2456-2458:2456-2458/udp"
    environment:
      SERVER_NAME: "My Test Lab"
      WORLD_NAME: "Dedicated"
      SERVER_PASS: "choose-a-password"   # 5+ characters
      SERVER_PUBLIC: "false"
      BEPINEX: "true"                    # installs BepInEx and starts the server through it
      CROSSPLAY: "true"                  # needed, see below
    volumes:
      - ./config:/config                 # worlds, bepinex/plugins, the mods' config files
      - ./data:/opt/valheim              # the server install
```

- **Mods:** put each mod's folder in `config/bepinex/plugins/` (for example `config/bepinex/plugins/DrakeMods-DrakesTestLabServer/`), then restart the container. Install the mods you are testing the same way.
- **Mod config files** are in `config/bepinex/` (for example `config/bepinex/com.drakemods.testlab.server.cfg`), **not** in `config/bepinex/config/`. Edit them with the server stopped, or edit and restart. Settings a mod synchronises to clients come from the server's copy.
- **Without Docker,** copy the mod folder into the server's `BepInEx/plugins` and run the server through BepInEx.
- **Crossplay is required.** A Game Pass / Xbox client only speaks the PlayFab (crossplay) network, and a Steam client has to join through PlayFab as well. The lab does that for you. The server needs internet access for PlayFab.
- **Ports.** Docker publishes UDP 2456-2458. If clients are on another network, forward UDP 2456-2458 on your router to the server PC and allow them in the firewall.
- **Find the address clients need.** At start the server prints `This is the serverIP used to register the server: <ip>:<port>`. That **public** IP and port is what the clients use. The lab looks the server up on PlayFab by that IP, so a client on the same network still uses the public address.

## Set up the clients

In each client's `BepInEx/config/com.drakemods.testlab.cfg`:

```ini
[Bootstrap]
Target = Server

[Connect]
Address = <server public IP>:2456
Password = <the server password>
Role = Player1        ; Player2 on the second client

[Save]
CharacterDisplayName = Bjorn       ; use a different name, e.g. Bjornita, on Player 2
```

- Use **different character names** on the two clients (checks that look up "the other player" go by name).
- **Both clients and the server need the same game version and the same mods**, or the game refuses the client with "incompatible version".
- Start **Player 2 first**, then Player 1. If a mod you test needs its test hooks, build it as a Debug build on the clients that call them.
- If the server cannot be reached, the lab writes a failed result and closes the game (`[Connect] QuitOnFailure`).

## Settings (`BepInEx/config/com.drakemods.testlab.server.cfg`)

| Key | Default | |
| --- | --- | --- |
| `World.Mode` | `FreshEveryStart` | `FreshEveryStart`: a clean lab world on every start. `CreateIfMissing`: the lab world only if the server has none, then the server keeps its own saves. `Off`: never touch the world. |
| `World.WorldName` | empty | World to replace. Empty = the server's own `-world` name. |
| `World.KeepBackups` | `3` | Replaced worlds kept in `testlab_world_backups` beside the save folders. `0` = keep none. |

The replaced world is moved to the backup folder, never deleted outright (unless `KeepBackups` is `0`). If preparing the lab world fails, the server starts with the world it already has. **Do not point this at a world you care about:** `FreshEveryStart` replaces the server's world on every start. Use a test server.

## Good to know

- **A server restart disconnects both clients, and the lab does not reconnect by itself.** After a restart, relaunch Player 2 first, then Player 1.
- **If you force-close a client,** the server keeps its old session for a short while. A relaunch right away can fail to join. Wait a couple of minutes, or restart the server.
- **"Incompatible version"** in the game means the mods or the game version differ between the server and the client.
- **Error 5003 / "timed out attempting to connect"** on a Steam client was the game choosing the Steam network, which a crossplay server never answers. DrakesTestLab 0.4.0 and newer joins through PlayFab instead. Make sure you use the server's public `IP:port`.
- **Checks on Player 2.** A `check` (or a `hook` with `expect`) that runs on Player 2 fails the command, and Player 1's step shows the reason (DrakesTestLab 0.4.1 and newer; older versions silently reported those as passing).
- **A tagged object is not shared.** Each command on Player 2 runs on its own; Player 1's tags do not carry over, which is why a tagged object is sent as its prefab plus a position.

## Version

See the [changelog](CHANGELOG.md) and the [design notes](https://github.com/drakethos/DrakesTestLab/blob/main/docs/MULTIPLAYER.md).

## Control and queries

Set `[Control] Enabled = true` to let Player 1 run commands on the server from a test case (`"do": "server"`, `"do": "serverConfig"`, `"do": "agree"`): change the admin, whitelist and blacklist, kick, read and change any BepInEx setting, call other server mods' test hooks, restart the server. `[Control] Queries` (default on) lets the lab read ZNet, ZDO and RPC information. Only Player 1 may use either. Details: `docs/MULTIPLAYER.md` in DrakesTestLab (0.4.2+).
