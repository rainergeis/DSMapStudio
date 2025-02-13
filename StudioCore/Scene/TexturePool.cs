﻿using System;
using System.Collections.Generic;
using System.Text;
using SoulsFormats;
using StudioCore.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Veldrid;
using System.Collections.Concurrent;

namespace StudioCore.Scene
{
    /// <summary>
    /// Low level texture pool that maintains an array of descriptor sets that can
    /// be bound to a shader.
    /// </summary>
    public class TexturePool
    {
        private static PixelFormat GetPixelFormatFromFourCC(string str)
        {
            switch (str)
            {
                case "DXT1":
                    return PixelFormat.BC1_Rgba_UNorm_SRgb;
                case "DXT3":
                    return PixelFormat.BC2_UNorm_SRgb;
                case "DXT5":
                    return PixelFormat.BC3_UNorm_SRgb;
                case "ATI1":
                    return PixelFormat.BC4_UNorm; // Monogame workaround :fatcat:
                case "ATI2":
                    return PixelFormat.BC5_UNorm;
                // From wtf
                case "q\0\0\0":
                    return PixelFormat.R16_G16_B16_A16_Float;
                default:
                    throw new Exception($"Unknown DDS Type: {str}");
            }
        }

        private static PixelFormat GetPixelFormatFromDXGI(DDS.DXGI_FORMAT fmt)
        {
            switch (fmt)
            {
                case DDS.DXGI_FORMAT.B5G5R5A1_UNORM:
                    return PixelFormat.B5_G5_R5_A1_UNorm;
                case DDS.DXGI_FORMAT.B8G8R8A8_TYPELESS:
                case DDS.DXGI_FORMAT.B8G8R8A8_UNORM:
                case DDS.DXGI_FORMAT.B8G8R8X8_TYPELESS:
                case DDS.DXGI_FORMAT.B8G8R8X8_UNORM:
                    return PixelFormat.B8_G8_R8_A8_UNorm;
                case DDS.DXGI_FORMAT.B8G8R8A8_UNORM_SRGB:
                case DDS.DXGI_FORMAT.B8G8R8X8_UNORM_SRGB:
                    return PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
                case DDS.DXGI_FORMAT.R8G8B8A8_UNORM_SRGB:
                    return PixelFormat.R8_G8_B8_A8_UNorm_SRgb;
                case DDS.DXGI_FORMAT.R8G8B8A8_UNORM:
                case DDS.DXGI_FORMAT.R8G8B8A8_TYPELESS:
                    return PixelFormat.R8_G8_B8_A8_UNorm;
                case DDS.DXGI_FORMAT.BC1_TYPELESS:
                case DDS.DXGI_FORMAT.BC1_UNORM:
                    return PixelFormat.BC1_Rgba_UNorm;
                case DDS.DXGI_FORMAT.BC1_UNORM_SRGB:
                    return PixelFormat.BC1_Rgba_UNorm_SRgb;
                case DDS.DXGI_FORMAT.BC2_TYPELESS:
                case DDS.DXGI_FORMAT.BC2_UNORM:
                    return PixelFormat.BC2_UNorm;
                case DDS.DXGI_FORMAT.BC2_UNORM_SRGB:
                    return PixelFormat.BC2_UNorm_SRgb;
                case DDS.DXGI_FORMAT.BC3_TYPELESS:
                case DDS.DXGI_FORMAT.BC3_UNORM:
                    return PixelFormat.BC3_UNorm;
                case DDS.DXGI_FORMAT.BC3_UNORM_SRGB:
                    return PixelFormat.BC3_UNorm_SRgb;
                case DDS.DXGI_FORMAT.BC4_TYPELESS:
                case DDS.DXGI_FORMAT.BC4_UNORM:
                    return PixelFormat.BC4_UNorm;
                case DDS.DXGI_FORMAT.BC4_SNORM:
                    return PixelFormat.BC4_SNorm;
                case DDS.DXGI_FORMAT.BC5_TYPELESS:
                case DDS.DXGI_FORMAT.BC5_UNORM:
                    return PixelFormat.BC5_UNorm;
                case DDS.DXGI_FORMAT.BC5_SNORM:
                    return PixelFormat.BC5_SNorm;
                case DDS.DXGI_FORMAT.BC6H_TYPELESS:
                case DDS.DXGI_FORMAT.BC6H_UF16:
                    return PixelFormat.BC6H_UFloat;
                case DDS.DXGI_FORMAT.BC6H_SF16:
                    return PixelFormat.BC6H_SFloat;
                case DDS.DXGI_FORMAT.BC7_TYPELESS:
                case DDS.DXGI_FORMAT.BC7_UNORM:
                    return PixelFormat.BC7_UNorm;
                case DDS.DXGI_FORMAT.BC7_UNORM_SRGB:
                    return PixelFormat.BC7_UNorm_SRgb;
                default:
                    throw new Exception($"Unimplemented DXGI Type: {fmt.ToString()}");
            }
        }

