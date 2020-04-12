using System;
using Valve.VR;

namespace OVRBrightnessPanic
{
    public static class VRExt
    {
        public static EVRCompositorError GetMirrorTextureGL2(this CVRCompositor compositor, EVREye eye, ref uint texture, ref IntPtr handle)
        {
            unsafe
            {
                fixed(IntPtr *handlePtr = &handle)
                {
                    return compositor.GetMirrorTextureGL(eye, ref texture, (IntPtr)handlePtr);
                }
            }
        }
    }
}
