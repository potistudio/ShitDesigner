using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using ShitDesigner.Core;
using ShitDesigner.Runtime;

namespace ShitDesigner.Nodes
{
    /// <summary>Complete serialized port descriptor for a build-generated
    /// catalog record. IDs alone are insufficient because changing the port
    /// contract must invalidate the player build.</summary>
    [Serializable]
    public sealed class NodeTypeCatalogPortRecord
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private NodePortDirection direction;
        [SerializeField] private NodePortType type;
        [SerializeField] private bool required;
        [SerializeField] private bool hasDefaultImage;
        [SerializeField] private RuntimeDefaultImageKind defaultImage;

        public string Id => id ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public NodePortDirection Direction => direction;
        public NodePortType Type => type;
        public bool Required => required;
        public bool HasDefaultImage => hasDefaultImage;
        public RuntimeDefaultImageKind DefaultImage => defaultImage;

        public NodeTypeCatalogPortRecord() { }
        internal NodeTypeCatalogPortRecord(NodePortDefinition source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            id = source.Id.Value;
            displayName = source.DisplayName;
            direction = source.Direction;
            type = source.Type;
            required = source.Required;
            hasDefaultImage = source.DefaultImage.HasValue;
            defaultImage = source.DefaultImage ?? RuntimeDefaultImageKind.TransparentBlack;
        }
        internal bool Matches(NodePortDefinition source) => source != null
            && string.Equals(Id, source.Id.Value, StringComparison.Ordinal)
            && string.Equals(DisplayName, source.DisplayName, StringComparison.Ordinal)
            && Direction == source.Direction && Type == source.Type && Required == source.Required
            && HasDefaultImage == source.DefaultImage.HasValue
            && (!HasDefaultImage || DefaultImage == source.DefaultImage.Value);
    }

    /// <summary>Complete serialized parameter descriptor, including values and
    /// presentation metadata that form the immutable catalog contract.</summary>
    [Serializable]
    public sealed class NodeTypeCatalogParameterRecord
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private ParameterType type;
        [SerializeField] private string defaultValue;
        [SerializeField] private bool hasMinimum;
        [SerializeField] private string minimumValue;
        [SerializeField] private bool hasMaximum;
        [SerializeField] private string maximumValue;
        [SerializeField] private bool runtimeStateful;
        [SerializeField] private List<string> enumOptions = new List<string>();
        [SerializeField] private string group;
        [SerializeField] private int displayOrder;
        [SerializeField] private string description;
        [SerializeField] private string unit;
        [SerializeField] private string step;
        [SerializeField] private bool isReadOnly;
        [SerializeField] private bool isHidden;

        public string Id => id ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public ParameterType Type => type;
        public string DefaultValue => defaultValue ?? string.Empty;
        public bool HasMinimum => hasMinimum;
        public string MinimumValue => minimumValue ?? string.Empty;
        public bool HasMaximum => hasMaximum;
        public string MaximumValue => maximumValue ?? string.Empty;
        public bool RuntimeStateful => runtimeStateful;
        public IReadOnlyList<string> EnumOptions => enumOptions ?? (IReadOnlyList<string>)Array.Empty<string>();
        public string Group => group ?? string.Empty;
        public int DisplayOrder => displayOrder;
        public string Description => description ?? string.Empty;
        public string Unit => unit ?? string.Empty;
        public string Step => step ?? string.Empty;
        public bool IsReadOnly => isReadOnly;
        public bool IsHidden => isHidden;

        public NodeTypeCatalogParameterRecord() { }
        internal NodeTypeCatalogParameterRecord(NodeParameterDefinition source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            id = source.Id.Value;
            displayName = source.DisplayName;
            type = source.Type;
            defaultValue = Encode(source.DefaultValue);
            hasMinimum = source.Minimum.HasValue;
            minimumValue = source.Minimum.HasValue ? Encode(source.Minimum.Value) : string.Empty;
            hasMaximum = source.Maximum.HasValue;
            maximumValue = source.Maximum.HasValue ? Encode(source.Maximum.Value) : string.Empty;
            runtimeStateful = source.RuntimeStateful;
            enumOptions = (source.EnumOptions ?? Array.Empty<string>()).ToList();
            group = source.Group;
            displayOrder = source.DisplayOrder;
            description = source.Description;
            unit = source.Unit;
            step = source.Step.ToString("R", CultureInfo.InvariantCulture);
            isReadOnly = source.IsReadOnly;
            isHidden = source.IsHidden;
        }
        internal bool Matches(NodeParameterDefinition source)
        {
            if (source == null || !string.Equals(Id, source.Id.Value, StringComparison.Ordinal)
                || !string.Equals(DisplayName, source.DisplayName, StringComparison.Ordinal) || Type != source.Type
                || !string.Equals(DefaultValue, Encode(source.DefaultValue), StringComparison.Ordinal)
                || HasMinimum != source.Minimum.HasValue || HasMaximum != source.Maximum.HasValue
                || RuntimeStateful != source.RuntimeStateful || DisplayOrder != source.DisplayOrder
                || !string.Equals(Group, source.Group ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(Description, source.Description ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(Unit, source.Unit ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(Step, source.Step.ToString("R", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                || IsReadOnly != source.IsReadOnly || IsHidden != source.IsHidden) return false;
            if (HasMinimum && !string.Equals(MinimumValue, Encode(source.Minimum.Value), StringComparison.Ordinal)) return false;
            if (HasMaximum && !string.Equals(MaximumValue, Encode(source.Maximum.Value), StringComparison.Ordinal)) return false;
            return EnumOptions.SequenceEqual(source.EnumOptions ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
        internal static string Encode(ParameterValue value)
        {
            var culture = CultureInfo.InvariantCulture;
            switch (value.Type)
            {
                case ParameterType.Float: return "float:" + value.AsFloat().ToString("R", culture);
                case ParameterType.Int: return "int:" + value.AsInt().ToString(culture);
                case ParameterType.Bool: return "bool:" + (value.AsBool() ? "true" : "false");
                case ParameterType.Vector2:
                    var v2 = value.AsVector2(); return "vector2:" + v2.X.ToString("R", culture) + "," + v2.Y.ToString("R", culture);
                case ParameterType.Vector3:
                    var v3 = value.AsVector3(); return "vector3:" + v3.X.ToString("R", culture) + "," + v3.Y.ToString("R", culture) + "," + v3.Z.ToString("R", culture);
                case ParameterType.Vector4:
                    var v4 = value.AsVector4(); return "vector4:" + v4.X.ToString("R", culture) + "," + v4.Y.ToString("R", culture) + "," + v4.Z.ToString("R", culture) + "," + v4.W.ToString("R", culture);
                case ParameterType.Color:
                    var c = value.AsColor(); return "color:" + c.R.ToString("R", culture) + "," + c.G.ToString("R", culture) + "," + c.B.ToString("R", culture) + "," + c.A.ToString("R", culture);
                case ParameterType.String: return "string:" + value.AsString();
                case ParameterType.Enum: return "enum:" + value.AsString();
                case ParameterType.MediaAssetReference: return "media:" + (value.AsString() ?? string.Empty);
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }

    [Serializable]
    public sealed class NodeTypeCatalogRecord
    {
        // ID-only projections are retained to produce a useful migration
        // failure for old assets; current generated assets always fill detail.
        [SerializeField] private string typeId;
        [SerializeField] private int schemaVersion = 1;
        [SerializeField] private string displayName;
        [SerializeField] private string category;
        [SerializeField] private bool systemOwned;
        [SerializeField] private bool userAddable = true;
        [SerializeField] private List<string> portIds = new List<string>();
        [SerializeField] private List<string> parameterIds = new List<string>();
        [SerializeField] private List<NodeTypeCatalogPortRecord> ports = new List<NodeTypeCatalogPortRecord>();
        [SerializeField] private List<NodeTypeCatalogParameterRecord> parameters = new List<NodeTypeCatalogParameterRecord>();
        [SerializeField] private string shaderKey;
        [SerializeField] private string prefabKey;
        [SerializeField] private Shader shader;
        [SerializeField] private Material templateMaterial;
        [SerializeField] private GameObject scenePrefab;
        [SerializeField] private int outputPass;

        public string TypeId => typeId ?? string.Empty;
        public int SchemaVersion => schemaVersion;
        public string DisplayName => displayName ?? string.Empty;
        public string Category => category ?? string.Empty;
        public bool SystemOwned => systemOwned;
        public bool UserAddable => userAddable;
        public IReadOnlyList<string> PortIds => portIds ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<string> ParameterIds => parameterIds ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<NodeTypeCatalogPortRecord> Ports => ports ?? (IReadOnlyList<NodeTypeCatalogPortRecord>)Array.Empty<NodeTypeCatalogPortRecord>();
        public IReadOnlyList<NodeTypeCatalogParameterRecord> Parameters => parameters ?? (IReadOnlyList<NodeTypeCatalogParameterRecord>)Array.Empty<NodeTypeCatalogParameterRecord>();
        public string ShaderKey => shaderKey ?? string.Empty;
        public string PrefabKey => prefabKey ?? string.Empty;
        public Shader Shader => shader;
        public Material TemplateMaterial => templateMaterial;
        public GameObject ScenePrefab => scenePrefab;
        public int OutputPass => outputPass;

        public NodeTypeCatalogRecord() { }
        public NodeTypeCatalogRecord(NodeCatalogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            typeId = entry.TypeId.Value; schemaVersion = entry.SchemaVersion; displayName = entry.DisplayName; category = entry.Category;
            systemOwned = entry.SystemOwned; userAddable = entry.UserAddable;
            portIds = entry.Ports.Select(x => x.Id.Value).ToList();
            parameterIds = entry.Parameters.Select(x => x.Id.Value).ToList();
            ports = entry.Ports.Select(x => new NodeTypeCatalogPortRecord(x)).ToList();
            parameters = entry.Parameters.Select(x => new NodeTypeCatalogParameterRecord(x)).ToList();
            shaderKey = entry.ShaderBinding?.ShaderKey ?? string.Empty;
            outputPass = entry.ShaderBinding?.OutputPass ?? 0;
            prefabKey = entry.SceneBinding?.PrefabKey ?? string.Empty;
        }
        public Result Validate()
        {
            if (!NodeTypeId.TryParse(TypeId, out _)) return Failure("nodes.catalog.asset_type", "Catalog record TypeId is invalid.");
            if (SchemaVersion < 1 || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Category)) return Failure("nodes.catalog.asset_metadata", "Catalog record metadata is incomplete.");
            if (portIds == null || parameterIds == null || ports == null || parameters == null) return Failure("nodes.catalog.asset_members", "Catalog record members are missing.");
            if (portIds.Any(string.IsNullOrWhiteSpace) || parameterIds.Any(string.IsNullOrWhiteSpace)
                || portIds.Distinct(StringComparer.Ordinal).Count() != portIds.Count || parameterIds.Distinct(StringComparer.Ordinal).Count() != parameterIds.Count)
                return Failure("nodes.catalog.asset_members", "Catalog record ports and parameters must be unique.");
            if (ports.Count != portIds.Count || parameters.Count != parameterIds.Count || ports.Any(x => x == null) || parameters.Any(x => x == null))
                return Failure("nodes.catalog.asset_detail_missing", "Catalog record must contain complete port and parameter descriptors.");
            if (!ports.Select(x => x.Id).SequenceEqual(portIds, StringComparer.Ordinal) || !parameters.Select(x => x.Id).SequenceEqual(parameterIds, StringComparer.Ordinal))
                return Failure("nodes.catalog.asset_detail_mismatch", "Catalog record ID projections do not match complete descriptors.");
            if (outputPass < 0) return Failure("nodes.catalog.asset_pass", "Catalog shader output pass cannot be negative.");
            return Result.Success();
        }
        internal Result ValidateAssetReferenceRequirements()
        {
            if ((Category == "3D" || Category == "2D") && (ScenePrefab == null || string.IsNullOrWhiteSpace(PrefabKey)))
                return Failure("nodes.catalog.prefab_missing", "Scene records require a direct prefab reference and key.");
            if (Category.StartsWith("Shader/", StringComparison.Ordinal)
                && ((Shader == null && TemplateMaterial == null) || string.IsNullOrWhiteSpace(ShaderKey)))
                return Failure("nodes.catalog.shader_missing", "Shader records require a direct Shader or template Material reference and key.");
            return Result.Success();
        }
        internal bool Matches(NodeCatalogEntry expected)
        {
            if (expected == null || !string.Equals(TypeId, expected.TypeId.Value, StringComparison.Ordinal) || SchemaVersion != expected.SchemaVersion
                || !string.Equals(DisplayName, expected.DisplayName, StringComparison.Ordinal) || !string.Equals(Category, expected.Category, StringComparison.Ordinal)
                || SystemOwned != expected.SystemOwned || UserAddable != expected.UserAddable
                || !string.Equals(ShaderKey, expected.ShaderBinding?.ShaderKey ?? string.Empty, StringComparison.Ordinal)
                || OutputPass != (expected.ShaderBinding?.OutputPass ?? 0) || !string.Equals(PrefabKey, expected.SceneBinding?.PrefabKey ?? string.Empty, StringComparison.Ordinal)
                || Ports.Count != expected.Ports.Count || Parameters.Count != expected.Parameters.Count) return false;
            for (var i = 0; i < Ports.Count; i++) if (!Ports[i].Matches(expected.Ports[i])) return false;
            for (var i = 0; i < Parameters.Count; i++) if (!Parameters[i].Matches(expected.Parameters[i])) return false;
            return true;
        }
        internal void SetAssetReferences(GameObject prefab, Shader shaderAsset, Material material, string prefabBindingKey, string shaderBindingKey, int pass)
        {
            scenePrefab = prefab; shader = shaderAsset; templateMaterial = material; prefabKey = prefabBindingKey ?? string.Empty; shaderKey = shaderBindingKey ?? string.Empty; outputPass = pass;
        }
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes"));
    }

    /// <summary>Build-generated immutable catalog consumed by Standalone.</summary>
    [CreateAssetMenu(fileName = "NodeTypeCatalog", menuName = "ShitDesigner/Node Type Catalog")]
    public sealed class NodeTypeCatalog : ScriptableObject
    {
        [SerializeField] private int catalogSchemaVersion = 1;
        [SerializeField] private List<NodeTypeCatalogRecord> entries = new List<NodeTypeCatalogRecord>();
        public int CatalogSchemaVersion => catalogSchemaVersion;
        public IReadOnlyList<NodeTypeCatalogRecord> Entries => entries ?? (IReadOnlyList<NodeTypeCatalogRecord>)Array.Empty<NodeTypeCatalogRecord>();

        public Result ValidateManifest()
        {
            if (catalogSchemaVersion != 1) return Failure("nodes.catalog.asset_schema", "Unsupported node catalog schema.");
            if (entries == null || entries.Count == 0) return Failure("nodes.catalog.asset_empty", "Node catalog asset contains no entries.");
            if (entries.Any(x => x == null)) return Failure("nodes.catalog.asset_null", "Node catalog asset contains a null entry.");
            if (entries.GroupBy(x => x.TypeId, StringComparer.Ordinal).Any(x => x.Count() > 1)) return Failure("nodes.catalog.asset_duplicate", "Node catalog asset contains duplicate TypeIds.");
            foreach (var entry in entries)
            {
                var result = entry.Validate(); if (result.IsFailure) return result;
                var references = entry.ValidateAssetReferenceRequirements(); if (references.IsFailure) return references;
            }
            return Result.Success();
        }

        /// <summary>Strictly compares every catalog field with explicit code
        /// definitions. Port IDs alone never satisfy this check.</summary>
        public Result ValidateAgainst(NodeDefinitionCatalog expected)
        {
            var manifest = ValidateManifest(); if (manifest.IsFailure) return manifest;
            if (expected == null) return Failure("nodes.catalog.expected_missing", "Expected runtime node catalog is required.");
            var valid = expected.Validate(); if (valid.IsFailure) return valid;
            if (Entries.Count != expected.Entries.Count) return Failure("nodes.catalog.asset_mismatch", "Catalog entry count differs from runtime definitions.");
            for (var i = 0; i < Entries.Count; i++) if (!Entries[i].Matches(expected.Entries[i])) return Failure("nodes.catalog.asset_mismatch", "Catalog metadata, ports, parameters or bindings differ from runtime definitions.");
            return Result.Success();
        }

        public Result ValidateAssetReferences(GameObject scene3dPrefab, GameObject scene2dPrefab, Shader shaderGenerator, Shader shaderEffect, Shader shaderBlend2)
        {
            var manifest = ValidateManifest(); if (manifest.IsFailure) return manifest;
            if (Find("shitdesigner.scene.3d")?.ScenePrefab != scene3dPrefab || Find("shitdesigner.scene.2d")?.ScenePrefab != scene2dPrefab) return Failure("nodes.catalog.prefab_mismatch", "Production scene prefabs do not match direct catalog references.");
            if (Find("shitdesigner.shader.generator")?.Shader != shaderGenerator || Find("shitdesigner.shader.effect")?.Shader != shaderEffect || Find("shitdesigner.shader.blend2")?.Shader != shaderBlend2) return Failure("nodes.catalog.shader_mismatch", "Production shaders do not match direct catalog references.");
            return Result.Success();
        }

        public Result<NodeDefinitionCatalog> BuildRuntimeCatalog(NodeFactoryBindings bindings = null)
        {
            var expected = NodeDefinitionCatalog.CreateInitial(bindings);
            var validation = ValidateAgainst(expected);
            return validation.IsFailure ? Result<NodeDefinitionCatalog>.Failure(validation.Diagnostic) : Result<NodeDefinitionCatalog>.Success(expected);
        }

        public void ReplaceManifest(IEnumerable<NodeCatalogEntry> source)
        {
            entries = (source ?? Enumerable.Empty<NodeCatalogEntry>()).Select(x => new NodeTypeCatalogRecord(x)).ToList(); catalogSchemaVersion = 1;
        }

        /// <summary>Editor generation attaches direct Unity asset references
        /// without changing the neutral descriptor or its save contract.</summary>
        public Result ConfigureReference(string typeId, GameObject prefab = null, Shader shader = null, Material templateMaterial = null)
        {
            var entry = Find(typeId); if (entry == null) return Failure("nodes.catalog.reference_type", "Catalog reference type is not present.");
            var expected = NodeDefinitionCatalog.CreateInitial().Entries.FirstOrDefault(x => x.TypeId.Value == typeId);
            if (expected == null) return Failure("nodes.catalog.reference_type", "Catalog reference type is not a built-in definition.");
            entry.SetAssetReferences(prefab, shader, templateMaterial, expected.SceneBinding?.PrefabKey, expected.ShaderBinding?.ShaderKey, expected.ShaderBinding?.OutputPass ?? 0);
            return Result.Success();
        }
        private NodeTypeCatalogRecord Find(string typeId) => Entries.FirstOrDefault(x => x != null && string.Equals(x.TypeId, typeId, StringComparison.Ordinal));
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes"));
    }
}
