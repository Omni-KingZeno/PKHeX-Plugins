using System;
using System.IO;
using System.Windows.Forms;
using AutoModPlugins.Properties;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System.Collections.Generic;
using Microsoft.VisualBasic.Devices;
using System.Threading.Tasks;
using AutoModPlugins.GUI;

namespace AutoModPlugins;

public class LivingEggDex : AutoModPlugin
{
    public override string Name => "Generate Living Egg Dex";
    public override int Priority => 1;

    protected override void AddPluginControl(ToolStripDropDownItem modmenu)
    {
        var ctrl = new ToolStripMenuItem(Name)
        {
            Image = Resources.livingdex,
            ShortcutKeys = Keys.Alt | Keys.E,
        };
        ctrl.Click += GenLivingEggDex;
        ctrl.Name = "Menu_LivingEggDex";
        modmenu.DropDownItems.Add(ctrl);
    }

    private async void GenLivingEggDex(object? sender, EventArgs e)
    {
        var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Generate a Living Egg Dex?");
        if (prompt != DialogResult.Yes)
            return;

        var sav = SaveFileEditor.SAV;
        var t = new ALMStatusBar("Living Egg Dex", sav.MaxSpeciesID)
        {
            Count = ModLogic.TrackingCount
        };
        t.Show();

        // Wait for the ALM status bar handle to be created
        await Task.Run(() =>
        {
            while (!t.IsHandleCreated)
                System.Threading.Thread.Sleep(10);
        });

        // After showing the status bar, then start the polling loop
        var pollingTask = Task.Run(() => PollingLoop(t));

        var dex = await Task.Run(() => sav.GenerateLivingEggDex(sav.Personal));
        List<PKM> extra = [];

        // Now we can safely close the status bar
        if (t.InvokeRequired)
        {
            t.Invoke(new Action(() => t.Close()));
        }
        else
        {
            t.Close();
        }
        // waiting for the task to finish
        await pollingTask;

        int generated = IngestToBoxes(sav, dex, extra);
        System.Diagnostics.Debug.WriteLine($"Generated Living Egg Dex with {generated} entries.");
        SaveFileEditor.ReloadSlots();
        if (extra.Count == 0)
            return;

        prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "This Living Egg Dex does not fit in all boxes. Save the extra to a folder?");
        if (prompt != DialogResult.Yes)
            return;

        using var ofd = new FolderBrowserDialog();
        if (ofd.ShowDialog() != DialogResult.OK)
            return;

        foreach (var f in extra)
            await File.WriteAllBytesAsync($"{ofd.SelectedPath}/{f.FileName}", f.DecryptedPartyData);
    }

    private static void PollingLoop(ALMStatusBar t)
    {
        int lastCount = -1;
        while (!t.IsDisposed && t.IsHandleCreated)
        {
            if (ModLogic.TrackingCount != lastCount)
            {
                lastCount = ModLogic.TrackingCount;
                if (t.InvokeRequired)
                {
                    try
                    {
                        t.Invoke(new Action(() => t.Count = lastCount));
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
                else
                {
                    t.Count = lastCount;
                }
            }
            System.Threading.Thread.Sleep(50);
        }
    }

    private static int IngestToBoxes(SaveFile sav, IEnumerable<PKM> list, IList<PKM> extra, int slot = 0)
    {
        int generated = 0;
        foreach (var pk in list)
        {
            generated++;
            if (TryAdd(sav, extra, pk, ref slot))
                continue;
            do
            {
                slot++;
            }
            while (!TryAdd(sav, extra, pk, ref slot));
        }
        return generated;
    }

    private static bool TryAdd(SaveFile sav, IList<PKM> extra, PKM pk, ref int slot)
    {
        if (slot >= sav.SlotCount)
        {
            extra.Add(pk);
            return true;
        }
        if (!sav.IsBoxSlotOverwriteProtected(slot))
        {
            sav.SetBoxSlotAtIndex(pk, slot++);
            return true;
        }
        return false;
    }
}
