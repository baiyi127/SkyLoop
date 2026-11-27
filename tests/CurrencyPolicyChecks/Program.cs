using SkyLoop.Services;

var checks = new (string Name, Action Run)[]
{
    ("a low trigger starts only an affordable purchase and tolerates its remainder", () =>
    {
        Expect(CurrencyPolicy.ShouldExchange(65, 20, 0, 40), "65 can spend 40");
        Expect(!CurrencyPolicy.ShouldExchange(25, 20, 0, 40), "the successful 25-ticket remainder must not trigger another trip");
        Expect(CurrencyPolicy.BeforeTurnIn(25, 20, 0, 40, 10000, 40, true) == TurnInCurrencyDecision.Continue,
            "continue handing in eligible items to earn enough tickets");
    }),
    ("a reserve is protected at exact purchase affordability", () =>
    {
        Expect(!CurrencyPolicy.ShouldExchange(69, 40, 30, 40), "69 leaves only 39 spendable");
        Expect(CurrencyPolicy.ShouldExchange(70, 40, 30, 40), "70 can buy exactly one and preserve 30");
        Expect(!CurrencyPolicy.CanAfford(30, 30, 40), "reserved balance is never spendable");
    }),
    ("cap headroom overrides the trigger only when a purchase is possible", () =>
    {
        Expect(CurrencyPolicy.BeforeTurnIn(9950, 9999, 0, 40, 10000, 100, true) == TurnInCurrencyDecision.Exchange,
            "an affordable purchase must make room below the ordinary trigger");
        Expect(CurrencyPolicy.BeforeTurnIn(9950, 9999, 0, 10000, 10000, 100, true) == TurnInCurrencyDecision.BlockedByCap,
            "do not sacrifice tickets or route forever when neither submission nor purchase can progress");
    }),
    ("a reward reaching exactly the cap is allowed", () =>
        Expect(CurrencyPolicy.BeforeTurnIn(9950, 9999, 0, 40, 10000, 50, true) == TurnInCurrencyDecision.Continue,
            "exact-cap rewards fit; overflow means strictly greater")),
    ("disabled auto exchange does not interrupt submissions at the trigger", () =>
    {
        Expect(CurrencyPolicy.BeforeTurnIn(65, 20, 0, 0, 10000, 40, false) == TurnInCurrencyDecision.Continue,
            "an unused reward selection is not required when automatic exchange is off");
        Expect(CurrencyPolicy.BeforeTurnIn(9950, 9999, 0, 0, 10000, 100, false) == TurnInCurrencyDecision.BlockedByCap,
            "automatic submission must still protect the currency cap");
    }),
    ("zero unknown and reserve-impossible prices cannot form an automatic loop", () =>
    {
        Expect(!CurrencyPolicy.IsValidConfiguration(9000, 0, 10000, 0), "zero price is invalid");
        Expect(!CurrencyPolicy.IsValidConfiguration(9000, 2000, 10000, 8400), "the item can never be bought without spending reserved tickets");
        Expect(CurrencyPolicy.IsValidConfiguration(9999, 0, 10000, 10000), "a cap-priced item is valid even when it cannot yet be bought");
        Expect(CurrencyPolicy.BeforeTurnIn(65, 20, 0, 0, 10000, 40, true) == TurnInCurrencyDecision.InvalidConfiguration,
            "invalid automatic exchange settings must not silently permit endless crafting");
    }),
    ("invalid balances and oversized rewards do not overflow arithmetic", () =>
    {
        Expect(!CurrencyPolicy.CanAfford(-1, 0, 40), "negative balances cannot buy");
        Expect(!CurrencyPolicy.CanAfford(100, -1, 40), "negative reserves are not extra money");
        Expect(CurrencyPolicy.BeforeTurnIn(10001, 9000, 0, 40, 10000, 1, true) == TurnInCurrencyDecision.InvalidConfiguration,
            "a balance beyond the cap is inconsistent");
        Expect(CurrencyPolicy.BeforeTurnIn(25, 9000, 0, 40, 10000, uint.MaxValue, true) == TurnInCurrencyDecision.BlockedByCap,
            "wide addition must catch overflow rather than wrap into a permitted reward");
    }),
};

var passed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + check.Name + ": " + ex.Message); }
}
Console.WriteLine($"{passed}/{checks.Length} currency policy checks passed.");
return passed == checks.Length ? 0 : 1;

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
