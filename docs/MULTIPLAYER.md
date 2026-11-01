# Server mode and Player 2 (design)

Status: the server mod and Player 1's server connect mode are built and tested. **Player 2 is a design, not code.** This
document is the spec for building and testing it.

## The pieces

| Piece | What it is | Where |
| --- | --- | --- |
| Server mod | `DrakesTestLabServer`, server only. Puts a fresh lab world in place on every server start. | `server/` |
| Player 1 | The normal lab client in server connect mode (`[Bootstrap] Target = Server`). Owns the test cases and runs them. | repo root |
| Player 2 | A second lab client that only does what it is told. Runs no test cases. | to build |

Both clients join the same dedicated server. The Game Pass / Xbox build of Valheim only speaks the PlayFab (crossplay)
backend, so the server runs with crossplay on (Docker: `CROSSPLAY=true`). The server prints its public IP at start
("serverIP used to register the server"); that is what `[Connect] Address` takes. The join code changes every restart.

## What already works (Player 1 on the server)

Run on one PC against the Docker server: `DrakesTestLab-SelfTest` gave 8 passed, 2 failed, 1 skipped.
`11-virtual-player` fails by design: a virtual player (`peerJoin`) is an in-process fake connection, and "virtual players
need this game to be the server". Against a dedicated server there is no such game, so a second player has to be real.
That is the Player 2 work.

## Design

**Test cases live on Player 1. Player 2 gets commands, not test cases.** Nothing is downloaded: Player 1 sends one step
at a time and waits for the answer. A test case stays a single JSON file with one author's view of the scenario.

1. **Roles are decided by the server.** Each lab client sends `DTL_Hello(wantedRole)` when it spawns. The server mod
   keeps the table `role -> peer`: the first lab client to say `Player1` gets it, `Player2` is taken by the next one. A
   client that asks for a role that is taken is told no (and its connect check fails). The server answers
   `DTL_Role(role, peers)`, and Player 1 can ask `DTL_Who` at any time.
2. **Commands go through the server.** Player 1 calls `DTL_Cmd(targetRole, stepJson)`. The server checks the sender is
   Player 1, looks up the target peer and forwards. Player 2 runs the step and answers `DTL_Result(id, ok, detail)`;
   the server forwards that back. Both ends use the game's own routed RPC (`ZRoutedRpc`), so no extra port or backend.
3. **Same step vocabulary as the virtual player.** `peerJoin`, `peerMove`, `peerRpc`, `peerOwn`, `peerSet`, `peerClear`
   and the checks `peerConnected`, `zdoOwner`, `peerReceived` already exist (`Automation/PeerSteps.cs`). Add a
   transport behind them: *virtual* (today, in-process) or *remote* (a real Player 2). A test case does not change; the
   run decides which one a peer name resolves to. If no Player 2 is connected, a test that needs one is **skipped with
   the reason** (not passed).
4. **Player 2 is passive.** `[Connect] Role = Player2`: connect, spawn, say hello, then execute commands. It never runs
   `RunOnLaunch`, never builds the arena, never applies initial conditions that Player 1 owns.
5. **The server never trusts a client for the world.** Player 1's authority is for *tests*; the server still owns the
   world and the lab world is reset on its next start.

## Order of work

1. `DTL_Hello` / `DTL_Role` and a role table in the server mod. Test: Player 1 connects and logs "you are Player1".
2. Remote transport for the peer steps, with a fake Player 2 in the same game for a unit check.
3. `[Connect] Role` and the passive Player 2 client.
4. Make `11-virtual-player` run for real against the server with a second client and pass.
5. A skipped-with-reason result when a test needs Player 2 and none is connected.

## What to test with it, in order

1. **The lab's own Player 2 self-tests first** (`DrakesTestLab-SelfTest`, starting with `11-virtual-player`). These
   prove the transport (hello, roles, command relay, results) before any other mod is involved.
2. **LockSmith** (`drakethos/LockSmith`): ward and lock access between an owner (Player 1) and a stranger or permitted
   player (Player 2). The `LockSmith-Net` suite already describes these as peer steps.
3. **DrakesServerTeams** (`drakethos/DrakesServerTeams`): team membership, invites and shared state across two real
   players on a real server, which is exactly what a single-player lab cannot show.

Both mods need to be on the server and on both clients at the same versions.

## Testing it

Two real clients cannot run on one PC (one Game Pass licence, one PlayFab account). Use two machines, or one machine plus
a second account. Start the server (`docker compose up -d` in the server folder), start Player 2 first with
`Role = Player2`, then Player 1 with `-testlab-run DrakesTestLab-SelfTest -testlab-quit`. Expected: 11 tests, with
`11-virtual-player` running against the real second client.

## Open items

- `02-chest-opens` fails against the server (the inventory window is never visible after 5 s) but **passes locally**
  (local baseline: 10 passed, 0 failed, 1 skipped; against the server: 8 passed, 2 failed, 1 skipped). So it is a real
  server-mode difference. First suspect: the chest is created in the server's world and Player 1 does not get ownership
  of it in time to open it. Check chest ZDO ownership and latency before changing the test.
- Hexium store listing for the server mod: category names and first-upload steps are unverified.
