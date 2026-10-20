using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ICSharpCode.SharpZipLib.BZip2;

try
{
    var dryRun = args.Contains("--patch-only");
    var paths = args.Where(arg => arg != "--patch-only").ToArray();
    if (paths.Length > 1) throw new InvalidOperationException("Usage: PCL2NoRestrictDownload.exe [official PCL exe] [--patch-only]");
    var directory = AppContext.BaseDirectory;
    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")))
        ?? throw new InvalidOperationException("Missing manifest");
    if (manifest.Format != 1 || manifest.Patch != "pcl.bsdiff") throw new InvalidOperationException("Unsupported patch manifest");
    var official = Path.GetFullPath(paths.Length == 0 ? Path.Combine(directory, "Plain Craft Launcher 2.exe") : paths[0]);
    var patched = Path.Combine(Path.GetDirectoryName(official)!, "Plain Craft Launcher 2.NoRestrictDownload.exe");
    var original = File.ReadAllBytes(official);
    Check(original, manifest.OfficialSha256, "Official PCL executable");
    if (File.Exists(patched))
    {
        Check(File.ReadAllBytes(patched), manifest.OutputSha256, "Existing patched executable");
    }
    else
    {
        var delta = File.ReadAllBytes(Path.Combine(directory, manifest.Patch));
        Check(delta, manifest.PatchSha256, "Patch file");
        var output = Apply(original, delta);
        Check(output, manifest.OutputSha256, "Patched executable");
        var temporary = patched + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, output);
            File.Move(temporary, patched);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    Console.WriteLine($"Official PCL {manifest.Version} was not modified. Patched copy: {patched}");
    if (!dryRun)
        _ = Process.Start(new ProcessStartInfo(patched) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(official)! })
            ?? throw new InvalidOperationException("Unable to launch patched PCL");
}
catch (Exception error)
{
    Console.Error.WriteLine("Patch rejected; original PCL was not started: " + error.Message);
    Environment.ExitCode = 1;
}

static void Check(byte[] bytes, string expected, string label)
{
    var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{label}: SHA-256 mismatch ({actual}); expected {expected}");
}

static byte[] Apply(byte[] original, byte[] delta)
{
    if (delta.Length < 32 || System.Text.Encoding.ASCII.GetString(delta, 0, 8) != "BSDIFF40")
        throw new InvalidOperationException("Not a BSDIFF40 patch");
    var controlSize = Number(delta, 8);
    var diffSize = Number(delta, 16);
    var outputSize = Number(delta, 24);
    if (controlSize < 0 || diffSize < 0 || outputSize < 0 || outputSize > 64 * 1024 * 1024 ||
        controlSize > delta.Length - 32 || diffSize > delta.Length - 32 - controlSize)
        throw new InvalidOperationException("Invalid patch sizes");
    using var control = Block(delta, 32, controlSize);
    using var difference = Block(delta, 32 + controlSize, diffSize);
    using var extra = Block(delta, 32 + controlSize + diffSize, delta.Length - 32 - controlSize - diffSize);
    var result = new byte[outputSize];
    long oldPosition = 0, newPosition = 0;
    var operations = 0;
    while (newPosition < result.Length)
    {
        var add = Number(Read(control, 8), 0);
        var copy = Number(Read(control, 8), 0);
        var skip = Number(Read(control, 8), 0);
        if (++operations > 1_000_000 || add < 0 || copy < 0 || add == 0 && copy == 0 && skip == 0 ||
            add > result.Length - newPosition || copy > result.Length - newPosition - add)
            throw new InvalidOperationException("Invalid patch control block");
        var diff = Read(difference, checked((int)add));
        for (var i = 0; i < diff.Length; i++)
        {
            var offset = oldPosition + i;
            var value = offset >= 0 && offset < original.Length ? original[(int)offset] : 0;
            result[checked((int)(newPosition + i))] = unchecked((byte)(value + diff[i]));
        }
        newPosition += add;
        oldPosition += add;
        ReadExact(extra, result, checked((int)newPosition), checked((int)copy));
        newPosition += copy;
        oldPosition = checked(oldPosition + skip);
    }
    return result;
}

static long Number(byte[] bytes, int offset)
{
    long value = bytes[offset + 7] & 0x7F;
    for (var i = 6; i >= 0; i--) value = checked(value * 256 + bytes[offset + i]);
    return (bytes[offset + 7] & 0x80) != 0 ? -value : value;
}

static BZip2InputStream Block(byte[] bytes, long offset, long length) =>
    new(new MemoryStream(bytes, checked((int)offset), checked((int)length), false));

static byte[] Read(Stream stream, int count)
{
    var bytes = new byte[count];
    ReadExact(stream, bytes, 0, count);
    return bytes;
}

static void ReadExact(Stream stream, byte[] bytes, int offset, int count)
{
    while (count > 0)
    {
        var amount = stream.Read(bytes, offset, count);
        if (amount == 0) throw new InvalidOperationException("Truncated patch");
        count -= amount;
        offset += amount;
    }
}

internal sealed record Manifest(int Format, string Version, string Patch, string OfficialSha256, string OutputSha256, string PatchSha256);
