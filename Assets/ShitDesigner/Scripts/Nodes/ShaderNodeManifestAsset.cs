using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Nodes
{
    /// <summary>
    /// Serializable build artifact for the shader manifest.  The immutable
    /// <see cref="ShaderNodeManifest"/> remains the editor/test model; this
    /// DTO is deliberately made only from Unity-serializable fields so the
    /// same declarations can be loaded in a Standalone player without
    /// AssetDatabase, JSON files, or Shader.Find.
    /// </summary>
    [CreateAssetMenu(fileName = "ShaderNodeManifest", menuName = "ShitDesigner/Shader Node Manifest")]
    public sealed class ShaderNodeManifestAsset : ScriptableObject
    {
        [SerializeField] private int schemaVersion = ShaderNodeManifest.CurrentSchemaVersion;
        [SerializeField] private string sourceFingerprint = string.Empty;
        [SerializeField] private List<ShaderNodeManifestAssetEntry> entries = new List<ShaderNodeManifestAssetEntry>();

        public int SchemaVersion => schemaVersion;
        public string SourceFingerprint => sourceFingerprint ?? string.Empty;
        public IReadOnlyList<ShaderNodeManifestAssetEntry> Entries => entries ?? (IReadOnlyList<ShaderNodeManifestAssetEntry>)Array.Empty<ShaderNodeManifestAssetEntry>();

        public ShaderNodeManifestAssetEntry Find(string typeId)
            => Entries.FirstOrDefault(x => x != null && string.Equals(x.TypeId, typeId ?? string.Empty, StringComparison.Ordinal));

        public ShaderNodeManifest BuildRuntimeManifest()
        {
            var runtime = new List<ShaderNodeManifestEntry>();
            for (var index = 0; index < Entries.Count; index++)
            {
                var source = Entries[index];
                if (source == null) continue;
                try
                {
                    runtime.Add(source.ToRuntimeEntry());
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("Shader manifest asset entry " + index + " (" + source.TypeId + ") could not be decoded: " + exception.ToString(), exception);
                }
            }
            return new ShaderNodeManifest(runtime, schemaVersion);
        }

        public Result ValidateManifest()
        {
            if (schemaVersion != ShaderNodeManifest.CurrentSchemaVersion)
                return Failure("nodes.shader_manifest_asset_schema", "Unsupported shader manifest asset schema.");
            if (entries == null || entries.Count == 0)
                return Failure("nodes.shader_manifest_asset_empty", "Shader manifest asset contains no entries.");
            if (entries.Any(x => x == null))
                return Failure("nodes.shader_manifest_asset_null", "Shader manifest asset contains a null entry.");
            try
            {
                var result = ShaderNodeManifestValidator.Validate(BuildRuntimeManifest());
                if (result.IsFailure) return result;
            }
            catch (Exception exception)
            {
                return Failure("nodes.shader_manifest_asset_decode", exception.Message);
            }
            return Result.Success();
        }

        /// <summary>All shader entries carry a direct serialized Shader
        /// reference.  This is the strip-safe runtime contract.</summary>
        public Result ValidateShaderReferences()
        {
            var valid = ValidateManifest();
            if (valid.IsFailure) return valid;
            foreach (var entry in Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ShaderKey) || entry.Shader == null)
                    return Failure("nodes.shader_manifest_asset_shader", "A shader manifest entry is missing its direct Shader reference: " + entry.TypeId + ".");
                if (entry.Passes.Count == 0 || entry.OutputPass < 0)
                    return Failure("nodes.shader_manifest_asset_pass", "A shader manifest entry has no valid output pass: " + entry.TypeId + ".");
            }
            return Result.Success();
        }

        /// <summary>Editor generators replace the complete DTO in one
        /// deterministic operation.  Unity references are attached in a
        /// separate pass so missing family assets fail loudly.</summary>
        public void ReplaceManifest(ShaderNodeManifest manifest, string fingerprint = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            schemaVersion = manifest.SchemaVersion;
            sourceFingerprint = fingerprint ?? string.Empty;
            entries = manifest.Entries.Select(x => new ShaderNodeManifestAssetEntry(x)).ToList();
        }

        public Result SetShaderReference(string typeId, Shader shader)
        {
            var entry = Find(typeId);
            if (entry == null) return Failure("nodes.shader_manifest_asset_type", "Shader manifest entry is not present: " + typeId + ".");
            if (shader == null) return Failure("nodes.shader_manifest_asset_shader", "A direct Shader reference is required: " + typeId + ".");
            entry.SetShader(shader);
            return Result.Success();
        }

        private static Result Failure(string code, string message)
            => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes"));
    }

    [Serializable]
    public sealed class ShaderNodeManifestAssetInput
    {
        [SerializeField] private string id;
        [SerializeField] private string sourceId;
        [SerializeField] private string displayName;
        [SerializeField] private string property;
        [SerializeField] private ShaderInputRole role;
        [SerializeField] private NodePortType type;
        [SerializeField] private bool required;
        [SerializeField] private bool hasDefaultImage;
        [SerializeField] private RuntimeDefaultImageKind defaultImage;

        public string Id => id ?? string.Empty;
        public string SourceId => sourceId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public string Property => property ?? string.Empty;
        public ShaderInputRole Role => role;
        public NodePortType Type => type;
        public bool Required => required;
        public bool HasDefaultImage => hasDefaultImage;
        public RuntimeDefaultImageKind DefaultImage => defaultImage;

        public ShaderNodeManifestAssetInput() { }
        internal ShaderNodeManifestAssetInput(ShaderNodeManifestInput source)
        {
            id = source.Id.Value;
            sourceId = source.SourceId;
            displayName = source.DisplayName;
            property = source.Property;
            role = source.Role;
            type = source.Type;
            required = source.Required;
            hasDefaultImage = source.DefaultImage.HasValue;
            defaultImage = source.DefaultImage ?? RuntimeDefaultImageKind.TransparentBlack;
        }

        internal ShaderNodeManifestInput ToRuntimeInput()
            => new ShaderNodeManifestInput(new PortId(Id), DisplayName, Property, Role, Type, Required,
                HasDefaultImage ? (RuntimeDefaultImageKind?)DefaultImage : null, SourceId);
    }

    [Serializable]
    public sealed class ShaderNodeManifestAssetParameter
    {
        [SerializeField] private string id;
        [SerializeField] private string sourceId;
        [SerializeField] private string displayName;
        [SerializeField] private string property;
        [SerializeField] private ParameterType type;
        [SerializeField] private string defaultValue;
        [SerializeField] private bool hasMinimum;
        [SerializeField] private string minimumValue;
        [SerializeField] private bool hasMaximum;
        [SerializeField] private string maximumValue;
        [SerializeField] private bool runtimeStateful;
        [SerializeField] private List<string> enumOptions = new List<string>();
        [SerializeField] private List<string> enumMappingKeys = new List<string>();
        [SerializeField] private List<int> enumMappingValues = new List<int>();
        [SerializeField] private string group;
        [SerializeField] private int displayOrder;
        [SerializeField] private string description;
        [SerializeField] private string unit;
        [SerializeField] private double step;
        [SerializeField] private bool isReadOnly;
        [SerializeField] private bool isHidden;

        public string Id => id ?? string.Empty;
        public string SourceId => sourceId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public string Property => property ?? string.Empty;
        public ParameterType Type => type;
        public string DefaultValue => defaultValue ?? string.Empty;
        public bool HasMinimum => hasMinimum;
        public string MinimumValue => minimumValue ?? string.Empty;
        public bool HasMaximum => hasMaximum;
        public string MaximumValue => maximumValue ?? string.Empty;
        public bool RuntimeStateful => runtimeStateful;
        public IReadOnlyList<string> EnumOptions => enumOptions ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<string> EnumMappingKeys => enumMappingKeys ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<int> EnumMappingValues => enumMappingValues ?? (IReadOnlyList<int>)Array.Empty<int>();
        public string Group => group ?? string.Empty;
        public int DisplayOrder => displayOrder;
        public string Description => description ?? string.Empty;
        public string Unit => unit ?? string.Empty;
        public double Step => step;
        public bool IsReadOnly => isReadOnly;
        public bool IsHidden => isHidden;

        public ShaderNodeManifestAssetParameter() { }
        internal ShaderNodeManifestAssetParameter(ShaderNodeManifestParameter source)
        {
            id = source.Id.Value;
            sourceId = source.SourceId;
            displayName = source.DisplayName;
            property = source.Property;
            type = source.Type;
            defaultValue = Encode(source.DefaultValue);
            hasMinimum = source.Minimum.HasValue;
            minimumValue = source.Minimum.HasValue ? Encode(source.Minimum.Value) : string.Empty;
            hasMaximum = source.Maximum.HasValue;
            maximumValue = source.Maximum.HasValue ? Encode(source.Maximum.Value) : string.Empty;
            runtimeStateful = source.Definition.RuntimeStateful;
            enumOptions = (source.Definition.EnumOptions ?? Array.Empty<string>()).ToList();
            enumMappingKeys = source.EnumMapping.Keys.ToList();
            enumMappingValues = enumMappingKeys.Select(x => source.EnumMapping[x]).ToList();
            group = source.Definition.Group;
            displayOrder = source.Definition.DisplayOrder;
            description = source.Definition.Description;
            unit = source.Definition.Unit;
            step = source.Definition.Step;
            isReadOnly = source.Definition.IsReadOnly;
            isHidden = source.Definition.IsHidden;
        }

        internal ShaderNodeManifestParameter ToRuntimeParameter()
        {
            var mapping = new Dictionary<string, int>(StringComparer.Ordinal);
            var count = Math.Min(EnumMappingKeys.Count, EnumMappingValues.Count);
            for (var index = 0; index < count; index++) mapping[EnumMappingKeys[index]] = EnumMappingValues[index];
            var definition = new NodeParameterDefinition(new ParameterId(Id), DisplayName, Type, Decode(DefaultValue),
                HasMinimum ? Decode(MinimumValue) : (ParameterValue?)null,
                HasMaximum ? Decode(MaximumValue) : (ParameterValue?)null,
                RuntimeStateful, EnumOptions, Group, DisplayOrder, Description, Unit, Step, IsReadOnly, IsHidden);
            return new ShaderNodeManifestParameter(definition, Property, mapping, SourceId);
        }

        private static string Encode(ParameterValue value)
        {
            var culture = CultureInfo.InvariantCulture;
            switch (value.Type)
            {
                case ParameterType.Float: return "float:" + value.AsFloat().ToString("R", culture);
                case ParameterType.Int: return "int:" + value.AsInt().ToString(culture);
                case ParameterType.Bool: return "bool:" + (value.AsBool() ? "true" : "false");
                case ParameterType.Vector2: var v2 = value.AsVector2(); return "vector2:" + v2.X.ToString("R", culture) + "," + v2.Y.ToString("R", culture);
                case ParameterType.Vector3: var v3 = value.AsVector3(); return "vector3:" + v3.X.ToString("R", culture) + "," + v3.Y.ToString("R", culture) + "," + v3.Z.ToString("R", culture);
                case ParameterType.Vector4: var v4 = value.AsVector4(); return "vector4:" + v4.X.ToString("R", culture) + "," + v4.Y.ToString("R", culture) + "," + v4.Z.ToString("R", culture) + "," + v4.W.ToString("R", culture);
                case ParameterType.Color: var c = value.AsColor(); return "color:" + c.R.ToString("R", culture) + "," + c.G.ToString("R", culture) + "," + c.B.ToString("R", culture) + "," + c.A.ToString("R", culture);
                case ParameterType.String: return "string:" + value.AsString();
                case ParameterType.Enum: return "enum:" + value.AsString();
                case ParameterType.MediaAssetReference: return "media:" + (value.AsString() ?? string.Empty);
                default: throw new ArgumentOutOfRangeException();
            }
        }

        private static ParameterValue Decode(string encoded)
        {
            var value = encoded ?? string.Empty;
            var separator = value.IndexOf(':');
            var kind = separator < 0 ? string.Empty : value.Substring(0, separator);
            var payload = separator < 0 ? value : value.Substring(separator + 1);
            var culture = CultureInfo.InvariantCulture;
            switch (kind)
            {
                case "float": return ParameterValue.FromFloat(float.Parse(payload, NumberStyles.Float, culture));
                case "int": return ParameterValue.FromInt(int.Parse(payload, NumberStyles.Integer, culture));
                case "bool": return ParameterValue.FromBool(string.Equals(payload, "true", StringComparison.OrdinalIgnoreCase));
                case "vector2": var v2 = Parse(payload, 2); return ParameterValue.FromVector2(new Vector2Value(v2[0], v2[1]));
                case "vector3": var v3 = Parse(payload, 3); return ParameterValue.FromVector3(new Vector3Value(v3[0], v3[1], v3[2]));
                case "vector4": var v4 = Parse(payload, 4); return ParameterValue.FromVector4(new Vector4Value(v4[0], v4[1], v4[2], v4[3]));
                case "color": var c = Parse(payload, 4); return ParameterValue.FromColor(new ColorValue(c[0], c[1], c[2], c[3]));
                case "string": return ParameterValue.FromString(payload);
                case "enum": return ParameterValue.FromEnum(payload);
                case "media": return ParameterValue.FromMediaAsset(string.IsNullOrEmpty(payload) ? (MediaAssetId?)null : new MediaAssetId(payload));
                default: return ParameterValue.Default(ParameterType.Float);
            }
        }

        private static float[] Parse(string payload, int expected)
        {
            var tokens = (payload ?? string.Empty).Split(',');
            if (tokens.Length != expected) throw new FormatException("Invalid shader manifest parameter vector.");
            var values = new float[expected];
            for (var index = 0; index < expected; index++) values[index] = float.Parse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture);
            return values;
        }
    }

    [Serializable]
    public sealed class ShaderNodeManifestAssetPass
    {
        [SerializeField] private string id;
        [SerializeField] private int index;
        [SerializeField] private ShaderPassKind kind;
        [SerializeField] private string variantId;
        [SerializeField] private string outputRole;
        [SerializeField] private ShaderFeatureFlags requiredFeatures;

        public string Id => id ?? string.Empty;
        public int Index => index;
        public ShaderPassKind Kind => kind;
        public string VariantId => variantId ?? string.Empty;
        public string OutputRole => outputRole ?? string.Empty;
        public ShaderFeatureFlags RequiredFeatures => requiredFeatures;

        public ShaderNodeManifestAssetPass() { }
        internal ShaderNodeManifestAssetPass(ShaderNodeManifestPass source)
        {
            id = source.Id; index = source.Index; kind = source.Kind; variantId = source.VariantId;
            outputRole = source.OutputRole; requiredFeatures = source.RequiredFeatures;
        }
        internal ShaderNodeManifestPass ToRuntimePass()
            => new ShaderNodeManifestPass(Id, Index, VariantId, Kind, OutputRole, RequiredFeatures);
    }

    [Serializable]
    public sealed class ShaderNodeManifestAssetEntry
    {
        [SerializeField] private string typeId;
        [SerializeField] private int schemaVersion = 1;
        [SerializeField] private string displayName;
        [SerializeField] private string category;
        [SerializeField] private ShaderNodeFamily family;
        [SerializeField] private string shaderKey;
        [SerializeField] private string variantId;
        [SerializeField] private ShaderFeatureFlags requiredFeatures;
        [SerializeField] private bool stateful;
        [SerializeField] private int historySlots;
        [SerializeField] private bool userAddable = true;
        [SerializeField] private string description;
        [SerializeField] private string priority;
        [SerializeField] private int warmupFrames;
        [SerializeField] private string sourceLedger;
        [SerializeField] private int sourceVariant;
        [SerializeField] private int outputPass;
        [SerializeField] private List<string> aliases = new List<string>();
        [SerializeField] private List<ShaderNodeManifestAssetInput> inputs = new List<ShaderNodeManifestAssetInput>();
        [SerializeField] private List<ShaderNodeManifestAssetParameter> parameters = new List<ShaderNodeManifestAssetParameter>();
        [SerializeField] private List<ShaderNodeManifestAssetPass> passes = new List<ShaderNodeManifestAssetPass>();
        [SerializeField] private Shader shader;

        public string TypeId => typeId ?? string.Empty;
        public int SchemaVersion => schemaVersion;
        public string DisplayName => displayName ?? string.Empty;
        public string Category => category ?? string.Empty;
        public ShaderNodeFamily Family => family;
        public string ShaderKey => shaderKey ?? string.Empty;
        public string VariantId => variantId ?? string.Empty;
        public ShaderFeatureFlags RequiredFeatures => requiredFeatures;
        public bool Stateful => stateful;
        public int HistorySlots => historySlots;
        public bool UserAddable => userAddable;
        public string Description => description ?? string.Empty;
        public string Priority => priority ?? string.Empty;
        public int WarmupFrames => warmupFrames;
        public string SourceLedger => sourceLedger ?? string.Empty;
        public int SourceVariant => sourceVariant;
        public int OutputPass => outputPass;
        public IReadOnlyList<string> Aliases => aliases ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<ShaderNodeManifestAssetInput> Inputs => inputs ?? (IReadOnlyList<ShaderNodeManifestAssetInput>)Array.Empty<ShaderNodeManifestAssetInput>();
        public IReadOnlyList<ShaderNodeManifestAssetParameter> Parameters => parameters ?? (IReadOnlyList<ShaderNodeManifestAssetParameter>)Array.Empty<ShaderNodeManifestAssetParameter>();
        public IReadOnlyList<ShaderNodeManifestAssetPass> Passes => passes ?? (IReadOnlyList<ShaderNodeManifestAssetPass>)Array.Empty<ShaderNodeManifestAssetPass>();
        public Shader Shader => shader;

        public ShaderNodeManifestAssetEntry() { }
        internal ShaderNodeManifestAssetEntry(ShaderNodeManifestEntry source)
        {
            typeId = source.TypeId.Value; schemaVersion = source.SchemaVersion; displayName = source.DisplayName;
            category = source.Category; family = source.Family; shaderKey = source.ShaderKey; variantId = source.VariantId;
            requiredFeatures = source.RequiredFeatures; stateful = source.Stateful; historySlots = source.HistorySlots;
            userAddable = source.UserAddable; description = source.Description; priority = source.Priority;
            warmupFrames = source.WarmupFrames; sourceLedger = source.SourceLedger; sourceVariant = source.SourceVariant;
            outputPass = source.OutputPass; aliases = source.Aliases.ToList();
            inputs = source.Inputs.Select(x => new ShaderNodeManifestAssetInput(x)).ToList();
            parameters = source.Parameters.Select(x => new ShaderNodeManifestAssetParameter(x)).ToList();
            passes = source.Passes.Select(x => new ShaderNodeManifestAssetPass(x)).ToList();
        }

        public void SetShader(Shader value) => shader = value;

        internal ShaderNodeManifestEntry ToRuntimeEntry()
        {
            var runtimeInputs = Inputs.Select(x => x.ToRuntimeInput()).ToList();
            var runtimeParameters = Parameters.Select(x => x.ToRuntimeParameter()).ToList();
            var runtimePasses = Passes.Select(x => x.ToRuntimePass()).ToList();
            return new ShaderNodeManifestEntry(new NodeTypeId(TypeId), DisplayName, Category, Family, ShaderKey, VariantId,
                runtimeInputs, runtimeParameters, runtimePasses, OutputPass, RequiredFeatures, Stateful, HistorySlots,
                Aliases, Description, schemaVersion, UserAddable, Priority, WarmupFrames, SourceLedger, SourceVariant);
        }
    }
}
