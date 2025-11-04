using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace VideoEditor.Models
{
    class ThumbnailItem
    {
        public BitmapImage Image { get; set; }
        public double TimePosition { get; set; }
        public string ImagePath { get; set; }
    }
}
