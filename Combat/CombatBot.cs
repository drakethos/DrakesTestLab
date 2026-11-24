using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>How well the bot plays. Recorded in every result, so a number is never "mystery skill".</summary>
internal sealed class BotProfile
{
    public string Name = "trained";
    public float ReactionSeconds = 0.45f;   // delay between a foe starting an attack and the bot answering it
    public float BlockChance = 0.5f;        // per foe attack, when it has a shield (or any blocking weapon)
    public float DodgeChance = 0.2f;        // per foe attack that is not blocked, when stamina allows
    public float BlockSeconds = 0.55f;
    public float StaminaReserve = 0.25f;    // fraction of stamina kept for dodging before it keeps swinging

    // "Fighter" play (fighter, veteran): fights the way people do, not a button masher. Off for twig and trained.
    public bool Fighter;
    public float ParryChance;               // per foe attack it reads in time: raise the block just before the hit (Valheim: under 0.25 s = parry)
    public float ParryLead = 0.12f;         // how early before the predicted hit it raises the block
    public float TimingJitter = 0.08f;      // human error on that timing (standard deviation, seconds)
    public int ComboLength = 3;             // swings in a row before it checks stamina and spacing again
    public float BackOffBelow = 0.3f;       // stamina fraction: below this it backs off to recover (unless the foe is staggered)
    public float ResumeAbove = 0.6f;        // ... and comes back in above this
    public bool PunishStagger = true;       // a staggered foe takes double damage: go all in
    // Human imitation (fighter, veteran): people are not machines.
    public float ReactionSpread = 0.35f;    // reaction time varies (log-normal spread around ReactionSeconds)
    public float LapseChance = 0.1f;        // a foe swing it simply does not see coming
    public int ComboSpread = 1;             // combos of ComboLength +- this many swings
    public float WhiffChance = 0.06f;       // swings a step too early (out of reach) and misses
    public bool Strafe = true;              // circles sideways between combos instead of standing still
    public float CautiousBelow = 0.35f;     // health fraction: under it, more blocking and fewer swings
    // Stealth play (rogue): sneak up crouched on a foe that has not noticed you, get behind it and open with a backstab;
    // then fight hit-and-run, rolling out of the way instead of blocking.
    public bool Sneak;
    public float HitAndRun;
    // "you": nobody drives this player but you (live fights in the Combat tab). Your swings are still counted.
    public bool Manual;                 // seconds stepping back after each combo (0 = stand and strafe)

    public static BotProfile Get(string name) => name.ToLowerInvariant() switch
    {
        "twig" => new BotProfile { Name = "twig", ReactionSeconds = 0.9f, BlockChance = 0.1f, DodgeChance = 0f },
        "fighter" => new BotProfile
        {
            Name = "fighter", Fighter = true, ReactionSeconds = 0.3f, ParryChance = 0.45f, BlockChance = 0.4f, DodgeChance = 0.3f,
            ParryLead = 0.12f, TimingJitter = 0.09f, ComboLength = 3, BackOffBelow = 0.3f, ResumeAbove = 0.6f,
            ReactionSpread = 0.35f, LapseChance = 0.12f, WhiffChance = 0.07f
        },
        "veteran" => new BotProfile
        {
            Name = "veteran", Fighter = true, ReactionSeconds = 0.2f, ParryChance = 0.8f, BlockChance = 0.15f, DodgeChance = 0.5f,
            ParryLead = 0.1f, TimingJitter = 0.05f, ComboLength = 3, BackOffBelow = 0.25f, ResumeAbove = 0.55f,
            ReactionSpread = 0.25f, LapseChance = 0.05f, WhiffChance = 0.03f
        },
        "rogue" => new BotProfile
        {
            Name = "rogue", Fighter = true, Sneak = true, ReactionSeconds = 0.25f, ParryChance = 0.1f, BlockChance = 0.05f, DodgeChance = 0.85f,
            ParryLead = 0.12f, TimingJitter = 0.08f, ComboLength = 2, BackOffBelow = 0.35f, ResumeAbove = 0.6f, HitAndRun = 0.6f,
            ReactionSpread = 0.3f, LapseChance = 0.08f, WhiffChance = 0.05f
        },
        "you" or "manual" or "me" => new BotProfile { Name = "you", Manual = true },
        _ => new BotProfile { Name = "trained" }
    };

