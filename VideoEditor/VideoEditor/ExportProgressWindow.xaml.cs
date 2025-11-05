using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VideoEditor.ViewModels;

namespace VideoEditor
{
    public partial class ExportProgressWindow : Window
    {

        public ExportProgressWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ExportProgressWindow_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is ExportProgressViewModel vm)
            {
                if (!vm.IsFinished && vm.CancelCommand.CanExecute(null))
                {
                    e.Cancel = true;
                    vm.CancelCommand.Execute(null);
                }
            }
        }
    }
}
