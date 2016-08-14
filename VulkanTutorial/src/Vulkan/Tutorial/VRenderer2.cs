using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vulkan;
using Vulkan.Windows;
using Vulkan.Tutorial;
using System.Diagnostics;

namespace Vulkan.Tutorial
{
    struct QueueFamilyIndices
    {
        public readonly int GraphicsFamily;
        public readonly int PresentFamily;

        public bool IsComplete => GraphicsFamily != null && PresentFamily != null;

        public QueueFamilyIndices(PhysicalDevice device, SurfaceKhr surface)
        {
            int graphicsIndex = -1;
            int presentIndex = -1;

            int i = 0;
            foreach (var f in device.GetQueueFamilyProperties())
            {
                if (graphicsIndex < 0 && f.QueueCount > 0 && (f.QueueFlags & QueueFlags.Graphics) != 0)
                    graphicsIndex = i;

                if (presentIndex < 0 && f.QueueCount > 0 && device.GetSurfaceSupportKHR((uint)i, surface))
                    presentIndex = i;

                ++i;
            }

            GraphicsFamily = graphicsIndex;
            PresentFamily = presentIndex;
        }
    };

    struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKhr capabilities;
        public SurfaceFormatKhr[] formats;
        public PresentModeKhr[] presentModes;

        public bool IsComplete => formats.Length > 0 && presentModes.Length > 0;

