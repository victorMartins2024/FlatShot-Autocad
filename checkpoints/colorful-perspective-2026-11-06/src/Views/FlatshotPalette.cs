using System;
using Autodesk.AutoCAD.Windows;
using ColorFlatshot.ViewModels;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ColorFlatshot.Views
{
    internal static class FlatshotPalette
    {
        internal static readonly FlatshotViewModel Settings = new FlatshotViewModel();
        private static PaletteSet palette;
        private static long lastRefresh;

        internal static void Start() { AcApplication.Idle += OnIdle; AcApplication.Idle += RefreshProjection; }

        private static void RefreshProjection(object sender, EventArgs args)
        {
            long now=Environment.TickCount64;if(now-lastRefresh<400)return;lastRefresh=now;
            var document=AcApplication.DocumentManager.MdiActiveDocument;if(document==null)return;
            try{using(var view=document.Editor.GetCurrentView())Settings.ProjectionStatus=view.PerspectiveEnabled?"Perspectiva / pontos de fuga":"Paralela / ortogonal";}
            catch(Autodesk.AutoCAD.Runtime.Exception){ }
        }

        private static void OnIdle(object sender, EventArgs args)
        {
            if (AcApplication.DocumentManager.MdiActiveDocument == null) return;
            AcApplication.Idle -= OnIdle;
            try { Show(); }
            catch (System.Exception ex)
            {
                AcApplication.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                    "\nColorful FlatShot: não foi possível abrir o painel: " + ex.Message);
            }
        }

        internal static void Show()
        {
            if (palette == null)
            {
                palette = new PaletteSet("Colorful FlatShot", new Guid("C374A544-E33B-43BD-9B8A-2282E4103E17"));
                palette.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu;
                palette.MinimumSize = new System.Drawing.Size(290, 380);
                palette.Size = new System.Drawing.Size(340, 650);
                palette.DockEnabled = DockSides.Left | DockSides.Right;
                palette.KeepFocus = false;
                palette.AddVisual("Colorful FlatShot", new FlatshotPanel(Settings), true);
                palette.Visible = true;
                palette.Dock = DockSides.Left;
            }
            else palette.Visible = true;
        }

        internal static void Stop()
        {
            AcApplication.Idle -= OnIdle;
            AcApplication.Idle -= RefreshProjection;
            if (palette != null) { palette.Dispose(); palette = null; }
        }
    }
}
