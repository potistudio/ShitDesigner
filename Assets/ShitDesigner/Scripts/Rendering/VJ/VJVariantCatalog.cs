using System;

namespace ShitDesigner.Rendering.VJ
{
    /// <summary>
    /// Stable names shared by the shader manifest, runtime selectors, and
    /// contract tests.  Keep the order in sync with the switch statements in
    /// the three VJ family shaders.
    /// </summary>
    public static class VJVariantCatalog
    {
        public static readonly string[] Blend =
        {
            "normal_alpha_over", "premultiplied_over", "under", "add",
            "linear_dodge", "subtract", "reverse_subtract", "multiply",
            "screen", "overlay", "hard_light", "soft_light", "vivid_light",
            "linear_light", "pin_light", "hard_mix", "difference", "exclusion",
            "darken", "lighten", "color_dodge", "color_burn", "linear_burn",
            "divide", "average", "negation", "phoenix", "reflect", "glow_blend",
            "hue", "saturation", "color", "luminosity", "luma_mask_composite",
            "external_mask_composite", "depth_composite"
        };

        public static readonly string[] Transition =
        {
            "crossfade", "dip_to_color", "wipe_left_right", "wipe_up_down",
            "radial_wipe", "iris_circle", "iris_box", "clock_wipe",
            "linear_dissolve", "noise_dissolve", "pixel_dissolve", "luma_dissolve",
            "push", "slide", "split", "barn_door", "venetian_blinds",
            "checker_wipe", "grid_flip", "page_curl", "cube_rotate",
            "perspective_swap", "zoom", "cross_zoom", "blur_crossfade",
            "radial_blur", "swirl", "kaleidoscope", "ripple", "displacement_map",
            "glitch", "rgb_split", "burn", "ink_spread", "liquid", "voronoi_cells"
        };

        public static readonly string[] Temporal =
        {
            "feedback_transform", "feedback_zoom", "feedback_rotate", "feedback_kaleidoscope",
            "echo", "trails", "frame_delay", "strobe", "freeze_hold", "accumulate_add",
            "accumulate_max", "temporal_average", "multi_tap_echo", "slit_scan_horizontal",
            "slit_scan_vertical", "time_displacement_map", "temporal_rgb_split",
            "datamosh_feedback", "motion_trails", "persistence_phosphor", "long_exposure",
            "frame_difference", "background_subtract", "temporal_median", "temporal_posterize",
            "beat_repeat", "optical_flow_visualizer", "optical_flow_warp", "frame_interpolation",
            "fluid_feedback", "reaction_diffusion_feedback", "multi_buffer_cellular_simulation"
        };

        public const int BlendCount = 36;
        public const int TransitionCount = 36;
        public const int TemporalCount = 32;
        public const int TotalCount = BlendCount + TransitionCount + TemporalCount;

        public static string FamilyName(int family, int variant)
        {
            switch (family)
            {
                case 0: return variant >= 0 && variant < Blend.Length ? Blend[variant] : string.Empty;
                case 1: return variant >= 0 && variant < Transition.Length ? Transition[variant] : string.Empty;
                case 2: return variant >= 0 && variant < Temporal.Length ? Temporal[variant] : string.Empty;
                default: return string.Empty;
            }
        }

        public static string StableId(int family, int variant)
        {
            var prefix = family == 0 ? "blend" : family == 1 ? "transition" : "temporal";
            var name = FamilyName(family, variant);
            return string.IsNullOrEmpty(name) ? string.Empty : prefix + "." + name;
        }

        public static int CountForFamily(string family)
        {
            if (string.Equals(family, "Blend", StringComparison.OrdinalIgnoreCase)) return BlendCount;
            if (string.Equals(family, "Transition", StringComparison.OrdinalIgnoreCase)) return TransitionCount;
            if (string.Equals(family, "Temporal", StringComparison.OrdinalIgnoreCase)) return TemporalCount;
            return 0;
        }
    }
}
