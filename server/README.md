# DrakesTestLabServer

**Server only.** The dedicated-server half of DrakesTestLab. On every server start it replaces the server's world with the shipped lab world (a levelled arena with a house of every crafting station), so each start is a clean lab for testing mods against a real server. The client side joins it with DrakesTestLab's server connect mode.

It does nothing on a game client: it checks for the dedicated server (batch mode) and stays inactive otherwise.

## Config (`BepInEx/config/com.drakemods.testlab.server.cfg`)

| Key | Default | |
| --- | --- | --- |
| `World.Mode` | `FreshEveryStart` | `FreshEveryStart`: clean lab on every start. `CreateIfMissing`: lab world only if the server has none. `Off`: never touch the world. |
| `World.WorldName` | empty | World to replace. Empty = the server's own `-world` name. |
| `World.KeepBackups` | `3` | Replaced worlds kept in `testlab_world_backups` beside the save folders. |

The replaced world is moved to the backup folder first, never deleted outright (unless `KeepBackups` is 0).

## Install

Copy `DrakeMods-DrakesTestLabServer` into the server's `BepInEx/plugins`. For the Docker setup: `config/bepinex/plugins`. Do not install the client DrakesTestLab on the server.
