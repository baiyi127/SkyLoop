using SkyLoop.Services;

const string screenshotPrompt = "确定要领取吗？\n库啵好运票的持有数量已达上限，递交该道具无法获得新的库啵好运章。";
const string clientTablePrompt = "确定要领取吗？\r\n库啵好运票的持有数量已达上限，无法获得新的库啵好运章。";
var fixtures = new (string Name, bool HandoverIssued, uint ItemId, bool NpcContext, string? Prompt, bool Continue, KupoFullConfirmationResult Expected)[]
{
    ("exact screenshot warning confirms our authorized handover", true, 31952, true, screenshotPrompt, true, KupoFullConfirmationResult.Confirm),
    ("a speculative question plus short reminder is not accepted", true, 31952, true, clientTablePrompt, true, KupoFullConfirmationResult.NotApplicable),
    ("Addon 12771 body alone is not a complete confirmation", true, 31952, true, "库啵好运票的持有数量已达上限，无法获得新的库啵好运章。", true, KupoFullConfirmationResult.NotApplicable),
    ("inserting the unverified character hou is not accepted", true, 31952, true, screenshotPrompt.Replace("递交该道具无法", "递交该道具后无法"), true, KupoFullConfirmationResult.NotApplicable),
    ("CRLF and surrounding whitespace do not change the recognized warning", true, 31952, true, " \t" + screenshotPrompt.Replace("\n", "\r\n") + "\r\n", true, KupoFullConfirmationResult.Confirm),
    ("Unicode spacing and line wrapping do not change the recognized warning", true, 31952, true, screenshotPrompt.Replace("\n", "\u3000\u00a0").Replace("递交", "递\t交"), true, KupoFullConfirmationResult.Confirm),
    ("the full warning also matches without line breaks", true, 31952, true, screenshotPrompt.Replace("\n", ""), true, KupoFullConfirmationResult.Confirm),
    ("an explicit stop-at-cap preference does not confirm", true, 31952, true, screenshotPrompt, false, KupoFullConfirmationResult.StopAtCap),
    ("no issued handover cannot authorize this warning", false, 31952, true, screenshotPrompt, true, KupoFullConfirmationResult.Unowned),
    ("no expected item cannot authorize this warning", true, 0, true, screenshotPrompt, true, KupoFullConfirmationResult.Unowned),
    ("a different NPC context cannot authorize this warning", true, 31952, false, screenshotPrompt, true, KupoFullConfirmationResult.Unowned),
    ("missing ownership still takes precedence over a stop-at-cap preference", false, 31952, true, screenshotPrompt, false, KupoFullConfirmationResult.Unowned),
    ("empty text is not recognized", true, 31952, true, "", true, KupoFullConfirmationResult.NotApplicable),
    ("missing text is not recognized", true, 31952, true, null, true, KupoFullConfirmationResult.NotApplicable),
    ("whitespace-only text is not recognized", true, 31952, true, "\r\n\t\u3000", true, KupoFullConfirmationResult.NotApplicable),
    ("mentioning only Kupo vouchers is not enough", true, 31952, true, "库啵好运票的持有数量已达上限。", true, KupoFullConfirmationResult.NotApplicable),
    ("a truncated warning remains outside this policy", true, 31952, true, "确定要领取吗？\n库啵好运票的持有数量已达上限，", true, KupoFullConfirmationResult.NotApplicable),
    ("a normal item handover remains outside this policy", true, 31952, true, "确定要递交复兴用的炖菜吗？", true, KupoFullConfirmationResult.NotApplicable),
    ("an unrelated discard warning is not recognized", true, 31952, true, "确定要丢弃库啵好运票吗？", true, KupoFullConfirmationResult.NotApplicable),
    ("an unrelated cancellation warning is not recognized", true, 31952, true, "确定要取消领取库啵好运章吗？", true, KupoFullConfirmationResult.NotApplicable),
    ("extra instructions after the exact warning are not accepted", true, 31952, true, screenshotPrompt + "\n确认后会丢弃背包物品。", true, KupoFullConfirmationResult.NotApplicable),
    ("the native additional Skybuilders scrip cap branch is not authorized", true, 31952, true, screenshotPrompt + "\n天穹街振兴票的持有数量已达上限，无法领取到全部天穹街振兴票。", true, KupoFullConfirmationResult.NotApplicable),
    ("an exact warning embedded in another prompt is not accepted", true, 31952, true, "确定要放弃任务吗？\n" + screenshotPrompt, true, KupoFullConfirmationResult.NotApplicable),
    ("a different full sentence is not accepted by substring matching", true, 31952, true, screenshotPrompt.Replace("无法获得新的", "仍然可以获得新的"), true, KupoFullConfirmationResult.NotApplicable),
};

var passed = 0;
foreach (var fixture in fixtures)
{
    var actual = KupoFullConfirmation.Evaluate(fixture.HandoverIssued, fixture.ItemId,
        fixture.NpcContext, fixture.Prompt, fixture.Continue);
    if (actual == fixture.Expected)
    {
        passed++;
        Console.WriteLine("PASS " + fixture.Name);
    }
    else
        Console.Error.WriteLine($"FAIL {fixture.Name}: expected {fixture.Expected}; got {actual}");
}
Console.WriteLine($"{passed}/{fixtures.Length} Kupo cap confirmation checks passed.");
return passed == fixtures.Length ? 0 : 1;
