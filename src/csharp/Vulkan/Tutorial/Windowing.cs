using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using Vulkan.Windows;

namespace Vulkan.Tutorial
{
    class Windowing : IDisposable
    {
        public uint Width;
        public uint Height;

        private Form Window;
        private Thread WindowThread;
        private IntPtr WindowHandle;

        public Windowing(uint width, uint height)
        {
            InitWindow(width, height);
            this.Width = width;
            this.Height = height;
        }

        private void InitWindow(uint width, uint height)
        {
            using (EventWaitHandle WindowInitalized = new EventWaitHandle(false, EventResetMode.ManualReset))
            {
                WindowThread = new Thread(() => {
                    Window = new Form();
                    Window.FormBorderStyle = FormBorderStyle.FixedSingle;
                    Window.Padding = new Padding(0);
                    Window.ClientSize = new Size((int)width, (int)height);
                    Window.MinimumSize = Window.Size;
                    Window.MaximumSize = Window.Size;
                    Window.MaximizeBox = false;
                    Window.MinimizeBox = false;
                    Window.Text = "Vulkan";

                    // force handle creation
                    WindowHandle = Window.Handle;
                    WindowInitalized.Set();

                    Application.Run(Window);
                });

                WindowThread.SetApartmentState(ApartmentState.STA);
                WindowThread.IsBackground = true;
                WindowThread.Start();
                WindowInitalized.WaitOne();
            }
        }

        public T InvokeAndWait<T>(Func<Form, T> inWindowThread)
        {
            Func<T> f = () => inWindowThread(Window);
            return (T)Window.Invoke(f);
        }

        public void InvokeAndWait(Action<Form> inWindowThread)
        {
            Action a = () => inWindowThread(Window);
            Window.Invoke(a);
        }

        public ICollection<string> RequiredVulkanExtensions { get; } = new HashSet<string>()
        {
            "VK_KHR_surface",
            "VK_KHR_win32_surface",
        };

        public SurfaceKhr CreateSurface(Instance vkInstance)
        {
            return vkInstance.CreateWin32SurfaceKHR(new Win32SurfaceCreateInfoKhr
            {
                Hinstance = Process.GetCurrentProcess().Handle,
                Hwnd = WindowHandle,
            });
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    InvokeAndWait((f) => f.Dispose());
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //~Windowing()
        //{
        //    Dispose(false);
        //}

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

    }
}
