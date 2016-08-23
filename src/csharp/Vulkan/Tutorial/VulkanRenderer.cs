using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vulkan;
using Vulkan.Windows;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Vulkan.Tutorial
{
    struct QueueFamilyIndices
    {
        public readonly int GraphicsFamily;
        public readonly int PresentFamily;

        public bool IsComplete => GraphicsFamily >= 0 && PresentFamily >= 0;

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

    struct Vertex
    {
        public Vector2 Pos;
        public Vector3 Color;

        public static readonly uint OffsetPos = 0;
        public static readonly uint OffsetColor = 2 * sizeof(float);
        public static readonly uint Size = 5 * sizeof(float);

        public static readonly VertexInputBindingDescription BindingDescription = new VertexInputBindingDescription()
        {
            Binding = 0,
            Stride = 5 * sizeof(float),
            InputRate = VertexInputRate.Vertex,
        };

        public static void CopyToBuffer(Vertex[] vertices, IntPtr buffer)
        {
            var tempArray = new float[vertices.Length * 5];
            for (int i=0; i<vertices.Length; ++i)
            {
                vertices[i].Pos.CopyTo(tempArray, i * 5);
                vertices[i].Color.CopyTo(tempArray, i * 5 + 2);
            }
            Marshal.Copy(tempArray, 0, buffer, tempArray.Length);
        }

        public Vertex(Vector2 pos, Vector3 color)
        {
            Pos = pos;
            Color = color;
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 192)]
    struct UniformBufferObject
    {
        public Matrix4x4 model;
        public Matrix4x4 view;
        public Matrix4x4 proj;
    }

    class VulkanRenderer : IDisposable
    {
        public static readonly Vertex[] vertices = 
        {
            new Vertex(new Vector2(-0.5f, -0.5f), new Vector3(1.0f, 0.0f, 0.0f)),
            new Vertex(new Vector2( 0.5f, -0.5f), new Vector3(0.0f, 1.0f, 0.0f)),
            new Vertex(new Vector2( 0.5f,  0.5f), new Vector3(0.0f, 0.0f, 1.0f)),
            new Vertex(new Vector2(-0.5f,  0.5f), new Vector3(1.0f, 1.0f, 1.0f)),
        };

        public static readonly short[] indices = {
            0, 1, 2, 2, 3, 0,
        };

        private const uint VK_SUBPASS_EXTERNAL = ~0U; // see vulkan.h

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
        private Framebuffer[] vkSwapChainFramebuffers;
        private ShaderModule vkVertShaderModule;
        private ShaderModule vkFragShaderModule;
        private PipelineLayout vkPipelineLayout;
        private RenderPass vkRenderPass;
        private DescriptorSetLayout vkDescriptorSetLayout;
        private Pipeline vkPipeline;
        private CommandPool vkCommandPool;
        private CommandBuffer[] vkCommandBuffers;
        private Semaphore vkImageAvailableSemaphore;
        private Semaphore vkRenderFinishedSemaphore;

        private Buffer vkVertexBuffer;
        private DeviceMemory vkVertexBufferMemory;
        private Buffer vkStagingVertexBuffer;
        private DeviceMemory vkStagingVertexBufferMemory;

        private Buffer vkIndexBuffer;
        private DeviceMemory vkIndexBufferMemory;
        private Buffer vkStagingIndexBuffer;
        private DeviceMemory vkStagingIndexBufferMemory;

        private Buffer vkUniformBuffer;
        private DeviceMemory vkUniformBufferMemory;
        private Buffer vkStagingUniformBuffer;
        private DeviceMemory vkStagingUniformBufferMemory;

        public VulkanRenderer(Windowing Windowing, bool Debug)
        {
            debug = Debug;
            windowing = Windowing;

            CreateInstance();
            CreateSurface();
            PickPhysicalDevice();
            CreateLogicalDevice();
            CreateSwapChain();
            CreateImageViews();
            CreateRenderPass();
            CreateDescriptorSetLayout();
            CreateGraphicsPipeline();
            CreateFramebuffers();
            CreateCommandPool();
            CreateVertexBuffer();
            CreateIndexBuffer();
            CreateUniformBuffer();
            CreateDescriptorPool();
            CreateDescriptorSet();
            CreateCommandBuffers();
            CreateSemaphores();
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
                var availableExtensions = device.EnumerateDeviceExtensionProperties()?.Select(e => e.ExtensionName) ?? Enumerable.Empty<string>();
                Debug.WriteLine($"Available extensions: {list(availableExtensions)}");
                var requiredExtensions = GetRequiredDeviceExtensions().ToHashSet();
                var missingExtensions = requiredExtensions.Except(availableExtensions).ToSortedSet();
                if (missingExtensions.Count > 0)
                    Debug.WriteLine($"Missing extensions: {list(missingExtensions)}");

                var availableLayers = device.EnumerateDeviceLayerProperties()?.Select(e => e.LayerName) ?? Enumerable.Empty<string>();
                Debug.WriteLine($"Available layers: {list(availableLayers)}");
                var requiredLayers = GetRequiredDeviceLayers().ToHashSet();
                var missingLayers = requiredLayers.Except(availableLayers).ToSortedSet();
                if (missingLayers.Count > 0)
                    Debug.WriteLine($"Missing layers: {list(missingLayers)}");

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

        private void CreateRenderPass()
        {
            var colorAttachment = new AttachmentDescription()
            {
                Format = vkSwapChainImageFormat,
                Samples = SampleCountFlags.Count1,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            };

            var colorAttachmentRef = new AttachmentReference() {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            };

            var subPass = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachments = new[] { colorAttachmentRef },
            };

            var dependency = new SubpassDependency()
            {
                SrcSubpass = VK_SUBPASS_EXTERNAL,
                SrcStageMask = PipelineStageFlags.BottomOfPipe,
                SrcAccessMask = AccessFlags.MemoryRead,

                DstSubpass = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutput,
                DstAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
            };

            var renderPassInfo = new RenderPassCreateInfo()
            {
                Attachments = new[] { colorAttachment },
                Subpasses = new[] { subPass },
                Dependencies = new[] { dependency },
            };

            vkRenderPass = vkDevice.CreateRenderPass(renderPassInfo);
        }

        private void CreateDescriptorSetLayout()
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo()
            {
                Bindings = new[]
                {
                    new DescriptorSetLayoutBinding()
                    {
                        Binding = 0,
                        DescriptorType = DescriptorType.UniformBuffer,
                        StageFlags = ShaderStageFlags.Vertex,
                        DescriptorCount = 1,
                        // ImmutableSamplers = null,
                    }
                },
            };

            vkDescriptorSetLayout = vkDevice.CreateDescriptorSetLayout(layoutInfo);
        }

        private void CreateGraphicsPipeline()
        {
            vkVertShaderModule = vkDevice.CreateShaderModule(ReadFile("Shaders\\project.vert.spv"));
            vkFragShaderModule = vkDevice.CreateShaderModule(ReadFile("Shaders\\shader.frag.spv"));

            var shaderStages = new[] {
                new PipelineShaderStageCreateInfo()
                {
                    Name = "main",
                    Stage = ShaderStageFlags.Vertex,
                    Module = vkVertShaderModule,
                },
                new PipelineShaderStageCreateInfo()
                {
                    Name = "main",
                    Stage = ShaderStageFlags.Fragment,
                    Module = vkFragShaderModule,
                },
            };

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo()
            {
                VertexBindingDescriptions = new[] {
                    Vertex.BindingDescription,
                },
                VertexAttributeDescriptions = new[] {
                    new VertexInputAttributeDescription()
                    {
                        Binding = 0,
                        Location = 0,
                        Format = Format.R32g32Sfloat,
                        Offset = Vertex.OffsetPos,
                    },
                    new VertexInputAttributeDescription()
                    {
                        Binding = 0,
                        Location = 1,
                        Format = Format.R32g32b32Sfloat,
                        Offset = Vertex.OffsetColor,
                    },
                },
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo()
            {
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false,
            };

            var viewport = new Viewport()
            {
                X = 0f,
                Y = 0f,
                Width = (float) vkSwapChainExtent.Width,
                Height = (float) vkSwapChainExtent.Height,
                MinDepth = 0f,
                MaxDepth = 0f,
            };

            var scissor = new Rect2D()
            {
                Extent = vkSwapChainExtent
            };

            var viewportState = new PipelineViewportStateCreateInfo()
            {
                Viewports = new[] { viewport },
                Scissors = new[] { scissor },
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo()
            {
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.Back,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false,
            };

            var multisampling = new PipelineMultisampleStateCreateInfo()
            {
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1,
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.R | ColorComponentFlags.G | ColorComponentFlags.B | ColorComponentFlags.A,
                BlendEnable = false,
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo()
            {
                LogicOpEnable = false,
                LogicOp = LogicOp.Copy,
                Attachments = new[] { colorBlendAttachment },
                BlendConstants = new[] { 0.0f, 0.0f, 0.0f, 0.0f },
            };

            var pipelineLayoutInfo = new PipelineLayoutCreateInfo()
            {
                SetLayouts = new[] { vkDescriptorSetLayout },
            };
            vkPipelineLayout = vkDevice.CreatePipelineLayout(pipelineLayoutInfo);

            var pipelineInfo = new GraphicsPipelineCreateInfo()
            {
                Stages = shaderStages,
                VertexInputState = vertexInputInfo,
                ViewportState = viewportState,
                RasterizationState = rasterizer,
                MultisampleState = multisampling,
                // DepthStencilState = null,
                ColorBlendState = colorBlending,
                // DynamicState = null,
                Layout = vkPipelineLayout,
                RenderPass = vkRenderPass,
                Subpass = 0,
                InputAssemblyState = inputAssembly,
                // TessellationState = null,
                // BasePipelineHandle = null,
                BasePipelineIndex = -1,
            };

            var pipelineInfos = new[] { pipelineInfo };

            vkPipeline = vkDevice.CreateGraphicsPipelines(vkNullHandle<PipelineCache>(), pipelineInfos)[0];
        }

        private void CreateFramebuffers()
        {
            vkSwapChainFramebuffers = vkSwapChainImageViews
                .Select(iv => new FramebufferCreateInfo()
                {
                    RenderPass = vkRenderPass,
                    Attachments = new [] { iv },
                    Width = vkSwapChainExtent.Width,
                    Height = vkSwapChainExtent.Height,
                    Layers = 1,
                })
                .Select(fci => vkDevice.CreateFramebuffer(fci))
                .ToArray();
        }

        private void CreateCommandPool()
        {
            var indices = new QueueFamilyIndices(vkPhysicalDevice, vkSurface);

            vkCommandPool = vkDevice.CreateCommandPool(new CommandPoolCreateInfo()
            {
                QueueFamilyIndex = (uint)indices.GraphicsFamily,
                Flags = 0,
            });
        }

        private void CreateVertexBuffer()
        {
            DeviceSize bufferSize = Vertex.Size * (uint)vertices.Length;

            CreateBuffer(bufferSize,
                BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out vkStagingVertexBuffer, out vkStagingVertexBufferMemory);

            IntPtr bufferPtr = vkDevice.MapMemory(vkStagingVertexBufferMemory, 0, bufferSize);
            Vertex.CopyToBuffer(vertices, bufferPtr);
            vkDevice.UnmapMemory(vkStagingVertexBufferMemory);

            CreateBuffer(bufferSize,
                BufferUsageFlags.VertexBuffer | BufferUsageFlags.TransferDst,
                MemoryPropertyFlags.DeviceLocal,
                out vkVertexBuffer, out vkVertexBufferMemory);

            CopyBuffer(vkStagingVertexBuffer, vkVertexBuffer, bufferSize);
        }

        private void CreateIndexBuffer()
        {
            DeviceSize bufferSize = sizeof(short) * (uint)indices.Length;

            CreateBuffer(bufferSize,
                BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out vkStagingIndexBuffer, out vkStagingIndexBufferMemory);

            IntPtr bufferPtr = vkDevice.MapMemory(vkStagingIndexBufferMemory, 0, bufferSize);
            Marshal.Copy(indices, 0, bufferPtr, indices.Length);
            vkDevice.UnmapMemory(vkStagingIndexBufferMemory);

            CreateBuffer(bufferSize,
                BufferUsageFlags.VertexBuffer | BufferUsageFlags.TransferDst,
                MemoryPropertyFlags.DeviceLocal,
                out vkIndexBuffer, out vkIndexBufferMemory);

            CopyBuffer(vkStagingIndexBuffer, vkIndexBuffer, bufferSize);
        }

        private void CreateUniformBuffer()
        {
            DeviceSize bufferSize = Marshal.SizeOf<UniformBufferObject>();

            CreateBuffer(bufferSize,
                BufferUsageFlags.TransferSrc,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out vkStagingUniformBuffer, out vkStagingUniformBufferMemory);

            IntPtr bufferPtr = vkDevice.MapMemory(vkStagingUniformBufferMemory, 0, bufferSize);
            Marshal.Copy(indices, 0, bufferPtr, indices.Length);
            vkDevice.UnmapMemory(vkStagingUniformBufferMemory);

            CreateBuffer(bufferSize,
                BufferUsageFlags.UniformBuffer | BufferUsageFlags.TransferDst,
                MemoryPropertyFlags.DeviceLocal,
                out vkUniformBuffer, out vkUniformBufferMemory);

            CopyBuffer(vkStagingUniformBuffer, vkUniformBuffer, bufferSize);
        }

        private void CreateBuffer(DeviceSize size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory)
        {
            var bufferInfo = new BufferCreateInfo()
            {
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };

            buffer = vkDevice.CreateBuffer(bufferInfo);

            var memRequirements = vkDevice.GetBufferMemoryRequirements(buffer);

            var allocInfo = new MemoryAllocateInfo()
            {
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties),
            };

            bufferMemory = vkDevice.AllocateMemory(allocInfo);

            vkDevice.BindBufferMemory(buffer, bufferMemory, 0);
        }

        private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        {
            var memProperties = vkPhysicalDevice.GetMemoryProperties();

            for (var i=0; i<memProperties.MemoryTypeCount; ++i)
            {
                if ((typeFilter & (1 << i)) != 0 
                    && (memProperties.MemoryTypes[i].PropertyFlags & properties) != 0)
                {
                    return (uint)i;
                }
            }

            throw new InvalidOperationException("Failed to find suitable memory type");
        }

        private void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, DeviceSize size)
        {
            var allocInfo = new CommandBufferAllocateInfo()
            {
                Level = CommandBufferLevel.Primary,
                CommandPool = vkCommandPool,
                CommandBufferCount = 1,
            };

            var commandBuffer = vkDevice.AllocateCommandBuffers(allocInfo)[0];

            commandBuffer.Begin(new CommandBufferBeginInfo() { Flags = CommandBufferUsageFlags.OneTimeSubmit });
            commandBuffer.CmdCopyBuffer(srcBuffer, dstBuffer, new[] { new BufferCopy { Size = size } });
            commandBuffer.End();

            vkGraphicsQueue.Submit(new[] { new SubmitInfo() { CommandBuffers = new[] { commandBuffer } } }, vkNullHandle<Fence>());
            vkGraphicsQueue.WaitIdle();

            vkDevice.FreeCommandBuffers(vkCommandPool, new[] { commandBuffer });
        }

        private void CreateDescriptorPool()
        {
            var poolInfo = new DescriptorPoolCreateInfo()
            {
                PoolSizes = new[]
                {
                    new DescriptorPoolSize()
                    {
                        Type = DescriptorType.UniformBuffer,
                        DescriptorCount = 1,                        
                    },
                },
                MaxSets = 1,
            };

            vkDescriptorPool = vkDevice.CreateDescriptorPool(poolInfo);
        }

        private void CreateDescriptorSet()
        {
            var allocInfo = new DescriptorSetAllocateInfo()
            {
                DescriptorPool = vkDescriptorPool,
                SetLayouts = new[] { vkDescriptorSetLayout },
            };

            vkDescriptorSet = vkDevice.AllocateDescriptorSets(allocInfo)[0];

            var bufferInfo = new DescriptorBufferInfo()
            {
                Buffer = vkUniformBuffer,
                Offset = 0,
                Range = Marshal.SizeOf<UniformBufferObject>(),
            };

            var descriptorWrite = new WriteDescriptorSet()
            {
                DstSet = vkDescriptorSet,
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                BufferInfo = new[] { bufferInfo },
                // ImageInfo = null,
                // TexelBufferView = null,
            };

            vkDevice.UpdateDescriptorSets(new[] { descriptorWrite }, null);
        }

        private void CreateCommandBuffers()
        {
            var allocInfo = new CommandBufferAllocateInfo()
            {
                CommandPool = vkCommandPool,
                CommandBufferCount = (uint)vkSwapChainFramebuffers.Length,
                Level = CommandBufferLevel.Primary,
            };

            vkCommandBuffers = vkDevice.AllocateCommandBuffers(allocInfo);

            int i = 0;
            foreach (var buffer in vkCommandBuffers)
            {
                var beginInfo = new CommandBufferBeginInfo()
                {
                    Flags = CommandBufferUsageFlags.SimultaneousUse,
                    // InheritanceInfo = null,
                };
                buffer.Begin(beginInfo);

                var renderPassInfo = new RenderPassBeginInfo()
                {
                    RenderPass = vkRenderPass,
                    Framebuffer = vkSwapChainFramebuffers[i],
                    RenderArea = new Rect2D() { Extent=vkSwapChainExtent },
                    ClearValues = new[] { new ClearValue() { Color = new ClearColorValue(new[] { 0.2f, 0.2f, 0.4f, 1.0f })  } },
                };
                buffer.CmdBeginRenderPass(renderPassInfo, SubpassContents.Inline);

                buffer.CmdBindPipeline(PipelineBindPoint.Graphics, vkPipeline);

                buffer.CmdBindDescriptorSets(PipelineBindPoint.Graphics, vkPipelineLayout, 0, new[] { vkDescriptorSet }, new uint[] {});
                buffer.CmdBindVertexBuffers(0, new[] { vkVertexBuffer }, new DeviceSize[] { 0 });
                buffer.CmdBindIndexBuffer(vkIndexBuffer, 0, IndexType.Uint16);
                buffer.CmdDrawIndexed((uint)indices.Length, 1, 0, 0, 0);

                buffer.CmdEndRenderPass();

                buffer.End();

                ++i;
            }
        }

        private void CreateSemaphores()
        {
            var semaphoreInfo = new SemaphoreCreateInfo();

            vkImageAvailableSemaphore = vkDevice.CreateSemaphore(semaphoreInfo);
            vkRenderFinishedSemaphore = vkDevice.CreateSemaphore(semaphoreInfo);
        }


        private void UpdateUniformBuffer()
        {
            DeviceSize bufferSize = Marshal.SizeOf<UniformBufferObject>();

            var oneFullRotPer4SecInRad = (DateTime.Now.Ticks % (4 * TimeSpan.TicksPerSecond)) 
                * ((Math.PI / 2f) / TimeSpan.TicksPerSecond);

            var ubo = new UniformBufferObject()
            {
                model = Matrix4x4.CreateRotationZ((float)oneFullRotPer4SecInRad),
                view = Matrix4x4.CreateLookAt(new Vector3(2, 2, -2), new Vector3(0, 0, 0), new Vector3(0, 0, 1)),
                proj = Matrix4x4.CreatePerspectiveFieldOfView((float)(Math.PI / 4), windowing.Width / (float)windowing.Height, 0.1f, 10.0f),
            };

            IntPtr buffer = vkDevice.MapMemory(vkStagingUniformBufferMemory, 0, Marshal.SizeOf<UniformBufferObject>());
            Marshal.StructureToPtr(ubo, buffer, false);
            vkDevice.UnmapMemory(vkStagingUniformBufferMemory);

            CopyBuffer(vkStagingUniformBuffer, vkUniformBuffer, bufferSize);
        }

        public void DrawFrame()
        {
            UpdateUniformBuffer();

            var imageIndex = vkDevice.AcquireNextImageKHR(vkSwapChain, ulong.MaxValue, vkImageAvailableSemaphore, vkNullHandle<Fence>());

            var submitInfo = new SubmitInfo()
            {
                WaitSemaphores = new[] { vkImageAvailableSemaphore },
                WaitDstStageMask = new[] { PipelineStageFlags.ColorAttachmentOutput },
                CommandBuffers = new[] { vkCommandBuffers[imageIndex] },
                SignalSemaphores = new[] { vkRenderFinishedSemaphore },
            };

            vkGraphicsQueue.Submit(new[] { submitInfo }, vkNullHandle<Fence>());

            var presentInfo = new PresentInfoKhr()
            {
                WaitSemaphores = new[] { vkRenderFinishedSemaphore },
                Swapchains = new[] { vkSwapChain },
                ImageIndices = new[] { imageIndex },
                // Results = null,
            };

            vkPresentQueue.PresentKHR(presentInfo);
        }

        private byte[] ReadFile(string filename)
        {
            string dir = Directory.GetCurrentDirectory();
            string path = Path.Combine(dir, filename);

            using (var fileStream = File.Open(path, FileMode.Open))
            {
                byte[] fileContent = new byte[fileStream.Length];
                fileStream.Read(fileContent, 0, (int)fileStream.Length - 1);
                return fileContent;
            }            
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

        /// <summary>
        /// VK_NULL_HANDLE equivalent for VulkanSharp
        /// </summary>
        /// <typeparam name="T">the type of Vulkan handle to create a null-handle for</typeparam>
        /// <returns>the null-handle</returns>
        private static T vkNullHandle<T>() where T : new()
        {
            // VulkanSharp wraps Vulkan handles into managed classes with a field 'm' for the handle.
            // So by simply creating the wrapper we get a null-handle because 'm' is intialized to null.
            return new T();
        }

        private static string list(IEnumerable<string> items) => string.Join(", ", items);

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls
        private DescriptorPool vkDescriptorPool;
        private DescriptorSet vkDescriptorSet;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (vkDevice != null)
                    {
                        vkDevice.WaitIdle();

                        if (vkUniformBuffer != null)
                            vkDevice.DestroyBuffer(vkUniformBuffer);
                        if (vkUniformBufferMemory != null)
                            vkDevice.FreeMemory(vkUniformBufferMemory);
                        if (vkStagingUniformBuffer != null)
                            vkDevice.DestroyBuffer(vkStagingUniformBuffer);
                        if (vkStagingUniformBufferMemory != null)
                            vkDevice.FreeMemory(vkStagingUniformBufferMemory);

                        if (vkIndexBuffer != null)
                            vkDevice.DestroyBuffer(vkIndexBuffer);
                        if (vkIndexBufferMemory != null)
                            vkDevice.FreeMemory(vkIndexBufferMemory);
                        if (vkStagingIndexBuffer != null)
                            vkDevice.DestroyBuffer(vkStagingIndexBuffer);
                        if (vkStagingIndexBufferMemory != null)
                            vkDevice.FreeMemory(vkStagingIndexBufferMemory);

                        if (vkVertexBuffer != null)
                            vkDevice.DestroyBuffer(vkVertexBuffer);
                        if (vkVertexBufferMemory != null)
                            vkDevice.FreeMemory(vkVertexBufferMemory);
                        if (vkStagingVertexBuffer != null)
                            vkDevice.DestroyBuffer(vkStagingVertexBuffer);
                        if (vkStagingVertexBufferMemory != null)
                            vkDevice.FreeMemory(vkStagingVertexBufferMemory);

                        if (vkDescriptorPool != null)
                            vkDevice.DestroyDescriptorPool(vkDescriptorPool);
                        if (vkCommandPool != null)
                            vkDevice.DestroyCommandPool(vkCommandPool);

                        if (vkSwapChainFramebuffers != null)
                            foreach (var fb in vkSwapChainFramebuffers)
                                vkDevice.DestroyFramebuffer(fb);

                        if (vkPipeline != null)
                            vkDevice.DestroyPipeline(vkPipeline);
                        if (vkPipelineLayout != null)
                            vkDevice.DestroyPipelineLayout(vkPipelineLayout);
                        if (vkDescriptorSetLayout != null)
                            vkDevice.DestroyDescriptorSetLayout(vkDescriptorSetLayout);
                        if (vkRenderPass != null)
                            vkDevice.DestroyRenderPass(vkRenderPass);
                        if (vkFragShaderModule != null)
                            vkDevice.DestroyShaderModule(vkFragShaderModule);
                        if (vkVertShaderModule != null)
                            vkDevice.DestroyShaderModule(vkVertShaderModule);
                        if (vkImageAvailableSemaphore != null)
                            vkDevice.DestroySemaphore(vkImageAvailableSemaphore);
                        if (vkRenderFinishedSemaphore != null)
                            vkDevice.DestroySemaphore(vkRenderFinishedSemaphore);

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
