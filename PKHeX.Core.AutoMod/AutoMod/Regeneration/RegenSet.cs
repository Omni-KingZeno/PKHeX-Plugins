using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace PKHeX.Core.AutoMod;

public sealed class RegenSet
{
    public static readonly RegenSet Default = new([], Latest.Generation);

    public RegenSetting Extra { get; }
    public ITrainerInfo? Trainer { get; }
    public StringInstructionSet Batch { get; }
    public IReadOnlyList<StringInstruction> EncounterFilters { get; }
    public IReadOnlyList<StringInstruction> VersionFilters { get; }
    public IReadOnlyList<string> SeedFilters { get; }

    public readonly bool HasExtraSettings;
    public readonly bool HasTrainerSettings;
    public bool HasBatchSettings => Batch.Filters.Count != 0 || Batch.Instructions.Count != 0;

    public RegenSet(PKM pk) : this([], pk.Format)
    {
        Extra.Ball = (Ball)pk.Ball;
        Extra.ShinyType = pk.ShinyXor == 0 ? Shiny.AlwaysSquare : pk.IsShiny ? Shiny.AlwaysStar : Shiny.Never;
        if (pk is IAlphaReadOnly { IsAlpha: true })
            Extra.Alpha = true;
        HasExtraSettings = true;
        var tr = new SimpleTrainerInfo(pk.Version) { OT = pk.OriginalTrainerName, TID16 = pk.TID16, SID16 = pk.SID16, Gender = pk.OriginalTrainerGender };
        Trainer = tr;
        HasTrainerSettings = true;
        _ = StringInstruction.TryParseFilter($"=Version={pk.Version}", out var verFilter);
        VersionFilters = [verFilter!];

        List<string> modified = [];
        var ribbons = RibbonInfo.GetRibbonInfo(pk);
        foreach (var rib in ribbons)
        {
            if (rib.HasRibbon)
                modified.Add($".{rib.Name}=true");
        }

        modified.Add($".MetLocation={pk.MetLocation}");
        modified.Add($".MetDate={pk.MetDate}");
        modified.Add($".MetLevel={pk.MetLevel}");
        if (pk is IFormArgument { FormArgument: not 0 } fa)
            modified.Add($".FormArgument={fa.FormArgument}");
        if (modified.Count > 0)
            Batch = new StringInstructionSet(modified.ToArray().AsSpan());
    }

    public RegenSet(IList<BattleTemplateParseError> lines, byte format, Shiny shiny = Shiny.Never)
    {
        Extra = new RegenSetting { ShinyType = shiny };
        HasExtraSettings = Extra.SetRegenSettings(lines);
        HasTrainerSettings = RegenUtil.GetTrainerInfo(lines, format, out var tr);
        Trainer = tr;

        if (lines.Count == 0)
        {
            Batch = new StringInstructionSet(Array.Empty<string>());
            EncounterFilters = [];
            VersionFilters = [];
            SeedFilters = [];
            return;
        }

        List<StringInstruction> eFilter = [];
        List<StringInstruction> vFilter = [];
        List<StringInstruction> mods = [];
        List<string> sFilter = [];
        for (int i = 0; i < lines.Count;)
        {
            var line = lines[i];
            if (line.Type == BattleTemplateParseErrorType.LineLength && (line.Value is null || line.Value.Length == 0))
            {
                lines.RemoveAt(i);
                continue;
            }
            if (line.Type == BattleTemplateParseErrorType.TokenUnknown)
            {
                var sanitized = line.Value.Replace(">=", "≥").Replace("<=", "≤");

                if (TryConvertToBatchFormat(sanitized, out var converted))
                {
                    sanitized = converted;
                }

                if (StringInstruction.TryParseInstruction(sanitized, out var mod))
                {
                    mods.Add(mod);
                    lines.RemoveAt(i);
                    continue;
                }

                if (RegenUtil.IsEncounterFilter(sanitized, out var e))
                {
                    eFilter.Add(e);
                    lines.RemoveAt(i);
                    continue;
                }
                if (RegenUtil.IsVersionFilter(sanitized, out var v))
                {
                    vFilter.Add(v);
                    lines.RemoveAt(i);
                    continue;
                }
                if (RegenUtil.IsSeedFilter(sanitized, out var s))
                {
                    sFilter.Add(s);
                    lines.RemoveAt(i);
                    continue;
                }
            }
            i++;
        }
        Batch = new([], mods);
        EncounterFilters = eFilter;
        VersionFilters = vFilter;
        SeedFilters = sFilter;
    }