    /// <summary>The bot names, easiest first, with one line each (the Combat tab and the docs).</summary>
    public static readonly (string Name, string What)[] All =
    {
        ("twig", "new player: slow to react, rarely blocks, never rolls"),
        ("trained", "button masher with a shield: blocks half the hits, rolls a little"),
        ("fighter", "plays like a decent player: parries, rolls, backs off to recover stamina, punishes staggers"),
        ("veteran", "a skilled player: fast, parries most attacks, few mistakes"),
        ("rogue", "stealth build: sneaks up for a backstab, then hit and run with rolls")
    };
}

/// <summary>
/// Plays a Player the way a person with a controller would: walks up, faces the foe, swings with the game's own attack,
/// blocks and dodges foe attacks with some reaction delay. It sends its controls through Player.SetControls every
/// physics step; the real input is held off for that player while the bot drives (see the patch below), so the
/// bot and a keyboard never fight. Melee only in this first version.
/// </summary>
internal sealed class CombatBot
{
    private static readonly HashSet<Player> Driven = new();
    internal static bool InBotCall;

    public readonly Player Self;
    public readonly BotProfile Profile;
    public AttackSpec Attack = new();
    public int Primary, Secondary;     // swings of each kind, counted when the swing really started
    private readonly Func<IEnumerable<Character>> _foes;
    private readonly System.Random _rng;

    private readonly Dictionary<Character, float> _seenAttack = new();   // foe -> time its current attack began
    private float _blockUntil;
    private float _dodgeNotBefore;
    private readonly HashSet<Character> _answered = new();
    private bool _wasInAttack;
    private bool _lastWasSecondary;
    private float _lastSecondaryAt = -999f;
    private int _swingNo;
    private Vector3 _lastPos;
    private float _lastCheck;
    private bool _wasWalking;
    private float _unstickUntil;
    private float _unstickSide = 1f;

    public CombatBot(Player self, BotProfile profile, Func<IEnumerable<Character>> foes, int seed)
    {
        Self = self;
        Profile = profile;
        _foes = foes;
        _rng = new System.Random(seed);
        if (!profile.Manual) Driven.Add(self);
        Bots[self] = this;
    }

    public void Release()
    {
        Driven.Remove(Self);
        Bots.Remove(Self);
        Send(Vector3.zero, false, false, false, false, false);
    }

    public static bool Drives(Player p) => Driven.Contains(p);

