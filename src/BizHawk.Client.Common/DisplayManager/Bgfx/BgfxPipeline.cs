#pragma warning disable SA1413
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bgfx;

namespace BizHawk.Client.Common.Bgfx
{
    public enum SliderType { Float, Int, IntEnum, Vec2, Color }
    public enum ScreenType { None, Raster, Vector, Lcd, Any }
    public enum ParameterType { Frame, Window, Time }

    public class ChainSlider
    {
        public string Name { get; set; }
        public SliderType Type { get; set; }
        public string Text { get; set; }
        public float[] Min { get; set; }
        public float[] Default { get; set; }
        public float[] Max { get; set; }
        public float Step { get; set; }
        public string Format { get; set; }
        public ScreenType Screen { get; set; }
        public List<string> Strings { get; set; } = new List<string>();
        public float[] CurrentValue { get; set; }

        public int ComponentCount => Type switch
        {
            SliderType.Vec2 => 2,
            SliderType.Color => 3,
            _ => 1
        };
    }

    public class ChainParameter
    {
        public string Name { get; set; }
        public ParameterType Type { get; set; }
        public uint Period { get; set; }
        public float Limit { get; set; }
        public uint WindowIndex { get; set; }

        public float GetValue(uint frameCount, float time)
        {
            return Type switch
            {
                ParameterType.Frame => frameCount % Period,
                ParameterType.Window => WindowIndex,
                ParameterType.Time => time % Limit,
                _ => 0.0f
            };
        }
    }

    public class ChainInput
    {
        public string Sampler { get; set; }
        public string Texture { get; set; }
        public string Target { get; set; }
        public string Option { get; set; }
        public bool Bilinear { get; set; } = true;
        public bool Clamp { get; set; } = false;
        public string Selection { get; set; }
    }

    public class ChainUniform
    {
        public string Uniform { get; set; }
        public string Slider { get; set; }
        public string Parameter { get; set; }
        public float[] Value { get; set; }
    }

    public class ChainPass
    {
        public string Effect { get; set; }
        public string Name { get; set; }
        public string Output { get; set; }
        public List<ChainInput> Inputs { get; set; } = new List<ChainInput>();
        public List<ChainUniform> Uniforms { get; set; } = new List<ChainUniform>();
        public List<Dictionary<string, object>> DisableWhen { get; set; } = new List<Dictionary<string, object>>();
    }

    public class ChainTarget
    {
        public string Name { get; set; }
        public string Mode { get; set; }
        public bool Bilinear { get; set; } = true;
        public bool UserPrescale { get; set; } = false;
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public int Scale { get; set; } = 1;
    }

