using DesignatedDriver.Core;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.SelfTest;

/// <summary>Scan's board line names the board's own columns and the ones the workflow screens use.</summary>
public static partial class SelfTestRunner
{
    private static void ScanBoardLineChecks(Action<bool, string> check)
    {
        check(TextList.And([]) == "" && TextList.And(["Done"]) == "Done" && TextList.And(["Todo", "Done"]) == "Todo and Done"
            && TextList.And(["Todo", "In Progress", "Done"]) == "Todo, In Progress and Done",
            "Text list: one item alone, two joined with and, more with commas and a final and");

        string BoardLine(IReadOnlyList<string> columns)
        {
            var facts = new ScanFacts
            {
                OwnerAndName = "example-org/example-app",
                RemoteLookedUp = true,
                Details = new GhRepositoryDetails { FullName = "example-org/example-app" },
                LinkedBoards = [new ScanBoard(3, "Delivery", "", columns)],
            };
            return ScanRules.Assess(facts).Findings.FirstOrDefault(f => f.Code == ScanCode.BoardUsable)?.Text ?? "";
        }

        check(BoardLine(["Todo", "In Progress", "Done"]) == string.Format(Strings.Scan_BoardUsable, 3, "Delivery", "Todo, In Progress and Done", "Todo", "In Progress"),
            "Scan board line: GitHub's default columns are named, with Todo to pick up work and In Progress for work under way");
        var own = GhJson.Parse<GhProjectFieldList>(Sample("project-field-list.json")).StatusOptions;
        var line = BoardLine(own);
        check(line == string.Format(Strings.Scan_BoardUsable, 3, "Delivery", "Backlog, Ready, In progress, In review and Done", "Ready", "In progress")
            && !line.Contains("Todo", StringComparison.Ordinal) && !line.Contains("In Progress", StringComparison.Ordinal),
            "Scan board line: a board of its own is named with its real columns, Ready to pick up work and In progress for work under way");
    }
}