    /// <summary>Call once per FixedUpdate (a coroutine on WaitForFixedUpdate).</summary>
    public void Step()
    {
        if (Self == null || Self.IsDead())
            return;

        // count real swings (the game may refuse one: no stamina, no such attack), by kind
        var inAttack = Self.InAttack();
        if (inAttack && !_wasInAttack)
        {
            _swingNo++;
            if (_lastWasSecondary) { Secondary++; _lastSecondaryAt = Time.time; } else Primary++;
        }
        if (!inAttack && _wasInAttack && _swingStartedAt > 0f)
            _swingSeconds = Mathf.Lerp(_swingSeconds, Mathf.Clamp(Time.time - _swingStartedAt, 0.3f, 2.5f), 0.4f);   // learn own swing length
        if (inAttack && !_wasInAttack) _swingStartedAt = Time.time;
        _wasInAttack = inAttack;
        if (Profile.Manual) return;          // you play: only count

        var foe = Nearest();
        if (foe == null)
        {
            Send(Vector3.zero, false, false, false, false, false);
            return;
        }

        var now = Time.time;
        var to = foe.transform.position - Self.transform.position;
        to.y = 0f;
        var dist = to.magnitude;
        var dir = dist > 0.001f ? to / dist : Self.transform.forward;

        // face the foe like the mouse would
        Self.transform.rotation = Quaternion.LookRotation(dir);
        Self.SetLookDir(dir);

        var weapon = Self.GetCurrentWeapon();
        var reach = Mathf.Max(1.6f, weapon?.m_shared.m_attack != null ? weapon.m_shared.m_attack.m_attackRange : 1.6f) + 0.4f;
        var gap = dist - foe.GetRadius();
        if (Profile.Fighter)
        {
            FighterStep(foe, now, gap, reach);
            return;
        }

        var foeAttacking = foe is Humanoid h && h.InAttack();
        if (foeAttacking)
        {
            if (!_seenAttack.ContainsKey(foe)) { _seenAttack[foe] = now; _answered.Remove(foe); }
        }
        else
        {
            _seenAttack.Remove(foe);
            _answered.Remove(foe);
        }

        var block = now < _blockUntil;
        var dodge = false;
        var move = Vector3.zero;
        var attack = false;
        var secondary = false;

        // answer a foe attack once, after the reaction delay
        if (foeAttacking && _seenAttack.TryGetValue(foe, out var since) && now - since >= Profile.ReactionSeconds && !_answered.Contains(foe) && gap < reach + 1.5f)
        {
            _answered.Add(foe);
            var roll = (float)_rng.NextDouble();
            if (CanBlock() && roll < Profile.BlockChance)
                _blockUntil = now + Profile.BlockSeconds;
            else if (now >= _dodgeNotBefore && StaminaFraction() > Profile.StaminaReserve && (float)_rng.NextDouble() < Profile.DodgeChance)
            {
                dodge = true;
                _dodgeNotBefore = now + 1.2f;
                move = new Vector3(_rng.Next(2) == 0 ? 1f : -1f, 0f, 0f);       // sideways: x is right of where we look
            }
        }

        block = now < _blockUntil;
        var jump = false;
        // stuck on a rock or a stump: not getting closer while walking at the foe. Jump and sidestep for a moment.
        if (now >= _unstickUntil && now - _lastCheck >= 0.6f)
        {
            var moved = (Self.transform.position - _lastPos).magnitude;
            if (_wasWalking && moved < 0.25f)
            {
                _unstickUntil = now + 0.5f;
                _unstickSide = _rng.Next(2) == 0 ? 1f : -1f;
                TestLabPlugin.Log?.LogDebug("Combat bot: stuck, sidestepping.");
            }
            _lastPos = Self.transform.position;
            _lastCheck = now;
        }
        if (now < _unstickUntil)
        {
            Send(new Vector3(_unstickSide, 0f, 0.6f), false, false, false, false, false, true);
            _wasWalking = false;
            return;
        }
        if (!block && !dodge)
        {
            if (gap > reach)
                move = new Vector3(0f, 0f, 1f);               // forward: SetControls takes movement relative to the look direction (z forward, x right)
            else if (!Self.InAttack() && StaminaFraction() > 0.05f)
            {
                attack = true;                                // in reach: swing again when the last swing is done
                secondary = WantSecondary(now);
                _lastWasSecondary = secondary;
            }
        }

        _wasWalking = move.z > 0.5f && !dodge;
        Send(move, attack && !secondary, attack && secondary, block, dodge, move.sqrMagnitude > 0.01f && gap > reach * 2f);
    }

    // ---- fighter play -------------------------------------------------------------------------------------------------
    private static readonly Dictionary<Player, CombatBot> Bots = new();
    private static readonly System.Reflection.FieldInfo? CurrentAttack = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
    private readonly Dictionary<Character, string> _attackKey = new();     // foe -> "Troll/attack_slam" for the swing in progress
    private readonly Dictionary<string, float> _hitDelay = new();   // per foe type: seconds from its swing starting to the hit landing (learned this fight)
    private readonly Dictionary<Character, int> _planKind = new();   // foe -> the answer planned for its swing in progress (1 block/parry)
    private readonly Dictionary<string, (int ok, int bad)> _blockTally = new();   // per attack: guards that held / did not (learned this fight)
    public int AvoidedBlocks;                                        // swings it rolled instead of blocking, after learning
    private float _defStart = -1f, _defEnd = -1f;
    private int _defKind;                                            // 0 none, 1 block (parry when timed), 2 dodge
    private bool _dodgeSent;
    private int _combo;
    private bool _backingOff;
    private float _pauseUntil;
    private float _swingStartedAt = -1f;
    private float _swingSeconds = 0.9f;
    public int Parried, Punishes;
    /// <summary>Team fights: the side of the foe this player fights from (world direction from the foe). Null = straight at it.</summary>
    public Vector3? FlankDir;
    public int ParryTries, BlockTries, DodgeTries, Lapses, Whiffs;
    public bool OpenedFromStealth;                       // rogue: the first hit landed while the foe had not noticed it
    public string OpenerNote = "";                       // rogue: how the opener ended (for the log)
    private float _openerStart = -1f;
    private bool _openerDone;
    private float _backstepUntil;
    private int _comboTarget = 3;
    private float _strafeSide = 1f;      // what it meant to do (the recorder counts what the game accepted)

