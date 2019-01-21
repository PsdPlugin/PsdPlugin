using PaintDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaintDotNet.Data.PhotoshopFileType
{
    public class BitmapLayerInfo
    {
        public BitmapLayer Layer { get; set; }
        public bool IsGroupStart { get; set; }
        public bool IsGroupEnd { get; set; }
        public bool RenderAsRegularLayer { get; set; }
        public string Name { get; set; }
    }
}