    private static bool TryConvertToBatchFormat(string input, [NotNullWhen(true)] out string? converted)
    {
        converted = null;

        var colonIndex = input.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= input.Length - 1)
            return false;

        if (input[0] is '.' or '=')
            return false;

        var keySpan = input.AsSpan(0, colonIndex).Trim();
        var valueSpan = input.AsSpan(colonIndex + 1).Trim();

        if (keySpan.IsEmpty || valueSpan.IsEmpty)
            return false;

        var key = ConvertKey(keySpan.ToString());
        var value = valueSpan.ToString();

        if (key == "RibbonMark")
        {
            converted = $".RibbonMark{value}=true";
            return true;
        }

        var convertedValue = ConvertValue(key, value.ToLower());
        converted = $".{key}={convertedValue}";
        return true;
    }

    private static string ConvertKey(string key) => key switch
    {
        "Height" => "HeightScalar",
        "HyperTrained" => "HyperTrainFlags",
        "Size" => "Scale",
        "Weight" => "WeightScalar",
        "Mark" => "RibbonMark",
        "Sweet" => "FormArgument",
        _ => key
    };

    private static string ConvertValue(string key, string value)
    {
        return value switch
        {
            "strawberry" => "0",
            "berry" => "1",
            "love" => "2",
            "star" => "3",
            "clover" => "4",
            "flower" => "5",
            "ribbon" => "6",
            "all" => "$suggestAll",
            "none" => "$suggestNone",
            "suggest" => "$suggest",
            "yes" => key == "HyperTrainFlags" ? "1" : "true",
            "no" => key == "HyperTrainFlags" ? "0" : "false",
            _ when key is "Scale" or "WeightScalar" or "HeightScalar" => ConvertScalarValue(key, value),        
            _ => value
        };
    }

    private static string ConvertScalarValue(string key, string value)
    {
        var rnd = new Random();

        return (key, value) switch
        {
            ("Scale", "xxxs") => "0",
            ("Scale", "xxs") => $"{rnd.Next(1, 31)}",
            ("Scale", "xs") => $"{rnd.Next(31, 61)}",
            ("Scale", "s") => $"{rnd.Next(61, 101)}",
            ("Scale", "av") => $"{rnd.Next(101, 161)}",
            ("Scale", "l") => $"{rnd.Next(161, 196)}",
            ("Scale", "xl") => $"{rnd.Next(196, 242)}",
            ("Scale", "xxl") => $"{rnd.Next(242, 255)}",
            ("Scale", "xxxl") => "255",
            (_, "xs") => $"{rnd.Next(0, 16)}",
            (_, "s") => $"{rnd.Next(16, 48)}",
            (_, "av") => $"{rnd.Next(48, 208)}",
            (_, "l") => $"{rnd.Next(208, 240)}",
            (_, "xl") => $"{rnd.Next(240, 256)}",
            _ => value
        };
    }

    public string GetSummary()
    {
        var sb = new StringBuilder();
        if (HasExtraSettings)
            sb.AppendLine(RegenUtil.GetSummary(Extra));

        if (HasTrainerSettings && Trainer != null)
            sb.AppendLine(RegenUtil.GetSummary(Trainer));

        if (HasBatchSettings)
            sb.AppendLine(RegenUtil.GetSummary(Batch));

        if (EncounterFilters.Any())
            sb.AppendLine(RegenUtil.GetSummary(EncounterFilters));

        if (VersionFilters.Any())
            sb.AppendLine(RegenUtil.GetSummary(VersionFilters));

        if (SeedFilters.Any())
            sb.AppendLine(SeedFilters[0]);

        return sb.ToString();
    }

    public bool AnyInstructionStartsWith(string name, string value)
    {
        foreach (var z in Batch.Instructions)
        {
            if (!z.PropertyName.StartsWith(name))
                continue;
            if (!z.PropertyValue.StartsWith(value))
                continue;
            return true;
        }
        return false;
    }

    public bool TryGetBatchValue(ReadOnlySpan<char> key, [NotNullWhen(true)] out string? value)
    {
        foreach (var instruction in Batch.Instructions)
        {
            if (!key.SequenceEqual(instruction.PropertyName))
                continue;

            value = instruction.PropertyValue;
            return true;
        }
        value = null;
        return false;
    }
}