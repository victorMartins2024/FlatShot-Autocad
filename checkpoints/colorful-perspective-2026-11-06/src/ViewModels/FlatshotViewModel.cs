using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using ColorFlatshot.Models;

namespace ColorFlatshot.ViewModels
{
    public sealed class FlatshotViewModel : INotifyPropertyChanged
    {
        private bool hidden, tangencies, allObjects, asBlock = true;
        private string tolerance="0.1",projection="Vista atual";
        public event PropertyChangedEventHandler PropertyChanged;
        public bool IncludeHiddenLines { get { return hidden; } set { hidden = value; Changed("IncludeHiddenLines"); } }
        public bool IncludeTangencies { get { return tangencies; } set { tangencies = value; Changed("IncludeTangencies"); } }
        public bool AllObjects { get { return allObjects; } set { allObjects = value; Changed("AllObjects"); } }
        public bool InsertAsBlock { get { return asBlock; } set { asBlock = value; Changed("InsertAsBlock"); } }
        public string PerspectiveToleranceText { get { return tolerance; } set { tolerance=value;Changed("PerspectiveToleranceText"); } }
        public string ProjectionStatus { get { return projection; } set { if(projection!=value){projection=value;Changed("ProjectionStatus");} } }
        public FlatshotOptions Options()
        {
            double value;
            if(!double.TryParse(tolerance,NumberStyles.Float,CultureInfo.CurrentCulture,out value)&&
                !double.TryParse(tolerance,NumberStyles.Float,CultureInfo.InvariantCulture,out value))value=double.NaN;
            return new FlatshotOptions { View = ProjectionView.Current, IncludeHiddenLines = hidden,
                IncludeTangencies = tangencies, InsertAsBlock = asBlock,PerspectiveTolerance=value };
        }
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action execute;
        public RelayCommand(Action execute) { this.execute = execute; }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { execute(); }
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
}