        // From MonoGame.Framework/Graphics/Texture2D.cs and MonoGame.Framework/Graphics/TextureCube.cs
        //private static (int ByteCount, Rectangle Rect) GetMipInfo(PixelFormat sf, int width, int height, int mip, bool isCubemap)
        private static int GetMipInfo(PixelFormat sf, int width, int height, int mip, bool isCubemap)
        {
            width = Math.Max(width >> mip, 1);
            height = Math.Max(height >> mip, 1);

            int formatTexelSize = GetTexelSize(sf);

            if (isCubemap)
            {
                if (IsCompressedFormat(sf))
                {
                    var roundedWidth = (width + 3) & ~0x3;
                    var roundedHeight = (height + 3) & ~0x3;

                    int byteCount = roundedWidth * roundedHeight * formatTexelSize / 16;

                    //return (byteCount, new Rectangle(0, 0, roundedWidth, roundedHeight));
                    return byteCount;
                }
                else
                {
                    int byteCount = width * height * formatTexelSize;

                    return byteCount;
                    //return (byteCount, new Rectangle(0, 0, width, height));
                }
            }
            else
            {
                if (IsCompressedFormat(sf))
                {
                    int blockWidth, blockHeight;
                    GetBlockSize(sf, out blockWidth, out blockHeight);

                    int blockWidthMinusOne = blockWidth - 1;
                    int blockHeightMinusOne = blockHeight - 1;

                    var roundedWidth = (width + blockWidthMinusOne) & ~blockWidthMinusOne;
                    var roundedHeight = (height + blockHeightMinusOne) & ~blockHeightMinusOne;

                    var rect = new Rectangle(0, 0, roundedWidth, roundedHeight);

                    int byteCount;

                    byteCount = roundedWidth * roundedHeight * formatTexelSize / (blockWidth * blockHeight);

                    //return (byteCount, rect);
                    return byteCount;
                }
                else
                {
                    int byteCount = width * height * formatTexelSize;

                    //return (byteCount, new Rectangle(0, 0, width, height));
                    return byteCount;
                }


            }

        }

        internal static int GetBlockSize(byte tpfTexFormat)
        {
            switch (tpfTexFormat)
            {
                case 105:
                    return 4;
                case 0:
                case 1:
                case 22:
                case 25:
                case 103:
                case 108:
                case 109:
                    return 8;
                case 5:
                case 100:
                case 102:
                case 106:
                case 107:
                case 110:
                    return 16;
                default:
                    throw new NotImplementedException($"TPF Texture format {tpfTexFormat} BlockSize unknown.");
            }
        }

        public static bool IsCompressedFormat(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.BC1_Rgba_UNorm:
                case PixelFormat.BC1_Rgba_UNorm_SRgb:
                case PixelFormat.BC1_Rgb_UNorm:
                case PixelFormat.BC1_Rgb_UNorm_SRgb:
                case PixelFormat.BC2_UNorm:
                case PixelFormat.BC2_UNorm_SRgb:
                case PixelFormat.BC3_UNorm:
                case PixelFormat.BC3_UNorm_SRgb:
                case PixelFormat.BC4_UNorm:
                case PixelFormat.BC4_SNorm:
                case PixelFormat.BC5_UNorm:
                case PixelFormat.BC5_SNorm:
                case PixelFormat.BC6H_SFloat:
                case PixelFormat.BC6H_UFloat:
                case PixelFormat.BC7_UNorm:
                case PixelFormat.BC7_UNorm_SRgb:
                    return true;
            }
            return false;
        }

