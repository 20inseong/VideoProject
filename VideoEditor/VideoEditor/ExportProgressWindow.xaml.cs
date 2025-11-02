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
        private bool _isCloseAllowed = false;

        public ExportProgressWindow()
        {
            InitializeComponent();
        }

        public void AllowClose()
        {
            _isCloseAllowed = true;
        }

        private void ExportProgressWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isCloseAllowed)
            {
                return;
            }

            e.Cancel = true;

            if (DataContext is ExportProgressViewModel vm && vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
            }
        }
    }
}
