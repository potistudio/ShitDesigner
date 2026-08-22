using System;

namespace ShitDesigner.Project
{
    /// <summary>
    /// The project-wide internal dynamic range.  This is deliberately a
    /// persistence model value and does not reference Unity's GraphicsFormat
    /// enum, so loading remains independent of the runtime and editor.
    /// </summary>
    public enum ProjectDynamicRange
    {
        Hdr,
        Ldr
    }

    /// <summary>
    /// Project-owned output settings.  The application applies these settings
    /// while opening/reloading a project; a running session must not mutate
    /// the selected internal format.
    /// </summary>
    public sealed class ProjectOutputSettings : IEquatable<ProjectOutputSettings>
    {
        public const int DefaultProgramDisplay = 2;
        public const string HdrGraphicsFormat = "R16G16B16A16_SFloat";
        public const string LdrGraphicsFormat = "R8G8B8A8_UNorm";
        // Naming aliases used by rendering/persistence adapters. Keep the
        // canonical value in one place so HDR defaults cannot drift between
        // modules.
        public const string HdrRgba16fGraphicsFormat = HdrGraphicsFormat;
        public const string DefaultHdrGraphicsFormat = HdrGraphicsFormat;

        public ProjectDynamicRange DynamicRange { get; }
        public int ProgramDisplay { get; }
        public string InternalGraphicsFormat => DynamicRange == ProjectDynamicRange.Hdr ? HdrGraphicsFormat : LdrGraphicsFormat;
        public string GraphicsFormat => InternalGraphicsFormat;

        public ProjectOutputSettings(ProjectDynamicRange dynamicRange = ProjectDynamicRange.Hdr, int programDisplay = DefaultProgramDisplay)
        {
            if (!Enum.IsDefined(typeof(ProjectDynamicRange), dynamicRange)) throw new ArgumentOutOfRangeException(nameof(dynamicRange));
            if (programDisplay < 1) throw new ArgumentOutOfRangeException(nameof(programDisplay));
            DynamicRange = dynamicRange;
            ProgramDisplay = programDisplay;
        }

        public static ProjectOutputSettings CreateDefault() => new ProjectOutputSettings();
        public ProjectOutputSettings WithDynamicRange(ProjectDynamicRange value) => new ProjectOutputSettings(value, ProgramDisplay);
        public ProjectOutputSettings WithProgramDisplay(int value) => new ProjectOutputSettings(DynamicRange, value);

        public bool Equals(ProjectOutputSettings other)
        {
            return other != null && DynamicRange == other.DynamicRange && ProgramDisplay == other.ProgramDisplay;
        }

        public override bool Equals(object obj) => Equals(obj as ProjectOutputSettings);
        public override int GetHashCode() => HashCode.Combine(DynamicRange, ProgramDisplay);
    }
}
