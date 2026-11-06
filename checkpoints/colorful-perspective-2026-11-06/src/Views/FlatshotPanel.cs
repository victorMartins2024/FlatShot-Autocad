using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ColorFlatshot.ViewModels;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ColorFlatshot.Views
{
    // WPF content hosted in AutoCAD's native, dockable PaletteSet (like Properties).
    public sealed class FlatshotPanel : UserControl
    {
        private static readonly Brush Body = ColorBrush("#384352");
        private static readonly Brush Heading = ColorBrush("#29313E");
        private static readonly Brush Divider = ColorBrush("#293441");
        private static readonly Brush Text = ColorBrush("#E5EAF0");

        public FlatshotPanel(FlatshotViewModel settings)
        {
            DataContext = settings;
            Background = Body;
            Foreground = Text;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            var root = new DockPanel { Margin = new Thickness(8) };
            var bottom = new StackPanel { Margin = new Thickness(0, 16, 0, 4) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            var create = new Button { Content = "Criar FlatShot", Padding = new Thickness(12, 9, 12, 9),
                Background = ColorBrush("#1677C8"), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center };
            create.Click += delegate {
                var document = AcApplication.DocumentManager.MdiActiveDocument;
                if (document != null) document.SendStringToExecute("COLORFULFLATSHOTCREATE ", true, false, false);
            };
            bottom.Children.Add(create);
            bottom.Children.Add(new TextBlock { Text = "Selecione os objetos e indique o ponto de inserção no desenho.",
                Foreground = Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0) });
            root.Children.Add(bottom);

            var rows = new StackPanel();
            Section(rows, "Vista");
            ReadOnlyRow(rows, "Orientação", "Atual / ViewCube");
            BoundRow(rows, "Projeção", "ProjectionStatus");
            var toleranceGrid=Row(rows,"Tolerância perspectiva");
            var tolerance=new TextBox{Margin=new Thickness(6,4,4,4),MinWidth=55,ToolTip="Desvio máximo da aproximação por segmentos, em unidades do desenho. Menor = mais detalhe e processamento."};
            tolerance.SetBinding(TextBox.TextProperty,new Binding("PerspectiveToleranceText"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            Grid.SetColumn(tolerance,1);toleranceGrid.Children.Add(tolerance);
            Section(rows, "Linhas");
            ToggleRow(rows, "Linhas encobertas", "IncludeHiddenLines");
            ToggleRow(rows, "Tangências", "IncludeTangencies");
            ReadOnlyRow(rows, "Cores", "Modelo 3D");
            ReadOnlyRow(rows, "Layers", "Originais");
            Section(rows, "Destino");
            ToggleRow(rows, "Novo bloco", "InsertAsBlock");
            rows.Children.Add(Note("Desmarque Novo bloco para inserir curvas independentes."));
            Section(rows, "Seleção");
            ToggleRow(rows, "Todos os objetos", "AllObjects");
            rows.Children.Add(Note("Desmarcado: selecionar objetos ao criar."));
            Section(rows, "Informações");
            rows.Children.Add(Note("A orientação e o tipo de projeção acompanham a vista atual.\n\nEm perspectiva, curvas e silhuetas são aproximadas por segmentos conforme a tolerância, com ocultação entre componentes.\n\nNa projeção paralela, a ocultação continua por componente."));
            root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Content = root;
        }

        private static Brush ColorBrush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze(); return brush;
        }

        private static void Section(Panel rows, string title)
        {
            rows.Children.Add(new Border { Background = Heading, Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(6, 4, 6, 4), Child = new TextBlock { Text = title, Foreground = Text, FontWeight = FontWeights.SemiBold } });
        }

        private static Grid Row(Panel rows, string label)
        {
            var grid = new Grid { MinHeight = 29, Background = Body };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock { Text = label, Foreground = Text, Margin = new Thickness(8, 5, 4, 5),
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            rows.Children.Add(new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
            return grid;
        }

        private static void ReadOnlyRow(Panel rows, string label, string value)
        {
            var grid = Row(rows, label);
            var field = new TextBlock { Text = value, Foreground = ColorBrush("#C2CCD8"),
                Margin = new Thickness(6, 5, 4, 5), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(field, 1); grid.Children.Add(field);
        }

        private static void BoundRow(Panel rows,string label,string property)
        {
            var grid=Row(rows,label);
            var field=new TextBlock{Foreground=ColorBrush("#C2CCD8"),Margin=new Thickness(6,5,4,5),TextWrapping=TextWrapping.Wrap};
            field.SetBinding(TextBlock.TextProperty,new Binding(property));Grid.SetColumn(field,1);grid.Children.Add(field);
        }

        private static void ToggleRow(Panel rows, string label, string property)
        {
            var grid = Row(rows, label);
            var check = new CheckBox { Content = "Ativar", Foreground = Text,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 5, 0, 5) };
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Grid.SetColumn(check, 1); grid.Children.Add(check);
        }

        private static TextBlock Note(string text)
        {
            return new TextBlock { Text = text, Foreground = ColorBrush("#C2CCD8"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 8, 8, 6), FontSize = 11 };
        }
    }
}
