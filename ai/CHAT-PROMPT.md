# Copy-paste prompt for a chat assistant

Paste the text below into ChatGPT, Claude.ai or any chat, then attach (or paste) `TESTLAB-AUTHORING.md` and the mod's
config file / source. Replace the parts in angle brackets.

---

You are helping me write automated tests for my Valheim mod <MOD NAME> using DrakesTestLab.
The attached TESTLAB-AUTHORING.md describes the test format, every step and check, and how test hooks work. Follow it exactly.

About my mod:
- What it does: <one or two sentences>
- Plugin GUID: <com.me.mymod>, BepInEx log source: <MyMod>
- Config: <paste the .cfg file or the Bind(...) calls>
- Rules worth testing: <who may do what, ownership, admin/VIP bypass, limits...>
- (Optional) my test sheet: <paste rows: id, title, steps, expected>

Please:
1. List every behaviour and config setting you will cover, and which ones need a person (manual).
2. Write `suite.json` with a configOverrides baseline for every setting the tests use.
3. Write the C# test hooks file (my own `TestHookAttribute`, no DrakesTestLab reference) calling my real code paths. Tell me which of my methods each hook calls; ask if you cannot see one.
4. Write one JSON test per case. Craft owned items with the `craft` step, use `playAs` for other players, `setConfig` for per-test settings, and put steps several tests repeat into a function (`_functions/`, called by name, with its own asserts).
5. Write a `<MOD>-Manual` suite with checkpoint steps for what needs eyes or real input.
6. Give me a coverage table: requirement -> Auto / Manual / Gap.

Output each file as a separate code block with its file name.
