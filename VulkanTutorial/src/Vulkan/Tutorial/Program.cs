using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

namespace Vulkan.Tutorial
{
    class Program
    {
        static void Main(string[] args)
        {
            Windowing windowing = new Windowing(500, 500);
            VRenderer2 r = new VRenderer2(windowing, true);

            Console.ReadLine();
            r.Dispose();
            Console.ReadLine();
        }
    }
}
