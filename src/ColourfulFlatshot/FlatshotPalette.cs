using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ColourfulFlatshot
{
    public sealed class FlatshotPalette : UserControl
    {
        private static readonly Color Background=Color.FromArgb(57,66,81);
        private static readonly Color Header=Color.FromArgb(45,52,64);
        private static readonly Color TextColor=Color.FromArgb(235,238,242);
        private readonly PaletteSettings settings=new PaletteSettings();
        private readonly PropertyGrid grid;
        private readonly Label status;
        private readonly ToolTip hints=new ToolTip();
        public FlatshotOptions Options
        {
            get { grid.Refresh(); return new FlatshotOptions {PreserveColors=settings.PreserveColors,PreserveLayers=settings.PreserveLayers,ShowHidden=settings.HiddenMode=="Personalizadas",HiddenAci=(short)settings.HiddenAci}; }
        }
        public void SetStatus(string message) {status.Text=message;hints.SetToolTip(status,message);}
        public FlatshotPalette()
        {
            AutoScaleMode=AutoScaleMode.Font;
            Dock=DockStyle.Fill;
            BackColor=Background;
            ForeColor=TextColor;
            Padding=Padding.Empty;
            BorderStyle=BorderStyle.None;
            var surface=new Panel {Dock=DockStyle.Fill,BackColor=Background,ForeColor=TextColor,Padding=new Padding(8,8,8,6),Margin=Padding.Empty};
            Controls.Add(surface);
            var title=new Label {Text="Colourful Flatshot",Dock=DockStyle.Top,Height=23,BackColor=Color.FromArgb(78,90,109),ForeColor=TextColor,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(5,0,0,0)};
            hints.SetToolTip(title,"Colourful Flatshot 0.2.3 • AutoCAD 2023");
            grid=new PropertyGrid
            {
                Dock=DockStyle.Fill,
                ToolbarVisible=false,
                HelpVisible=false,
                PropertySort=PropertySort.Categorized,
                BackColor=Background,
                ForeColor=TextColor,
                ViewBackColor=Background,
                ViewForeColor=TextColor,
                ViewBorderColor=Background,
                LineColor=Header,
                CategoryForeColor=TextColor,
                CategorySplitterColor=Header,
                DisabledItemForeColor=Color.FromArgb(177,188,204),
                SelectedItemWithFocusBackColor=Color.FromArgb(78,90,109),
                SelectedItemWithFocusForeColor=Color.White,
                CommandsBackColor=Background,
                CommandsForeColor=TextColor,
                Font=SystemFonts.MessageBoxFont,
                SelectedObject=settings,
                Margin=Padding.Empty
            };
            var actionArea=new Panel {Dock=DockStyle.Bottom,Height=83,BackColor=Background,Padding=new Padding(0,6,0,0)};
            var generate=new Button {Text="Gerar Flatshot",Dock=DockStyle.Top,Height=29,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(78,90,109),ForeColor=TextColor,UseVisualStyleBackColor=false};
            generate.FlatAppearance.BorderColor=Color.FromArgb(91,105,125);
            generate.FlatAppearance.MouseOverBackColor=Color.FromArgb(94,108,129);
            generate.Click+=delegate {Plugin.QueueGeneration(Options);};
            status=new Label {Text="Pronto. Indique a inserção após gerar.\nCurvas aproximadas por segmentos.",Dock=DockStyle.Fill,BackColor=Background,ForeColor=TextColor,Padding=new Padding(0,6,0,0),AutoEllipsis=true};
            actionArea.Controls.Add(status);
            actionArea.Controls.Add(generate);
            surface.Controls.Add(grid);
            surface.Controls.Add(actionArea);
            surface.Controls.Add(title);
            grid.ExpandAllGridItems();
        }
        // PaletteSet can assign a light host background when it attaches a control.
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if(BackColor!=Background)BackColor=Background;
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)hints.Dispose();
            base.Dispose(disposing);
        }
    }
    [TypeConverter(typeof(PaletteSettingsConverter))]
    public sealed class PaletteSettings
    {
        private int hiddenAci=8;
        private decimal tolerance=0.01m;
        public PaletteSettings(){PreserveColors=true;PreserveLayers=true;HiddenMode="Personalizadas";}
        [Category("Geral"),DisplayName("Preservar cores"),Description("Preservar a cor efetiva de cada objeto, incluindo RGB."),TypeConverter(typeof(YesNoConverter))]
        public bool PreserveColors {get;set;}
        [Category("Geral"),DisplayName("Preservar layers"),Description("Manter os layers originais no bloco 2D."),TypeConverter(typeof(YesNoConverter))]
        public bool PreserveLayers {get;set;}
        [Category("Linhas ocultas"),DisplayName("Exibição"),TypeConverter(typeof(HiddenModeConverter)),RefreshProperties(RefreshProperties.All)]
        public string HiddenMode {get;set;}
        [Category("Linhas ocultas"),DisplayName("Cor (ACI)"),Description("Cor das linhas ocultas: índice ACI entre 1 e 255.")]
        public int HiddenAci
        {
            get{return hiddenAci;}
            set {if(value<1 || value>255)throw new ArgumentOutOfRangeException("value","Use uma cor ACI entre 1 e 255.");hiddenAci=value;}
        }
        [Category("Linhas ocultas"),DisplayName("Tipo de linha")]
        public string HiddenLinetype {get{return "HIDDEN";}}
        [Browsable(false)]
        public string Overlap {get{return "Visible";}}
        [Browsable(false)]
        public decimal Tolerance
        {
            get{return tolerance;}
            set {if(value<0.0001m || value>1000m)throw new ArgumentOutOfRangeException("value","Use uma tolerância de 0,0001 a 1000.");tolerance=value;}
        }
        [Category("Escopo"),DisplayName("Origem")]
        public string Source {get{return "Model Space";}}
        [Category("Escopo"),DisplayName("Escala"),Description("Mantém as unidades e medidas do Model, sem aplicar a escala anotativa ou o zoom da tela.")]
        public string Scale {get{return "1:1 (Model)";}}
        [Category("Escopo"),DisplayName("Vista")]
        public string View {get{return "Atual / paralela";}}
        [Category("Escopo"),DisplayName("Objetos"),Description("Sólidos, superfícies e malhas, incluindo blocos locais. Não inclui xrefs e objetos 2D.")]
        public string Objects {get{return "Geometria 3D";}}
    }
    public sealed class PaletteSettingsConverter : ExpandableObjectConverter
    {
        public override bool GetPropertiesSupported(ITypeDescriptorContext context){return true;}
        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context,object value,Attribute[] attributes)
        {
            var properties=TypeDescriptor.GetProperties(typeof(PaletteSettings),attributes).Sort(new[]{"PreserveColors","PreserveLayers","HiddenMode","HiddenAci","HiddenLinetype","Source","Scale","View","Objects"});
            var model=value as PaletteSettings;
            if(model!=null && model.HiddenMode!="Personalizadas")
            {
                var descriptors=new PropertyDescriptor[properties.Count];
                for(int i=0;i<properties.Count;i++)descriptors[i]=properties[i].Name=="HiddenAci"?TypeDescriptor.CreateProperty(typeof(PaletteSettings),properties[i],ReadOnlyAttribute.Yes):properties[i];
                return new PropertyDescriptorCollection(descriptors);
            }
            return properties;
        }
    }
    public sealed class YesNoConverter : BooleanConverter
    {
        public override object ConvertTo(ITypeDescriptorContext context,CultureInfo culture,object value,Type destinationType)
        {if(destinationType==typeof(string) && value is bool)return (bool)value?"Sim":"Não";return base.ConvertTo(context,culture,value,destinationType);}
        public override object ConvertFrom(ITypeDescriptorContext context,CultureInfo culture,object value)
        {if(value is string){if((string)value=="Sim")return true;if((string)value=="Não")return false;}return base.ConvertFrom(context,culture,value);}
    }
    public sealed class HiddenModeConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context){return true;}
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context){return true;}
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context){return new StandardValuesCollection(new[]{"Personalizadas","Não exibir"});}
    }
}
