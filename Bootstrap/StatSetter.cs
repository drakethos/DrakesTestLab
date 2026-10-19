using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>Sets the player's vitals and movement. Max values are pinned through <see cref="TestRules"/> patches.</summary>
internal static class StatSetter
{
    private static readonly FieldInfo? Stamina = AccessTools.Field(typeof(Player), "m_stamina");
    private static readonly FieldInfo? Eitr = AccessTools.Field(typeof(Player), "m_eitr");

    private static float _baseRun;
    private static float _baseJump;
    private static Player? _basedFor;

    public static void SetMaxHealth(float value)
    {
        TestRules.MaxHealth = value;
        var p = Player.m_localPlayer;
        if (p == null || value <= 0f)
            return;

        p.SetMaxHealth(value, false);
        p.SetHealth(value);
    }

    public static void SetMaxStamina(float value)
    {
        TestRules.MaxStamina = value;
        var p = Player.m_localPlayer;
        if (p == null || value <= 0f)
            return;

        p.SetMaxStamina(value, false);
        Stamina?.SetValue(p, value);
    }

    public static void SetMaxEitr(float value)
    {
        TestRules.MaxEitr = value;
        var p = Player.m_localPlayer;
        if (p == null || value <= 0f)
            return;

        AccessTools.Method(typeof(Player), "SetMaxEitr")?.Invoke(p, new object[] { value, false });
        Eitr?.SetValue(p, value);
    }

    public static void FillAll()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return;

        p.SetHealth(p.GetMaxHealth());
        Stamina?.SetValue(p, p.GetMaxStamina());
        Eitr?.SetValue(p, p.GetMaxEitr());
    }

    /// <summary>Re-applies the speed multipliers every frame; the game rewrites these fields on equip changes.</summary>
    public static void Tick()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return;

        if (!ReferenceEquals(_basedFor, p))
        {
            _basedFor = p;
            _baseRun = p.m_runSpeed;
            _baseJump = p.m_jumpForce;
        }

        if (!Mathf.Approximately(TestRules.RunSpeedMultiplier, 1f) || !Mathf.Approximately(p.m_runSpeed, _baseRun))
            p.m_runSpeed = _baseRun * TestRules.RunSpeedMultiplier;

        if (!Mathf.Approximately(TestRules.JumpMultiplier, 1f) || !Mathf.Approximately(p.m_jumpForce, _baseJump))
            p.m_jumpForce = _baseJump * TestRules.JumpMultiplier;
    }
}
