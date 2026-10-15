using ZhaoHuoSystem;

internal static partial class Program
{
    private sealed class EqualButDifferent
    {
        public override bool Equals(object? obj) => obj is EqualButDifferent;
        public override int GetHashCode() => 1;
    }
    private static void ValueTests()
    {
        Test("final stat targets map to base without changing the addition", () =>
        {
            Require(ValueSemantics.TryRange(0, 100, 20, 120, out int min, out int max), "Range missing");
            Equal(20, min); Equal(120, max);
            Require(ValueSemantics.TryBase(75, 10, 0, 100, 20, 120, out int value), "Final target rejected"); Equal(55, value);
            Require(!ValueSemantics.TryBase(19, 10, 0, 100, 20, 120, out _), "Unattainable final target allowed");
        });
        Test("positive and negative additions respect both native clamps", () =>
        {
            Require(ValueSemantics.TryRange(0, 100, -20, 100, out int min, out int max), "Negative range missing"); Equal(0, min); Equal(80, max);
            Require(ValueSemantics.TryBase(0, 0, 0, 100, -20, 100, out int zero), "Lower clamped target rejected"); Equal(0, zero);
            Require(ValueSemantics.TryBase(100, 10, 0, 100, 20, 100, out int cap), "Upper clamped target rejected"); Equal(80, cap);
            Require(ValueSemantics.TryBase(100, 99, 0, 100, 20, 100, out int unchanged), "Existing cap rejected"); Equal(99, unchanged);
            Require(!ValueSemantics.TryBase(81, 50, 0, 100, -20, 100, out _), "Negative addition ignored");
        });
        Test("all targets in a safe stat interval have an exact base solution", () =>
        {
            foreach (int addition in new[] { -60, -1, 0, 1, 60 })
            {
                Require(ValueSemantics.TryRange(-30, 70, addition, 90, out int min, out int max), "Range missing");
                for (int target = min; target <= max; target++)
                {
                    Require(ValueSemantics.TryBase(target, 0, -30, 70, addition, 90, out int baseValue), "Reachable integer rejected");
                    Equal(target, Math.Clamp(baseValue + addition, -30, 90));
                }
            }
        });
        Test("invalid and overflowing ranges cannot expose editable stat targets", () =>
        {
            Require(!ValueSemantics.TryRange(10, 0, 0, 100, out _, out _), "Reversed range allowed");
            Require(!ValueSemantics.TryRange(0, int.MaxValue, 1, int.MaxValue, out _, out _), "Native addition overflow allowed");
            Require(!ValueSemantics.TryRange(int.MinValue, 0, -1, 100, out _, out _), "Negative native overflow allowed");
        });
        Test("write verification reads actual output once and never retries a mismatching write", () =>
        {
            int writes = 0, reads = 0, actual = 0;
            bool success = ValueSemantics.WriteAndVerify(70, target => { writes++; actual = target - 2; }, () => { reads++; return actual; }, out int readback);
            Require(!success, "Mismatch claimed as success"); Equal(68, readback); Equal(1, writes); Equal(1, reads);
            Require(ValueSemantics.WriteAndVerify(10, target => actual = target, () => actual, out readback), "Exact readback failed"); Equal(10, readback);
        });
        Test("condition targets are displayed final values and check integer edges", () =>
        {
            Equal<int?>(41, ValueSemantics.Suggested(40, ">")); Equal<int?>(39, ValueSemantics.Suggested(40, "<"));
            Equal<int?>(40, ValueSemantics.Suggested(40, ">=")); Equal<int?>(null, ValueSemantics.Suggested(int.MaxValue, ">"));
            Equal<int?>(null, ValueSemantics.Suggested(int.MinValue, "<"));
        });
        Test("native attribute and sect pages override categories in verified order without merging cumulative counters", () =>
        {
            // Native PlayerBonusStatData bindings, not inferred from display names.
            var expected = new[]
            {
                ("life", 0), ("stamina", 0), ("dexterity", 0), ("m-sword", 0),
                ("m-fist", 0), ("m-projectile", 0), ("talking", 0), ("literacy", 0),
                ("poison-resistance", 0), ("paralysis-resistance", 0), ("disposition", 1), ("behaviour", 1),
                ("karma", 2), ("training", 2), ("money", 99), ("martial-point", 0), ("poison", 0), ("weapon", 0)
            };
            for (int index = 0; index < expected.Length; index++)
            {
                string key = "PlayerStat/" + expected[index].Item1;
                Equal("attributes", ValueSemantics.GroupId(key, expected[index].Item2));
                Equal(index, ValueSemantics.AttributeOrder(key));
            }
            var ordered = expected.Reverse().OrderBy(item => ValueSemantics.AttributeOrder("PlayerStat/" + item.Item1)).ToArray();
            Equal(18, ordered.Select(item => item.Item1).Distinct().Count());
            Equal(string.Join(",", expected.Select(item => item.Item1)), string.Join(",", ordered.Select(item => item.Item1)));
            var propertyPage = new[] { ("mental", 99), ("internal", 1), ("fate", 0) };
            for (int index = 0; index < propertyPage.Length; index++)
            {
                string key = "PlayerStat/" + propertyPage[index].Item1;
                Equal("attributes", ValueSemantics.GroupId(key, propertyPage[index].Item2));
                Equal(18 + index, ValueSemantics.AttributeOrder(key));
            }
            var studies = new[] { "buddhism", "confucianism", "taoism" };
            for (int index = 0; index < studies.Length; index++)
            {
                string key = "PlayerStat/" + studies[index];
                Equal("attributes", ValueSemantics.GroupId(key, 0)); Equal(21 + index, ValueSemantics.AttributeOrder(key));
            }
            foreach (string retained in new[] { "m-internal", "m-leg", "m-odd", "m-soft", "m-stick" })
            {
                Equal("hidden", ValueSemantics.GroupId("PlayerStat/" + retained, 3));
                Require(ValueSemantics.IsRetainedMartial("PlayerStat/" + retained), "Retained source key missing");
            }
            Require(!ValueSemantics.IsRetainedMartial("PlayerStat/stamina") && !ValueSemantics.IsRetainedMartial("PlayerStat/internal"), "Actual attribute confused with retained stat");
            Equal("general", ValueSemantics.GroupId("PlayerStat/unverified", 0));
            Equal("other", ValueSemantics.GroupId("unverified/money", 99));
            Equal(int.MaxValue, ValueSemantics.AttributeOrder("unverified/life"));
            Equal(int.MaxValue, ValueSemantics.SafeCatalogOrder(() => throw new InvalidOperationException("unreadable term")));
            var sect = new[] { ("faction", 99), ("fame", 0), ("people", 0), ("team", 0), ("assets", 99) };
            for (int index = 0; index < sect.Length; index++)
            {
                string key = "PlayerStat/" + sect[index].Item1;
                Equal("sect", ValueSemantics.GroupId(key, sect[index].Item2));
                Equal(24 + index, ValueSemantics.CatalogOrder(key));
            }
            Equal("hidden", ValueSemantics.GroupId("PlayerStat/total-martial-point", 9));
            Equal("hidden", ValueSemantics.GroupId("PlayerStat/total-poison-point", 9));
            Equal("other", ValueSemantics.GroupId("PlayerStat/faction-contribution", 99));
        });
        Test("native enum keys preserve zero, sparse lover values and informational editor semantics", () =>
        {
            Equal(26, NativeEnumSemantics.Values("PlayerStat/title").Count);
            Equal(10, NativeEnumSemantics.Values("PlayerStat/lover").Count);
            Require(NativeEnumSemantics.TryLocaleKey("PlayerStat/title", 0, out var title), "Title zero missing"); Equal("PlayerInfo/Title/0", title);
            Require(NativeEnumSemantics.TryLocaleKey("PlayerStat/lover", 0, out var lover), "Lover zero missing"); Equal("PlayerInfo/Lover/0", lover);
            Require(NativeEnumSemantics.TryLocaleKey("PlayerStat/lover", 99, out lover), "Lover self value missing"); Equal("PlayerInfo/Lover/99", lover);
            foreach (int unknown in new[] { -1, 9, 98, 100, int.MaxValue }) Require(!NativeEnumSemantics.TryLocaleKey("PlayerStat/lover", unknown, out _), "Unknown lover value guessed");
            Require(!NativeEnumSemantics.TryLocaleKey("PlayerStat/title", 26, out _) && !NativeEnumSemantics.TryLocaleKey("PlayerStat/unverified", 0, out _), "Unknown enum key guessed");
            var view = new ValueSnapshot { EnumValues = new() { [0] = "name" }, Value = 0, Kind = "integer", Writable = true };
            Equal(0, view.Options.Count); Equal("integer", view.Kind); Require(view.Writable, "Hover metadata changed write capability");
        });
        Test("one object reference deduplicates but distinct equal objects remain ambiguous", () =>
        {
            var index = new FieldIdentityIndex(); var first = new EqualButDifferent(); var second = new EqualButDifferent();
            Equal(FieldIdentity.New, index.Register("flag:A", first));
            Equal(FieldIdentity.SameReference, index.Register("flag:A", first));
            Equal(FieldIdentity.Ambiguous, index.Register("flag:A", second));
            Equal(FieldIdentity.SameReference, index.Register("flag:A", second));
            Equal(FieldIdentity.New, index.Register("flag:B", second));
            index.Clear(); Equal(FieldIdentity.New, index.Register("flag:A", second));
        });
        Test("faction mappings use actual story flags and signed integer semantics", () =>
        {
            foreach (string raw in new[] { "H00001", "H00002", "H00003", "H00004", "H00005", "H00006", "H01001", "H01002", "H01003", "H02001", "H02002", "H02003", "M21000", "M21001", "M21002", "M21003" })
            {
                Require(FlagDictionary.TryGet("flag:" + raw, out var info), "Missing faction " + raw);
                Require(info.Verified && info.GroupId == "factions" && info.Kind == "integer" && info.Options.Count == 0, "Faction semantics changed");
            }
            Require(!FlagDictionary.GetInfo("flag:unreviewed_1").Verified, "Unknown flag guessed");
        });
        Test("map candidate pool excludes future, cleared, dateless and scriptless records", () =>
        {
            Require(MapSemantics.IsCurrentCandidate("step2", "step2", true, true), "Current item missing");
            Require(!MapSemantics.IsCurrentCandidate("step2", "step3", true, true), "Future item leaked");
            Require(!MapSemantics.IsCurrentCandidate("clear", "clear", true, true), "Cleared item leaked");
            Require(!MapSemantics.IsCurrentCandidate("step2", "step2", false, true), "Scriptless item leaked");
            Require(!MapSemantics.IsCurrentCandidate("step2", "step2", true, false), "Out-of-date item leaked");
        });
        Test("map dates match verified native bounds without inventing periodic constraints", () =>
        {
            Require(MapSemantics.InDateWindow(5, 0, 5, 0), "Exact day rejected"); Require(!MapSemantics.InDateWindow(6, 0, 5, 0), "Wrong day allowed");
            Require(MapSemantics.InDateWindow(5, 1, 5, 8) && MapSemantics.InDateWindow(8, 1, 5, 8), "Interval endpoint rejected");
            Require(!MapSemantics.InDateWindow(9, 1, 5, 8), "After interval allowed"); Require(MapSemantics.InDateWindow(9, 2, 5, 8), "Unsupported native periodic restriction invented");
        });
        Test("map preview cannot mark unknown or native-blocked conditions ready", () =>
        {
            var known = new[] { new ConditionSnapshot { Known = true, Satisfied = true } };
            Require(MapSemantics.Ready(true, known), "Ready native item blocked"); Require(!MapSemantics.Ready(false, known), "Native gate ignored");
            Require(!MapSemantics.Ready(true, new[] { new ConditionSnapshot { Known = false, Satisfied = true } }), "Unknown condition allowed");
        });
        Test("map implementation has no gameplay mutation or event-pool execution calls", () =>
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string map = File.ReadAllText(Path.Combine(root, "src/ZhaoHuoSystem/MapOverview.cs"));
            foreach (string forbidden in new[] { "UpdateCheckMissions", "GetActiveMissions", "Execute", "Check", "SetFlag", "SetKey", "GetDialog", "GetActiveConditionResult", "SpecifyEvent" })
                Require(!System.Text.RegularExpressions.Regex.IsMatch(map, @"\." + forbidden + @"\s*\("), "Preview calls mutating/unsafe " + forbidden);
            string contract = File.ReadAllText(Path.Combine(root, "src/ZhaoHuoSystem/Contracts.cs"));
            string adapter = contract[contract.IndexOf("public interface IGameAdapter", StringComparison.Ordinal)..];
            Require(!adapter.Contains("Map", StringComparison.Ordinal), "Map mutation API introduced");
        });
    }
}
