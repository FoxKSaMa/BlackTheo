using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.BlackTheo;

/// <summary>
/// Small, dependency-free hex color parser for the custom disk color option.
/// Supports RRGGBB and RRGGBBAA, with an optional leading '#'.
/// </summary>
public static class HexColor
{
    public static readonly Color Fallback = new Color(80, 180, 255);

    public static bool TryParse(string? text, out Color color)
    {
        color = Fallback;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex.Substring(1);
        }

        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        byte r, g, b, a = 255;
        if (hex.Length == 6)
        {
            r = (byte)((value >> 16) & 0xFF);
            g = (byte)((value >> 8) & 0xFF);
            b = (byte)(value & 0xFF);
        }
        else
        {
            r = (byte)((value >> 24) & 0xFF);
            g = (byte)((value >> 16) & 0xFF);
            b = (byte)((value >> 8) & 0xFF);
            a = (byte)(value & 0xFF);
        }

        color = new Color(r, g, b, a);
        return true;
    }
}
