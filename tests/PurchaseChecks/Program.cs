using SkyLoop.Services;

const string currency = "天穹街振兴票";
const string actualPrompt = "确定要用天穹街振兴票×40，\n换取以下道具吗？";
var checks = new (string Name, PurchaseConfirmationMatch Actual, PurchaseConfirmationMatch Expected)[]
{
    ("live nameless purchase prompt matches its separate item identity", Check(), PurchaseConfirmationMatch.Match),
    ("another item is rejected despite the same cost", Check(item: 13632), PurchaseConfirmationMatch.Mismatch),
    ("another cost is rejected despite the same item", Check(cost: 80), PurchaseConfirmationMatch.Mismatch),
    ("a dialog without an issued purchase is rejected", Check(issued: false), PurchaseConfirmationMatch.Mismatch),
    ("an unrelated shop cannot authorize confirmation", Check(shop: false), PurchaseConfirmationMatch.Unknown),
    ("empty native item waits without confirming", Check(item: 0), PurchaseConfirmationMatch.Unknown),
    ("empty prompt waits without confirming", Check(prompt: ""), PurchaseConfirmationMatch.Unknown),
    ("an item disposal prompt is rejected", Check(prompt: "确定要丢弃空贼白钢通货吗？"), PurchaseConfirmationMatch.Mismatch),
    ("a different currency is rejected", Check(prompt: "确定要用金币×40换取道具吗？"), PurchaseConfirmationMatch.Mismatch),
    ("batch total must match the pending purchase", Check(cost: 80, prompt: "确定要用天穹街振兴票×80换取以下道具吗？"), PurchaseConfirmationMatch.Match),
    ("grouped large costs are parsed exactly", Check(cost: 1040, prompt: "确定要用天穹街振兴票×1,040换取道具吗？"), PurchaseConfirmationMatch.Match),
    ("missing currency amount cannot authorize confirmation", Check(prompt: "天穹街振兴票不足。"), PurchaseConfirmationMatch.Mismatch),
};
foreach (var (name, actual, expected) in checks)
{
    if (actual != expected) throw new Exception($"FAIL {name}: {actual} != {expected}");
    Console.WriteLine("PASS " + name);
}
Console.WriteLine($"{checks.Length}/{checks.Length} purchase confirmation checks passed.");

static PurchaseConfirmationMatch Check(bool issued = true, bool shop = true, uint item = 13631,
    long cost = 40, string prompt = actualPrompt) =>
    PurchaseConfirmation.Evaluate(issued, shop, 13631, item, cost, currency, prompt);
