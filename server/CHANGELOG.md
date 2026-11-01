# DrakesTestLabServer

## 0.1.0

- First release: the server-only half of DrakesTestLab. On a dedicated server it puts a fresh copy of the lab world in place before the server loads it, so every start is a clean lab. Does nothing on a game client.
- `World.Mode`: `FreshEveryStart` (default), `CreateIfMissing`, `Off`. The replaced world is moved to `testlab_world_backups` first (`World.KeepBackups`, default 3).
