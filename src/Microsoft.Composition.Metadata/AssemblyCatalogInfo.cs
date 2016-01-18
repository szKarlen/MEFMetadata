using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;

namespace Microsoft.Composition.Metadata
{
    public class AssemblyCatalogInfo
    {
        private Discovery discovery;
        private MetadataReader metadataReader;
        private string assemblyFilePath;
        private string fullAssemblyName;

        private Dictionary<TypeDefinitionHandle, TypeInfo> compositionTypes = new Dictionary<TypeDefinitionHandle, TypeInfo>();
        private Dictionary<FieldDefinitionHandle, MemberInfo> fields;
        private Dictionary<PropertyDefinitionHandle, MemberInfo> properties;
        private Dictionary<MethodDefinitionHandle, MemberInfo> methods;

        private readonly Dictionary<Handle, bool> isImportAttributeCache = new Dictionary<Handle, bool>();
        private readonly Dictionary<Handle, ExportOrInheritedExport> isExportAttributeCache = new Dictionary<Handle, ExportOrInheritedExport>();
        private HashSet<string> knownExportAttributeOrDerivedType;
        private HashSet<string> knownImportAttributeOrDerivedType;

        //private bool hasInheritedExports = false;
        //private readonly Dictionary<Handle, bool> inheritedExportTypes = new Dictionary<Handle, bool>();
        //private readonly HashSet<string> inheritedExportTypesByName = new HashSet<string>();

        public AssemblyCatalogInfo(Discovery discovery, MetadataReader metadataReader, string assemblyFilePath)
        {
            Debug.WriteLine("AssemblyCatalogInfo: " + assemblyFilePath);
            this.discovery = discovery;
            this.metadataReader = metadataReader;
            this.assemblyFilePath = assemblyFilePath;
        }

        public string AssemblyFilePath
        {
            get { return this.assemblyFilePath; }
        }

        public string FullAssemblyName
        {
            get
            {
                return fullAssemblyName;
            }
        }

        public bool IsMefAssembly { get; private set; }

        public IEnumerable<TypeInfo> CompositionTypes
        {
            get
            {
                return compositionTypes.Values;
            }
        }

        public HashSet<string> KnownExportOrDerived
        {
            get
            {
                return this.knownExportAttributeOrDerivedType;
            }
        }

        public HashSet<string> KnownImportOrDerived
        {
            get
            {
                return this.knownImportAttributeOrDerivedType;
            }
        }

        public async Task Populate()
        {
            this.IsMefAssembly = true;
            this.fullAssemblyName = this.metadataReader.GetFullAssemblyName();

            this.knownExportAttributeOrDerivedType = new HashSet<string>()
            {
                "System.ComponentModel.Composition.ExportAttribute",
                "System.ComponentModel.Composition.InheritedExportAttribute"
            };
            this.knownImportAttributeOrDerivedType = new HashSet<string>()
            {
                "System.ComponentModel.Composition.ImportAttribute",
                "System.ComponentModel.Composition.ImportManyAttribute"
            };

            var referenceFullNames = metadataReader.GetReferenceAssemblyFullNames();
            var tasks = new List<Task<AssemblyCatalogInfo>>();
            foreach (var referenceAssemblyFullName in referenceFullNames)
            {
                var referenceCatalog = this.discovery.GetAssemblyCatalogInfoFromAssemblyName(referenceAssemblyFullName);
                if (referenceCatalog != null)
                {
                    tasks.Add(referenceCatalog);
                }
            }

            foreach (var task in tasks)
            {
                var referenceCatalog = await task.ConfigureAwait(false);
                if (referenceCatalog == null)
                {
                    continue;
                }

                var knownExportAttributes = referenceCatalog.KnownExportOrDerived;
                foreach (var known in knownExportAttributes)
                {
                    knownExportAttributeOrDerivedType.Add(known);
                }

                var knownImportAttributes = referenceCatalog.KnownImportOrDerived;
                foreach (var known in knownImportAttributes)
                {
                    knownImportAttributeOrDerivedType.Add(known);
                }
            }

            foreach (var TypeDefinitionHandle in metadataReader.TypeDefinitions)
            {
                var typeDefinition = metadataReader.GetTypeDefinition(TypeDefinitionHandle);
                Walk(typeDefinition);
            }

            foreach (var customAttributeHandle in metadataReader.CustomAttributes)
            {
                var customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
                var attributeTypeDefinitionHandle = metadataReader.GetAttributeTypeDefinitionHandle(customAttribute);

                if (TryHandleImportAttribute(customAttribute, attributeTypeDefinitionHandle))
                {
                    continue;
                }

                if (TryHandleExportAttribute(customAttribute, attributeTypeDefinitionHandle))
                {
                    continue;
                }
            }
        }

