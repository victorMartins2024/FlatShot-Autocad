using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ColorFlatshot.Services;
using ColorFlatshot.Views;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ColorFlatshot.Commands))]
[assembly: ExtensionApplication(typeof(ColorFlatshot.Commands))]

namespace ColorFlatshot
{
    public sealed class Commands : IExtensionApplication
    {
        public void Initialize()
        {
            if (!IsCoreConsole) FlatshotPalette.Start();
            // Registering the managed FLATSHOT is enough to provide the override.
            // Never UNDEFINE the native command: it must remain available even when
            // this assembly is absent or cannot be loaded in a later session.
            if (!ActivateOverride())
                AcApplication.DocumentManager.DocumentActivated += OnDocumentActivated;
        }

        private static bool ActivateOverride()
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null) return false;
            document.Editor.WriteMessage("\nColorful FlatShot carregado. Use o painel lateral ou digite COLORFULFLATSHOT para abri-lo.\n");
            return true;
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs args)
        {
            if (ActivateOverride())
                AcApplication.DocumentManager.DocumentActivated -= OnDocumentActivated;
        }

        public void Terminate()
        {
            if (!IsCoreConsole) FlatshotPalette.Stop();
            AcApplication.DocumentManager.DocumentActivated -= OnDocumentActivated;
        }

        [CommandMethod("FLATSHOT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Run()
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document != null && !document.Database.TileMode)
                document.Editor.WriteMessage("\nFLATSHOT: abra a aba Modelo para gerar a projeção.");
            else if (!IsCoreConsole) FlatshotPalette.Show();
        }

        private static bool IsCoreConsole
        {
            get { return string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName,
                "accoreconsole", StringComparison.OrdinalIgnoreCase); }
        }

        [CommandMethod("COLORFULFLATSHOT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void CreateFlatshot()
        {
            if (!IsCoreConsole) FlatshotPalette.Show();
        }

        [CommandMethod("COLORFULFLATSHOTCREATE", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void GenerateFlatshot()
        {
            Generate();
        }

        [CommandMethod("COLORFULFLATSHOTTAB", CommandFlags.Modal)]
        public void ShowTab()
        {
            if (!IsCoreConsole) FlatshotPalette.Show();
        }

        private static void Generate()
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            var editor = document.Editor;
            try
            {
                if (!document.Database.TileMode) { editor.WriteMessage("\nFLATSHOT: abra a aba Modelo para gerar a projeção."); return; }
                var model = FlatshotPalette.Settings;
                // Snapshot the controls and active viewport before prompting for selection/insertion.
                var options = model.Options();
                var allObjects = model.AllObjects;
                PromptSelectionResult selection;
                if (allObjects)
                    selection = editor.SelectAll(new SelectionFilter(new[] { new TypedValue((int)DxfCode.LayoutName, "Model") }));
                else
                {
                    selection = editor.SelectImplied();
                    if (selection.Status != PromptStatus.OK)
                        selection = editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione sólidos, superfícies, blocos ou componentes Plant 3D: " });
                }
                if (selection.Status != PromptStatus.OK) { editor.WriteMessage("\nFLATSHOT: nenhuma seleção; operação encerrada."); return; }
                var point = editor.GetPoint("\nPonto de inserção (canto inferior esquerdo): ");
                if (point.Status != PromptStatus.OK) return;
                // Capture the actual final view; selection may include a pan/orbit.
                // UCS View does not mean WCS TOP: preserve the camera's direction and twist.
                var outputUcs = editor.CurrentUserCoordinateSystem;
                var insertion = point.Value.TransformBy(outputUcs);
                ColorFlatshot.Models.FlatshotResult result;
                bool perspective;
                using (var view = editor.GetCurrentView())
                {
                    perspective=view.PerspectiveEnabled;
                    if(perspective)
                    {
                        var camera=PerspectiveFlatshotService.CaptureCamera(document,view);
                        result=new PerspectiveFlatshotService().Generate(document,selection.Value.GetObjectIds(),options,camera,insertion,outputUcs);
                    }
                    else
                    {
                        var matrix=FlatshotService.GetWorldToView(ColorFlatshot.Models.ProjectionView.Current,view);
                        result=new FlatshotService().Generate(document.Database,selection.Value.GetObjectIds(),options,matrix,insertion,outputUcs);
                    }
                }
                foreach (string warning in result.Warnings) editor.WriteMessage("\nAviso: " + warning);
                if (result.CurveCount == 0) editor.WriteMessage("\nFLATSHOT: nenhuma geometria 3D projetável. Nenhum bloco criado.");
                else editor.WriteMessage("\nFLATSHOT: {0} curvas geradas de {1} componentes. {2}\n{3}",
                    result.CurveCount,result.SourceCount,result.BlockName??"Entidades independentes.",
                    perspective?"Perspectiva da câmera; ocultação entre componentes.":"Projeção paralela; ocultação por objeto.");
            }
            catch (System.Exception ex) { editor.WriteMessage("\nFLATSHOT: " + ex.Message + "\nOperação cancelada; alterações da projeção revertidas."); }
        }
    }
}
