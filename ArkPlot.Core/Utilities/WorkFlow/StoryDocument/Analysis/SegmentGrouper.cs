using ArkPlot.Core.Model;
using EntryGroups = System.Collections.Generic.List<System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>>;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// 分段器：将行列表按分隔规则切分为组。
/// 三层分段策略：显式 --- 分隔符 → playmusic 场景转换 → 16行+分隔线隐式切段。
/// </summary>
public static class SegmentGrouper
{
    private const int MinSegmentSize = 16;

    public static EntryGroups Group(EntryList lineList)
    {
        EntryGroups groups = new();
        EntryList temp = new();

        foreach (var item in lineList)
        {
            if (item.MdText.Trim() == "---")
            {
                if (temp.Count > 0)
                {
                    RemoveLeadingDashes(temp);
                    groups.Add(new EntryList(temp));
                    temp = new EntryList();
                }
                continue;
            }

            if (item.Type.Equals("playmusic", StringComparison.OrdinalIgnoreCase))
            {
                if (temp.Count > 0)
                {
                    RemoveLeadingDashes(temp);
                    groups.Add(new EntryList(temp));
                    temp = new EntryList();
                }
                temp.Add(item);
                continue;
            }

            if (!ShouldSplit(item, temp))
            {
                temp.Add(item);
                continue;
            }

            RemoveLeadingDashes(temp);
            groups.Add(new EntryList(temp));
            temp = new EntryList();
            temp.Add(item);
        }

        if (temp.Count > 0)
        {
            RemoveLeadingDashes(temp);
            groups.Add(new EntryList(temp));
        }

        return groups;
    }

    private static bool ShouldSplit(FormattedTextEntry item, EntryList current)
    {
        return current.Count >= MinSegmentSize && IsSeparator(item);
    }

    private static bool IsSeparator(FormattedTextEntry item)
    {
        return item.MdText.StartsWith('-');
    }

    private static void RemoveLeadingDashes(EntryList entries)
    {
        entries.ForEach(item =>
        {
            if (item.MdText.StartsWith('-'))
                item.MdText = "";
        });
    }
}