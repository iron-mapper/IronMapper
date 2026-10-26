using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace MstatReport;

/// <summary>Owner of a size entry: assembly, namespace and type name (generic instantiations fold into the definition).</summary>
public readonly record struct TypeKey(string Assembly, string Namespace, string Name)
{
    public string FullName => Namespace.Length == 0 ? Name : $"{Namespace}.{Name}";
}

/// <summary>One record of an mstat section: what it belongs to and its size in bytes.</summary>
public sealed record SizeEntry(string Section, TypeKey Owner, string Member, long Size);

/// <summary>
/// An .mstat file is an ECMA-335 assembly. Its &lt;Module&gt; type has one method per section ("Types", "Methods",
/// "Blobs", ...); each method body is a list of records <c>ldtoken X | ldstr "name"</c> followed by <c>ldc.i4</c>
/// values, the first of which is the size in bytes. The layout of the remaining values differs between mstat
/// versions, so records are split generically on ldtoken/ldstr and only the first value is used.
/// </summary>
public sealed class MstatFile
{
    public static readonly TypeKey Unattributed = new("(not attributed)", "", "(not attributed)");
    public static readonly TypeKey CompilerGenerated = new("(compiler generated)", "", "(compiler generated)");

    private readonly MetadataReader _md;

    public string Path { get; }
    public Version Version { get; }
    public IReadOnlyList<SizeEntry> Entries { get; }