        public static void GetBlockSize(PixelFormat surfaceFormat, out int width, out int height)
        {
            switch (surfaceFormat)
            {
                case PixelFormat.BC1_Rgba_UNorm:
                case PixelFormat.BC1_Rgba_UNorm_SRgb:
                case PixelFormat.BC1_Rgb_UNorm:
                case PixelFormat.BC1_Rgb_UNorm_SRgb:
                case PixelFormat.BC2_UNorm:
                case PixelFormat.BC2_UNorm_SRgb:
                case PixelFormat.BC3_UNorm:
                case PixelFormat.BC3_UNorm_SRgb:
                case PixelFormat.BC4_UNorm:
                case PixelFormat.BC4_SNorm:
                case PixelFormat.BC5_UNorm:
                case PixelFormat.BC5_SNorm:
                case PixelFormat.BC6H_SFloat:
                case PixelFormat.BC6H_UFloat:
                case PixelFormat.BC7_UNorm:
                case PixelFormat.BC7_UNorm_SRgb:
                    width = 4;
                    height = 4;
                    break;
                default:
                    width = 1;
                    height = 1;
                    break;
            }
        }

        public static int GetTexelSize(PixelFormat surfaceFormat)
        {
            switch (surfaceFormat)
            {
                case PixelFormat.BC1_Rgba_UNorm:
                case PixelFormat.BC1_Rgba_UNorm_SRgb:
                case PixelFormat.BC1_Rgb_UNorm:
                case PixelFormat.BC1_Rgb_UNorm_SRgb:
                case PixelFormat.BC4_UNorm:
                case PixelFormat.BC4_SNorm:
                case PixelFormat.BC5_UNorm:
                case PixelFormat.BC5_SNorm:
                    return 8;
                case PixelFormat.BC2_UNorm:
                case PixelFormat.BC2_UNorm_SRgb:
                case PixelFormat.BC3_UNorm:
                case PixelFormat.BC3_UNorm_SRgb:
                case PixelFormat.BC6H_SFloat:
                case PixelFormat.BC6H_UFloat:
                case PixelFormat.BC7_UNorm:
                case PixelFormat.BC7_UNorm_SRgb:
                    return 16;
                case PixelFormat.R8_UNorm:
                case PixelFormat.R8_SNorm:
                case PixelFormat.R8_UInt:
                case PixelFormat.R8_SInt:
                    return 1;
                case PixelFormat.B5_G5_R5_A1_UNorm:
                case PixelFormat.R16_UNorm:
                case PixelFormat.R16_SNorm:
                case PixelFormat.R8_G8_SInt:
                case PixelFormat.R8_G8_SNorm:
                case PixelFormat.R8_G8_UInt:
                case PixelFormat.R8_G8_UNorm:
                    return 2;
                case PixelFormat.R8_G8_B8_A8_SInt:
                case PixelFormat.R8_G8_B8_A8_SNorm:
                case PixelFormat.R8_G8_B8_A8_UInt:
                case PixelFormat.R8_G8_B8_A8_UNorm:
                case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
                case PixelFormat.B8_G8_R8_A8_UNorm:
                case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
                    return 4;
                case PixelFormat.R16_G16_B16_A16_Float:
                    return 8;
                //case SurfaceFormat.Vector4:
                //    return 16;
                default:
                    throw new ArgumentException();
            }
        }

        private FreeListAllocator _allocator = null;

        public uint TextureCount { get; private set; } = 0;
        private List<TextureHandle> _handles = new List<TextureHandle>();

        private ResourceLayout _poolLayout = null;
        private ResourceSet _poolResourceSet = null;
        private string _resourceName = null;

        private object _allocationLock = new object();

        private List<Texture> _disposalQueue = new List<Texture>();
        private int _framesToDisposal = 0;
        private object _disposalLock = new object();

        public bool DescriptorTableDirty { get; private set; } = false;

