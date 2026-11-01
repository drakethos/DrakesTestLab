# DrakesTestLabServer

## 0.1.0

First release: the server-only half of DrakesTestLab (needs DrakesTestLab 0.4.0 or newer on the clients to use the roles).

- **A fresh lab world on every start.** Before the dedicated server loads its world, the mod puts a fresh copy of the lab world in place (a levelled arena with a house of every crafting station). `World.Mode`: `FreshEveryStart` (default), `CreateIfMissing`, `Off`. The replaced world is moved to `testlab_world_backups` first (`World.KeepBackups`, default 3).
- **Player 1 / Player 2 roles and a command relay.** The server hands `Player1` and `Player2` to one lab client each (`DTL_Hello`), and relays commands from Player 1 to Player 2 and the answers back (`DTL_Cmd`, `DTL_ResultUp`). Only Player 1 may send commands; a role that is taken is refused.
- Does nothing on a game client.