        public SwapChainSupportDetails(PhysicalDevice device, SurfaceKhr surface)
        {
            capabilities = device.GetSurfaceCapabilitiesKHR(surface);
            formats = device.GetSurfaceFormatsKHR(surface);
            presentModes = device.GetSurfacePresentModesKHR(surface);
        }
    };

    class VRenderer2 : IDisposable
    {
        private readonly string[] DEBUG_INSTANCE_EXTENSIONS = { "VK_EXT_debug_report" };
        private readonly string[] DEBUG_INSTANCE_LAYERS = { "VK_LAYER_LUNARG_standard_validation" };
        private readonly string[] DEBUG_DEVICE_LAYERS = { "VK_LAYER_LUNARG_standard_validation" };
        private readonly string[] REQUIRED_DEVICE_EXTENSIONS = { "VK_KHR_swapchain" };

        private readonly bool debug;
        private readonly Windowing windowing;
        private Instance vkInstance;
        private SurfaceKhr vkSurface;
        private PhysicalDevice vkPhysicalDevice;
        private Device vkDevice;
        private Queue vkGraphicsQueue;
        private Queue vkPresentQueue;
        private SwapchainKhr vkSwapChain;
        private Format vkSwapChainImageFormat;
        private Extent2D vkSwapChainExtent;
        private Image[] vkSwapChainImages;
        private ImageView[] vkSwapChainImageViews;

        public VRenderer2(Windowing Windowing, bool Debug)
        {
            debug = Debug;
            windowing = Windowing;

            CreateInstance();
            CreateSurface();
            PickPhysicalDevice();
            CreateLogicalDevice();
            CreateSwapChain();
            CreateImageViews();
        }

        private void CreateInstance()
        {
            var availableExtensions = Commands.EnumerateInstanceExtensionProperties().Select(e => e.ExtensionName).ToHashSet();
            var availableLayers = Commands.EnumerateInstanceLayerProperties().Select(e => e.LayerName).ToHashSet();

            var requiredExtensions = GetRequiredInstanceExtensions().ToHashSet();
            var requiredLayers = GetRequiredInstanceLayers().ToHashSet();

            var missingExtensions = requiredExtensions.Except(availableExtensions).ToSortedSet();
            if (missingExtensions.Count > 0)
                throw new InvalidOperationException($"Vulkan is missing extensions [{list(missingExtensions)}].");

            var missingLayers = requiredLayers.Except(availableLayers).ToSortedSet();
            if (missingExtensions.Count > 0)
                throw new InvalidOperationException($"Vulkan is missing layers [{list(missingLayers)}].");

            var createInfo = new InstanceCreateInfo()
            {
                EnabledExtensionNames = requiredExtensions.ToArray(),
                EnabledLayerNames = requiredLayers.ToArray(),
            };

            vkInstance = new Instance(createInfo);
        }

        private void CreateSurface()
        {
            vkSurface = windowing.CreateSurface(vkInstance);
        }

        private void PickPhysicalDevice()
        {
            var physicalDevices = vkInstance.EnumeratePhysicalDevices();
            if (physicalDevices.Length == 0)
                throw new InvalidOperationException("No devices with Vulkan support available. Is the Vulkan driver for your GPU installed?");

            foreach (var device in physicalDevices)
            {
                if (IsDeviceSufficient(device)) {
                    vkPhysicalDevice = device;
                    return;
                }
            }

            throw new InvalidOperationException("Non of the availalbe Vulkan devices is sufficient.");
        }

        private bool IsDeviceSufficient(PhysicalDevice device)
        {
            PhysicalDeviceProperties props = device.GetProperties();
            PhysicalDeviceFeatures features = device.GetFeatures();

            Debug.WriteLine(props.DeviceName);
            Debug.Indent();

            try
            {
                var requiredExtensions = GetRequiredDeviceExtensions().ToHashSet();
                var availableExtensions = device.EnumerateDeviceExtensionProperties().Select(e => e.ExtensionName);
                var missingExtensions = requiredExtensions.Except(availableExtensions).ToSortedSet();
                if (missingExtensions.Count > 0)
                    Debug.WriteLine($"Missing extensions {list(missingExtensions)}");

                var availableLayers = device.EnumerateDeviceLayerProperties().Select(e => e.LayerName);
                var requiredLayers = GetRequiredDeviceLayers().ToHashSet();
                var missingLayers = requiredLayers.Except(availableLayers).ToSortedSet();
                if (missingLayers.Count > 0)
                    Debug.WriteLine($"Missing layers {list(missingLayers)}");

                if (missingExtensions.Count > 0 || missingLayers.Count > 0) 
                    return false; // no further checks, extension functions to do so might be missing

                var indices = new QueueFamilyIndices(device, vkSurface);
                if (!indices.IsComplete)
                    Debug.WriteLine($"Command queue support insufficient");

                var swapChain = new SwapChainSupportDetails(device, vkSurface);
                if (!swapChain.IsComplete)
                    Debug.WriteLine($"Swapchain support insufficient");

                return indices.IsComplete && swapChain.IsComplete;
            }
            finally
            {
                Debug.Unindent();
            }
        }

        private void CreateLogicalDevice()
        {
            var indices = new QueueFamilyIndices(vkPhysicalDevice, vkSurface);

            var queueCreateInfos = new HashSet<uint>()
            {
                (uint)indices.GraphicsFamily,
                (uint)indices.PresentFamily,
            }
            .Select(index => new DeviceQueueCreateInfo()
            {
                QueueFamilyIndex = index,
                QueueCount = 1,
                QueuePriorities = new [] { 1.0f },
            })
            .ToArray();

            var features = new PhysicalDeviceFeatures();

            vkDevice = vkPhysicalDevice.CreateDevice(new DeviceCreateInfo()
            {
                EnabledExtensionNames = GetRequiredDeviceExtensions().ToArray(),
                EnabledLayerNames = GetRequiredDeviceLayers().ToArray(),
                EnabledFeatures = features,
                QueueCreateInfos = queueCreateInfos,
            });

            vkGraphicsQueue = vkDevice.GetQueue((uint)indices.GraphicsFamily, 0);
            vkPresentQueue = vkDevice.GetQueue((uint)indices.PresentFamily, 0);
        }

        private void CreateSwapChain()
        {
            var swapChainSupport = new SwapChainSupportDetails(vkPhysicalDevice, vkSurface);

            SurfaceFormatKhr surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.formats);
            PresentModeKhr presentMode = ChooseSwapPresentMode(swapChainSupport.presentModes);
            Extent2D extent = ChooseSwapExtent(swapChainSupport.capabilities);

            uint imageCount = swapChainSupport.capabilities.MinImageCount + 1;
            if (swapChainSupport.capabilities.MaxImageCount > 0)
                imageCount = Math.Min(imageCount, swapChainSupport.capabilities.MaxImageCount);

            var createInfo = new SwapchainCreateInfoKhr()
            {
                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachment,
                
                PreTransform = swapChainSupport.capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKhr.Opaque,
                PresentMode = presentMode,
                Clipped = true,
                // OldSwapchain = null,
                Surface = vkSurface,
            };

            var indices = new QueueFamilyIndices(vkPhysicalDevice, vkSurface);
            if (indices.GraphicsFamily != indices.PresentFamily)
            {
                createInfo.ImageSharingMode = SharingMode.Concurrent;
                createInfo.QueueFamilyIndices = new[] 
                {
                    (uint)indices.GraphicsFamily,
                    (uint)indices.PresentFamily
                };
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }

            vkSwapChain = vkDevice.CreateSwapchainKHR(createInfo);
            vkSwapChainImages = vkDevice.GetSwapchainImagesKHR(vkSwapChain);
            vkSwapChainImageFormat = surfaceFormat.Format;
            vkSwapChainExtent = extent;
        }

        private SurfaceFormatKhr ChooseSwapSurfaceFormat(SurfaceFormatKhr[] formats)
        {
            if (formats.Length == 1 && formats[0].Format == Format.Undefined)
                return new SurfaceFormatKhr() { Format = Format.B8g8r8a8Unorm, ColorSpace = ColorSpaceKhr.SrgbNonlinear };

            foreach (var format in formats)
            {
                if (format.Format == Format.B8g8r8a8Unorm && format.ColorSpace == ColorSpaceKhr.SrgbNonlinear)
                    return format;
            }

            return formats[0];
        }

        private PresentModeKhr ChooseSwapPresentMode(PresentModeKhr[] presentModes)
        {
            return presentModes.Any(pm => pm == PresentModeKhr.Mailbox) ? PresentModeKhr.Mailbox : PresentModeKhr.Fifo;
        }

        private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKhr capabilities)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
                return capabilities.CurrentExtent;

            return new Extent2D()
            {
                Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, windowing.Width)),
                Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, windowing.Height)),
            };
        }

        private void CreateImageViews()
        {
            vkSwapChainImageViews = vkSwapChainImages
                .Select(i => new ImageViewCreateInfo()
                {
                    Image = i,
                    ViewType = ImageViewType.View2D,
                    Format = vkSwapChainImageFormat,
                    Components = new ComponentMapping()
                    {
                        R = ComponentSwizzle.Identity,
                        G = ComponentSwizzle.Identity,
                        B = ComponentSwizzle.Identity,
                        A = ComponentSwizzle.Identity,
                    },
                    SubresourceRange = new ImageSubresourceRange()
                    {
                        AspectMask = ImageAspectFlags.Color,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                    },
                })
                .Select(ci => vkDevice.CreateImageView(ci))
                .ToArray();
        }

        protected virtual IEnumerable<string> GetRequiredInstanceLayers()
        {
            var result = Enumerable.Empty<string>();
            if (debug) result = result.Concat(DEBUG_INSTANCE_LAYERS);
            return result;
        }

        protected virtual IEnumerable<string> GetRequiredInstanceExtensions()
        {
            var result = Enumerable.Empty<string>();
            if (debug) result = result.Concat(DEBUG_INSTANCE_EXTENSIONS);
            result = result.Concat(windowing.RequiredVulkanExtensions);
            return result;
        }

        protected virtual IEnumerable<string> GetRequiredDeviceExtensions()
        {
            return REQUIRED_DEVICE_EXTENSIONS;
        }

        protected virtual IEnumerable<string> GetRequiredDeviceLayers()
        {
            var result = Enumerable.Empty<string>();
            if (debug) result = result.Concat(DEBUG_DEVICE_LAYERS);
            return result;
        }

        private static string list(IEnumerable<string> items) => string.Join(", ", items);

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (vkDevice != null)
                    {
                        if (vkSwapChainImageViews != null)
                            foreach (var iv in vkSwapChainImageViews)
                                vkDevice.DestroyImageView(iv);

                        if (vkSwapChain != null)
                            vkDevice.DestroySwapchainKHR(vkSwapChain);

                        vkDevice.Destroy();
                    }

                    if (vkInstance != null)
                    {
                        if (vkSurface != null)
                            vkInstance.DestroySurfaceKHR(vkSurface);

                        vkInstance.Destroy();
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~VRenderer2() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
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
