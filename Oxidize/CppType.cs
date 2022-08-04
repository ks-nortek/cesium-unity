﻿using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Oxidize
{
    [Flags]
    internal enum CppTypeFlags
    {
        Pointer = 1,
        Reference = 2,
        Const = 4
    }

    /// <summary>
    /// Describes a C++ type.
    /// </summary>
    internal class CppType
    {
        public readonly CppTypeKind Kind;
        public readonly IReadOnlyCollection<string> Namespaces;
        public readonly string Name;
        public readonly IReadOnlyCollection<CppType>? GenericArguments;
        public readonly CppTypeFlags Flags;
        public readonly string? HeaderOverride;

        private static readonly string[] StandardNamespace = { "std" };
        private static readonly string[] NoNamespace = { };

        private const string IncludeCStdInt = "<cstdint>";
        private const string IncludeCStdDef = "<cstddef>";

        public static readonly CppType Int8 = CreatePrimitiveType(StandardNamespace, "int8_t", 0, IncludeCStdInt);
        public static readonly CppType Int16 = CreatePrimitiveType(StandardNamespace, "int16_t", 0, IncludeCStdInt);
        public static readonly CppType Int32 = CreatePrimitiveType(StandardNamespace, "int32_t", 0, IncludeCStdInt);
        public static readonly CppType Int64 = CreatePrimitiveType(StandardNamespace, "int64_t", 0, IncludeCStdInt);
        public static readonly CppType UInt16 = CreatePrimitiveType(StandardNamespace, "uint16_t", 0, IncludeCStdInt);
        public static readonly CppType UInt8 = CreatePrimitiveType(StandardNamespace, "uint8_t", 0, IncludeCStdInt);
        public static readonly CppType UInt32 = CreatePrimitiveType(StandardNamespace, "uint32_t", 0, IncludeCStdInt);
        public static readonly CppType UInt64 = CreatePrimitiveType(StandardNamespace, "uint64_t", 0, IncludeCStdInt);
        public static readonly CppType Boolean = CreatePrimitiveType(NoNamespace, "bool");
        public static readonly CppType Single = CreatePrimitiveType(NoNamespace, "float");
        public static readonly CppType Double = CreatePrimitiveType(NoNamespace, "double");
        public static readonly CppType VoidPointer = CreatePrimitiveType(NoNamespace, "void", CppTypeFlags.Pointer);
        public static readonly CppType Void = CreatePrimitiveType(NoNamespace, "void");
        public static readonly CppType NullPointer = CreatePrimitiveType(StandardNamespace, "nullptr_t", 0, IncludeCStdDef);

        public static CppType FromCSharp(CppGenerationContext context, ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                    return Int8;
                case SpecialType.System_Int16:
                    return Int16;
                case SpecialType.System_Int32:
                    return Int32;
                case SpecialType.System_Int64:
                    return Int64;
                case SpecialType.System_Single:
                    return Single;
                case SpecialType.System_Double:
                    return Double;
                case SpecialType.System_Byte:
                    return UInt8;
                case SpecialType.System_UInt16:
                    return UInt16;
                case SpecialType.System_UInt32:
                    return UInt32;
                case SpecialType.System_UInt64:
                    return UInt64;
                case SpecialType.System_Boolean:
                    return Boolean;
                case SpecialType.System_IntPtr:
                    return VoidPointer;
                case SpecialType.System_Void:
                    return Void;
            }

            IPointerTypeSymbol? pointer = type as IPointerTypeSymbol;
            if (pointer != null)
            {
                CppType original = FromCSharp(context, pointer.PointedAtType);
                return original.AsPointer();
            }

            List<string> namespaces = new List<string>();

            INamespaceSymbol ns = type.ContainingNamespace;
            while (ns != null)
            {
                if (ns.Name.Length > 0)
                    namespaces.Add(ns.Name);
                ns = ns.ContainingNamespace;
            }

            if (context.BaseNamespace.Length > 0)
            {
                namespaces.Add(context.BaseNamespace);
            }

            namespaces.Reverse();

            // If the first two namespaces are identical, remove the duplication.
            // This is to avoid `Oxidize::Oxidize`.
            if (namespaces.Count >= 2 && namespaces[0] == namespaces[1])
                namespaces.RemoveAt(0);

            // TODO: generics

            if (SymbolEqualityComparer.Default.Equals(type.BaseType, context.Compilation.GetSpecialType(SpecialType.System_Enum)))
                return new CppType(CppTypeKind.Enum, namespaces, type.Name, null, 0);
            else if (type.IsReferenceType)
                return new CppType(CppTypeKind.ClassWrapper, namespaces, type.Name, null, 0);
            else if (IsBlittableStruct(context, type))
                return new CppType(CppTypeKind.BlittableStruct, namespaces, type.Name, null, 0);
            else
                return new CppType(CppTypeKind.NonBlittableStructWrapper, namespaces, type.Name, null, 0);
        }

        public CppType(
            CppTypeKind kind,
            IReadOnlyCollection<string> namespaces,
            string name,
            IReadOnlyCollection<CppType>? genericArguments,
            CppTypeFlags flags,
            string? headerOverride = null)
        {
            this.Kind = kind;
            if (namespaces == StandardNamespace || namespaces == NoNamespace)
                this.Namespaces = namespaces;
            else
                this.Namespaces = new List<string>(namespaces);
            this.Name = name;
            if (genericArguments != null)
                this.GenericArguments = new List<CppType>(genericArguments);
            this.Flags = flags;

            if (headerOverride != null)
            {
                // If the header name is not wrapped in quotes or angle brackets, wrap it in quotes.
                if (!headerOverride.StartsWith("<") && !headerOverride.StartsWith("\""))
                    headerOverride = '"' + headerOverride + '"';
                this.HeaderOverride = headerOverride;
            }
        }

        public bool CanBeForwardDeclared
        {
            get
            {
                // TODO: currently any type that uses a custom header cannot be forward declared, but this may need more nuance.
                return this.HeaderOverride == null;
            }
        }

        public string GetFullyQualifiedNamespace(bool startWithGlobal = true)
        {
            string ns = string.Join("::", Namespaces);
            if (!startWithGlobal)
                return ns;

            if (ns.Length > 0)
                return "::" + ns;
            else
                return "";
        }

        public string GetFullyQualifiedName(bool startWithGlobal = true)
        {
            string modifier = Flags.HasFlag(CppTypeFlags.Const) ? "const " : "";
            string suffix = Flags.HasFlag(CppTypeFlags.Pointer)
                ? "*"
                : Flags.HasFlag(CppTypeFlags.Reference)
                    ? "&"
                    : "";
            string ns = GetFullyQualifiedNamespace(startWithGlobal);
            if (ns.Length > 0)
                return $"{modifier}{ns}::{Name}{suffix}";
            else
                return $"{modifier}{Name}{suffix}";
        }

        public void AddForwardDeclarationsToSet(ISet<string> forwardDeclarations)
        {
            // Primitives do not need to be forward declared
            if (Kind == CppTypeKind.Primitive)
                return;

            // Non-pointer, non-reference types cannot be forward declared.
            //if (!Flags.HasFlag(CppTypeFlags.Reference) && !Flags.HasFlag(CppTypeFlags.Pointer))
            //    return;

            string ns = GetFullyQualifiedNamespace(false);
            if (ns != null)
            {
                string typeType;
                if (Kind == CppTypeKind.BlittableStruct || Kind == CppTypeKind.NonBlittableStructWrapper)
                    typeType = "struct";
                else if (Kind == CppTypeKind.Enum)
                    typeType = "enum class";
                else
                    typeType = "class";
                forwardDeclarations.Add(
                    $$"""
                    namespace {{ns}} {
                    {{typeType}} {{Name}};
                    }
                    """);
            }
        }

        /// <summary>
        /// Adds the includes that are required to use this type in a generated
        /// header file as part of a method signature. If the type can be
        /// forward declared instead, this method will do nothing.
        /// </summary>
        /// <param name="includes">The set of includes to which to add this type's includes.</param>
        public void AddHeaderIncludesToSet(ISet<string> includes)
        {
            AddIncludesToSet(includes, true);
        }

        /// <summary>
        /// Adds the includes that are required to use this type in a generated
        /// source file.
        /// </summary>
        /// <param name="includes">The set of includes to which to add this type's includes.</param>
        public void AddSourceIncludesToSet(ISet<string> includes)
        {
            AddIncludesToSet(includes, false);
        }

        private void AddIncludesToSet(ISet<string> includes, bool forHeader)
        {
            if (this.HeaderOverride != null)
            {
                includes.Add(this.HeaderOverride);
                return;
            }

            if (Kind == CppTypeKind.Primitive)
            {
                // Special case for primitives in <cstdint>.
                if (Namespaces == StandardNamespace)
                {
                    includes.Add(IncludeCStdInt);
                }
                return;
            }

            bool canBeForwardDeclared = true; // Flags.HasFlag(CppTypeFlags.Reference) || Flags.HasFlag(CppTypeFlags.Pointer);
            if (!forHeader || !canBeForwardDeclared)
            {
                // Build an include name from the namespace and type names.
                string path = string.Join("/", Namespaces);
                if (path.Length > 0)
                    includes.Add($"<{path}/{Name}.h>");
                else
                    includes.Add($"<{Name}.h>");
            }

            // Add includes for generic arguments, too.
            // TODO
        }

        public CppType AsPointer()
        {
            return new CppType(Kind, Namespaces, Name, GenericArguments, Flags | CppTypeFlags.Pointer, HeaderOverride);
        }

        public CppType AsConstReference()
        {
            return new CppType(Kind, Namespaces, Name, GenericArguments, Flags | CppTypeFlags.Const | CppTypeFlags.Reference & ~CppTypeFlags.Pointer, HeaderOverride);
        }

        /// <summary>
        /// Gets a version of this type suitable for use as the return value
        /// of a wrapped method. This simply returns the type unmodified.
        /// </summary>
        public CppType AsReturnType()
        {
            // All types are returned by value.
            return this;
        }

        /// <summary>
        /// Gets a version of this type suitable for use as a wrapped function
        /// parameter. For classes and structs, this returns a const reference
        /// to the type.
        /// </summary>
        public CppType AsParameterType()
        {
            switch (this.Kind)
            {
                case CppTypeKind.ClassWrapper:
                case CppTypeKind.BlittableStruct:
                case CppTypeKind.NonBlittableStructWrapper:
                    return this.AsConstReference();
            }

            return this;
        }

        /// <summary>
        /// Gets the version of this type that should be used in a function
        /// pointer that will call into the managed side.
        /// </summary>
        public CppType AsInteropType()
        {
            if (this.Kind == CppTypeKind.Primitive || this.Kind == CppTypeKind.BlittableStruct)
                return this;
            else if (this.Kind == CppTypeKind.Enum)
                return UInt32;

            return VoidPointer;
        }

        /// <summary>
        /// Gets an expression that converts this type to the
        /// {@link AsInteropType}.
        /// </summary>
        public string GetConversionToInteropType(CppGenerationContext context, string variableName)
        {
            switch (this.Kind)
            {
                case CppTypeKind.ClassWrapper:
                    return $"{variableName}.GetHandle().GetRaw()";
                case CppTypeKind.Enum:
                    return $"::std::uint32_t({variableName})";
                case CppTypeKind.Primitive:
                case CppTypeKind.BlittableStruct:
                case CppTypeKind.Unknown:
                default:
                    return variableName;
            }
        }

        public string GetConversionFromInteropType(CppGenerationContext context, string variableName)
        {
            switch (this.Kind)
            {
                case CppTypeKind.ClassWrapper:
                    return $"{GetFullyQualifiedName()}({CppObjectHandle.GetCppType(context).GetFullyQualifiedName()}({variableName}))";
                case CppTypeKind.Enum:
                    return $"{this.GetFullyQualifiedName()}({variableName})";
                case CppTypeKind.Primitive:
                case CppTypeKind.BlittableStruct:
                case CppTypeKind.Unknown:
                default:
                    return variableName;
            }
        }

        private static CppType CreatePrimitiveType(IReadOnlyCollection<string> cppNamespaces, string cppTypeName, CppTypeFlags flags = 0, string? headerOverride = null)
        {
            return new CppType(CppTypeKind.Primitive, cppNamespaces, cppTypeName, null, flags, headerOverride);
        }

        /// <summary>
        /// Determines if the given type is a blittable value type (struct).
        /// </summary>
        /// <param name="context"></param>
        /// <param name="type"></param>
        /// <returns>True if the struct is blittable.</returns>
        private static bool IsBlittableStruct(CppGenerationContext context, ITypeSymbol type)
        {
            if (!type.IsValueType)
                return false;

            if (IsBlittablePrimitive(type))
                return true;

            ImmutableArray<ISymbol> members = type.GetMembers();
            foreach (ISymbol member in members)
            {
                if (member.Kind != SymbolKind.Field)
                    continue;

                ITypeSymbol? memberType = member as ITypeSymbol;
                if (memberType == null)
                    continue;

                if (!IsBlittableStruct(context, memberType))
                    return false;
            }

            return true;
        }

        private static bool IsBlittablePrimitive(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Boolean:
                    return true;
                default:
                    return false;
            }
        }
    }
}
