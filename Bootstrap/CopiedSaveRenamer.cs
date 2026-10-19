using System;
using System.IO;
using System.Text;

namespace DrakesTestLab.Bootstrap;

/// <summary>Rewrite internal world/character names inside copied save files so they match cfg file basenames.</summary>
internal static class CopiedSaveRenamer
{
    public static bool TryPatchDeployedWorld(string fwlPath, string newInternalName)
    {
        try
        {
            if (!File.Exists(fwlPath) || string.IsNullOrWhiteSpace(newInternalName))
                return false;

            var data = File.ReadAllBytes(fwlPath);
            if (data.Length < 12)
                return false;

            var nameOffset = 9;
            var oldLen = data[8];
            if (oldLen <= 0 || nameOffset + oldLen > data.Length)
                return false;

            var oldName = Encoding.UTF8.GetString(data, nameOffset, oldLen);
            if (string.Equals(oldName, newInternalName, StringComparison.Ordinal))
                return true;

            var patched = ReplaceLengthPrefixedUtf8(data, lenOffset: 8, oldName, newInternalName);
            if (patched == null)
                return false;

            WriteFwlTotalLength(patched);
            File.WriteAllBytes(fwlPath, patched);
            TestLabPlugin.Log?.LogInfo($"Patched world meta {Path.GetFileName(fwlPath)}: '{oldName}' -> '{newInternalName}'.");
            return true;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"World meta patch failed for {fwlPath}: {ex.Message}");
            return false;
        }
    }

    public static bool TryPatchDeployedCharacter(string fchPath, string newCharacterName, string newWorldName)
    {
        try
        {
            if (!File.Exists(fchPath) || string.IsNullOrWhiteSpace(newCharacterName))
                return false;

            var data = File.ReadAllBytes(fchPath);
            var originalLength = data.Length;
            var changed = false;

            if (TryReplaceCharacterNameField(ref data, PresetDeployer.TemplateCharacterInternalMaster, newCharacterName))
                changed = true;

            if (!string.IsNullOrWhiteSpace(newWorldName)
                && TryReplaceLinkedWorldNames(ref data, PresetDeployer.TemplateWorldInternalMaster, newWorldName))
            {
                changed = true;
            }

            if (!changed)
                return true;

            // .fch = [int32 dataLen][data][int32 hashLen][hash]; the name edits resize data, so keep dataLen honest.
            if (originalLength >= 4)
            {
                var dataLen = BitConverter.ToInt32(data, 0) + (data.Length - originalLength);
                Buffer.BlockCopy(BitConverter.GetBytes(dataLen), 0, data, 0, 4);
            }

            File.WriteAllBytes(fchPath, data);
            TestLabPlugin.Log?.LogInfo(
                $"Patched character save {Path.GetFileName(fchPath)} -> char '{newCharacterName}', world '{newWorldName}'.");
            return true;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Character save patch failed for {fchPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>00 80 00 {nameLen} {utf8 name}</summary>
    private static bool TryReplaceCharacterNameField(ref byte[] data, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return false;

        var oldBytes = Encoding.UTF8.GetBytes(oldName);
        var newBytes = Encoding.UTF8.GetBytes(newName);
        if (newBytes.Length > 255)
            return false;

        var marker = new byte[] { 0x00, 0x80, 0x00, (byte)oldBytes.Length };
        var index = IndexOf(data, marker);
        if (index < 0 || index + marker.Length + oldBytes.Length > data.Length)
            return false;

        if (!MatchesUtf8(data, index + marker.Length, oldBytes))
            return false;

        var newMarker = new byte[] { 0x00, 0x80, 0x00, (byte)newBytes.Length };
        var segmentLen = marker.Length + oldBytes.Length;
        var replacement = new byte[newMarker.Length + newBytes.Length];
        Buffer.BlockCopy(newMarker, 0, replacement, 0, newMarker.Length);
        Buffer.BlockCopy(newBytes, 0, replacement, newMarker.Length, newBytes.Length);
        data = ReplaceSegment(data, index, segmentLen, replacement);
        return true;
    }

    /// <summary>{nameLen} {utf8 name} — world linkage blocks inside .fch (e.g. 01 00 00 00 0d TestLabMaster).</summary>
    private static bool TryReplaceLinkedWorldNames(ref byte[] data, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return false;

        var oldBytes = Encoding.UTF8.GetBytes(oldName);
        var newBytes = Encoding.UTF8.GetBytes(newName);
        if (oldBytes.Length > 255 || newBytes.Length > 255)
            return false;

        var expectedPrefix = oldBytes.Length;
        var changed = false;

        for (var i = 0; i <= data.Length - oldBytes.Length - 1; i++)
        {
            if (data[i] != expectedPrefix)
                continue;

            if (!MatchesUtf8(data, i + 1, oldBytes))
                continue;

            var replacement = new byte[1 + newBytes.Length];
            replacement[0] = (byte)newBytes.Length;
            Buffer.BlockCopy(newBytes, 0, replacement, 1, newBytes.Length);
            data = ReplaceSegment(data, i, 1 + oldBytes.Length, replacement);
            changed = true;
            i += replacement.Length - 1;
        }

        return changed;
    }

    public static bool TryReadCharacterInternalName(byte[] data, out string name)
    {
        name = string.Empty;
        var markerPrefix = new byte[] { 0x00, 0x80, 0x00 };
        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (!MatchesBytes(data, i, markerPrefix))
                continue;

            var nameLen = data[i + 3];
            var nameOffset = i + 4;
            if (nameLen <= 0 || nameOffset + nameLen > data.Length)
                continue;

            name = Encoding.UTF8.GetString(data, nameOffset, nameLen);
            return true;
        }

        return false;
    }

    public static bool ContainsUtf8Text(byte[] data, string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return IndexOf(data, Encoding.UTF8.GetBytes(text)) >= 0;
    }

    private static byte[]? ReplaceLengthPrefixedUtf8(byte[] data, int lenOffset, string oldName, string newName)
    {
        var oldBytes = Encoding.UTF8.GetBytes(oldName);
        var newBytes = Encoding.UTF8.GetBytes(newName);
        if (newBytes.Length > 255)
            return null;

        var nameOffset = lenOffset + 1;
        if (!MatchesUtf8(data, nameOffset, oldBytes))
            return null;

        var segmentLen = 1 + oldBytes.Length;
        var replacement = new byte[1 + newBytes.Length];
        replacement[0] = (byte)newBytes.Length;
        Buffer.BlockCopy(newBytes, 0, replacement, 1, newBytes.Length);
        return ReplaceSegment(data, lenOffset, segmentLen, replacement);
    }

    private static void WriteFwlTotalLength(byte[] data)
    {
        if (data.Length < 4)
            return;

        var totalMinusFour = BitConverter.GetBytes(data.Length - 4);
        Buffer.BlockCopy(totalMinusFour, 0, data, 0, 4);
    }

    private static byte[] ReplaceSegment(byte[] data, int start, int oldLength, byte[] replacement)
    {
        var result = new byte[data.Length - oldLength + replacement.Length];
        Buffer.BlockCopy(data, 0, result, 0, start);
        Buffer.BlockCopy(replacement, 0, result, start, replacement.Length);
        Buffer.BlockCopy(data, start + oldLength, result, start + replacement.Length, data.Length - start - oldLength);
        return result;
    }

    private static int IndexOf(byte[] data, byte[] pattern)
    {
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (!MatchesBytes(data, i, pattern))
                continue;

            return i;
        }

        return -1;
    }

    private static bool MatchesBytes(byte[] data, int offset, byte[] pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (data[offset + i] != pattern[i])
                return false;
        }

        return true;
    }

    private static bool MatchesUtf8(byte[] data, int offset, byte[] utf8)
    {
        if (offset + utf8.Length > data.Length)
            return false;

        for (var i = 0; i < utf8.Length; i++)
        {
            if (data[offset + i] != utf8[i])
                return false;
        }

        return true;
    }
}
