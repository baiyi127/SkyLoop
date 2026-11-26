using System.Diagnostics.CodeAnalysis;

namespace SkyLoop.Services;

/// <summary>Maps the game's displayed 0–99 macro number directly to its native bank and slot.</summary>
public sealed class MacroAddress
{
    public const int MinimumNumber = 0;
    public const int MaximumNumber = 99;

    private MacroAddress(uint set, uint index)
    {
        Set = set;
        Index = index;
    }

    /// <summary>Native bank: 0 is individual, 1 is shared.</summary>
    public uint Set { get; }

    /// <summary>The native index is exactly the number displayed in the game's macro editor.</summary>
    public uint Index { get; }

    public static bool IsValidNumber(int number) => number is >= MinimumNumber and <= MaximumNumber;

    public static bool TryCreate(bool shared, int number, [NotNullWhen(true)] out MacroAddress? address)
    {
        if (!IsValidNumber(number))
        {
            address = null;
            return false;
        }
        address = new MacroAddress(shared ? 1u : 0u, (uint)number);
        return true;
    }
}
