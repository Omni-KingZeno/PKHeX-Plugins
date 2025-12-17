using AutoModPlugins.GUI;
using AutoModPlugins.Properties;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoModPlugins;

public class TransferLivingDex : AutoModPlugin
{
    public override string Name => "Transfer Living Dex";

    public override int Priority => 1;

    protected override void AddPluginControl(ToolStripDropDownItem modmenu)
    {
        var ctrl = new ToolStripMenuItem(Name)
        {
            Image = Resources.livingdex,
            ShortcutKeys = Keys.Alt | Keys.T,
        };
        ctrl.Click += GenTLivingDex;
        ctrl.Name = "Menu_TransferDex";
        modmenu.DropDownItems.Add(ctrl);
    }

    private async void GenTLivingDex(object? sender, EventArgs e)
    {
        if (_settings.TransferVersion == GameVersion.Any)
        {
            WinFormsUtil.Alert("Please set a valid Transfer Version in the settings.");
            return;
        }

        var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, $"Generate a Transfer Dex for {_settings.TransferVersion}?");
        if (prompt != DialogResult.Yes)
        {
            return;
        }

        var sav = SaveFileEditor.SAV;
        var t = new ALMStatusBar("Living Transfer Dex", sav.MaxSpeciesID)
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

        var pkms = await Task.Run(() => sav.GenerateTransferLivingDex().ToArray());
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

        int generated = IngestToBoxes(sav, pkms, extra);
        System.Diagnostics.Debug.WriteLine($"Generated Living Transfer Dex with {pkms.Length} entries.");
        SaveFileEditor.ReloadSlots();
        if (extra.Count == 0)
            return;

        prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "This Living Transfer Dex does not fit in all boxes. Save the extra to a folder?");
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

    private static int IngestToBoxes(SaveFile sav, Span<PKM> list, IList<PKM> extra, int slot = 0)
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
