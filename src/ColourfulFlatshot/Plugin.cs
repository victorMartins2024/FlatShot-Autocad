using System;
using System.Drawing;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(ColourfulFlatshot.Plugin))]
[assembly: CommandClass(typeof(ColourfulFlatshot.Commands))]

namespace ColourfulFlatshot
{
    public sealed class Plugin : IExtensionApplication
    {
        // One palette per AutoCAD process, shared by all open drawings.
        private static PaletteSet palette;
        private static FlatshotPalette content;
        private static Autodesk.AutoCAD.ApplicationServices.Document queuedDocument;
        private static FlatshotOptions queuedOptions;
        internal static void QueueGeneration(FlatshotOptions options)
        {
            var doc=AcadApplication.DocumentManager.MdiActiveDocument;
            if(doc==null){if(content!=null)content.SetStatus("Abra um desenho antes de gerar.");return;}
            if(queuedDocument!=null){if(content!=null)content.SetStatus("Há uma geração aguardando na linha de comando. Finalize o comando atual.");return;}
            queuedDocument=doc;queuedOptions=options;
            doc.SendStringToExecute("CFS_GENERATE ",true,false,true);
            if(content!=null)content.SetStatus("Confira a linha de comando e indique o ponto de inserção.");
        }
        internal static void Generate()
        {
            var doc=AcadApplication.DocumentManager.MdiActiveDocument;
            if(doc==null)return;
            var options=queuedDocument==doc?queuedOptions:(content==null?new FlatshotOptions():content.Options);
            queuedDocument=null;queuedOptions=null;
            try
            {
                if(!doc.Database.TileMode)throw new InvalidOperationException("Abra a guia Model para gerar o Flatshot.");
                if(!doc.Database.UndoRecording)throw new InvalidOperationException("Ative o histórico UNDO do desenho antes de gerar, para permitir cancelar ou desfazer a operação.");
                using(var view=doc.Editor.GetCurrentView())
                {
                    if(view.PerspectiveEnabled)throw new InvalidOperationException("Desative a perspectiva antes de gerar (PERSPECTIVE = 0).");
                    Matrix3d viewToWorld=Matrix3d.PlaneToWorld(view.ViewDirection);
                    viewToWorld=Matrix3d.Displacement(view.Target-Point3d.Origin)*viewToWorld;
                    viewToWorld=Matrix3d.Rotation(-view.ViewTwist,view.ViewDirection,view.Target)*viewToWorld;
                    Matrix3d ucs=doc.Editor.CurrentUserCoordinateSystem;
                    var point=doc.Editor.GetPoint("\nColourful Flatshot — ponto de inserção (Esc cancela): ");
                    if(point.Status!=PromptStatus.OK){if(content!=null)content.SetStatus("Geração cancelada. Nenhum bloco criado.");return;}
                    doc.Editor.WriteMessage("\nCalculando projeção e linhas ocultas...\n");
                    using(var tr=doc.Database.TransactionManager.StartTransaction())
                    {
                        var result=FlatshotEngine.Generate(doc.Database,tr,viewToWorld.Inverse(),ucs,point.Value.TransformBy(ucs),options,delegate { if(HostApplicationServices.Current.UserBreak())throw new OperationCanceledException(); });
                        tr.Commit();
                        string message=string.Format("Bloco {0}: {1} objetos, {2} trechos visíveis, {3} ocultos. {4} itens ignorados (tipos não suportados, xrefs ou resultados anteriores).",result.BlockName,result.Objects,result.Visible,result.Hidden,result.Skipped);
                        doc.Editor.WriteMessage("\n"+message+"\n");
                        if(content!=null)content.SetStatus(message);
                    }
                }
            }
            catch(OperationCanceledException){if(content!=null)content.SetStatus("Geração cancelada. Nenhum bloco criado.");doc.Editor.WriteMessage("\nGeração cancelada.\n");}
            catch(System.Exception ex){if(content!=null)content.SetStatus("Não foi possível gerar: "+ex.Message);doc.Editor.WriteMessage("\nColourful Flatshot: "+ex.Message+"\n");}
        }
        private static readonly Guid PaletteId = new Guid("CE706E7F-C84F-4522-A35D-BDBDA91A6518");

        public void Initialize()
        {
            var document = AcadApplication.DocumentManager.MdiActiveDocument;
            if (document != null)
                document.Editor.WriteMessage("\nColourful Flatshot carregado. Digite COLOURFULFLATSHOT ou CF.\n");
        }

        internal static void ShowPalette()
        {
            if (palette == null)
            {
                var newContent = new FlatshotPalette();
                PaletteSet newPalette = null;
                try
                {
                    // Stable GUID lets AutoCAD restore the user's palette placement.
                    newPalette = new PaletteSet("Colourful Flatshot", PaletteId);
                    newPalette.Style = PaletteSetStyles.ShowAutoHideButton
                        | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu;
                    newPalette.MinimumSize = new Size(200, 300);
                    newPalette.Size = new Size(300, 500);
                    newPalette.DockEnabled = DockSides.Left | DockSides.Right;
                    newPalette.KeepFocus = false;
                    newPalette.Add("Configurações", newContent);
                    content = newContent;
                    palette = newPalette;
                }
                catch
                {
                    if (newPalette != null) newPalette.Dispose();
                    newContent.Dispose();
                    throw;
                }
            }

            // Do not toggle: repeated commands reopen the same instance.
            palette.Visible = true;
            palette.Activate(0);
        }

        public void Terminate()
        {
            if (palette != null) palette.Dispose();
            if (content != null) content.Dispose();
            palette = null;
            content = null;
        }
    }

    public sealed class Commands
    {
        [CommandMethod("COLOURFULFLATSHOT", CommandFlags.Modal)]
        public void OpenPalette() { Plugin.ShowPalette(); }

        // A second registered command provides CF without editing acad.pgp.
        [CommandMethod("CF", CommandFlags.Modal)]
        public void OpenPaletteShort() { Plugin.ShowPalette(); }

        [CommandMethod("CFS_GENERATE", CommandFlags.Modal)]
        public void GenerateFlatshot() { Plugin.Generate(); }
    }
}
