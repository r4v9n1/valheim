using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace TerramizerServer
{
    // Vanilla ZPackage.Write(ZPackage) calls GetArray(), which copies the
    // source MemoryStream before writing it. The normal ZPackage constructor
    // uses an exposable MemoryStream, so copy the existing buffer directly and
    // preserve the exact length-prefixed wire format.
    [HarmonyPatch(typeof(ZPackage), "Write", new[] { typeof(ZPackage) })]
    internal static class ZPackageWritePackagePatch
    {
        private static readonly AccessTools.FieldRef<ZPackage, MemoryStream> Stream =
            AccessTools.FieldRefAccess<ZPackage, MemoryStream>("m_stream");
        private static readonly AccessTools.FieldRef<ZPackage, BinaryWriter> Writer =
            AccessTools.FieldRefAccess<ZPackage, BinaryWriter>("m_writer");

        private static bool Prefix(ZPackage __instance, ZPackage pkg)
        {
            if (__instance == null || pkg == null)
                return true;

            MemoryStream source = Stream(pkg);
            BinaryWriter sourceWriter = Writer(pkg);
            BinaryWriter target = Writer(__instance);
            if (source == null || sourceWriter == null || target == null)
                return true;

            sourceWriter.Flush();
            source.Flush();
            if (!source.TryGetBuffer(out ArraySegment<byte> buffer))
                return true;

            target.Write((int)source.Length);
            target.Write(buffer.Array, buffer.Offset, (int)source.Length);
            return false;
        }
    }
}
