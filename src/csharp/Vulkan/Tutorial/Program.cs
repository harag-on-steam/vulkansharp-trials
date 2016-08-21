using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Vulkan.Tutorial
{
    class Program
    {
        static void Main(string[] args)
        {
            Windowing windowing = new Windowing(500, 500);
            VulkanRenderer renderer = new VulkanRenderer(windowing, true);

            Console.WriteLine("Renderer created, press [Enter] to draw or enter \"q\" to exit.");
            while (Console.ReadLine() != "q")
                renderer.DrawFrame();
                // windowing.InvokeAndWait(f => renderer.DrawFrame());

            windowing.Dispose();
            renderer.Dispose();
            Console.WriteLine("Renderer disposed, press [Enter] to exit.");
            Console.ReadLine();
        }
    }
}
