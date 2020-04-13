using Newtonsoft.Json;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Valve.VR;

namespace OVRBrightnessPanic
{
    public class Program
    {
        private NativeWindow tkWindow;
        private GraphicsContext tkContext;

        private CVRCompositor vrCompositor;
        private CVRInput vrInput;
        private CVRSettings vrSettings;
        private CVRSystem vrSystem;

        private IntPtr leftMirrorHandle;
        private uint leftBrightnessFBO, leftBrightnessTexture, leftMirrorTexture;

        private IntPtr rightMirrorHandle;
        private uint rightBrightnessFBO, rightBrightnessTexture, rightMirrorTexture;

        private int brightnessProgram, brightnessShaderVert, brightnessShaderFrag;
        private int brightnessVAO, brightnessVBO;

        private int leftWidth, leftHeight, leftLevels;
        private int rightWidth, rightHeight, rightLevels;

        private float? imageBrightness, imageRate;

        private ulong inputSet;
        private ulong inputActivate, inputResetAuto, inputResetHold;
        private VRActiveActionSet_t[] inputSets;

        private Config config;
        private SoundPlayer activateSound, autoActivateSound, resetSound;

        private float minBrightness, maxBrightness;

        private float? autoInitialBrightness = null;
        private float? manualInitialBrightness = null;
        private bool resetting = false;
        private bool running = true;

        [StructLayout(LayoutKind.Sequential)]
        private struct BrightnessVertex
        {
            public float x, y;
            public float u, v;
        }

        public static void Main(string[] args)
        {
            var program = new Program();

            try
            {
                program.Init();
                program.Run();
            }
            catch(Exception e)
            {
                Console.Error.WriteLine($"An error has occurred: {e.Message}");
                //throw;
            }
            finally
            {
                program.Deinit();
            }
        }

        public void Init()
        {
            Console.CancelKeyPress += CancelKeyPressed;

            config = LoadConfig("config.json");

            InitVR();
            if(!running) return;

            if(config.Auto.Enabled)
            {
                imageBrightness = null;
                imageRate = null;

                InitGL();
                if(!running) return;
            }

            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            activateSound = LoadSound(config.ActivateSound);
            resetSound    = LoadSound(config.ResetSound);

            if(config.Auto.Enabled)
            {
                autoActivateSound = LoadSound(config.Auto.ActivateSound);
            }
        }

        private void InitGL()
        {
            var graphicsMode = new GraphicsMode(new ColorFormat(8, 8, 8, 0), 0, 0, 0);

            tkWindow = new NativeWindow();
            tkContext = new GraphicsContext(graphicsMode, tkWindow.WindowInfo, 3, 3, GraphicsContextFlags.ForwardCompatible | GraphicsContextFlags.Offscreen);
            tkContext.ErrorChecking = true;
            tkContext.LoadAll();

            var vrcError = vrCompositor.GetMirrorTextureGL2(EVREye.Eye_Left, ref leftMirrorTexture, ref leftMirrorHandle);
            if(vrcError != EVRCompositorError.None)
            {
                throw new Exception($"Failed to get mirror texture for left eye: {vrcError}");
            }

            vrCompositor.LockGLSharedTextureForAccess(leftMirrorHandle);
            GL.BindTexture(TextureTarget.Texture2D, (int)leftMirrorTexture);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth,  out leftWidth);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out leftHeight);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            vrCompositor.UnlockGLSharedTextureForAccess(leftMirrorHandle);

            vrcError = vrCompositor.GetMirrorTextureGL2(EVREye.Eye_Right, ref rightMirrorTexture, ref rightMirrorHandle);
            if(vrcError != EVRCompositorError.None)
            {
                throw new Exception($"Failed to get mirror texture for right eye: {vrcError}");
            }

