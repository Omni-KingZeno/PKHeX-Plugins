using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using Xunit;
using static PKHeX.Core.GameVersion;

namespace AutoModTests;

public static class TransferDexTests
{
    static TransferDexTests() => TestUtil.InitializePKHeXEnvironment();

    private static readonly GameVersion[] GetGameVersionsToTest =
    [
        RD,
        C,
        E,
        Pt,
        B,
        B2,
        X,
        OR,
        SN,
        US,
        SW,
        PLA,
        BD,
        SL,
    ];

    private static GenerateResult SingleSaveTest(this GameVersion s, LivingDexConfig cfg)
    {
        var sav = BlankSaveFile.Get(s, "ALMUT");
        RecentTrainerCache.SetRecentTrainer(sav);

        var expected = sav.GetExpectedDexCount(cfg);
        expected.Should().NotBe(0);

        var pkms = sav.GenerateTransferLivingDex(cfg).ToArray();
        var genned = pkms.Length;
        var val = new GenerateResult(genned == expected, expected, genned);
        return val;
    }

    public static IEnumerable<object[]> GetLivingDexTestData()
    {
        var cfgs = new LivingDexConfig[16];
        for (int i = 0; i < 16; i++)
            cfgs[i] = new LivingDexConfig((byte)i);
        foreach (var ver in GetGameVersionsToTest)
        {
            for (int i = Array.IndexOf(GetGameVersionsToTest, ver) + 1; i < GetGameVersionsToTest.Length; i++)
            {
                foreach (var cf in cfgs)
                    yield return [ver, cf, GetGameVersionsToTest[i]];
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetLivingDexTestData))]
    public static void VerifyDex(GameVersion game, LivingDexConfig cfg, GameVersion dest)
    {
        APILegality.Timeout = 99999;
        Legalizer.EnableEasterEggs = false;
        APILegality.SetAllLegalRibbons = false;
        APILegality.EnableDevMode = true;
        cfg = cfg with { TransferVersion = dest };
        var res = game.SingleSaveTest(cfg);
        res.Success.Should().BeTrue($"GameVersion: {game}\n{cfg}\nExpected: {res.Expected}\nGenerated: {res.Generated}");
    }

    private readonly record struct GenerateResult(bool Success, int Expected, int Generated);

    // Ideally should use purely PKHeX's methods or known total counts so that we're not verifying against ourselves.
    private static int GetExpectedDexCount(this SaveFile sav, LivingDexConfig cfg)
    {
        Dictionary<ushort, List<(byte Form, byte Gender)>> speciesDict = [];
        var personal = sav.Personal;
        var destSav = BlankSaveFile.Get(cfg.TransferVersion, "ALM");
        var species = Enumerable.Range(1, sav.MaxSpeciesID).Select(x => (ushort)x);
        foreach (ushort s in species)
        {
            if (!personal.IsSpeciesInGame(s))
                continue;

            List<(byte Form, byte Gender)> formGenderPairs = [];
            var formCount = personal[s].FormCount;
            var str = GameInfo.Strings;
            if (formCount == 1 && cfg.IncludeForms) // Validate through form lists
                formCount = (byte)FormConverter.GetFormList(s, str.types, str.forms, GameInfo.GenderSymbolUnicode, sav.Context).Length;

            // Handle Alcremie special case
            if (s == (ushort)Species.Alcremie)
                formCount = (byte)(formCount * 6);

            byte acform = 0;
            for (byte f = 0; f < formCount; f++)
            {
                var form = f;
                if (s == (ushort)Species.Alcremie)
                {
                    form = acform;
                    if (f % 6 == 0 && f != 0)
                        acform++;
                }

                if (!destSav.Personal.IsPresentInGame(s, form) || !sav.Personal.IsPresentInGame(s, form))
                    continue;

                if (FormInfo.IsFusedForm(s, form, sav.Generation) || FormInfo.IsBattleOnlyForm(s, form, sav.Generation) || (FormInfo.IsTotemForm(s, form) && sav.Context is not EntityContext.Gen7) || FormInfo.IsLordForm(s, form, sav.Context))
                    continue;

                var gendersToCheck = new List<byte> { 2 };
                if (cfg.IncludeGenderVariants && Aesthetics.NonFormGenderVariant((Species)s) && sav.Generation != 1)
                {
                    if (s == (ushort)Species.Pikachu && form != 0)
                        gendersToCheck = [0];
                    else
                        gendersToCheck = [0, 1];
                }

                foreach (var gender in gendersToCheck)
                {
                    var valid = sav.GetRandomEncounter(s, form, gender, cfg.SetShiny, cfg.SetAlpha, out PKM? pk);
                    if (pk is not null && valid)
                    {
                        var pkForm = pk.Form;

                        if (pkForm == form || (sav.Generation == 2 && s == (ushort)Species.Unown && cfg.SetShiny))
                        {
                            var pair = (pkForm, pk.Gender);
                            if (!formGenderPairs.Contains(pair))
                            {
                                formGenderPairs.Add(pair);
                            }
                        }
                    }
                }

                if (!cfg.IncludeForms && formGenderPairs.Count > 0)
                    break;
            }

            if (formGenderPairs.Count > 0)
                speciesDict.TryAdd(s, formGenderPairs);
        }

        return speciesDict.Values.Sum(x => x.Count);
    }
}
