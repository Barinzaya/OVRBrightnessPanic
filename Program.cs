﻿using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using Valve.VR;

namespace OVRBrightnessPanic
{
    public class Program
    {
        public const float ACTIVATE_FACTOR = 0.5f;
        public const float DT = 0.05f;
        public const float RESET_SPEED = 0.25f;

        private CVRInput vrInput;
        private CVRSettings vrSettings;
        private CVRSystem vrSystem;

        private ulong inputSet;
        private ulong inputActivate, inputResetAuto, inputResetHold;

        private SoundPlayer activateSound, resetSound;

        private float? initialBrightness = null;
        private bool resetting = false;
        private bool running = true;

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
            }
            finally
            {
                program.Deinit();
            }
        }

        public void Init()
        {
            Console.CancelKeyPress += CancelKeyPressed;

            var initError = EVRInitError.None;
            vrSystem = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
            if(initError != EVRInitError.None)
            {
                var message = OpenVR.GetStringForHmdError(initError);
                throw new Exception($"Failed to initialize OpenVR: {message}");
            }

            vrInput = OpenVR.Input;
            vrSettings = OpenVR.Settings;

            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var manifestPath = Path.Combine(appDir, "action_manifest.json");

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

            activateSound = new SoundPlayer(Path.Combine(appDir, "activate.wav"));
            resetSound    = new SoundPlayer(Path.Combine(appDir, "reset.wav"));
        }

        public void Run()
        {
            var ev = new VREvent_t();
            var evSize = (uint)Marshal.SizeOf(typeof(VREvent_t));

            var actionSets = new VRActiveActionSet_t[]
            {
                new VRActiveActionSet_t
                {
                    ulActionSet = inputSet,
                },
            };
            var actionSetSize = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));

            var actionData = new InputDigitalActionData_t();
            var actionDataSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));

            Console.WriteLine("Brightness panic button is running. Input bindings may be changed through SteamVR's input bindings.");

            var sleepTime = (int)Math.Round(1000 * DT);
            while(running)
            {
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

                var inputError = vrInput.UpdateActionState(actionSets, actionSetSize);
                if(inputError != EVRInputError.None)
                {
                    var message = inputError.ToString();
                    throw new Exception($"Failed to update action state: {message}");
                }

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
                    TriggerActivate();
                }

                if(initialBrightness.HasValue && resetAutoPressed)
                {
                    resetting = !resetting;
                    if(resetting)
                    {
                        Console.WriteLine("Starting automatic reset.");
                    }
                    else
                    {
                        Console.WriteLine("Cancelling automatic reset.");
                    }
                }

                if(initialBrightness.HasValue && resetHoldChanged)
                {
                    resetting = resetHoldHeld;
                    if(resetting)
                    {
                        Console.WriteLine("Starting held reset.");
                    }
                    else
                    {
                        Console.WriteLine("Cancelling held reset.");
                    }
                }

                if(resetting)
                {
                    resetting = TriggerReset(DT * RESET_SPEED);
                    if(resetting && (resetAutoPressed || resetHoldChanged))
                    {
                        try
                        {
                            resetSound.Play();
                        }
                        catch(FileNotFoundException) {}
                    }
                }

                Thread.Sleep(sleepTime);
            }
        }

        public void Deinit()
        {
            if(vrSettings != null)
            {
                TriggerReset();
            }

            if(vrSystem != null)
            {
                OpenVR.Shutdown();
                vrInput = null;
                vrSettings = null;
                vrSystem = null;

                inputActivate = 0;
                inputResetAuto = 0;
                inputResetHold = 0;
                inputSet = 0;
            }
        }

        public void CancelKeyPressed(object sender, ConsoleCancelEventArgs args)
        {
            if(running)
            {
                args.Cancel = true;
                running = false;
            }
        }

        public void TriggerActivate()
        {
            var brightness = GetBrightness();
            if(initialBrightness == null || initialBrightness < brightness)
            {
                initialBrightness = brightness;
            }

            brightness = Math.Max(0.2f, Math.Min(ACTIVATE_FACTOR * brightness, 1.6f));
            SetBrightness(brightness);

            Console.WriteLine($"Panic button activated! Brightness decreased to {100 * brightness:f0}%.");
            try
            {
                activateSound.Play();
            }
            catch(FileNotFoundException) {}
        }

        public bool TriggerReset(float maxAmount = float.PositiveInfinity)
        {
            var result = false;

            if(initialBrightness.HasValue)
            {
                var brightness = GetBrightness();
                var targetBrightness = initialBrightness.Value;

                var delta = targetBrightness - brightness;
                if(delta > 0)
                {
                    brightness = brightness + Math.Min(delta, maxAmount);
                    SetBrightness(brightness);

                    result = true;
                }
                
                if(brightness >= targetBrightness)
                {
                    Console.WriteLine("Reset completed.");
                    initialBrightness = null;
                }
            }

            return result;
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
    }
}
