using System;
using System.Collections.Generic;
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

        private void ExportProgressWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isCloseAllowed)
            {
                e.Cancel = true;
            }
        }
    }
}
