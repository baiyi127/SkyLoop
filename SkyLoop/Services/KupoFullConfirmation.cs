using System;
using System.Text;

namespace SkyLoop.Services;

public enum KupoFullConfirmationResult { NotApplicable, Confirm, StopAtCap, Unowned }

/// <summary>Recognizes only the complete Kupo voucher-cap warning shown during our handover.</summary>
public static class KupoFullConfirmation
{
    public const string ExpectedPrompt = "确定要领取吗？\r\n库啵好运票的持有数量已达上限，递交该道具无法获得新的库啵好运章。";
    private static readonly string NormalizedExpectedPrompt = RemoveWhitespace(ExpectedPrompt);

    public static KupoFullConfirmationResult Evaluate(bool handoverIssued, uint expectedItemId,
        bool expectedNpcContext, string? prompt, bool continueWhenKupoFull)
    {
        // A mention of Kupo, an item name, or a partially loaded warning is not enough.
        // Ordinary handover confirmations remain the caller's separate responsibility.
        var normalized = RemoveWhitespace(prompt);
        if (!string.Equals(normalized, NormalizedExpectedPrompt, StringComparison.Ordinal))
            return KupoFullConfirmationResult.NotApplicable;
        if (!handoverIssued || expectedItemId == 0 || !expectedNpcContext)
            return KupoFullConfirmationResult.Unowned;
        return continueWhenKupoFull ? KupoFullConfirmationResult.Confirm : KupoFullConfirmationResult.StopAtCap;
    }

    private static string RemoveWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var normalized = new StringBuilder(text.Length);
        foreach (var character in text)
            if (!char.IsWhiteSpace(character)) normalized.Append(character);
        return normalized.ToString();
    }
}