        private void Walk(TypeDefinition typeDefinition)
        {
            var baseTypeDefinitionHandle = typeDefinition.BaseType;
            if (baseTypeDefinitionHandle.IsNil)
            {
                return;
            }

            var Kind = baseTypeDefinitionHandle.Kind;
            if (Kind == HandleKind.TypeDefinition)
            {
                typeDefinition = metadataReader.GetTypeDefinition((TypeDefinitionHandle)baseTypeDefinitionHandle);
                var typeFullName = metadataReader.GetFullTypeName(typeDefinition);
                if (knownExportAttributeOrDerivedType.Contains(typeFullName))
                {
                    typeFullName = metadataReader.GetFullTypeName(typeDefinition);
                    knownExportAttributeOrDerivedType.Add(typeFullName);
                    //isExportAttributeCache[baseTypeDefinitionHandle] = ExportOrInheritedExport.Export;
                    return;
                }
                else if (knownImportAttributeOrDerivedType.Contains(typeFullName))
                {
                    typeFullName = metadataReader.GetFullTypeName(typeDefinition);
                    knownImportAttributeOrDerivedType.Add(typeFullName);
                    //isImportAttributeCache[baseTypeDefinitionHandle] = true;
                    return;
                }

                Walk(typeDefinition);
            }
            else if (Kind == HandleKind.TypeDefinition)
            {
                TypeReference typeReference = metadataReader.GetTypeReference((TypeReferenceHandle)baseTypeDefinitionHandle);
                var typeFullName = metadataReader.GetFullTypeName(typeReference);
                if (knownExportAttributeOrDerivedType.Contains(typeFullName))
                {
                    typeFullName = metadataReader.GetFullTypeName(typeDefinition);
                    knownExportAttributeOrDerivedType.Add(typeFullName);
                    //isExportAttributeCache[baseTypeDefinitionHandle] = ExportOrInheritedExport.Export;
                    return;
                }
                else if (knownImportAttributeOrDerivedType.Contains(typeFullName))
                {
                    typeFullName = metadataReader.GetFullTypeName(typeDefinition);
                    knownImportAttributeOrDerivedType.Add(typeFullName);
                    //isImportAttributeCache[baseTypeDefinitionHandle] = true;
                    return;
                }
            }
            else if (Kind == HandleKind.TypeDefinition)
            {
                // TODO: not implemented
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private bool IsImportAttribute(Handle attributeTypeDefinitionHandle)
        {
            bool isImport = false;

            if (isImportAttributeCache.TryGetValue(attributeTypeDefinitionHandle, out isImport))
            {
                return isImport;
            }

            isImport = IsImportOrImportManyAttribute(attributeTypeDefinitionHandle);
            isImportAttributeCache[attributeTypeDefinitionHandle] = isImport;
            return isImport;
        }

        private bool IsExportAttribute(Handle attributeTypeDefinitionHandle, out bool isInheritedExport)
        {
            ExportOrInheritedExport exportOrInheritedExport;

            if (!isExportAttributeCache.TryGetValue(attributeTypeDefinitionHandle, out exportOrInheritedExport))
            {
                exportOrInheritedExport = IsExportOrInheritedExportAttribute(attributeTypeDefinitionHandle);
                isExportAttributeCache.Add(attributeTypeDefinitionHandle, exportOrInheritedExport);
            }

            isInheritedExport = exportOrInheritedExport == ExportOrInheritedExport.InheritedExport;
            return exportOrInheritedExport != ExportOrInheritedExport.None;
        }

        private bool TryHandleImportAttribute(CustomAttribute customAttribute, Handle attributeTypeDefinitionHandle)
        {
            bool isImport = IsImportAttribute(attributeTypeDefinitionHandle);
            if (!isImport)
            {
                return false;
            }

            var parent = customAttribute.Parent;
            switch (parent.Kind)
            {
                case HandleKind.PropertyDefinition:
                    MemberInfo propertyInfo = GetOrAddPropertyInfo((PropertyDefinitionHandle)parent);
                    this.AddImportedMember(propertyInfo);
                    break;
                case HandleKind.FieldDefinition:
                    MemberInfo fieldInfo = GetOrAddFieldInfo((FieldDefinitionHandle)parent);
                    this.AddImportedMember(fieldInfo);
                    break;
                case HandleKind.Parameter:
                    // ignore Import attributes on parameters of importing constructors
                    break;
                case HandleKind.MethodDefinition:
                case HandleKind.MethodImplementation:
                case HandleKind.MethodSpecification:
                default:
                    throw new NotImplementedException();
            }

            return true;
        }

        private bool TryHandleExportAttribute(CustomAttribute customAttribute, Handle attributeTypeDefinitionHandle)
        {
            bool isInheritedExport = false;
            bool isExport = IsExportAttribute(attributeTypeDefinitionHandle, out isInheritedExport);
            if (!isExport)
            {
                return false;
            }

            var parent = customAttribute.Parent;
            switch (parent.Kind)
            {
                case HandleKind.TypeDefinition:
                    var TypeDefinitionHandle = (TypeDefinitionHandle)parent;
                    TypeInfo exportedTypeInfo = GetOrCreateTypeInfo(TypeDefinitionHandle);
                    if (isInheritedExport)
                    {
                        //this.inheritedExportTypes.Add(TypeDefinitionHandle, true);
                        //this.inheritedExportTypesByName.Add(exportedTypeInfo.FullName);
                        //this.hasInheritedExports = true;
                    }
                    else
                    {
                        exportedTypeInfo.IsExported = true;
                    }

                    break;
                case HandleKind.PropertyDefinition:
                    MemberInfo propertyInfo = GetOrAddPropertyInfo((PropertyDefinitionHandle)parent);
                    this.AddExportedMember(propertyInfo);
                    break;
                case HandleKind.MethodDefinition:
                    MemberInfo methodInfo = GetOrAddMethodInfo((MethodDefinitionHandle)parent);
                    this.AddExportedMember(methodInfo);
                    break;
                case HandleKind.FieldDefinition:
                    MemberInfo exportedFieldInfo = GetOrAddFieldInfo((FieldDefinitionHandle)parent);
                    this.AddExportedMember(exportedFieldInfo);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return true;
        }

        private TypeInfo GetOrCreateTypeInfo(TypeDefinitionHandle TypeDefinitionHandle)
        {
            TypeInfo typeInfo = null;
            if (this.compositionTypes.TryGetValue(TypeDefinitionHandle, out typeInfo))
            {
                return typeInfo;
            }

            typeInfo = new TypeInfo(isExported: false);
            typeInfo.MetadataToken = MetadataTokens.GetToken(metadataReader, TypeDefinitionHandle);

            this.compositionTypes.Add(TypeDefinitionHandle, typeInfo);
            return typeInfo;
        }

        private MemberInfo GetOrAddPropertyInfo(PropertyDefinitionHandle handle)
        {
            if (this.properties == null)
            {
                properties = new Dictionary<PropertyDefinitionHandle, MemberInfo>();
            }

            MemberInfo result = null;
            if (!properties.TryGetValue(handle, out result))
            {
                var property = metadataReader.GetPropertyDefinition(handle);
                var propertyMethodDefinitionHandles = property.GetAccessors();
                TypeDefinitionHandle declaringTypeDefinitionHandle;
                MethodDefinitionHandle accessorMethod = propertyMethodDefinitionHandles.Getter;
                if (accessorMethod.IsNil)
                {
                    accessorMethod = propertyMethodDefinitionHandles.Setter;
                }

                declaringTypeDefinitionHandle = metadataReader.GetMethodDefinition(accessorMethod).GetDeclaringType();
                result = new MemberInfo(MemberKind.Property);
                result.Token = this.metadataReader.GetToken(handle);
                result.DeclaringTypeDefinitionHandle = declaringTypeDefinitionHandle;
                result.Handle = handle;
                properties.Add(handle, result);
            }

            return result;
        }

        private MemberInfo GetOrAddFieldInfo(FieldDefinitionHandle handle)
        {
            if (this.fields == null)
            {
                this.fields = new Dictionary<FieldDefinitionHandle, MemberInfo>();
            }

            MemberInfo result = null;
            if (!fields.TryGetValue(handle, out result))
            {
                result = new MemberInfo(MemberKind.Field);
                var field = metadataReader.GetFieldDefinition(handle);
                result.Token = metadataReader.GetToken(handle);
                result.DeclaringTypeDefinitionHandle = field.GetDeclaringType();
                if (result.DeclaringTypeDefinitionHandle.IsNil)
                {
                    throw null;
                }

                result.Handle = handle;
                fields[handle] = result;
            }

            return result;
        }

        private MemberInfo GetOrAddMethodInfo(MethodDefinitionHandle handle)
        {
            if (this.methods == null)
            {
                this.methods = new Dictionary<MethodDefinitionHandle, MemberInfo>();
            }

            MemberInfo result = null;
            if (!methods.TryGetValue(handle, out result))
            {
                var method = metadataReader.GetMethodDefinition(handle);
                result = new MemberInfo(MemberKind.Method);
                result.Token = this.metadataReader.GetToken(handle);
                result.DeclaringTypeDefinitionHandle = method.GetDeclaringType();
                result.Handle = handle;
                methods[handle] = result;
            }

            return result;
        }

        private void AddImportedMember(MemberInfo memberInfo)
        {
            var type = GetOrCreateTypeInfo(memberInfo.DeclaringTypeDefinitionHandle);
            type.AddImportedMember(memberInfo);
        }

        private void AddExportedMember(MemberInfo memberInfo)
        {
            var type = GetOrCreateTypeInfo(memberInfo.DeclaringTypeDefinitionHandle);
            type.AddExportedMember(memberInfo);
        }

        public ExportOrInheritedExport IsExportOrInheritedExportAttribute(Handle attributeTypeDefinitionHandle)
        {
            var typeFullName = metadataReader.GetFullTypeName(attributeTypeDefinitionHandle);
            if (knownExportAttributeOrDerivedType.Contains(typeFullName))
            {
                return ExportOrInheritedExport.Export;
            }

            return ExportOrInheritedExport.None;
        }

        public bool IsImportOrImportManyAttribute(Handle attributeTypeDefinitionHandle)
        {
            var typeFullName = metadataReader.GetFullTypeName(attributeTypeDefinitionHandle);
            if (knownImportAttributeOrDerivedType.Contains(typeFullName))
            {
                return true;
            }

            return false;
        }

        public enum ExportOrInheritedExport : byte
        {
            None,
            Export,
            InheritedExport
        }
    }
}