            vrCompositor.LockGLSharedTextureForAccess(rightMirrorHandle);
            GL.BindTexture(TextureTarget.Texture2D, (int)rightMirrorTexture);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth,  out rightWidth);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out rightHeight);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            vrCompositor.UnlockGLSharedTextureForAccess(rightMirrorHandle);

            leftLevels  = CalculateNumLevels(leftWidth, leftHeight);
            rightLevels = CalculateNumLevels(rightWidth, rightHeight);

            leftBrightnessTexture  = (uint)GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, leftBrightnessTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16, leftWidth, leftHeight, 0, PixelFormat.Red, PixelType.UnsignedShort, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);

            leftBrightnessFBO = (uint)GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, leftBrightnessFBO);
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, leftBrightnessTexture, 0);

            var fboStatus = GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer);
            if(fboStatus != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception($"Failed to create left brightness FBO: {fboStatus}");
            }

            rightBrightnessTexture = (uint)GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, rightBrightnessTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16, rightWidth, rightHeight, 0, PixelFormat.Red, PixelType.UnsignedShort, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);

            rightBrightnessFBO = (uint)GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, rightBrightnessFBO);
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, rightBrightnessTexture, 0);

            fboStatus = GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer);
            if(fboStatus != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception($"Failed to create right brightness FBO: {fboStatus}");
            }

            brightnessShaderVert = CompileShader(ShaderType.VertexShader, Resource.BrightnessVert);
            brightnessShaderFrag = CompileShader(ShaderType.FragmentShader, Resource.BrightnessFrag);
            brightnessProgram = LinkProgram(brightnessShaderVert, brightnessShaderFrag);

            GL.UseProgram(brightnessProgram);

            var location = GL.GetUniformLocation(brightnessProgram, "u_texture");
            GL.Uniform1(location, 0);

            brightnessVAO = GL.GenVertexArray();
            GL.BindVertexArray(brightnessVAO);

            var brightnessVertices = new BrightnessVertex[]
            {
                new BrightnessVertex { x=-1, y=-1, u=0, v = 0, },
                new BrightnessVertex { x=+1, y=+1, u=1, v = 1, },
                new BrightnessVertex { x=-1, y=+1, u=0, v = 1, },

                new BrightnessVertex { x=-1, y=-1, u=0, v = 0, },
                new BrightnessVertex { x=+1, y=-1, u=1, v = 0, },
                new BrightnessVertex { x=+1, y=+1, u=1, v = 1, },
            };

            var brightnessVertexSize = Marshal.SizeOf(typeof(BrightnessVertex));
            var brightnessVerticesSize = brightnessVertices.Length * brightnessVertexSize;

            brightnessVBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, brightnessVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, brightnessVerticesSize, brightnessVertices, BufferUsageHint.StaticDraw);

            location = GL.GetAttribLocation(brightnessProgram, "a_pos");
            GL.VertexAttribPointer(location, 2, VertexAttribPointerType.Float, false, brightnessVertexSize, 0);
            GL.EnableVertexAttribArray(location);

            location = GL.GetAttribLocation(brightnessProgram, "a_texcoord");
            GL.VertexAttribPointer(location, 2, VertexAttribPointerType.Float, false, brightnessVertexSize, 8);
            GL.EnableVertexAttribArray(location);
        }

        private void InitVR()
        {
            var initError = EVRInitError.None;
            vrSystem = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
            if(initError != EVRInitError.None)
            {
                var message = OpenVR.GetStringForHmdError(initError);
                throw new Exception($"Failed to initialize OpenVR: {message}");
            }

            vrCompositor = OpenVR.Compositor;
            vrInput = OpenVR.Input;
            vrSettings = OpenVR.Settings;

            var manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "action_manifest.json");

            var inputError = vrInput.SetActionManifestPath(manifestPath);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to set action manifest path: {message}");
            }

            inputError = vrInput.GetActionSetHandle("/actions/main", ref inputSet);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to action set handle: {message}");
            }

            inputError = vrInput.GetActionHandle("/actions/main/in/activate", ref inputActivate);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to get action handle for Activate: {message}");
            }

            inputError = vrInput.GetActionHandle("/actions/main/in/reset-auto", ref inputResetAuto);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to get action handle for Reset (Auto): {message}");
            }

            inputError = vrInput.GetActionHandle("/actions/main/in/reset", ref inputResetHold);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to get action handle for Reset (Hold): {message}");
            }

            var propertyError = ETrackedPropertyError.TrackedProp_Success;
            minBrightness = vrSystem.GetFloatTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_DisplayMinAnalogGain_Float, ref propertyError);
            maxBrightness = vrSystem.GetFloatTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_DisplayMaxAnalogGain_Float, ref propertyError);
        }

        private Config LoadConfig(string path)
        {
            var config = new Config();

            try
            {
                var configStr = File.ReadAllText(path, Encoding.UTF8);
                JsonConvert.PopulateObject(configStr, config);

                if(config.ActivateFactor < 0 || config.ActivateFactor >= 1)
                {
                    var def = new Config().ActivateFactor;
                    Console.WriteLine($"ActiveFactor configuration value must be at least 0 and less than 1. Defaulting to {def}.");
                    config.ActivateFactor = def;
                }

                if(config.ResetRate <= 0)
                {
                    var def = new Config().ResetRate;
                    Console.WriteLine($"ResetRate configuration value must be greater than 0. Defaulting to {def}.");
                    config.ResetRate = def;
                }

                if(config.UpdateFrequency <= 0)
                {
                    var def = new Config().UpdateFrequency;
                    Console.WriteLine($"UpdateFrequency must be greater than 0. Defaulting to {def}.");
                    config.UpdateFrequency = def;
                }

                if(config.Auto.BrightnessFrequency <= 0)
                {
                    var def = new Config().Auto.BrightnessFrequency;
                    Console.WriteLine($"Auto.BrightnessFrequency must be greater than 0. Defaulting to {def}.");
                    config.Auto.BrightnessFrequency = def;
                }

                if(config.Auto.DynamicMaxRate <= 0)
                {
                    var def = new Config().Auto.DynamicMaxRate;
                    Console.WriteLine($"Auto.DynamicMaxRate must be greater than 0. Defaulting to {def}.");
                    config.Auto.DynamicMaxRate = def;
                }

                if(config.Auto.DynamicMinBrightness <= 0)
                {
                    var def = new Config().Auto.DynamicMinBrightness;
                    Console.WriteLine($"Auto.DynamicMinBrightness must be greater than 0. Defaulting to {def}.");
                    config.Auto.DynamicMinBrightness = def;
                }

                if(config.Auto.StaticMaxBrightness <= 0)
                {
                    var def = new Config().Auto.StaticMaxBrightness;
                    Console.WriteLine($"Auto.StaticMaxBrightness must be greater than 0. Defaulting to {def}.");
                    config.Auto.StaticMaxBrightness = def;
                }
            }
            catch(FileNotFoundException) {}
            catch(Exception e)
            {
                Console.Error.WriteLine($"Failed to read configuration from <{path}>: {e.Message}");
            }

            try
            {
                var str = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path, str, Encoding.UTF8);
            }
            catch(Exception e)
            {
                Console.Error.WriteLine($"Failed to write configuration to <{path}>: {e.Message}");
            }

            return config;
        }

        public void Run()
        {
            inputSets = new VRActiveActionSet_t[]
            {
                new VRActiveActionSet_t
                {
                    ulActionSet = inputSet,
                },
            };

            Console.WriteLine("Brightness panic button is running. Input bindings may be changed through SteamVR's input bindings.");

            var nextImageTime = 0d;
            var nextProcessTime = 0d;

            var imageTargetDT = 1 / config.Auto.BrightnessFrequency;
            var processTargetDT = 1 / config.UpdateFrequency;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while(running)
            {
                var oldBrightness = GetBrightness();
                var newBrightness = oldBrightness;

                var time = stopwatch.Elapsed.TotalSeconds;

                if(config.Auto.Enabled && time >= nextImageTime)
                {
                    nextImageTime += imageTargetDT;
                    ProcessImage(imageTargetDT);
                }

                if(time >= nextProcessTime)
                {
                    nextProcessTime += processTargetDT;

                    if(config.Auto.Enabled)
                    {
                        ProcessAuto(processTargetDT, ref newBrightness);
                    }

                    ProcessManual(processTargetDT, ref newBrightness);
                }

                if(newBrightness != oldBrightness)
                {
                    SetBrightness(newBrightness);
                }

                time = stopwatch.Elapsed.TotalSeconds;
                var nextTime = Math.Min(nextImageTime, nextProcessTime);

                var sleepTime = (int)Math.Round(1000 * (nextTime - time));
                if(sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }

        public void Deinit()
        {
            DeinitGL();
            DeinitVR();
        }

        private void DeinitGL()
        {
            if(brightnessVBO != 0)
            {
                GL.DeleteBuffer(brightnessVBO);
                brightnessVBO = 0;
            }

            if(brightnessVAO != 0)
            {
                GL.DeleteVertexArray(brightnessVAO);
                brightnessVAO = 0;
            }

            if(brightnessProgram != 0)
            {
                GL.DeleteProgram(brightnessProgram);
                brightnessProgram = 0;
            }

            if(brightnessShaderFrag != 0)
            {
                GL.DeleteShader(brightnessShaderFrag);
                brightnessShaderFrag = 0;
            }

            if(brightnessShaderVert != 0)
            {
                GL.DeleteShader(brightnessShaderVert);
                brightnessShaderVert = 0;
            }

            if(leftBrightnessFBO != 0)
            {
                GL.DeleteFramebuffer(leftBrightnessFBO);
                leftBrightnessFBO = 0;
            }

            if(leftBrightnessTexture != 0)
            {
                GL.DeleteTexture(leftBrightnessTexture);
                leftBrightnessTexture = 0;
            }

            if(rightBrightnessFBO != 0)
            {
                GL.DeleteFramebuffer(rightBrightnessFBO);
                rightBrightnessFBO = 0;
            }

            if(rightBrightnessTexture != 0)
            {
                GL.DeleteTexture(rightBrightnessTexture);
                rightBrightnessTexture = 0;
            }

            if(leftMirrorTexture != 0)
            {
                vrCompositor.ReleaseSharedGLTexture(leftMirrorTexture, leftMirrorHandle);
                leftMirrorHandle = IntPtr.Zero;
                leftMirrorTexture = 0;
            }

            if(rightMirrorTexture != 0)
            {
                vrCompositor.ReleaseSharedGLTexture(rightMirrorTexture, rightMirrorHandle);
                rightMirrorHandle = IntPtr.Zero;
                rightMirrorTexture = 0;
            }

            tkContext?.Dispose();
            tkContext = null;

            tkWindow?.Dispose();
            tkWindow = null;
        }

        private void DeinitVR()
        {
            if(vrSettings != null && manualInitialBrightness.HasValue)
            {
                SetBrightness(manualInitialBrightness.Value);
                manualInitialBrightness = null;
            }

            if(vrSystem != null)
            {
                OpenVR.Shutdown();
                vrCompositor = null;
                vrInput = null;
                vrSettings = null;
                vrSystem = null;

                inputActivate = 0;
                inputResetAuto = 0;
                inputResetHold = 0;
                inputSet = 0;
                inputSets = null;
            }
        }

        private void CancelKeyPressed(object sender, ConsoleCancelEventArgs args)
        {
            if(running)
            {
                args.Cancel = true;
                running = false;
            }
        }

        public float GetBrightness()
        {
            var error = EVRSettingsError.None;
            var gain = vrSettings.GetFloat("steamvr", "analogGain", ref error);
            if(error != EVRSettingsError.None)
            {
                var message = vrSettings.GetSettingsErrorNameFromEnum(error);
                throw new Exception($"Failed to get display brightness: {message}");
            }

            return (float)Math.Pow(gain, 1/2.2);
        }

        public void SetBrightness(float brightness)
        {
            var gain = (float)Math.Pow(brightness, 2.2);

            var error = EVRSettingsError.None;
            vrSettings.SetFloat("steamvr", "analogGain", gain, ref error);

            if(error != EVRSettingsError.None)
            {
                var message = vrSettings.GetSettingsErrorNameFromEnum(error);
                throw new Exception($"Failed to set display brightness: {message}");
            }
        }

        private int CalculateNumLevels(int width, int height)
        {
            var d = Math.Max(width, height);
            var n = 1;

            if(d >= 0x10000) { n += 16; d >>= 16; }
            if(d >= 0x00100) { n +=  8; d >>=  8; }
            if(d >= 0x00010) { n +=  4; d >>=  4; }
            if(d >= 0x00004) { n +=  2; d >>=  2; }
            if(d >= 0x00002) { n +=  1; d >>=  1; }

            return n;
        }

        private int CompileShader(ShaderType type, string source)
        {
            var shader = GL.CreateShader(type);

            try
            {
                GL.ShaderSource(shader, source);
                GL.CompileShader(shader);
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);

                if(status == 0)
                {
                    var message = GL.GetShaderInfoLog(shader);
                    throw new Exception($"Failed to compile {type}: {message}");
                }
            }
            catch(Exception)
            {
                GL.DeleteShader(shader);
                throw;
            }

            return shader;
        }

        private int LinkProgram(params int[] shaders)
        {
            var program = GL.CreateProgram();

            try
            {
                foreach(var shader in shaders)
                {
                    GL.AttachShader(program, shader);
                }

                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);

                if(status == 0)
                {
                    var message = GL.GetProgramInfoLog(program);
                    throw new Exception($"Failed to link shader program: {message}");
                }
            }
            catch(Exception)
            {
                GL.DeleteProgram(program);
                throw;
            }

            return program;
        }

        private SoundPlayer LoadSound(string path)
        {
            if(path == null) return null;

            try
            {
                var player = new SoundPlayer(path);
                player.Load();
                return player;
            }
            catch(Exception e)
            {
                Console.Error.WriteLine($"Failed to load <{path}>: {e.Message}");
                return null;
            }
        }

        private void ProcessAuto(float dt, ref float screenBrightness)
        {
            if(!imageBrightness.HasValue)
            {
                return;
            }

            var maxBrightness = manualInitialBrightness ?? screenBrightness;

            if(imageBrightness > config.Auto.StaticMaxBrightness)
            {
                maxBrightness = Math.Min(maxBrightness, config.Auto.StaticMaxBrightness / imageBrightness.Value);
            }

            if(imageBrightness > config.Auto.DynamicMinBrightness && imageRate > config.Auto.DynamicMaxRate)
            {
                maxBrightness = Math.Min(maxBrightness, config.Auto.DynamicMaxRate / imageRate.Value);
            }

            if(maxBrightness < screenBrightness)
            {
                if(!autoInitialBrightness.HasValue)
                {
                    autoInitialBrightness = screenBrightness;
                    autoActivateSound?.Play();
                }

                screenBrightness = maxBrightness;
                Console.WriteLine("Automatic brightness adjustment is reducing the screen brightness.");
            }
            else if(autoInitialBrightness.HasValue)
            {
                var targetBrightness = autoInitialBrightness.Value;
                if(imageBrightness > 0)
                {
                    var recoverBrightness = (config.Auto.StaticMaxBrightness / imageBrightness.Value) - config.Auto.RecoverMargin;
                    targetBrightness = Math.Min(recoverBrightness, targetBrightness);
                }

                var maxDelta = dt * config.Auto.RecoverRate;
                var actualDelta = Math.Min(maxDelta, targetBrightness - screenBrightness);
                if(actualDelta > 0)
                {
                    screenBrightness += actualDelta;
                }

                if(screenBrightness >= autoInitialBrightness.Value)
                {
                    autoInitialBrightness = null;
                }
            }
        }

        private void ProcessImage(float dt)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindVertexArray(brightnessVAO);
            GL.UseProgram(brightnessProgram);

            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, leftBrightnessFBO);
            GL.BindTexture(TextureTarget.Texture2D, leftMirrorTexture);
            GL.Viewport(0, 0, leftWidth, leftHeight);

            vrCompositor.LockGLSharedTextureForAccess(leftMirrorHandle);
            try
            {
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }
            finally
            {
                vrCompositor.UnlockGLSharedTextureForAccess(leftMirrorHandle);
            }

            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, rightBrightnessFBO);
            GL.BindTexture(TextureTarget.Texture2D, rightMirrorTexture);
            GL.Viewport(0, 0, rightWidth, rightHeight);

            vrCompositor.LockGLSharedTextureForAccess(rightMirrorHandle);
            try
            {
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }
            finally
            {
                vrCompositor.UnlockGLSharedTextureForAccess(rightMirrorHandle);
            }

            ushort leftBrightnessRaw = 0;
            GL.BindTexture(TextureTarget.Texture2D, leftBrightnessTexture);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.GetTexImage(TextureTarget.Texture2D, leftLevels - 1, PixelFormat.Red, PixelType.UnsignedShort, ref leftBrightnessRaw);
            var leftBrightness = leftBrightnessRaw / 65535f;

            ushort rightBrightnessRaw = 0;
            GL.BindTexture(TextureTarget.Texture2D, rightBrightnessTexture);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.GetTexImage(TextureTarget.Texture2D, rightLevels - 1, PixelFormat.Red, PixelType.UnsignedShort, ref rightBrightnessRaw);
            var rightBrightness = rightBrightnessRaw / 65535f;

            var newImageBrightness = (float)Math.Pow(Math.Max(leftBrightness, rightBrightness), 1 / 2.2);

            if(imageBrightness.HasValue)
                imageRate = (newImageBrightness - imageBrightness.Value) / dt;

            imageBrightness = newImageBrightness;
        }

        private void ProcessManual(float dt, ref float screenBrightness)
        {
            var ev = new VREvent_t();
            var evSize = (uint)Marshal.SizeOf(typeof(VREvent_t));

            while(vrSystem.PollNextEvent(ref ev, evSize))
            {
                switch((EVREventType)ev.eventType)
                {
                    case EVREventType.VREvent_DriverRequestedQuit:
                    case EVREventType.VREvent_Quit:
                        vrSystem.AcknowledgeQuit_Exiting();
                        running = false;
                        break;
                }
            }

            var inputSetsSize = inputSets.Length * Marshal.SizeOf(typeof(VRActiveActionSet_t));

            var inputError = vrInput.UpdateActionState(inputSets, (uint)inputSetsSize);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to update action state: {message}");
            }

            var actionData = new InputDigitalActionData_t();
            var actionDataSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));

            inputError = vrInput.GetDigitalActionData(inputActivate, ref actionData, actionDataSize, 0);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to get Activate action state: {message}");
            }
            var activatePressed = actionData.bChanged && actionData.bState;

            inputError = vrInput.GetDigitalActionData(inputResetAuto, ref actionData, actionDataSize, 0);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to get Reset (Auto) action state: {message}");
            }
            var resetAutoPressed = actionData.bChanged && actionData.bState;

            inputError = vrInput.GetDigitalActionData(inputResetHold, ref actionData, actionDataSize, 0);
            if(inputError != EVRInputError.None)
            {
                var message = inputError.ToString();
                throw new Exception($"Failed to get Reset (Hold) action state: {message}");
            }
            var resetHoldChanged = actionData.bChanged;
            var resetHoldHeld = actionData.bState;

            if(activatePressed)
            {
                if(manualInitialBrightness == null || manualInitialBrightness < screenBrightness)
                {
                    manualInitialBrightness = screenBrightness;
                }

                var oldBrightness = screenBrightness;
                screenBrightness = Math.Max(minBrightness, Math.Min(config.ActivateFactor * screenBrightness, maxBrightness));
                autoInitialBrightness = null;
                resetting = false;

                if(screenBrightness < oldBrightness)
                {
                    Console.WriteLine($"Panic button activated! Brightness decreased to {100 * screenBrightness:f0}%.");
                    activateSound?.Play();
                }
            }

            if(manualInitialBrightness.HasValue && resetAutoPressed)
            {
                resetting = !resetting;
                if(resetting)
                {
                    Console.WriteLine("Starting automatic reset.");
                    resetSound?.Play();
                }
                else
                {
                    Console.WriteLine("Cancelling automatic reset.");
                }
            }

            if(manualInitialBrightness.HasValue && resetHoldChanged)
            {
                resetting = resetHoldHeld;
                if(resetting)
                {
                    Console.WriteLine("Starting held reset.");
                    resetSound?.Play();
                }
                else
                {
                    Console.WriteLine("Cancelling held reset.");
                }
            }

            if(resetting)
            {
                autoInitialBrightness = null;

                var delta = manualInitialBrightness.Value - screenBrightness;
                if(delta > 0)
                {
                    screenBrightness += Math.Min(delta, dt * config.ResetRate);
                }
                else
                {
                    Console.WriteLine("Reset completed.");
                    manualInitialBrightness = null;
                    resetting = false;
                }
            }
        }
    }
}