    public class Chain
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public bool Transform { get; set; } = false;
        public List<ChainSlider> Sliders { get; set; } = new List<ChainSlider>();
        public List<ChainParameter> Parameters { get; set; } = new List<ChainParameter>();
        public List<ChainTarget> Targets { get; set; } = new List<ChainTarget>();
        public List<ChainPass> Passes { get; set; } = new List<ChainPass>();
    }

    public class ChainReader
    {
        public static Chain ReadFromJson(string json)
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;

            var chain = new Chain
            {
                Name = GetString(root, "name", ""),
                Author = GetString(root, "author", ""),
                Transform = GetBool(root, "transform", false)
            };

            if (root.TryGetProperty("sliders", out var slidersEl))
                foreach (var sliderEl in slidersEl.EnumerateArray())
                    chain.Sliders.Add(ReadSlider(sliderEl));

            if (root.TryGetProperty("parameters", out var paramsEl))
                foreach (var paramEl in paramsEl.EnumerateArray())
                    chain.Parameters.Add(ReadParameter(paramEl));

            if (root.TryGetProperty("targets", out var targetsEl))
                foreach (var targetEl in targetsEl.EnumerateArray())
                    chain.Targets.Add(ReadTarget(targetEl));

            if (root.TryGetProperty("passes", out var passesEl))
                foreach (var passEl in passesEl.EnumerateArray())
                    chain.Passes.Add(ReadPass(passEl));

            foreach (var slider in chain.Sliders)
                slider.CurrentValue = (float[])slider.Default.Clone();

            return chain;
        }

        private static ChainSlider ReadSlider(JsonElement el)
        {
            var slider = new ChainSlider
            {
                Name = GetString(el, "name", ""),
                Text = GetString(el, "text", ""),
                Type = GetSliderType(GetString(el, "type", "float")),
                Screen = GetScreenType(GetString(el, "screen", "any")),
                Step = GetFloat(el, "step", 0.1f),
                Format = GetString(el, "format", "%1.2f")
            };

            int count = slider.ComponentCount;

            if (el.TryGetProperty("strings", out var stringsEl))
                foreach (var s in stringsEl.EnumerateArray())
                    slider.Strings.Add(s.GetString() ?? "");

            slider.Min = GetFloatArray(el, "min", count, 0.0f);
            slider.Default = GetFloatArray(el, "default", count, 0.0f);
            slider.Max = GetFloatArray(el, "max", count, 1.0f);

            return slider;
        }

        private static ChainParameter ReadParameter(JsonElement el)
        {
            return new ChainParameter
            {
                Name = GetString(el, "name", ""),
                Type = GetParameterType(GetString(el, "type", "frame")),
                Period = (uint)GetFloat(el, "period", 1.0f),
                Limit = GetFloat(el, "limit", 1.0f)
            };
        }

        private static ChainTarget ReadTarget(JsonElement el)
        {
            return new ChainTarget
            {
                Name = GetString(el, "name", ""),
                Mode = GetString(el, "mode", "guest"),
                Bilinear = GetBool(el, "bilinear", true),
                UserPrescale = GetBool(el, "user_prescale", false),
                Width = GetUInt16(el, "width", 0),
                Height = GetUInt16(el, "height", 0),
                Scale = GetInt(el, "scale", 1)
            };
        }

        private static ChainPass ReadPass(JsonElement el)
        {
            var pass = new ChainPass
            {
                Effect = GetString(el, "effect", ""),
                Name = GetString(el, "name", ""),
                Output = GetString(el, "output", "output")
            };

            if (el.TryGetProperty("input", out var inputsEl))
                foreach (var inputEl in inputsEl.EnumerateArray())
                    pass.Inputs.Add(ReadInput(inputEl));

            if (el.TryGetProperty("uniforms", out var uniformsEl))
                foreach (var uniformEl in uniformsEl.EnumerateArray())
                    pass.Uniforms.Add(ReadUniform(uniformEl));

            return pass;
        }

        private static ChainInput ReadInput(JsonElement el)
        {
            return new ChainInput
            {
                Sampler = GetString(el, "sampler", ""),
                Texture = GetString(el, "texture", ""),
                Target = GetString(el, "target", ""),
                Option = GetString(el, "option", ""),
                Bilinear = GetBool(el, "bilinear", true),
                Clamp = GetBool(el, "clamp", false),
                Selection = GetString(el, "selection", "")
            };
        }

        private static ChainUniform ReadUniform(JsonElement el)
        {
            var uniform = new ChainUniform
            {
                Uniform = GetString(el, "uniform", ""),
                Slider = GetString(el, "slider", ""),
                Parameter = GetString(el, "parameter", "")
            };

            if (el.TryGetProperty("value", out var valueEl))
            {
                if (valueEl.ValueKind == JsonValueKind.Number)
                    uniform.Value = new float[] { (float)valueEl.GetDouble() };
                else if (valueEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<float>();
                    foreach (var v in valueEl.EnumerateArray())
                        list.Add((float)v.GetDouble());
                    uniform.Value = list.ToArray();
                }
            }

            return uniform;
        }

        private static string GetString(JsonElement el, string name, string def)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? def;
            return def;
        }

        private static bool GetBool(JsonElement el, string name, bool def)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
            if (el.TryGetProperty(name, out prop) && prop.ValueKind == JsonValueKind.False)
                return false;
            return def;
        }

        private static float GetFloat(JsonElement el, string name, float def)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return (float)prop.GetDouble();
            return def;
        }

        private static int GetInt(JsonElement el, string name, int def)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt32();
            return def;
        }

        private static ushort GetUInt16(JsonElement el, string name, ushort def)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return (ushort)prop.GetInt32();
            return def;
        }

        private static float[] GetFloatArray(JsonElement el, string name, int count, float def)
        {
            var result = new float[count];
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    for (int i = 0; i < count; i++) result[i] = (float)prop.GetDouble();
                }
                else if (prop.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    foreach (var v in prop.EnumerateArray())
                        if (i < count) result[i++] = (float)v.GetDouble();
                    while (i < count) result[i++] = def;
                }
            }
            return result;
        }

        private static SliderType GetSliderType(string type) => type?.ToLower(CultureInfo.InvariantCulture) switch
        {
            "intenum" => SliderType.IntEnum,
            "int" => SliderType.Int,
            "vec2" => SliderType.Vec2,
            "color" => SliderType.Color,
            _ => SliderType.Float
        };

        private static ScreenType GetScreenType(string screen) => screen?.ToLower(CultureInfo.InvariantCulture) switch
        {
            "raster" => ScreenType.Raster,
            "vector" => ScreenType.Vector,
            "lcd" => ScreenType.Lcd,
            "any" or "all" => ScreenType.Any,
            _ => ScreenType.None
        };

        private static ParameterType GetParameterType(string type) => type?.ToLower(CultureInfo.InvariantCulture) switch
        {
            "frame" => ParameterType.Frame,
            "window" => ParameterType.Window,
            "time" => ParameterType.Time,
            _ => ParameterType.Frame
        };
    }

    public unsafe class BgfxPipeline : IDisposable
    {
        private Dictionary<string, bgfx.UniformHandle> uniformCache = new();
        private Dictionary<string, bgfx.TextureHandle> textureCache = new();
        private Dictionary<string, (int Width, int Height)> textureSizes = new();
        private Dictionary<string, bgfx.FrameBufferHandle> targetCache = new();
        private Dictionary<string, (ushort Width, ushort Height)> targetSizes = new();
        private Dictionary<string, bgfx.ProgramHandle> programCache = new();
        private Dictionary<string, ulong> effectBlendStates = new();

        private bgfx.VertexBufferHandle vertexBufferHandle;
        private bgfx.VertexBufferHandle vertexBufferFlipped;
        private bgfx.VertexLayoutHandle vertexLayoutHandle;
        private bgfx.IndexBufferHandle indexBufferHandle;



        private bgfx.ProgramHandle blitProgram;
        private bgfx.TextureHandle sourceTexture;
        private int textureWidth;
        private int textureHeight;

        private Chain currentChain;
        private uint frameCount = 0;
        private DateTime startTime;
        private int currentMaskWidth = 1;
        private int currentMaskHeight = 1;
        private int currentInputWidth = 1;
        private int currentInputHeight = 1;

        private string[] shaderSearchDirs;
        private string[] effectSearchDirs;
        private string[] textureSearchDirs;

        public bool Initialized { get; private set; }
        public Chain CurrentChain => currentChain;
        public int TextureWidth => textureWidth;
        public int TextureHeight => textureHeight;

        public event Action<string> ChainLoaded;

        public BgfxPipeline(string exeDirectory)
        {
            shaderSearchDirs = new string[] {
                Path.Combine(exeDirectory, "Bgfx", "shaders", "dx11", "chains"),
            };
            effectSearchDirs = new string[] {
                Path.Combine(exeDirectory, "Bgfx", "effects"),
            };
            textureSearchDirs = new string[] {
                Path.Combine(exeDirectory, "artwork"),
            };
            startTime = DateTime.Now;
        }

        public void Initialize()
        {
            CreateVertexBuffer();
            Initialized = true;
        }

        public void SetBlitProgram(bgfx.ProgramHandle program)
        {
            blitProgram = program;
        }

        public void SetSourceTexture(bgfx.TextureHandle texture, int width, int height)
        {
            sourceTexture = texture;
            textureWidth = width;
            textureHeight = height;
        }

        private void CreateVertexBuffer()
        {
            var layout = default(bgfx.VertexLayout);
            bgfx.vertex_layout_begin(&layout, bgfx.RendererType.Direct3D11);
            bgfx.vertex_layout_add(&layout, bgfx.Attrib.Position, 3, bgfx.AttribType.Float, false, false);
            bgfx.vertex_layout_add(&layout, bgfx.Attrib.Color0, 4, bgfx.AttribType.Float, false, false);
            bgfx.vertex_layout_add(&layout, bgfx.Attrib.TexCoord0, 2, bgfx.AttribType.Float, false, false);
            bgfx.vertex_layout_end(&layout);
            vertexLayoutHandle = bgfx.create_vertex_layout(&layout);

            float[] vertices = {
                -1.0f, -1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f,
                1.0f, -1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                1.0f,  1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                -1.0f,  1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
            };

            fixed (float* pVertices = vertices)
            {
                bgfx.Memory* mem = bgfx.copy(pVertices, (uint)(vertices.Length * sizeof(float)));
                vertexBufferHandle = bgfx.create_vertex_buffer(mem, &layout, 0);
            }

            float[] verticesFlipped = {
                -1.0f, -1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                1.0f, -1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                1.0f,  1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                -1.0f,  1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f,
            };

            fixed (float* pVerticesFlipped = verticesFlipped)
            {
                bgfx.Memory* mem = bgfx.copy(pVerticesFlipped, (uint)(verticesFlipped.Length * sizeof(float)));
                vertexBufferFlipped = bgfx.create_vertex_buffer(mem, &layout, 0);
            }

            ushort[] indices = { 0, 1, 2, 0, 2, 3 };
            fixed (ushort* pIndices = indices)
            {
                bgfx.Memory* mem = bgfx.copy(pIndices, (uint)(indices.Length * sizeof(ushort)));
                indexBufferHandle = bgfx.create_index_buffer(mem, 0);
            }
        }

        public bool LoadChain(string presetPath)
        {
            if (!Initialized || !File.Exists(presetPath)) return false;

            try
            {
                string json = File.ReadAllText(presetPath);
                var chain = ChainReader.ReadFromJson(json);

                if (chain.Passes.Count == 0) return false;

                DestroyChainResources();

                currentChain = chain;

                CreateChainTargets();
                LoadChainTextures();

                if (LoadChainShaders())
                {
                    ChainLoaded?.Invoke(chain.Name);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public void ClearChain()
        {
            DestroyChainResources();
            currentChain = null;
        }

        private void DestroyChainResources()
        {
            foreach (var kvp in programCache)
                if (kvp.Value.idx != ushort.MaxValue && kvp.Value.idx != blitProgram.idx)
                    bgfx.destroy_program(kvp.Value);
            programCache.Clear();

            foreach (var kvp in targetCache)
                if (kvp.Value.idx != ushort.MaxValue)
                    bgfx.destroy_frame_buffer(kvp.Value);
            targetCache.Clear();
            targetSizes.Clear();

            foreach (var kvp in textureCache)
                if (kvp.Value.idx != ushort.MaxValue && kvp.Value.idx != sourceTexture.idx)
                    bgfx.destroy_texture(kvp.Value);
            textureCache.Clear();
            textureSizes.Clear();

            foreach (var kvp in uniformCache)
                if (kvp.Value.idx != ushort.MaxValue)
                    bgfx.destroy_uniform(kvp.Value);
            uniformCache.Clear();
        }

        private void CreateChainTargets()
        {
            if (currentChain == null) return;

            foreach (var target in currentChain.Targets)
            {
                ushort width, height;

                if (target.Mode == "native")
                {
                    width = 800;
                    height = 600;
                }
                else
                {
                    ushort defaultWidth = textureWidth > 0 ? (ushort)textureWidth : (ushort)256;
                    ushort defaultHeight = textureHeight > 0 ? (ushort)textureHeight : (ushort)256;
                    width = target.Width > 0 ? target.Width : defaultWidth;
                    height = target.Height > 0 ? target.Height : defaultHeight;
                }

                if (target.Scale > 1)
                {
                    width = (ushort)(width * target.Scale);
                    height = (ushort)(height * target.Scale);
                }

                var fbh = bgfx.create_frame_buffer(width, height, bgfx.TextureFormat.RGBA8, (ulong)(bgfx.TextureFlags.Rt | (bgfx.TextureFlags)bgfx.SamplerFlags.MinPoint | (bgfx.TextureFlags)bgfx.SamplerFlags.MagPoint));
                if (fbh.idx != ushort.MaxValue)
                {
                    targetCache[target.Name] = fbh;
                    targetSizes[target.Name] = (Width: width, Height: height);
                }
            }
        }

        private void LoadChainTextures()
        {
            if (currentChain == null) return;

            foreach (var pass in currentChain.Passes)
            {
                foreach (var input in pass.Inputs)
                {
                    if (!string.IsNullOrEmpty(input.Texture) && input.Texture != "screen" && !textureCache.ContainsKey(input.Texture))
                    {
                        var texHandle = LoadExternalTexture(input.Texture, input.Bilinear, input.Clamp);
                        if (texHandle.idx != ushort.MaxValue)
                            textureCache[input.Texture] = texHandle;
                    }
                }
            }
        }

        private bgfx.TextureHandle LoadExternalTexture(string texturePath, bool bilinear, bool clamp)
        {
            foreach (var searchPath in textureSearchDirs)
            {
                string fullPath = Path.Combine(searchPath, texturePath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        return LoadTextureFromFile(fullPath, bilinear, clamp, texturePath);
                    }
                    catch { }
                }
            }
            return new bgfx.TextureHandle { idx = ushort.MaxValue };
        }

        private bgfx.TextureHandle LoadTextureFromFile(string path, bool bilinear, bool clamp, string textureKey)
        {
            using var bitmap = new System.Drawing.Bitmap(path);
            textureSizes[textureKey] = (Width: bitmap.Width, Height: bitmap.Height);
            var rgbaData = new byte[bitmap.Width * bitmap.Height * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    int idx = ((bitmap.Height - 1 - y) * bitmap.Width + x) * 4;
                    rgbaData[idx + 0] = pixel.R;
                    rgbaData[idx + 1] = pixel.G;
                    rgbaData[idx + 2] = pixel.B;
                    rgbaData[idx + 3] = pixel.A;
                }
            }

            var handle = GCHandle.Alloc(rgbaData, GCHandleType.Pinned);
            try
            {
                bgfx.Memory* mem = bgfx.copy(handle.AddrOfPinnedObject().ToPointer(), (uint)rgbaData.Length);
                ulong flags = 0;
                if (!bilinear) flags |= (ulong)(bgfx.SamplerFlags.MinPoint | bgfx.SamplerFlags.MagPoint);
                if (clamp) flags |= (ulong)(bgfx.SamplerFlags.UClamp | bgfx.SamplerFlags.VClamp);
                return bgfx.create_texture_2d((ushort)bitmap.Width, (ushort)bitmap.Height, false, 1, bgfx.TextureFormat.RGBA8, flags, mem, 0);
            }
            finally { handle.Free(); }
        }

        private bool LoadChainShaders()
        {
            if (currentChain == null) return false;

            bool anyLoaded = false;

            foreach (var pass in currentChain.Passes)
            {
                if (programCache.ContainsKey(pass.Effect)) continue;

                var program = LoadEffectProgram(pass.Effect);
                if (program.idx != ushort.MaxValue)
                {
                    programCache[pass.Effect] = program;
                    effectBlendStates[pass.Effect] = ParseEffectBlendState(pass.Effect);
                    anyLoaded = true;
                }
            }

            return anyLoaded;
        }

        private bgfx.ProgramHandle LoadEffectProgram(string effect)
        {
            string[] parts = effect.Split('/');
            string effectName = parts[^1];
            string effectDir = parts.Length > 1 ? string.Join("/", parts, 0, parts.Length - 1) : "";

            bgfx.ProgramHandle program = new bgfx.ProgramHandle { idx = ushort.MaxValue };

            foreach (var shaderDir in shaderSearchDirs)
            {
                if (!Directory.Exists(shaderDir)) continue;

                string vsPath = Path.Combine(shaderDir, effectDir, $"vs_{effectName}.bin");
                string fsPath = Path.Combine(shaderDir, effectDir, $"fs_{effectName}.bin");

                if (!File.Exists(vsPath) || !File.Exists(fsPath)) continue;

                try
                {
                    var vsh = LoadShader(vsPath);
                    var fsh = LoadShader(fsPath);
                    if (vsh.idx == ushort.MaxValue || fsh.idx == ushort.MaxValue) continue;

                    program = bgfx.create_program(vsh, fsh, true);
                    break;
                }
                catch { continue; }
            }

            return program;
        }

        private bgfx.ShaderHandle LoadShader(string path)
        {
            byte[] shaderData = File.ReadAllBytes(path);
            fixed (byte* pData = shaderData)
            {
                bgfx.Memory* mem = bgfx.copy(pData, (uint)shaderData.Length);
                return bgfx.create_shader(mem);
            }
        }

        private ulong ParseEffectBlendState(string effect)
        {
            string[] parts = effect.Split('/');
            string effectName = parts[^1];
            string effectDir = parts.Length > 1 ? string.Join("/", parts, 0, parts.Length - 1) : "";

            foreach (var dir in effectSearchDirs)
            {
                string jsonPath = Path.Combine(dir, effectDir, $"{effectName}.json");
                if (!File.Exists(jsonPath)) continue;

                try
                {
                    string json = File.ReadAllText(jsonPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("blend", out var blendEl)) continue;

                    string equation = GetString(blendEl, "equation", "add");
                    string srcColor = GetString(blendEl, "srcColor", "1");
                    string dstColor = GetString(blendEl, "dstColor", "0");
                    string srcAlpha = GetString(blendEl, "srcAlpha", "1");
                    string dstAlpha = GetString(blendEl, "dstAlpha", "0");

                    ulong state = 0;
                    state |= GetBlendFactor(srcColor);
                    state |= GetBlendFactor(dstColor) << 4;
                    state |= GetBlendFactor(srcAlpha) << 8;
                    state |= GetBlendFactor(dstAlpha) << 12;
                    state |= GetBlendEquation(equation);
                    state |= (ulong)bgfx.StateFlags.WriteR | (ulong)bgfx.StateFlags.WriteG | (ulong)bgfx.StateFlags.WriteB | (ulong)bgfx.StateFlags.WriteA;

                    return state;
                }
                catch { continue; }
            }

            return (ulong)bgfx.StateFlags.Default;
        }

        private string GetString(JsonElement el, string name, string def)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? def;
            return def;
        }

        private ulong GetBlendFactor(string factor) => factor.ToLower(CultureInfo.InvariantCulture) switch
        {
            "0" => 0,
            "1" => (ulong)bgfx.StateFlags.BlendOne >> (int)bgfx.StateFlags.BlendShift,
            "srccolor" => (ulong)bgfx.StateFlags.BlendSrcColor >> (int)bgfx.StateFlags.BlendShift,
            "1-srccolor" or "invsrccolor" => (ulong)bgfx.StateFlags.BlendInvSrcColor >> (int)bgfx.StateFlags.BlendShift,
            "srcalpha" => (ulong)bgfx.StateFlags.BlendSrcAlpha >> (int)bgfx.StateFlags.BlendShift,
            "1-srcalpha" or "invsrcalpha" => (ulong)bgfx.StateFlags.BlendInvSrcAlpha >> (int)bgfx.StateFlags.BlendShift,
            "dstalpha" => (ulong)bgfx.StateFlags.BlendDstAlpha >> (int)bgfx.StateFlags.BlendShift,
            "1-dstalpha" or "invdstalpha" => (ulong)bgfx.StateFlags.BlendInvDstAlpha >> (int)bgfx.StateFlags.BlendShift,
            "dstcolor" => (ulong)bgfx.StateFlags.BlendDstColor >> (int)bgfx.StateFlags.BlendShift,
            "1-dstcolor" or "invdstcolor" => (ulong)bgfx.StateFlags.BlendInvDstColor >> (int)bgfx.StateFlags.BlendShift,
            _ => (ulong)bgfx.StateFlags.BlendOne >> (int)bgfx.StateFlags.BlendShift
        };

        private ulong GetBlendEquation(string equation) => equation.ToLower(CultureInfo.InvariantCulture) switch
        {
            "add" => (ulong)bgfx.StateFlags.BlendEquationAdd >> (int)bgfx.StateFlags.BlendEquationShift,
            "sub" => (ulong)bgfx.StateFlags.BlendEquationSub >> (int)bgfx.StateFlags.BlendEquationShift,
            "revsub" => (ulong)bgfx.StateFlags.BlendEquationRevsub >> (int)bgfx.StateFlags.BlendEquationShift,
            "min" => (ulong)bgfx.StateFlags.BlendEquationMin >> (int)bgfx.StateFlags.BlendEquationShift,
            "max" => (ulong)bgfx.StateFlags.BlendEquationMax >> (int)bgfx.StateFlags.BlendEquationShift,
            _ => (ulong)bgfx.StateFlags.BlendEquationAdd >> (int)bgfx.StateFlags.BlendEquationShift
        };

        public void Render(int windowWidth, int windowHeight)
        {
            bgfx.set_view_rect(0, 0, 0, (ushort)windowWidth, (ushort)windowHeight);

            float* identity = stackalloc float[16] {
                1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f
            };
            bgfx.set_view_transform(0, null, identity);
            bgfx.touch(0);

            RenderShaderEffect(windowWidth, windowHeight);

            _ = bgfx.frame(0);
            frameCount++;
        }

        private void RenderShaderEffect(int windowWidth, int windowHeight)
        {
            if (currentChain == null || currentChain.Passes.Count == 0)
            {
                bgfx.set_state((ulong)bgfx.StateFlags.Default, 0);
                bgfx.set_texture(0, GetOrCreateUniform("s_tex0", bgfx.UniformType.Sampler), sourceTexture, (uint)(bgfx.SamplerFlags.UClamp | bgfx.SamplerFlags.VClamp));
                bgfx.set_vertex_buffer(0, vertexBufferHandle, 0, 4);
                bgfx.set_index_buffer(indexBufferHandle, 0, 6);
                bgfx.submit(0, blitProgram, 0, 0);
                return;
            }

            bgfx.TextureHandle lastOutput = sourceTexture;
            float* identity = stackalloc float[16] {
                1.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 1.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 1.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f
            };

            for (int passIndex = 0; passIndex < currentChain.Passes.Count; passIndex++)
            {
                var pass = currentChain.Passes[passIndex];

                if (!programCache.TryGetValue(pass.Effect, out var program))
                    continue;

                bgfx.FrameBufferHandle outputTarget = new bgfx.FrameBufferHandle { idx = ushort.MaxValue };
                ushort targetWidth = 0, targetHeight = 0;

                if (!string.IsNullOrEmpty(pass.Output) && pass.Output != "output")
                {
                    if (targetCache.TryGetValue(pass.Output, out var fbh))
                    {
                        outputTarget = fbh;
                        if (targetSizes.TryGetValue(pass.Output, out var size))
                        {
                            targetWidth = size.Width;
                            targetHeight = size.Height;
                        }
                    }
                }

                ushort viewId = (ushort)(1 + passIndex);

                if (outputTarget.idx != ushort.MaxValue)
                {
                    bgfx.set_view_frame_buffer(viewId, outputTarget);
                    bgfx.set_view_rect(viewId, 0, 0, targetWidth, targetHeight);
                }
                else
                {
                    bgfx.set_view_frame_buffer(viewId, new bgfx.FrameBufferHandle { idx = ushort.MaxValue });
                    bgfx.set_view_rect(viewId, 0, 0, (ushort)windowWidth, (ushort)windowHeight);
                }

                bgfx.set_view_transform(viewId, null, identity);
                bgfx.set_view_clear(viewId, (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth), 0x000000ff, 1.0f, 0);
                bgfx.touch(viewId);

                ulong blendState = effectBlendStates.TryGetValue(pass.Effect, out var bs) ? bs : (ulong)bgfx.StateFlags.Default;
                bgfx.set_state(blendState, 0);

                SetPassInputs(pass, lastOutput);
                SetPassUniforms(pass, targetWidth, targetHeight, windowWidth, windowHeight);

                bool renderToFrameBuffer = outputTarget.idx != ushort.MaxValue;
                var vb = renderToFrameBuffer ? vertexBufferFlipped : vertexBufferHandle;
                bgfx.set_vertex_buffer(0, vb, 0, 4);
                bgfx.set_index_buffer(indexBufferHandle, 0, 6);
                bgfx.submit(viewId, program, 0, 0);

                if (outputTarget.idx != ushort.MaxValue)
                    lastOutput = bgfx.get_texture(outputTarget, 0);
            }
        }

        private void SetPassInputs(ChainPass pass, bgfx.TextureHandle defaultInput)
        {
            int textureSlot = 0;
            bool inputSizeSet = false;

            if (pass.Inputs.Count == 0)
            {
                bgfx.set_texture(0, GetOrCreateUniform("s_tex0", bgfx.UniformType.Sampler), sourceTexture, (uint)(bgfx.SamplerFlags.UClamp | bgfx.SamplerFlags.VClamp));
                currentInputWidth = textureWidth;
                currentInputHeight = textureHeight;
                return;
            }

            foreach (var input in pass.Inputs)
            {
                var (texHandle, texWidth, texHeight) = GetInputTexture(input, defaultInput);

                if (input.Sampler == "mask_texture")
                {
                    currentMaskWidth = texWidth;
                    currentMaskHeight = texHeight;
                }
                else if (!inputSizeSet)
                {
                    currentInputWidth = texWidth;
                    currentInputHeight = texHeight;
                    inputSizeSet = true;
                }

                var samplerUniform = GetOrCreateUniform(input.Sampler, bgfx.UniformType.Sampler);
                bgfx.set_texture((byte)textureSlot, samplerUniform, texHandle, (uint)(bgfx.SamplerFlags.UClamp | bgfx.SamplerFlags.VClamp));
                textureSlot++;
            }
        }

        private (bgfx.TextureHandle handle, int width, int height) GetInputTexture(ChainInput input, bgfx.TextureHandle defaultInput)
        {
            if (!string.IsNullOrEmpty(input.Texture))
            {
                if (input.Texture == "screen")
                    return (sourceTexture, textureWidth, textureHeight);

                if (textureCache.TryGetValue(input.Texture, out var cachedTex))
                {
                    if (textureSizes.TryGetValue(input.Texture, out var texSize))
                        return (cachedTex, texSize.Item1, texSize.Item2);
                    return (cachedTex, textureWidth, textureHeight);
                }
            }

            if (!string.IsNullOrEmpty(input.Target))
            {
                if (input.Target == "screen")
                    return (sourceTexture, textureWidth, textureHeight);

                if (targetCache.TryGetValue(input.Target, out var fbh))
                {
                    var texHandle = bgfx.get_texture(fbh, 0);
                    if (targetSizes.TryGetValue(input.Target, out var targetSize))
                        return (texHandle, targetSize.Item1, targetSize.Item2);
                    return (texHandle, textureWidth, textureHeight);
                }
            }

            return (defaultInput, textureWidth, textureHeight);
        }

        private void SetPassUniforms(ChainPass pass, ushort targetWidth, ushort targetHeight, int windowWidth, int windowHeight)
        {
            SetCommonUniforms(targetWidth, targetHeight, windowWidth, windowHeight);

            float time = (float)(DateTime.Now - startTime).TotalSeconds;
            float* data = stackalloc float[4];

            foreach (var uniformDef in pass.Uniforms)
            {
                if (string.IsNullOrEmpty(uniformDef.Uniform)) continue;

                float[] values = null;

                if (!string.IsNullOrEmpty(uniformDef.Slider))
                {
                    var slider = currentChain.Sliders.Find(s => s.Name == uniformDef.Slider);
                    if (slider != null)
                        values = slider.CurrentValue;
                }
                else if (!string.IsNullOrEmpty(uniformDef.Parameter))
                {
                    var param = currentChain.Parameters.Find(p => p.Name == uniformDef.Parameter);
                    if (param != null)
                        values = new float[] { param.GetValue(frameCount, time) };
                }
                else if (uniformDef.Value != null)
                {
                    values = uniformDef.Value;
                }

                if (values == null || values.Length == 0) continue;

                var uniformHandle = GetOrCreateUniform(uniformDef.Uniform, bgfx.UniformType.Vec4);
                if (uniformHandle.idx == ushort.MaxValue) continue;

                for (int i = 0; i < Math.Min(values.Length, 4); i++)
                    data[i] = values[i];
                for (int i = values.Length; i < 4; i++)
                    data[i] = 0;

                bgfx.set_uniform(uniformHandle, data, 1);
            }
        }

        private void SetCommonUniforms(ushort passTargetWidth, ushort passTargetHeight, int windowWidth, int windowHeight)
        {
            float* data = stackalloc float[4];

            ushort actualTargetWidth = passTargetWidth > 0 ? passTargetWidth : (ushort)windowWidth;
            ushort actualTargetHeight = passTargetHeight > 0 ? passTargetHeight : (ushort)windowHeight;

            data[0] = textureWidth;
            data[1] = textureHeight;
            data[2] = 1.0f / textureWidth;
            data[3] = 1.0f / textureHeight;
            bgfx.set_uniform(GetOrCreateUniform("u_source_size", bgfx.UniformType.Vec4), data, 1);
            bgfx.set_uniform(GetOrCreateUniform("u_source_dims", bgfx.UniformType.Vec4), data, 1);

            data[0] = currentInputWidth;
            data[1] = currentInputHeight;
            data[2] = 1.0f / currentInputWidth;
            data[3] = 1.0f / currentInputHeight;
            bgfx.set_uniform(GetOrCreateUniform("u_tex_size0", bgfx.UniformType.Vec4), data, 1);

            data[0] = currentMaskWidth;
            data[1] = currentMaskHeight;
            data[2] = 1.0f / currentMaskWidth;
            data[3] = 1.0f / currentMaskHeight;
            bgfx.set_uniform(GetOrCreateUniform("u_tex_size1", bgfx.UniformType.Vec4), data, 1);

            data[0] = actualTargetWidth;
            data[1] = actualTargetHeight;
            data[2] = 0.0f;
            data[3] = 0.0f;
            bgfx.set_uniform(GetOrCreateUniform("u_target_dims", bgfx.UniformType.Vec4), data, 1);

            data[0] = 1.0f / actualTargetWidth;
            data[1] = 1.0f / actualTargetHeight;
            data[2] = 0.0f;
            data[3] = 0.0f;
            bgfx.set_uniform(GetOrCreateUniform("u_inv_view_dims", bgfx.UniformType.Vec4), data, 1);

            data[0] = 0.0f;
            bgfx.set_uniform(GetOrCreateUniform("u_swap_xy", bgfx.UniformType.Vec4), data, 1);

            data[0] = windowWidth;
            data[1] = windowHeight;
            data[2] = 0.0f;
            data[3] = 0.0f;
            bgfx.set_uniform(GetOrCreateUniform("u_screen_dims", bgfx.UniformType.Vec4), data, 1);
            bgfx.set_uniform(GetOrCreateUniform("u_quad_dims", bgfx.UniformType.Vec4), data, 1);

            data[0] = 1.0f;
            data[1] = 0.0f;
            data[2] = 0.0f;
            data[3] = 0.0f;
            bgfx.set_uniform(GetOrCreateUniform("u_rotation_type", bgfx.UniformType.Vec4), data, 1);
        }

        private bgfx.UniformHandle GetOrCreateUniform(string name, bgfx.UniformType type)
        {
            if (uniformCache.TryGetValue(name, out var handle))
                return handle;

            handle = bgfx.create_uniform(name, type, 1);
            uniformCache[name] = handle;
            return handle;
        }

        public void Dispose()
        {
            DestroyChainResources();

            if (vertexBufferHandle.idx != ushort.MaxValue)
                bgfx.destroy_vertex_buffer(vertexBufferHandle);
            if (vertexBufferFlipped.idx != ushort.MaxValue)
                bgfx.destroy_vertex_buffer(vertexBufferFlipped);
            if (indexBufferHandle.idx != ushort.MaxValue)
                bgfx.destroy_index_buffer(indexBufferHandle);
            if (vertexLayoutHandle.idx != ushort.MaxValue)
                bgfx.destroy_vertex_layout(vertexLayoutHandle);
        }
    }
}
