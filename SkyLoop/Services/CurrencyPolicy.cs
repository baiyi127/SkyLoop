namespace SkyLoop.Services;

public enum TurnInCurrencyDecision { Continue, Exchange, BlockedByCap, InvalidConfiguration }

/// <summary>The trigger starts an exchange; it is not a required ending balance.</summary>
public static class CurrencyPolicy
{
    public static bool IsValidConfiguration(int threshold, int reserve, int cap, uint unitCost)
        => cap > 0 && threshold > 0 && threshold < cap && reserve >= 0 && reserve < threshold
            && unitCost > 0 && unitCost <= (long)cap - reserve;

    public static bool CanAfford(int balance, int reserve, uint unitCost)
        => balance >= 0 && reserve >= 0 && unitCost > 0 && (long)balance - reserve >= unitCost;

    public static bool ShouldExchange(int balance, int threshold, int reserve, uint unitCost)
        => threshold > 0 && reserve >= 0 && reserve < threshold && balance >= threshold
            && CanAfford(balance, reserve, unitCost);

    public static TurnInCurrencyDecision BeforeTurnIn(int balance, int threshold, int reserve,
        uint unitCost, int cap, uint nextReward, bool autoExchange)
    {
        if (balance < 0 || cap <= 0 || balance > cap || threshold <= 0 || threshold >= cap
            || reserve < 0 || reserve >= threshold
            || autoExchange && !IsValidConfiguration(threshold, reserve, cap, unitCost))
            return TurnInCurrencyDecision.InvalidConfiguration;

        if ((long)balance + nextReward > cap)
            return autoExchange && CanAfford(balance, reserve, unitCost)
                ? TurnInCurrencyDecision.Exchange : TurnInCurrencyDecision.BlockedByCap;

        return autoExchange && ShouldExchange(balance, threshold, reserve, unitCost)
            ? TurnInCurrencyDecision.Exchange : TurnInCurrencyDecision.Continue;
    }
}