        public TexturePool(GraphicsDevice d, string name, uint poolsize)
        {
            _resourceName = name;
            TextureCount = poolsize;
            _allocator = new FreeListAllocator(poolsize);
            for (int i = 0; i < poolsize; i++)
            {
                _handles.Add(null);
            }

            var layoutdesc = new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(_resourceName, ResourceKind.TextureReadOnly,
                    ShaderStages.Fragment, ResourceLayoutElementOptions.VariableCount, TextureCount));
            _poolLayout = d.ResourceFactory.CreateResourceLayout(layoutdesc);
        }

        public TextureHandle AllocateTextureDescriptor()
        {
            TextureHandle handle;
            lock (_allocationLock)
            {
                uint id;
                bool alloc = _allocator.AlignedAlloc(1, 1, out id);
                if (!alloc)
                {
                    return null;
                }
                handle = new TextureHandle(this, id);
                _handles[(int)id] = handle;
                //TextureCount++;
            }
            return handle;
        }

        public void RegenerateDescriptorTables()
        {
            Renderer.AddBackgroundUploadTask((d, cl) =>
            {
                lock (_allocationLock)
                {
                    if (TextureCount == 0)
                    {
                        return;
                    }
                    if (_poolLayout != null)
                    {
                        //_poolLayout.Dispose();
                    }
                    if (_poolResourceSet != null)
                    {
                        _poolResourceSet.Dispose();
                    }

                    //var layoutdesc = new ResourceLayoutDescription(
                    //    new ResourceLayoutElementDescription(_resourceName, ResourceKind.TextureReadOnly, ShaderStages.Fragment, TextureCount));
                    //_poolLayout = d.ResourceFactory.CreateResourceLayout(layoutdesc);

                    BindableResource[] resources = new BindableResource[TextureCount];
                    for (int i = 0; i < _handles.Count; i++)
                    {
                        if (_handles[i] == null)
                        {
                            resources[i] = _handles[0]._texture;
                            continue;
                        }
                        resources[_handles[i].TexHandle] = _handles[i]._texture;
                        if (_handles[i]._texture == null)
                        {
                            resources[_handles[i].TexHandle] = _handles[0]._texture;
                        }
                    }
                    _poolResourceSet = d.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_poolLayout, resources));
                    DescriptorTableDirty = false;
                }
            });

        }

        public ResourceLayout GetLayout()
        {
            return _poolLayout;
        }

        public void BindTexturePool(CommandList cl, uint slot)
        {
            if (_poolResourceSet != null)
            {
                cl.SetGraphicsResourceSet(slot, _poolResourceSet);
            }
        }

        public void CleanTexturePool()
        {
            lock (_disposalLock)
            {
                _framesToDisposal++;
                if (_framesToDisposal == 5)
                {
                    foreach (var t in _disposalQueue)
                    {
                        t.Dispose();
                    }
                    _disposalQueue.Clear();
                }
                else
                {
                    DescriptorTableDirty = true;
                }
            }
            lock (_allocationLock)
            {
                foreach (var t in _handles)
                {
                    if (t != null)
                    {
                        t.Clean();
                    }
                }
            }
        }

        public class TextureHandle : IDisposable
        {
            private TexturePool _pool;
            internal Texture _staging = null;
            internal Texture _texture = null;

            public uint TexHandle { get; private set; }

            public bool Resident { get; private set; } = false;

            public TextureHandle(TexturePool pool, uint handle)
            {
                _pool = pool;
                TexHandle = handle;
            }

            public static bool IsTPFCube(TPF.Texture tex, TPF.TPFPlatform platform)
            {
                if (platform == TPF.TPFPlatform.PC)
                {
                    DDS dds = new DDS(tex.Bytes);
                    return (dds.dwCaps2 & DDS.DDSCAPS2.CUBEMAP) > 0;
                }
                return (tex.Type == TPF.TexType.Cubemap);
            }

            public unsafe void FillWithTPF(GraphicsDevice d, CommandList cl, TPF.TPFPlatform platform, TPF.Texture tex, string name)
            {
                DDS dds;
                var bytes = tex.Bytes;
                if (platform != TPF.TPFPlatform.PC)
                {
                    bytes = tex.Headerize();
                    dds = new DDS(bytes);
                }
                else
                {
                    dds = new DDS(tex.Bytes);
                }

                uint width = (uint)dds.dwWidth;
                uint height = (uint)dds.dwHeight;
                PixelFormat format;
                if (dds.header10 != null)
                {
                    format = GetPixelFormatFromDXGI(dds.header10.dxgiFormat);
                }
                else
                {
                    if (dds.ddspf.dwFlags == (DDS.DDPF.RGB | DDS.DDPF.ALPHAPIXELS) &&
                        dds.ddspf.dwRGBBitCount == 32)
                    {
                        format = PixelFormat.R8_G8_B8_A8_UNorm_SRgb;
                    }
                    else if (dds.ddspf.dwFlags == (DDS.DDPF.RGB) && dds.ddspf.dwRGBBitCount == 24)
                    {
                        format = PixelFormat.R8_G8_B8_A8_UNorm_SRgb;
                        // 24-bit formats are annoying for now
                        return;
                    }
                    else
                    {
                        format = GetPixelFormatFromFourCC(dds.ddspf.dwFourCC);
                    }
                }

                if (!Utils.IsPowerTwo(width) || !Utils.IsPowerTwo(height))
                {
                    return;
                }
                width = IsCompressedFormat(format) ? (uint)((width + 3) & ~0x3) : width;
                height = IsCompressedFormat(format) ? (uint)((height + 3) & ~0x3) : height;

                bool isCubemap = false;
                if ((dds.dwCaps2 & DDS.DDSCAPS2.CUBEMAP) > 0)
                {
                    isCubemap = true;
                }

                var usage = (isCubemap) ? TextureUsage.Cubemap : 0;

                uint arrayCount = isCubemap ? 6u : 1;

                TextureDescription desc = new TextureDescription();
                desc.Width = width;
                desc.Height = height;
                desc.MipLevels = (uint)dds.dwMipMapCount;
                desc.SampleCount = TextureSampleCount.Count1;
                desc.ArrayLayers = arrayCount;
                desc.Depth = 1;
                desc.Type = TextureType.Texture2D;
                desc.Usage = TextureUsage.Staging;
                desc.Format = format;

                _staging = d.ResourceFactory.CreateTexture(desc);

                int paddedWidth = 0;
                int paddedHeight = 0;
                int paddedSize = 0;
                int copyOffset = dds.DataOffset;

                for (int slice = 0; slice < arrayCount; slice++)
                {
                    for (uint level = 0; level < dds.dwMipMapCount; level++)
                    {
                        MappedResource map = d.Map(_staging, MapMode.Write, (uint)slice * (uint)dds.dwMipMapCount + level);
                        var mipInfo = GetMipInfo(format, (int)dds.dwWidth, (int)dds.dwHeight, (int)level, false);
                        //paddedSize = mipInfo.ByteCount;
                        paddedSize = mipInfo;
                        fixed (void* data = &bytes[copyOffset])
                        {
                            Unsafe.CopyBlock(map.Data.ToPointer(), data, (uint)paddedSize);
                        }
                        copyOffset += paddedSize;
                    }
                }

                desc.Usage = TextureUsage.Sampled | usage;
                desc.ArrayLayers = 1;
                _texture = d.ResourceFactory.CreateTexture(desc);
                _texture.Name = name;
                cl.CopyTexture(_staging, _texture);
                Resident = true;
                _pool.DescriptorTableDirty = true;
            }

            public static void DeswizzleDDSBytesPS4(int width, int height, int offset, int offsetFactor, int Format, int BlockSize, int DDSWidth, Span<byte> InputBytes, Span<byte> OutputBytes, ref int WriteOffset)
            {
                if (width * height > 16)
                {
                    DeswizzleDDSBytesPS4(width / 2, height / 2, offset, offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4(width / 2, height / 2, offset + (width / 8) * BlockSize, offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4(width / 2, height / 2, offset + ((DDSWidth / 8) * (height / 4) * BlockSize), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4(width / 2, height / 2, offset + (DDSWidth / 8) * (height / 4) * BlockSize + (width / 8) * BlockSize, offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                }
                else
                {
                    for (int i = 0; i < BlockSize; i++)
                    {
                        if (offset + i < OutputBytes.Length)
                        {
                            OutputBytes[offset + i] = InputBytes[WriteOffset];
                        }
                        WriteOffset += 1;
                    }
                }
            }

            public static void DeswizzleDDSBytesPS4(int width, int height, int Format, int BlockSize, int DDSWidth, Span<byte> InputBytes, Span<byte> OutputBytes)
            {
                ////[Hork Comment]
                //if (Format != 22 && width <= 4 && height <= 4)
                //{
                //    for (int i = 0; i < BlockSize; i++)
                //    {
                //        OutputBytes[i] = InputBytes[i];
                //    }
                //    return;
                //}

                ////[Hork Comment]
                //if (Format != 22 && width <= 2 && height == 1)
                //{
                //    for (int i = 0; i < width * 8; i++)
                //    {
                //        OutputBytes[i] = InputBytes[i];
                //    }
                //    return;
                //}

                int WriteOffset = 0;
                int swizzleBlockSize = 0;
                int blocksH = 0;
                int blocksV = 0;

                if (Format == 22)
                {
                    blocksH = (width + 7) / 8;
                    blocksV = (height + 7) / 8;
                    swizzleBlockSize = 8;
                }
                else if (Format == 105)
                {
                    blocksH = (width + 15) / 16;
                    blocksV = (height + 15) / 16;
                    swizzleBlockSize = 16;
                }
                else
                {
                    blocksH = (width + 31) / 32;
                    blocksV = (height + 31) / 32;
                    swizzleBlockSize = 32;
                }

                if (Format == 22)
                {
                    DeswizzleDDSBytesPS4RGBA(width, height, 0, 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    return;
                }
                ////[Hork Comment]
                //else if (Format == 105)
                //{
                //    DeswizzleDDSBytesPS4RGBA8(width, height, 0, 2);
                //    WriteOffset = 0;
                //    return;
                //}

                int h = 0;
                int v = 0;
                int offset = 0;

                for (int i = 0; i < blocksV; i++)
                {
                    h = 0;
                    for (int j = 0; j < blocksH; j++)
                    {
                        offset = h + v;

                        if (Format == 105)
                            DeswizzleDDSBytesPS4RGBA8(16, 16, offset, 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                        else
                            DeswizzleDDSBytesPS4(32, 32, offset, 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);

                        h += (swizzleBlockSize / 4) * BlockSize;
                        ////[Hork Comment]
                        // swizzleBlockSize = 32
                    }

                    if (Format == 105)
                    {
                        v += swizzleBlockSize * swizzleBlockSize;
                    }
                    else
                    {
                        if (BlockSize == 8)
                            v += swizzleBlockSize * width / 2;
                        else
                            v += swizzleBlockSize * width;
                    }
                }
            }

            public static void DeswizzleDDSBytesPS4RGBA(int width, int height, int offset, int offsetFactor, int Format, int BlockSize, int DDSWidth, Span<byte> InputBytes, Span<byte> OutputBytes, ref int WriteOffset)
            {
                if (width * height > 4)
                {
                    DeswizzleDDSBytesPS4RGBA(width / 2, height / 2, offset, offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4RGBA(width / 2, height / 2, offset + (width / 2), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4RGBA(width / 2, height / 2, offset + ((width / 2) * (height / 2) * offsetFactor), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4RGBA(width / 2, height / 2, offset + ((width / 2) * (height / 2) * offsetFactor) + (width / 2), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                }
                else
                {
                    for (int i = 0; i < 16; i++)
                    {
                        OutputBytes[offset * 8 + i] = InputBytes[WriteOffset + i];
                    }

                    WriteOffset += 16;

                    for (int i = 0; i < 16; i++)
                    {
                        OutputBytes[offset * 8 + DDSWidth * 8 + i] = InputBytes[WriteOffset + i];
                    }

                    WriteOffset += 16;
                }
            }

            public static void DeswizzleDDSBytesPS4RGBA8(int width, int height, int offset, int offsetFactor, int Format, int BlockSize, int DDSWidth, Span<byte> InputBytes, Span<byte> OutputBytes, ref int WriteOffset)
            {
                if (width * height > 4)
                {
                    DeswizzleDDSBytesPS4RGBA8(width / 2, height / 2, offset, offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4RGBA8(width / 2, height / 2, offset + (width / 2), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4RGBA8(width / 2, height / 2, offset + ((width / 2) * (height / 2) * offsetFactor), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                    DeswizzleDDSBytesPS4RGBA8(width / 2, height / 2, offset + ((width / 2) * (height / 2) * offsetFactor) + (width / 2), offsetFactor * 2, Format, BlockSize, DDSWidth, InputBytes, OutputBytes, ref WriteOffset);
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        OutputBytes[offset * 4 + i] = InputBytes[WriteOffset + i];
                    }

                    WriteOffset += 8;

                    for (int i = 0; i < 8; i++)
                    {
                        OutputBytes[offset * 4 + DDSWidth * 4 + i] = InputBytes[WriteOffset + i];
                    }

                    WriteOffset += 8;
                }
            }

            public unsafe void FillWithPS4TPF(GraphicsDevice d, CommandList cl, TPF.TPFPlatform platform, TPF.Texture tex, string name)
            {
                if (platform != TPF.TPFPlatform.PS4)
                {
                    return;
                }
                uint width = (uint)tex.Header.Width;
                uint height = (uint)tex.Header.Height;
                uint mipCount = (uint)tex.Mipmaps;
                PixelFormat format;
                format = GetPixelFormatFromDXGI((DDS.DXGI_FORMAT)tex.Header.DXGIFormat);

                width = (uint)(Math.Ceiling(width / 4f) * 4f);
                height = (uint)(Math.Ceiling(height / 4f) * 4f);
                if (mipCount == 0)
                {
                    mipCount = (uint)(1 + Math.Floor(Math.Log(Math.Max(width, height), 2)));
                }

                bool isCubemap = (tex.Type == TPF.TexType.Cubemap);

                var usage = (isCubemap) ? TextureUsage.Cubemap : 0;

                uint arrayCount = isCubemap ? 6u : 1;

                TextureDescription desc = new TextureDescription();
                desc.Width = width;
                desc.Height = height;
                desc.MipLevels = mipCount;
                desc.SampleCount = TextureSampleCount.Count1;
                desc.ArrayLayers = arrayCount;
                desc.Depth = 1;
                desc.Type = TextureType.Texture2D;
                desc.Usage = TextureUsage.Staging;
                desc.Format = format;

                _staging = d.ResourceFactory.CreateTexture(desc);

                uint blockSize = (uint)GetBlockSize(tex.Format);
                uint paddedWidth = 0;
                uint paddedHeight = 0;
                uint paddedSize = 0;
                uint copyOffset = 0;

                for (int slice = 0; slice < arrayCount; slice++)
                {
                    uint currentWidth = width;
                    uint currentHeight = height;
                    for (uint level = 0; level < mipCount; level++)
                    {
                        if (tex.Format == 105)
                        {
                            paddedWidth = currentWidth;
                            paddedHeight = currentHeight;
                            paddedSize = paddedWidth * paddedHeight * blockSize;
                        }
                        else
                        {
                            paddedWidth = (uint)(Math.Ceiling(currentWidth / 32f) * 32f);
                            paddedHeight = (uint)(Math.Ceiling(currentHeight / 32f) * 32f);
                            paddedSize = (uint)(Math.Ceiling(paddedWidth / 4f) * Math.Ceiling(paddedHeight / 4f) * blockSize);
                        }

                        var mipInfo = GetMipInfo(format, tex.Header.Width, tex.Header.Height, (int)level, false);

                        MappedResource map = d.Map(_staging, MapMode.Write, (uint)slice * (uint)mipCount + level);
                        //fixed (void* data = &tex.Bytes[copyOffset])
                        //{
                        //Unsafe.CopyBlock(map.Data.ToPointer(), data, (uint)paddedSize);
                        DeswizzleDDSBytesPS4((int)currentWidth, (int)currentHeight, tex.Format, (int)blockSize,
                            (int)paddedWidth, new Span<byte>(tex.Bytes, (int)copyOffset, (int)paddedSize),
                            new Span<byte>(map.Data.ToPointer(), (int)mipInfo));
                        //}
                        copyOffset += paddedSize;

                        if (currentWidth > 1)
                        {
                            currentWidth /= 2;
                        }
                        if (currentHeight > 1)
                        {
                            currentHeight /= 2;
                        }
                    }
                }

                desc.Usage = TextureUsage.Sampled | usage;
                desc.ArrayLayers = 1;
                _texture = d.ResourceFactory.CreateTexture(desc);
                _texture.Name = name;
                cl.CopyTexture(_staging, _texture);
                Resident = true;
                _pool.DescriptorTableDirty = true;
            }

            public unsafe void FillWithColor(GraphicsDevice d, System.Drawing.Color c, string name)
            {
                TextureDescription desc = new TextureDescription();
                desc.Width = 1;
                desc.Height = 1;
                desc.MipLevels = 1;
                desc.SampleCount = TextureSampleCount.Count1;
                desc.ArrayLayers = 1;
                desc.Depth = 1;
                desc.Type = TextureType.Texture2D;
                desc.Usage = TextureUsage.Staging;
                desc.Format = PixelFormat.R8_G8_B8_A8_UNorm;
                _staging = d.ResourceFactory.CreateTexture(desc);

                byte[] col = new byte[4];
                col[0] = c.R;
                col[1] = c.G;
                col[2] = c.B;
                col[3] = c.A;
                MappedResource map = d.Map(_staging, MapMode.Write, 0);
                fixed (void* data = col)
                {
                    Unsafe.CopyBlock(map.Data.ToPointer(), data, 4);
                }

                _pool.DescriptorTableDirty = true;

                Renderer.AddBackgroundUploadTask((gd, cl) =>
                {
                    desc.Usage = TextureUsage.Sampled;
                    _texture = d.ResourceFactory.CreateTexture(desc);
                    _texture.Name = name;
                    cl.CopyTexture(_staging, _texture);
                    Resident = true;
                    _pool.DescriptorTableDirty = true;
                });
            }

            public unsafe void FillWithColorCube(GraphicsDevice d, System.Numerics.Vector4 c)
            {
                TextureDescription desc = new TextureDescription();
                desc.Width = 1;
                desc.Height = 1;
                desc.MipLevels = 1;
                desc.SampleCount = TextureSampleCount.Count1;
                desc.ArrayLayers = 6;
                desc.Depth = 1;
                desc.Type = TextureType.Texture2D;
                desc.Usage = TextureUsage.Staging;
                desc.Format = PixelFormat.R32_G32_B32_A32_Float;
                _staging = d.ResourceFactory.CreateTexture(desc);

                float[] col = new float[4];
                col[0] = c.X;
                col[1] = c.Y;
                col[2] = c.Z;
                col[3] = c.W;
                for (uint i = 0; i < 6; i++)
                {
                    MappedResource map = d.Map(_staging, MapMode.Write, i);
                    fixed (void* data = col)
                    {
                        Unsafe.CopyBlock(map.Data.ToPointer(), data, 16);
                    }
                }

                _pool.DescriptorTableDirty = true;

                Renderer.AddBackgroundUploadTask((gd, cl) =>
                {
                    desc.ArrayLayers = 1;
                    desc.Usage = TextureUsage.Sampled | TextureUsage.Cubemap;
                    _texture = d.ResourceFactory.CreateTexture(desc);
                    cl.CopyTexture(_staging, _texture);
                    Resident = true;
                    _pool.DescriptorTableDirty = true;
                });
            }

            public unsafe void FillWithGPUTexture(Texture texture)
            {
                if (_texture != null)
                {
                    _texture.Dispose();
                }
                _texture = texture;
                Resident = true;
                _pool.DescriptorTableDirty = true;
            }

            public void CreateRenderTarget(GraphicsDevice d, uint width, uint height, uint mips, uint layes, PixelFormat format, TextureUsage usage)
            {
                _texture = d.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layes, format, usage | TextureUsage.RenderTarget));
                Resident = true;
                _pool.DescriptorTableDirty = true;
            }

            public void Clean()
            {
                if (Resident && _staging != null)
                {
                    _staging.Dispose();
                    _staging = null;
                }
            }

            #region IDisposable Support
            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                    }

                    if (_texture != null)
                    {
                        //_texture.Dispose();
                        lock (_pool._disposalLock)
                        {
                            _pool._disposalQueue.Add(_texture);
                            _pool._framesToDisposal = 0;
                            _pool.DescriptorTableDirty = true;
                        }
                        _texture = null;
                    }
                    if (_staging != null)
                    {
                        _staging.Dispose();
                        _staging = null;
                    }
                    _pool._allocator.Free(TexHandle);
                    lock (_pool._allocationLock)
                        _pool._handles[(int)TexHandle] = null;
                    _pool.DescriptorTableDirty = true;

                    disposedValue = true;
                }
            }

            ~TextureHandle()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}
