using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

// Inspected 2.99.0.0 release pair; neither binary is included in this repository.
const string OfficialSha = "6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386";
try
{
    if (args.Length != 3) throw new InvalidOperationException("Usage: ILPatch <official-2.99.0.0.exe> <donor-2.99.0.0.exe> <output.exe>");
    var original = Path.GetFullPath(args[0]);
    var donorPath = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args[2]);
    if (output.Equals(original, StringComparison.OrdinalIgnoreCase) || output.Equals(donorPath, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Output must not overwrite an input");
    CheckSha(original, OfficialSha);
    using var official = ModuleDefMD.Load(original);
    using var donor = ModuleDefMD.Load(donorPath);
    if (official.Assembly?.Name != donor.Assembly?.Name) throw new InvalidOperationException("Different assembly identities");
    var donorRestriction = donor.GetTypes().Single(type => type.FullName == "PCL.ModSecret")
        .Fields.Single(field => field.Name == "RestrictMcDownload");
    var officialRestriction = official.GetTypes().Single(type => type.FullName == "PCL.ModSecret")
        .Fields.Single(field => field.Name == "RestrictMcDownload");
    if (donorRestriction.Constant?.Value is not false || officialRestriction.Constant?.Value is not true)
        throw new InvalidOperationException("Restriction constants do not match the inspected official and donor builds");
    officialRestriction.Constant!.Value = false;
    var targets = new (string Owner, string Name, int Parameters)[]
    {
        ("PCL.FormMain/_Closure$__25-0", "_Lambda$__0", 0),
        ("PCL.PageDownloadInstall", "LoaderInit", 0),
        ("PCL.PageDownloadModify", "LoadInstanceFinished", 1),
        ("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1),
        ("PCL.ModModpack", "ModpackInstall", 0),
        ("PCL.ModModpack", "ModpackInstall", 3)
    };
    var pairs = targets.Select(t => (Target: Find(official, t.Owner, t.Name, t.Parameters), Donor: Find(donor, t.Owner, t.Name, t.Parameters))).ToArray();
    foreach (var (target, source) in pairs)
        if (target.FullName != source.FullName || target.Body is null || source.Body is null || BodyText(target) == BodyText(source))
            throw new InvalidOperationException("Missing, incompatible, or identical target: " + target.FullName);
    var before = Snapshot(official);
    foreach (var method in official.GetTypes().SelectMany(type => type.Methods).Where(method => method.Body is not null))
        method.Body!.KeepOldMaxStack = true;
    var graft = new Graft(official, donor);
    foreach (var (target, source) in pairs)
    {
        graft.Copy(source, target);
        if (BodyText(target) != BodyText(source))
            throw new InvalidOperationException("Donor IL was not mapped identically: " + target.FullName);
    }
    var expected = pairs.ToDictionary(pair => pair.Target.FullName, pair => BodyText(pair.Target));
    var temporary = output + ".partial";
    if (File.Exists(output) || File.Exists(temporary)) throw new InvalidOperationException("Output or temporary path exists");
    try
    {
        official.Write(temporary);
        using var result = ModuleDefMD.Load(temporary);
        var after = Snapshot(result);
        if (!before.Keys.Order().SequenceEqual(after.Keys.Order())) throw new InvalidOperationException("Method inventory changed");
        foreach (var (name, text) in before)
            if (after[name] != (expected.TryGetValue(name, out var replacement) ? replacement : text))
                throw new InvalidOperationException("IL verification failed: " + name + "\nexpected: " + (expected.TryGetValue(name, out var replacement2) ? replacement2 : text) + "\nactual: " + after[name]);
        var resultRestriction = result.GetTypes().Single(type => type.FullName == "PCL.ModSecret")
            .Fields.Single(field => field.Name == "RestrictMcDownload");
        if (resultRestriction.Constant?.Value is not false) throw new InvalidOperationException("Restriction constant was not updated");
        if (!official.Resources.Select(r => r.Name).SequenceEqual(result.Resources.Select(r => r.Name)))
            throw new InvalidOperationException("Resource inventory changed");
        File.Move(temporary, output);
        Console.WriteLine($"Verified {expected.Count} grafted methods; all other method bodies unchanged: {output}");
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
}
catch (Exception exception)
{
    Console.Error.WriteLine("Patch rejected: " + exception);
    Environment.ExitCode = 1;
}

static void CheckSha(string path, string sha)
{
    using var stream = File.OpenRead(path);
    var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    if (actual != sha) throw new InvalidOperationException($"SHA-256 mismatch for {path}: got {actual}; expected {sha}");
}

static MethodDef Find(ModuleDef module, string owner, string name, int parameters)
{
    var methods = module.GetTypes().Where(t => t.FullName == owner).SelectMany(t => t.Methods)
        .Where(m => m.Name == name && m.MethodSig.Params.Count == parameters).ToArray();
    if (methods.Length != 1) throw new InvalidOperationException($"Expected one {owner}::{name}/{parameters}; got {methods.Length}");
    return methods[0];
}

static Dictionary<string, string> Snapshot(ModuleDef module) => module.GetTypes().SelectMany(t => t.Methods)
    .Where(m => m.Body is not null).ToDictionary(m => m.FullName, BodyText);

static string BodyText(MethodDef method)
{
    var body = method.Body!;
    var indices = body.Instructions.Select((i, n) => (i, n)).ToDictionary(pair => pair.i, pair => pair.n);
    string Index(Instruction? i) => i is null ? "end" : indices[i].ToString();
    string Operand(object? operand) => operand switch
    {
        null => "",
        Instruction i => Index(i),
        IList<Instruction> branches => string.Join(",", branches.Select(Index)),
        Local local => "local:" + body.Variables.IndexOf(local),
        Parameter parameter => "arg:" + parameter.Index,
        IFullName full => full.FullName,
        _ => Convert.ToString(operand, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };
    var lines = new List<string> { $"init:{body.InitLocals};max:{body.MaxStack}",
        string.Join(";", body.Variables.Select(v => v.Type.FullName)) };
    lines.AddRange(body.Instructions.Select(i => i.OpCode.Code + ":" + Operand(i.Operand)));
    lines.AddRange(body.ExceptionHandlers.Select(e => string.Join(":", e.HandlerType, Index(e.TryStart), Index(e.TryEnd), Index(e.HandlerStart), Index(e.HandlerEnd), Index(e.FilterStart), e.CatchType?.FullName)));
    return string.Join("\n", lines);
}

sealed class Graft
{
    private readonly ModuleDef target;
    private readonly ModuleDef donor;
    private readonly Importer importer;
    private readonly Dictionary<string, TypeDef> types;
    private readonly HashSet<string> assemblyNames;

    public Graft(ModuleDef target, ModuleDef donor)
    {
        this.target = target;
        this.donor = donor;
        types = target.GetTypes().ToDictionary(t => t.FullName);
        assemblyNames = target.GetAssemblyRefs().Select(a => a.Name.ToString()).ToHashSet();
        importer = new Importer(target, ImporterOptions.TryToUseExistingAssemblyRefs, default(GenericParamContext), new LocalMapper(this));
    }

    private bool IsLocal(ITypeDefOrRef type) => type is TypeDef def ? def.Module == donor :
        type is TypeRef reference && reference.DefinitionAssembly?.Name == donor.Assembly?.Name;

    private TypeDef MapType(ITypeDefOrRef type)
    {
        if (!IsLocal(type) || !types.TryGetValue(type.FullName, out var result))
            throw new InvalidOperationException("Unmapped local type: " + type.FullName);
        return result;
    }

    private MethodDef MapMethod(MethodDef method)
    {
        var local = MapType(method.DeclaringType);
        var candidates = local.Methods.Where(m => m.FullName == method.FullName).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Unmapped local method: " + method.FullName);
        return candidates[0];
    }

    private FieldDef MapField(FieldDef field)
    {
        var local = MapType(field.DeclaringType);
        var candidates = local.Fields.Where(f => f.FullName == field.FullName).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Unmapped local field: " + field.FullName);
        return candidates[0];
    }

    private sealed class LocalMapper(Graft graft) : ImportMapper
    {
        public override ITypeDefOrRef Map(ITypeDefOrRef source) => graft.IsLocal(source) ? graft.MapType(source) : null!;
        public override IMethod Map(MethodDef source) => graft.MapMethod(source);
        public override IField Map(FieldDef source) => graft.MapField(source);
    }

    private ITypeDefOrRef MapTypeRef(ITypeDefOrRef source)
    {
        if (!IsLocal(source) && source.DefinitionAssembly is not null && !assemblyNames.Contains(source.DefinitionAssembly.Name.ToString()))
            throw new InvalidOperationException("Unknown external assembly: " + source.DefinitionAssembly.Name);
        return importer.Import(source);
    }

    private IMethod MapMethodRef(IMethod source)
    {
        if (source is MethodDef definition) return MapMethod(definition);
        if (source is MethodSpec spec) return importer.Import(spec);
        if (source is MemberRef reference && IsLocal(reference.DeclaringType))
        {
            var resolved = reference.ResolveMethodDef() ?? throw new InvalidOperationException("Unresolved local method: " + source.FullName);
            MapMethod(resolved);
        }
        return importer.Import(source);
    }

    private IField MapFieldRef(IField source)
    {
        if (source is FieldDef definition) return MapField(definition);
        if (source is MemberRef reference && IsLocal(reference.DeclaringType))
        {
            var resolved = reference.ResolveFieldDef() ?? throw new InvalidOperationException("Unresolved local field: " + source.FullName);
            MapField(resolved);
        }
        return importer.Import(source);
    }

    public void Copy(MethodDef source, MethodDef destination)
    {
        var from = source.Body!;
        var body = new CilBody { InitLocals = from.InitLocals, MaxStack = from.MaxStack, KeepOldMaxStack = true };
        foreach (var local in from.Variables) body.Variables.Add(new Local(importer.Import(local.Type)));
        var map = from.Instructions.ToDictionary(i => i, i => new Instruction(i.OpCode));
        foreach (var instruction in from.Instructions) body.Instructions.Add(map[instruction]);
        foreach (var old in from.Instructions)
            map[old].Operand = old.Operand switch
            {
                null => null,
                Instruction branch => map[branch],
                IList<Instruction> branches => branches.Select(i => map[i]).ToArray(),
                Local local => body.Variables[from.Variables.IndexOf(local)],
                Parameter parameter => destination.Parameters[parameter.Index],
                IMethod method => MapMethodRef(method),
                IField field => MapFieldRef(field),
                ITypeDefOrRef type => MapTypeRef(type),
                TypeSig signature => importer.Import(signature),
                string or sbyte or byte or int or long or float or double => old.Operand,
                _ => throw new InvalidOperationException("Unsupported IL operand: " + old.Operand.GetType())
            };
        Instruction? Map(Instruction? old) => old is null ? null : map[old];
        foreach (var old in from.ExceptionHandlers)
            body.ExceptionHandlers.Add(new ExceptionHandler(old.HandlerType)
            {
                TryStart = Map(old.TryStart), TryEnd = Map(old.TryEnd), HandlerStart = Map(old.HandlerStart),
                HandlerEnd = Map(old.HandlerEnd), FilterStart = Map(old.FilterStart),
                CatchType = old.CatchType is null ? null : MapTypeRef(old.CatchType)
            });
        destination.Body = body;
    }
}
