using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct FrameConstants
{
    internal float Xx, Xy, Xz, Xw, Yx, Yy, Yz, Yw, Zx, Zy, Zz, Zw;
    internal uint Grid, Indirect;
    internal float Time;
    internal uint Count;
}

internal struct View
{
    internal double X, Y, Z, Yaw, Pitch, HalfHeight;

    internal static View Default()
    {
        return new View { X = 1, Y = 5.5, Z = 6, Yaw = 0.68, Pitch = 0.64, HalfHeight = 39 };
    }

    internal FrameConstants ForFrame(int grid, int objects, int width, int height, RenderPath path, float time)
    {
        double yawSin = Math.Sin(Yaw), yawCos = Math.Cos(Yaw);
        double pitchSin = Math.Sin(Pitch), pitchCos = Math.Cos(Pitch);
        double scaleX = height / (HalfHeight * width), scaleY = 1.0 / HalfHeight;
        double scaleZ = 1.0 / (HalfHeight * 4.0);
        var data = new FrameConstants
        {
            Xx = (float)(yawCos * scaleX), Xz = (float)(yawSin * scaleX),
            Yx = (float)(-pitchSin * yawSin * scaleY), Yy = (float)(pitchCos * scaleY),
            Yz = (float)(pitchSin * yawCos * scaleY),
            Zx = (float)(-pitchCos * yawSin * scaleZ), Zy = (float)(-pitchSin * scaleZ),
            Zz = (float)(pitchCos * yawCos * scaleZ),
            Grid = (uint)grid, Indirect = (uint)path, Time = time, Count = (uint)objects
        };
        data.Xw = (float)-(data.Xx * X + data.Xz * Z);
        data.Yw = (float)-(data.Yx * X + data.Yy * Y + data.Yz * Z);
        data.Zw = 0.5f - (float)(data.Zx * X + data.Zy * Y + data.Zz * Z);
        return data;
    }
}