    private MstatFile(string path, PEReader pe)
    {
        Path = path;
        _md = pe.GetMetadataReader();
        Version = _md.GetAssemblyDefinition().Version;

        var entries = new List<SizeEntry>();
        var moduleType = _md.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(1));
        foreach (var handle in moduleType.GetMethods())
        {
            var method = _md.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            string section = _md.GetString(method.Name);
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            foreach (var (key, values) in ReadRecords(il))
            {
                if (values.Count == 0)
                {
                    continue;
                }

                var (owner, member) = Resolve(key);
                entries.Add(new SizeEntry(section, owner, member, values[0]));
            }
        }
        Entries = entries;
    }

    public static MstatFile Load(string path)
    {
        using var pe = new PEReader(File.OpenRead(path), PEStreamOptions.PrefetchEntireImage);
        return new MstatFile(path, pe);
    }

    // Key of a record: a metadata token (ldtoken) or a string (ldstr).
    private readonly record struct RecordKey(int Token, string? Text);

    private List<(RecordKey Key, List<long> Values)> ReadRecords(BlobReader il)
    {
        var records = new List<(RecordKey, List<long>)>();
        List<long>? current = null;
        while (il.RemainingBytes > 0)
        {
            byte op = il.ReadByte();
            switch (op)
            {
                case 0xD0: // ldtoken
                    current = [];
                    records.Add((new RecordKey(il.ReadInt32(), null), current));
                    break;
                case 0x72: // ldstr
                    string text = _md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xFFFFFF));
                    current = [];
                    records.Add((new RecordKey(0, text), current));
                    break;
                case 0x15: current?.Add(-1); break;                      // ldc.i4.m1
                case >= 0x16 and <= 0x1E: current?.Add(op - 0x16); break; // ldc.i4.0 .. ldc.i4.8
                case 0x1F: current?.Add(il.ReadSByte()); break;          // ldc.i4.s
                case 0x20: current?.Add(il.ReadInt32()); break;          // ldc.i4
                case 0x21: current?.Add(il.ReadInt64()); break;          // ldc.i8
                case 0x26: break;                                        // pop
                case 0x00: break;                                        // nop
                case 0x2A: return records;                               // ret
                default:
                    throw new InvalidDataException($"{Path}: unexpected IL opcode 0x{op:X2} at offset {il.Offset - 1}.");
            }
        }
        return records;
    }

    private (TypeKey Owner, string Member) Resolve(RecordKey key)
    {
        if (key.Text is not null)
        {
            return (Unattributed, key.Text);
        }

        var handle = MetadataTokens.EntityHandle(key.Token);
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            case HandleKind.TypeDefinition:
            case HandleKind.TypeSpecification:
                return (TypeFromHandle(handle), "");
            case HandleKind.MemberReference:
                var mr = _md.GetMemberReference((MemberReferenceHandle)handle);
                return (OwnerOf(mr.Parent), _md.GetString(mr.Name));
            case HandleKind.MethodSpecification:
                return Resolve(new RecordKey(MetadataTokens.GetToken(_md.GetMethodSpecification((MethodSpecificationHandle)handle).Method), null));
            case HandleKind.MethodDefinition:
                var md = _md.GetMethodDefinition((MethodDefinitionHandle)handle);
                return (TypeFromHandle(md.GetDeclaringType()), _md.GetString(md.Name));
            case HandleKind.FieldDefinition:
                var fd = _md.GetFieldDefinition((FieldDefinitionHandle)handle);
                return (TypeFromHandle(fd.GetDeclaringType()), _md.GetString(fd.Name));
            default:
                return (Unattributed, handle.Kind.ToString());
        }
    }

    private TypeKey OwnerOf(EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeReference or HandleKind.TypeDefinition or HandleKind.TypeSpecification => TypeFromHandle(parent),
        HandleKind.MethodDefinition => TypeFromHandle(_md.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType()),
        _ => Unattributed,
    };

    internal TypeKey TypeFromHandle(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => FromTypeReference((TypeReferenceHandle)handle),
        HandleKind.TypeDefinition => FromTypeDefinition((TypeDefinitionHandle)handle),
        HandleKind.TypeSpecification => FromTypeSpecification((TypeSpecificationHandle)handle),
        _ => Unattributed,
    };

    private TypeKey FromTypeReference(TypeReferenceHandle handle)
    {
        var tr = _md.GetTypeReference(handle);
        string name = _md.GetString(tr.Name);
        var scope = tr.ResolutionScope;
        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
                string asm = _md.GetString(_md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                return new TypeKey(asm, _md.GetString(tr.Namespace), name);
            case HandleKind.TypeReference:
                var outer = FromTypeReference((TypeReferenceHandle)scope);
                return outer with { Name = $"{outer.Name}+{name}" };
            default:
                return new TypeKey(CompilerGenerated.Assembly, _md.GetString(tr.Namespace), name);
        }
    }

    // Types defined in the mstat itself are compiler-generated helpers (no source assembly).
    private TypeKey FromTypeDefinition(TypeDefinitionHandle handle)
    {
        var td = _md.GetTypeDefinition(handle);
        string name = _md.GetString(td.Name);
        var declaring = td.GetDeclaringType();
        if (!declaring.IsNil)
        {
            var outer = FromTypeDefinition(declaring);
            return outer with { Name = $"{outer.Name}+{name}" };
        }
        return new TypeKey(CompilerGenerated.Assembly, _md.GetString(td.Namespace), name);
    }

    private TypeKey FromTypeSpecification(TypeSpecificationHandle handle)
    {
        var reader = _md.GetBlobReader(_md.GetTypeSpecification(handle).Signature);
        return new SignatureDecoder<TypeKey, object?>(new OwnerProvider(this), _md, null).DecodeType(ref reader);
    }

    /// <summary>Decodes a type signature to the type that owns the size: generic instantiations → the definition.</summary>
    private sealed class OwnerProvider(MstatFile file) : ISignatureTypeProvider<TypeKey, object?>
    {
        private static readonly TypeKey Unknown = new("(generic parameter)", "", "!");

        public TypeKey GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => file.TypeFromHandle(handle);
        public TypeKey GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => file.TypeFromHandle(handle);
        public TypeKey GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => file.TypeFromHandle(handle);
        public TypeKey GetGenericInstantiation(TypeKey genericType, ImmutableArray<TypeKey> typeArguments) => genericType;
        public TypeKey GetSZArrayType(TypeKey elementType) => elementType with { Name = elementType.Name + "[]" };
        public TypeKey GetArrayType(TypeKey elementType, ArrayShape shape) => elementType with { Name = elementType.Name + "[,]" };
        public TypeKey GetPointerType(TypeKey elementType) => elementType with { Name = elementType.Name + "*" };
        public TypeKey GetByReferenceType(TypeKey elementType) => elementType;
        public TypeKey GetPinnedType(TypeKey elementType) => elementType;
        public TypeKey GetModifiedType(TypeKey modifier, TypeKey unmodifiedType, bool isRequired) => unmodifiedType;
        public TypeKey GetFunctionPointerType(MethodSignature<TypeKey> signature) => new("System.Private.CoreLib", "System", "(function pointer)");
        public TypeKey GetGenericMethodParameter(object? genericContext, int index) => Unknown;
        public TypeKey GetGenericTypeParameter(object? genericContext, int index) => Unknown;
        public TypeKey GetPrimitiveType(PrimitiveTypeCode typeCode) => new("System.Private.CoreLib", "System", typeCode.ToString());
    }
}