    /// <summary>The recorder tells the bot when a foe's hit landed on it (blocked or not): that is how it learns the foe's timing.</summary>
    internal static void NoteHit(Character victim, Character? attacker, float damage = 0f, bool blocked = false)
    {
        if (attacker == null || victim is not Player p || !Bots.TryGetValue(p, out var bot) || !bot.Profile.Fighter) return;
        if (!bot._seenAttack.TryGetValue(attacker, out var start)) return;
        var delay = Time.time - start;
        if (delay < 0.1f || delay > 3f) return;
        var key = bot._attackKey.TryGetValue(attacker, out var k) ? k : attacker.name;
        bot._hitDelay[key] = bot._hitDelay.TryGetValue(key, out var d) ? Mathf.Lerp(d, delay, 0.5f) : delay;
        // like a person: a guard that did not hold (the hit got through, or the block still cost a big chunk of health)
        // counts against blocking that attack; a clean block counts for it
        if (bot._planKind.TryGetValue(attacker, out var plan) && plan == 1)
        {
            var bad = !blocked || damage > 0.2f * p.GetMaxHealth();
            (int ok, int bad) tally = bot._blockTally.TryGetValue(key, out var t) ? t : (0, 0);
            bot._blockTally[key] = bad ? (tally.ok, tally.bad + 1) : (tally.ok + 1, tally.bad);
            bot._planKind.Remove(attacker);
        }
    }

    /// <summary>Learned this fight: blocking this attack failed at least twice and more often than it held, so roll it instead.</summary>
    private bool AvoidBlocking(string key) => _blockTally.TryGetValue(key, out var t) && t.bad >= 2 && t.bad > t.ok;

    /// <summary>A human reaction time: log-normal around the profile's reaction (most fast, now and then a slow one).</summary>
    private float Reaction() => Profile.ReactionSeconds * Mathf.Exp(Gauss(Profile.ReactionSpread));

