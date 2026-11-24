using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Foe behaviour switches for raw data. Meadows creatures flee when hurt or near fire and some never attack unless provoked, which
/// makes a fight meaningless for balance; "fearless" removes the fleeing, "aggro" makes them hunt the player at once.
/// Both default to off: real behaviour.
/// </summary>
internal static class CombatFoes
{
    private static readonly MethodInfo? SetAlerted = AccessTools.Method(typeof(BaseAI), "SetAlerted", new[] { typeof(bool) });
    private static readonly MethodInfo? SetTarget = AccessTools.Method(typeof(MonsterAI), "SetTarget", new[] { typeof(Character) });

    public static void Prepare(Character c, ScenarioSpec sc, Player target)
    {
        var ai = c.GetComponent<BaseAI>();
        if (ai == null)
            return;

        if (sc.Fearless)
        {
            ai.m_afraidOfFire = false;
            ai.m_avoidFire = false;
            if (ai is MonsterAI m)
            {
                m.m_fleeIfLowHealth = 0f;
                m.m_fleeIfNotAlerted = false;
                m.m_fleeIfHurtWhenTargetCantBeReached = false;
                m.m_fleeInLava = false;
            }
        }

        if (sc.Aggro || sc.Fearless)
        {
            ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);
            SetAlerted?.Invoke(ai, new object[] { true });
            if (ai is MonsterAI)
                SetTarget?.Invoke(ai, new object[] { target });
        }
    }
}
