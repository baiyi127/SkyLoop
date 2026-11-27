using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyLoop.Services;

public enum PurchaseConfirmationMatch { Unknown, Match, Mismatch }

/// <summary>Checks a pending purchase against the separate item and currency text in SelectYesno.</summary>
public static class PurchaseConfirmation
{
    public static PurchaseConfirmationMatch Evaluate(bool purchaseIssued, bool ownedShopMatches,
        uint expectedItemId, uint actualItemId, long expectedCost, string currencyName, string prompt)
    {
        if (!purchaseIssued || expectedItemId == 0 || expectedCost <= 0)
            return PurchaseConfirmationMatch.Mismatch;
        if (!ownedShopMatches || actualItemId == 0 || string.IsNullOrWhiteSpace(prompt))
            return PurchaseConfirmationMatch.Unknown;
        if (actualItemId != expectedItemId || string.IsNullOrEmpty(currencyName))
            return PurchaseConfirmationMatch.Mismatch;
        var currencyIndex = prompt.IndexOf(currencyName, StringComparison.Ordinal);
        if (currencyIndex < 0) return PurchaseConfirmationMatch.Mismatch;
        var afterCurrency = prompt[(currencyIndex + currencyName.Length)..];
        var amount = Regex.Match(afterCurrency, @"^\s*[×xX]\s*(\d[\d,]*)");
        if (!amount.Success || !long.TryParse(amount.Groups[1].Value.Replace(",", ""),
                NumberStyles.None, CultureInfo.InvariantCulture, out var actualCost))
            return PurchaseConfirmationMatch.Mismatch;
        return actualCost == expectedCost ? PurchaseConfirmationMatch.Match : PurchaseConfirmationMatch.Mismatch;
    }
}
