using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static partial class Program
{
    // Reads the installed game's metadata in place; nothing is loaded, executed or copied.
    private static void NativeEntryTests(string managed)
    {
        Test("click-to-roll uses only the dice dialog's own start and restart entries", () =>
        {
            var methods = Methods(Path.Combine(managed, "Mortal.Story.dll"), "Mortal.Story", "DiceMenuDialog");
            foreach (var (name, isPublic) in new[] { ("PressStartButton", true), ("PressRestartButton", true), ("UseFatePoint", false) })
                Require(methods.TryGetValue(name, out var found) && found.Parameters == 0 && found.IsPublic == isPublic, "missing native dice entry " + name);
        });
        Test("battle tab patch targets resolve by name with the injected parameter names", () =>
        {
            // CombatCheats resolves these by name only, so each must be a single, non-overloaded method.
            string battle = Path.Combine(managed, "Mortal.Battle.dll"), combat = Path.Combine(managed, "Mortal.Combat.dll");
            Signature(battle, "Mortal.Battle", "Character", "TakeDamage", "damage", "isCriticalHit", "canDodge", "attacker");
            Signature(battle, "Mortal.Battle", "CharacterMana", "Modify", "amount");
            Signature(battle, "Mortal.Battle", "SkillButton", "CoolDownSkill");
            Signature(combat, "Mortal.Combat", "CombatManager", "GetTotalDamage", "attacker", "attacked", "attackedAction", "initDamage", "ignoreDamageAbsorb");
            Signature(combat, "Mortal.Combat", "CombatActionController", "ModifyHealth", "value");
            Signature(combat, "Mortal.Combat", "CombatActionController", "ModifyStamina", "value");
            Signature(combat, "Mortal.Combat", "CombatEventActionModifyStat", "Action", "combatManager");
            Has(battle, "Mortal.Battle", "Character", "_entity", "CurrentHealth");
            Has(battle, "Mortal.Battle", "Entity", "_originType");
            Has(battle, "Mortal.Battle", "SkillButton", "_currentCooldownTime");
            Has(battle, "Mortal.Battle", "GameLevelManager", "Instance");
            Has(combat, "Mortal.Combat", "CombatManager", "Instance", "PlayerAction", "EnemyAction");
            Has(combat, "Mortal.Combat", "CombatActionController", "Stat");
            Has(combat, "Mortal.Combat", "CombatStatController", "CurrentHealth");
            Require(Members(combat, "Mortal.Combat", "CombatEventActionModifyStat").Nested.Any(name => name.StartsWith("<Action>d__", StringComparison.Ordinal)), "scripted stat change is not an iterator");
            var sides = Members(battle, "Mortal.Battle", "EntityType").Constants;
            Equal(0, sides["Player"]); Equal(1, sides["Friend"]); Equal(2, sides["Enemy"]);
        });
    }

    private static void BranchEntryTests(string managed)
    {
        Test("branch notice reads the native Switch result and its own condition data", () =>
        {
            string story = Path.Combine(managed, "Mortal.Story.dll"), core = Path.Combine(managed, "Mortal.Core.dll");
            Signature(story, "Mortal.Story", "CheckPointManager", "Switch", "name");
            Has(story, "Mortal.Story", "SwitchResultData", "_useCount", "_count", "_items", "_countItems");
            Has(core, "Mortal.Core", "ValueCompareItem", "_compareOp", "_value");
            // 修炼 / 锻造 without the count cap: the martial panel's own mode argument and the upgrade panel's count members.
            string mortalCore = Path.Combine(managed, "Mortal.Core.dll");
            Signature(mortalCore, "Mortal.Core", "MartialPanel", "Open", "mode");
            Signature(mortalCore, "Mortal.Core", "UpgradePanel", "Open");
            foreach (string method in new[] { "UpdateMartialPointInfo", "UpdateCurrentStatus" }) Signature(mortalCore, "Mortal.Core", "MartialPanel", method);
            Has(mortalCore, "Mortal.Core", "MartialPanel", "_mode", "_maxCountText");
            foreach (string method in new[] { "UpdateButton", "UpgradeButtonClick", "NextLevelButtonClick", "UpdateStatus" }) Signature(mortalCore, "Mortal.Core", "UpgradePanel", method);
            Has(mortalCore, "Mortal.Core", "UpgradePanel", "_currentCount", "_maxCountText");
            // The box is placed in the active dialogue box: left of its story text, below its name text.
            Has(Path.Combine(managed, "Fungus.dll"), "Fungus", "SayDialog", "ActiveSayDialog", "StoryTextRectTrans", "nameText", "nameTextGO");
        });
    }

    private sealed class TypeMembers
    {
        public readonly Dictionary<string, List<string[]>> Methods = new(StringComparer.Ordinal);
        public readonly HashSet<string> Names = new(StringComparer.Ordinal);
        public readonly List<string> Nested = new();
        public readonly Dictionary<string, int> Constants = new(StringComparer.Ordinal);
    }
    private static void Signature(string assembly, string ns, string type, string method, params string[] parameters)
    {
        Require(Members(assembly, ns, type).Methods.TryGetValue(method, out var overloads) && overloads.Count == 1, $"{type}.{method} missing or overloaded");
        Equal(string.Join(",", parameters), string.Join(",", overloads![0]));
    }
    private static void Has(string assembly, string ns, string type, params string[] names)
    {
        var members = Members(assembly, ns, type);
        foreach (string name in names) Require(members.Names.Contains(name), $"{type}.{name} missing");
    }
    private static TypeMembers Members(string assembly, string ns, string type)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var result = new TypeMembers();
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (reader.GetString(definition.Namespace) != ns || reader.GetString(definition.Name) != type) continue;
            foreach (var methodHandle in definition.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var names = method.GetParameters().Select(p => reader.GetParameter(p)).Where(p => p.SequenceNumber > 0)
                    .OrderBy(p => p.SequenceNumber).Select(p => reader.GetString(p.Name)).ToArray();
                string name = reader.GetString(method.Name);
                if (!result.Methods.TryGetValue(name, out var list)) result.Methods[name] = list = new();
                list.Add(names);
            }
            foreach (var fieldHandle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                string name = reader.GetString(field.Name); result.Names.Add(name);
                var constant = field.GetDefaultValue();
                if (!constant.IsNil) result.Constants[name] = reader.GetBlobReader(reader.GetConstant(constant).Value).ReadInt32();
            }
            foreach (var property in definition.GetProperties()) result.Names.Add(reader.GetString(reader.GetPropertyDefinition(property).Name));
            foreach (var nested in definition.GetNestedTypes()) result.Nested.Add(reader.GetString(reader.GetTypeDefinition(nested).Name));
        }
        return result;
    }

    private static Dictionary<string, (int Parameters, bool IsPublic)> Methods(string assembly, string ns, string type)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var result = new Dictionary<string, (int, bool)>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (reader.GetString(definition.Namespace) != ns || reader.GetString(definition.Name) != type) continue;
            foreach (var methodHandle in definition.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                bool isPublic = (method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask) == System.Reflection.MethodAttributes.Public;
                result[reader.GetString(method.Name)] = (method.GetParameters().Count, isPublic);
            }
        }
        return result;
    }
}