    private float Gauss(float sd)
    {
        var u1 = 1.0 - _rng.NextDouble(); var u2 = _rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2)) * sd;
    }

    /// <summary>
    /// How people fight: read the foe's swing and answer it at the right moment (parry just before the hit, else block or
    /// roll), hit back in short combos only when the swing will finish before the next hit lands, back off to get
    /// stamina back, and go all in on a staggered foe.
    /// </summary>
    private void FighterStep(Character foe, float now, float gap, float reach)
    {
        if (Profile.Sneak && !_openerDone && SneakOpener(foe, now, gap, reach))
            return;
        // a swing aimed at someone else (a teammate) is an opening, not a threat, unless I am right in its reach
        var foeTarget = foe.GetComponent<BaseAI>()?.GetTargetCreature();
        var aimedAtMe = foeTarget == null || foeTarget == Self || gap < 1.5f;
        var foeAttacking = foe is Humanoid h && h.InAttack() && aimedAtMe;
        var stamina = StaminaFraction();
        var staggered = foe.IsStaggering();

        // a new foe swing: plan the answer once
        if (foeAttacking && !_seenAttack.ContainsKey(foe))
        {
            _seenAttack[foe] = now;
            _combo = 0;
            // timing is learned per attack (a Troll's slam and swing land at different times), falling back to the foe's average
            var anim = CurrentAttack?.GetValue(foe) is Attack atk ? atk.m_attackAnimation : "";
            var key = foe.name + "/" + anim;
            _attackKey[foe] = key;
            var delay = _hitDelay.TryGetValue(key, out var d) ? d
                : _hitDelay.Where(kv => kv.Key.StartsWith(foe.name + "/")).Select(kv => kv.Value).DefaultIfEmpty(0.6f).Average();
            var hitAt = now + delay;
            var roll = (float)_rng.NextDouble();
            _defKind = 0; _dodgeSent = false;
            var hurt = Self.GetMaxHealth() > 0f && Self.GetHealth() / Self.GetMaxHealth() < Profile.CautiousBelow;
            var reaction = Reaction();
            if (gap < reach + 3f && (float)_rng.NextDouble() < Profile.LapseChance)
                Lapses++;                                                              // did not see it coming
            else if (gap < reach + 3f && reaction < delay + 0.1f)
            {
                if (hurt) roll *= 0.6f;                                                // hurt: more careful, defends more often
                var guardChance = Profile.ParryChance + Profile.BlockChance;
                if (CanBlock() && AvoidBlocking(key))
                {
                    // learned that this attack goes through the guard: roll it whenever it would have blocked
                    if (stamina > 0.15f && roll < Mathf.Min(0.9f, guardChance + Profile.DodgeChance))
                    {
                        _defKind = 2; DodgeTries++; AvoidedBlocks++;
                        _defStart = hitAt - 0.15f + Gauss(Profile.TimingJitter);
                        _defEnd = _defStart + 0.2f;
                    }
                }
                else if (CanBlock() && roll < Profile.ParryChance && delay >= Profile.ParryLead)
                {
                    _defKind = 1; ParryTries++;                                        // parry: block raised just before the hit
                    _defStart = Mathf.Max(now + reaction * 0.5f, hitAt - Profile.ParryLead + Gauss(Profile.TimingJitter));   // anticipates, but not faster than a glance
                    _defEnd = hitAt + 0.3f;
                }
                else if (CanBlock() && roll < Profile.ParryChance + Profile.BlockChance)
                {
                    _defKind = 1; BlockTries++;                                        // plain block: up as soon as it is seen
                    _defStart = now + reaction;
                    _defEnd = hitAt + 0.35f;
                }
                else if (stamina > 0.15f && (float)_rng.NextDouble() < Profile.DodgeChance)
                {
                    _defKind = 2; DodgeTries++;                                        // roll through the hit
                    _defStart = hitAt - 0.15f + Gauss(Profile.TimingJitter);
                    _defEnd = _defStart + 0.2f;
                }
            }
            _planKind[foe] = _defKind;
        }
        else if (!foeAttacking)
            _seenAttack.Remove(foe);

        var block = _defKind == 1 && now >= _defStart && now <= _defEnd;
        var dodge = false;
        var move = Vector3.zero;
        var attack = false;
        var secondary = false;
        if (_defKind == 2 && !_dodgeSent && now >= _defStart && now <= _defEnd)
        {
            dodge = true; _dodgeSent = true;
            move = new Vector3(_rng.Next(2) == 0 ? 1f : -1f, 0f, -0.3f);
        }
        if (now > _defEnd) _defKind = 0;

        if (!block && !dodge)
        {
            if (staggered && Profile.PunishStagger)
            {
                // double damage window: close in and swing, never mind combo or stamina reserve
                if (gap > reach) move = Vector3.forward;
                else if (!Self.InAttack() && stamina > 0.03f) { attack = true; if (!_wasPunishing) Punishes++; }
                _wasPunishing = true;
            }
            else
            {
                _wasPunishing = false;
                if (_backingOff && stamina >= Profile.ResumeAbove) _backingOff = false;
                if (!_backingOff && stamina < Profile.BackOffBelow) _backingOff = true;
                var nextHit = _defKind != 0 && _defStart > now ? _defStart : float.MaxValue;
                if (_backingOff)
                {
                    if (gap < reach + 3f) move = Vector3.back;                       // step out of reach and breathe
                }
                else if (now < _backstepUntil && !Self.InAttack() && now > _backstepUntil - Profile.HitAndRun)
                    move = new Vector3(_strafeSide * 0.5f, 0f, -1f);                 // hit and run: back out of reach
                else if (now < _pauseUntil && Profile.Strafe && !Self.InAttack())
                    move = new Vector3(_strafeSide, 0f, gap > reach ? 0.4f : 0f);   // between combos: circle, like people do
                else if (gap > reach && FlankDir is { } flank && FlankMove(foe, reach) is { } fm)
                    move = fm;                                                       // team: close in from my own side (in reach: fight)
                else if (gap > reach && !(gap < reach + 0.8f && (float)_rng.NextDouble() < Profile.WhiffChance * 0.1f))
                    move = Vector3.forward;
                else if (!Self.InAttack() && now >= _pauseUntil && now + _swingSeconds * 0.85f < nextHit)
                {
                    if (gap > reach) Whiffs++;                                       // swung a step too early
                    attack = true;
                    secondary = WantSecondary(now);
                    _lastWasSecondary = secondary;
                    var hurtNow = Self.GetMaxHealth() > 0f && Self.GetHealth() / Self.GetMaxHealth() < Profile.CautiousBelow;
                    if (++_combo >= (hurtNow ? Math.Max(1, _comboTarget - 1) : _comboTarget))
                    {
                        if (Profile.HitAndRun > 0f) _backstepUntil = now + _swingSeconds + Profile.HitAndRun;     // rogue: hit, then get out
                        _combo = 0;                                                   // end of a combo: reset footing, strafe a moment
                        _comboTarget = Math.Max(1, Profile.ComboLength + _rng.Next(-Profile.ComboSpread, Profile.ComboSpread + 1));
                        _pauseUntil = now + _swingSeconds + 0.3f + (float)_rng.NextDouble() * 0.5f;
                        _strafeSide = _rng.Next(2) == 0 ? 1f : -1f;
                    }
                }
            }
        }
        Send(move, attack && !secondary, attack && secondary, block, dodge, move.z > 0.5f && gap > reach * 2f);
    }

    private bool _wasPunishing;

    /// <summary>
    /// Rogue opener: while the foe has not noticed it, sneak (crouched) to a spot behind the foe and strike from there:
    /// Valheim gives an unaware target's hit the weapon's backstab bonus (knives the most). Ends when the foe is alerted
    /// or the first swing is out. Returns true while it is still sneaking.
    /// </summary>
    private bool SneakOpener(Character foe, float now, float gap, float reach)
    {
        var ai = foe.GetComponent<BaseAI>();
        if (_openerStart < 0f) _openerStart = now;
        if (ai == null || ai.IsAlerted())
        {
            _openerDone = true;
            var d = (foe.transform.position - Self.transform.position).magnitude;
            OpenerNote = ai == null ? "no AI" : now - _openerStart < 0.05f ? $"foe already alerted at the start ({d:0.0} m)" : $"spotted after {now - _openerStart:0.0} s at {d:0.0} m";
            return false;
        }
        var behind = foe.transform.position - foe.transform.forward * (foe.GetRadius() + reach * 0.7f);
        var world = behind - Self.transform.position;
        world.y = 0f;
        if (world.magnitude > 0.9f && !Self.InAttack())
        {
            var local = Quaternion.Inverse(Self.transform.rotation) * world.normalized;
            Send(new Vector3(local.x, 0f, local.z), false, false, false, false, false, false, true);
            return true;
        }
        if (!Self.InAttack() && gap <= reach)
        {
            OpenedFromStealth = true;
            _openerDone = true;
            OpenerNote = $"struck unseen after {now - _openerStart:0.0} s";
            Send(Vector3.zero, true, false, false, false, false, false, true);
            return true;
        }
        Send(Vector3.forward, false, false, false, false, false, false, true);
        return true;
    }

    /// <summary>
    /// Team fights: the move (relative to where the bot looks) that takes it to its flank spot beside the foe, or null
    /// when it is already there. Spread out like people do, so one swing cannot hit the whole team.
    /// </summary>
    private Vector3? FlankMove(Character foe, float reach)
    {
        if (FlankDir is not { } flank) return null;
        var spot = foe.transform.position + flank * (foe.GetRadius() + reach * 0.85f);
        var world = spot - Self.transform.position;
        world.y = 0f;
        if (world.magnitude < 1.2f) return null;
        var local = Quaternion.Inverse(Self.transform.rotation) * world.normalized;
        return new Vector3(local.x, 0f, local.z);
    }

    /// <summary>The next swing: primary, secondary, or a mix (every Nth swing, or one per interval).</summary>
    private bool WantSecondary(float now)
    {
        switch (Attack.Mode)
        {
            case AttackSpec.Kind.Secondary: return true;
            case AttackSpec.Kind.Mix:
                if (Attack.Seconds > 0f) return now - _lastSecondaryAt >= Attack.Seconds;
                return (_swingNo + 1) % Math.Max(2, Attack.EverySwings) == 0;
            default: return false;
        }
    }

    private Character? Nearest()
    {
        Character? best = null;
        var bestD = float.MaxValue;
        foreach (var c in _foes())
        {
            if (c == null || c.IsDead()) continue;
            var d = (c.transform.position - Self.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private bool CanBlock()
    {
        // the public inventory, not the protected Humanoid.GetLeftItem (compiles against the publicized assembly, throws in game)
        foreach (var it in Self.GetInventory().GetEquippedItems())
            if (it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                return true;
        return Self.GetCurrentWeapon()?.m_shared.m_blockPower > 0f;
    }

    private float StaminaFraction() => Self.GetMaxStamina() > 0f ? Self.GetStamina() / Self.GetMaxStamina() : 1f;

    /// <summary>Scenario "trace": true logs what each bot sees and does every 2 s (diagnosing stuck fights).</summary>
    public static bool Trace;
    private float _nextTrace;

    private void TraceLine(Vector3 move, bool attack, bool block, bool dodge)
    {
        if (!Trace || Time.time < _nextTrace) return;
        _nextTrace = Time.time + 2f;
        var foe = Nearest();
        var ai = foe != null ? foe.GetComponent<BaseAI>() : null;
        var d = foe != null ? foe.transform.position - Self.transform.position : Vector3.zero;
        var flat = new Vector3(d.x, 0f, d.z).magnitude;
        TestLabPlugin.Log?.LogInfo($"Combat trace {Self.GetPlayerName()}#{Self.GetInstanceID() % 1000}: foe {(foe != null ? foe.name : "-")} dist {flat:0.0} dy {d.y:0.0} radius {(foe != null ? foe.GetRadius() : 0f):0.0} " +
            $"hp {(foe != null ? foe.GetHealth() : 0f):0} sleep {(ai is MonsterAI m && m.IsSleeping())} alerted {(ai != null && ai.IsAlerted())} target {(ai?.GetTargetCreature() is { } t ? t.GetHoverName() + "#" + t.GetInstanceID() % 1000 : "-")} " +
            $"foeInAttack {(foe is Humanoid fh && fh.InAttack())} meInAttack {Self.InAttack()} stam {StaminaFraction():0.00} def {_defKind} " +
            $"pause {Mathf.Max(0f, _pauseUntil - Time.time):0.0} backoff {_backingOff} flank {FlankDir.HasValue} move {move} attack {attack} block {block} dodge {dodge} swings {Primary + Secondary}");
    }

    private void Send(Vector3 move, bool attack, bool secondary, bool block, bool dodge, bool run, bool jump = false, bool crouch = false)
    {
        TraceLine(move, attack || secondary, block, dodge);
        InBotCall = true;
        try
        {
            // movedir, attack, attackHold, secondaryAttack, secondaryAttackHold, block, blockHold, jump, crouch, run, autoRun, dodge
            Self.SetControls(move, attack, false, secondary, false, block, block, jump, crouch, run, false, dodge);
        }
        finally
        {
            InBotCall = false;
        }
    }
}

/// <summary>While a bot drives a player, controls from anywhere else (the keyboard, PlayerController) are ignored. Does nothing otherwise.</summary>
[HarmonyPatch(typeof(Player), "SetControls")]
internal static class CombatBot_Player_SetControls_Patch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Player __instance) => CombatBot.InBotCall || !CombatBot.Drives(__instance);
}
