using System.Globalization;
using System.Text;
using RayBork.Internal;

namespace RayBork.Bake.Shaders;

/// <summary>
/// Builds the generated GLSL prologue for one shader stage and validates the user source against the
/// shader ABI it is about to receive.
/// </summary>
/// <remarks>
/// <para>
/// The prologue declares the frame block, the bindless texture array, the per-instance record and
/// storage buffer, the texture macros, and the stage wrapper that forwards to the user entry point.
/// It is inserted immediately after the <c>#version</c> directive; a source without one gets
/// <c>#version 450</c> prepended first. The shaderc entry point is always the generated <c>main</c>
/// function, and the compiled module is renamed to <c>main_vertex</c> or <c>main_fragment</c>.
/// </para>
/// <para>
/// Validation rejects a user source that declares an engine identifier, a sidecar uniform or texture
/// name, a <c>layout(set = ...)</c> binding, or <c>location 15</c>. Comments are removed before the
/// scan so a comment can never trigger or hide a diagnostic, and the reported line is the original
/// source line of the offending token.
/// </para>
/// </remarks>
internal static class ShaderPrologueGenerator {
    private const string VERSION_DIRECTIVE = "#version";
    private const string GENERATED_VERSION_DIRECTIVE = "#version 450";
    private const string NEW_LINE = "\n";

    private static readonly HashSet<string> _reservedIdentifiers = new(StringComparer.Ordinal) {
        ShaderAbiNames.GLSL_ENTRY_POINT,
        ShaderAbiNames.FRAME_TYPE,
        ShaderAbiNames.FRAME_BLOCK,
        ShaderAbiNames.FRAME_BLOCK_INSTANCE,
        ShaderAbiNames.FRAME_ACCESSOR,
        ShaderAbiNames.TEXTURES,
        ShaderAbiNames.INSTANCE_TYPE,
        ShaderAbiNames.INSTANCE_BUFFER,
        ShaderAbiNames.INSTANCES,
        ShaderAbiNames.INSTANCE_ACCESSOR,
        ShaderAbiNames.INSTANCE_INDEX,
        ShaderAbiNames.INSTANCE_INDEX_OUT,
        ShaderAbiNames.USER_VERTEX_MAIN,
        ShaderAbiNames.USER_FRAGMENT_MAIN,
    };

    private static readonly HashSet<string> _declarationKeywords = new(StringComparer.Ordinal) {
        "attribute",
        "bool",
        "bvec2",
        "bvec3",
        "bvec4",
        "buffer",
        "centroid",
        "coherent",
        "const",
        "dmat2",
        "dmat2x2",
        "dmat2x3",
        "dmat2x4",
        "dmat3",
        "dmat3x2",
        "dmat3x3",
        "dmat3x4",
        "dmat4",
        "dmat4x2",
        "dmat4x3",
        "dmat4x4",
        "double",
        "dvec2",
        "dvec3",
        "dvec4",
        "flat",
        "float",
        "highp",
        "in",
        "inout",
        "int",
        "invariant",
        "layout",
        "lowp",
        "mat2",
        "mat2x2",
        "mat2x3",
        "mat2x4",
        "mat3",
        "mat3x2",
        "mat3x3",
        "mat3x4",
        "mat4",
        "mat4x2",
        "mat4x3",
        "mat4x4",
        "mediump",
        "noperspective",
        "out",
        "patch",
        "precision",
        "readonly",
        "restrict",
        "sample",
        "sampler1D",
        "sampler1DArray",
        "sampler1DArrayShadow",
        "sampler1DShadow",
        "sampler2D",
        "sampler2DArray",
        "sampler2DArrayShadow",
        "sampler2DMS",
        "sampler2DMSArray",
        "sampler2DShadow",
        "sampler3D",
        "samplerCube",
        "samplerCubeArray",
        "samplerCubeArrayShadow",
        "samplerCubeShadow",
        "shared",
        "smooth",
        "struct",
        "subroutine",
        "uint",
        "uniform",
        "uvec2",
        "uvec3",
        "uvec4",
        "varying",
        "vec2",
        "vec3",
        "vec4",
        "void",
        "volatile",
    };

    /// <summary>
    /// Validates a user source and builds the complete wrapped stage source.
    /// </summary>
    /// <param name="source">The user-authored GLSL source.</param>
    /// <param name="layout">The instance layout generated from the sidecar metadata.</param>
    /// <param name="stage">The stage to generate: <see cref="ShaderStageMask.Vertex"/> or
    /// <see cref="ShaderStageMask.Fragment"/>.</param>
    /// <param name="wrappedSource">The source with the version directive and prologue inserted.</param>
    /// <param name="message">The validation failure reason, or an empty string on success.</param>
    /// <returns><see langword="true"/> when the source can be wrapped and compiled.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is neither stage.</exception>
    internal static bool TryBuild(
        string source,
        ShaderAbiLayout layout,
        ShaderStageMask stage,
        out string wrappedSource,
        out string message) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(layout);
        EnsureSingleStage(stage);

        wrappedSource = string.Empty;
        string stripped = StripComments(source);
        if (!TryValidateDeclarations(stripped, layout, out message)
            || !TryValidateLayoutQualifiers(stripped, out message)) {
            return false;
        }

        string prologue = Generate(layout, stage);
        wrappedSource = InsertPrologue(source, stripped, prologue);
        return true;
    }

    /// <summary>
    /// Generates the prologue text for one stage from the instance layout.
    /// </summary>
    /// <param name="layout">The instance layout generated from the sidecar metadata.</param>
    /// <param name="stage">The stage to generate.</param>
    /// <returns>The complete prologue, ending with a newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is neither stage.</exception>
    internal static string Generate(ShaderAbiLayout layout, ShaderStageMask stage) {
        ArgumentNullException.ThrowIfNull(layout);
        EnsureSingleStage(stage);
        bool vertex = stage == ShaderStageMask.Vertex;

        var text = new StringBuilder(2048);
        text.Append("#extension GL_EXT_nonuniform_qualifier : require\n");
        text.Append('\n');
        text.Append("struct ").Append(ShaderAbiNames.FRAME_TYPE).Append(" {\n");
        text.Append("    mat4 ViewProjection;\n");
        text.Append("    mat4 View;\n");
        text.Append("    mat4 Projection;\n");
        text.Append("    vec4 CameraPosition;\n");
        text.Append("    vec4 Viewport;\n");
        text.Append("    vec4 Time;\n");
        text.Append("    vec4 ScreenSize;\n");
        text.Append("};\n");
        text.Append('\n');
        text.Append("layout(set = 0, binding = 0, std140) uniform ")
            .Append(ShaderAbiNames.FRAME_BLOCK)
            .Append(" {\n");
        text.Append("    ").Append(ShaderAbiNames.FRAME_TYPE).Append(' ')
            .Append(ShaderAbiNames.FRAME_FIELD).Append(";\n");
        text.Append("} ").Append(ShaderAbiNames.FRAME_BLOCK_INSTANCE).Append(";\n");
        text.Append('\n');
        text.Append("#define ").Append(ShaderAbiNames.FRAME_ACCESSOR).Append(' ')
            .Append(ShaderAbiNames.FRAME_BLOCK_INSTANCE).Append('.')
            .Append(ShaderAbiNames.FRAME_FIELD).Append('\n');

        if (layout.Textures.Count > 0) {
            text.Append('\n');
            text.Append("layout(set = 0, binding = 1) uniform sampler2D ")
                .Append(ShaderAbiNames.TEXTURES).Append("[];\n");
        }

        text.Append('\n');
        text.Append("struct ").Append(ShaderAbiNames.INSTANCE_TYPE).Append(" {\n");
        text.Append("    mat4 Transform;\n");
        text.Append("    vec4 Color;\n");
        text.Append("    vec4 UvRect;\n");
        foreach (ShaderAbiTexture texture in layout.Textures) {
            text.Append("    uint ").Append(texture.Name)
                .Append(ShaderAbiNames.TEXTURE_INDEX_FIELD_SUFFIX).Append(";\n");
        }

        foreach (ShaderAbiUniform uniform in layout.Uniforms) {
            text.Append("    ").Append(ToGlslType(uniform.Type)).Append(' ')
                .Append(uniform.Name).Append(";\n");
        }

        text.Append("};\n");
        text.Append('\n');
        text.Append("layout(set = 0, binding = 2, std430) readonly buffer ")
            .Append(ShaderAbiNames.INSTANCE_BUFFER).Append(" {\n");
        text.Append("    ").Append(ShaderAbiNames.INSTANCE_TYPE).Append(' ')
            .Append(ShaderAbiNames.INSTANCES_FIELD).Append("[];\n");
        text.Append("} ").Append(ShaderAbiNames.INSTANCES).Append(";\n");
        text.Append('\n');

        if (vertex) {
            text.Append("layout(location = ")
                .Append(ShaderAbiNames.RESERVED_VARYING_LOCATION.ToString(CultureInfo.InvariantCulture))
                .Append(") flat out uint ").Append(ShaderAbiNames.INSTANCE_INDEX_OUT).Append(";\n");
            text.Append('\n');
            text.Append("#define ").Append(ShaderAbiNames.INSTANCE_ACCESSOR).Append(' ')
                .Append(ShaderAbiNames.INSTANCES).Append('.')
                .Append(ShaderAbiNames.INSTANCES_FIELD).Append('[')
                .Append(ShaderAbiNames.INSTANCE_INDEX_OUT).Append("]\n");
        } else {
            text.Append("layout(location = ")
                .Append(ShaderAbiNames.RESERVED_VARYING_LOCATION.ToString(CultureInfo.InvariantCulture))
                .Append(") flat in uint ").Append(ShaderAbiNames.INSTANCE_INDEX).Append(";\n");
            text.Append('\n');
            text.Append("#define ").Append(ShaderAbiNames.INSTANCE_ACCESSOR).Append(' ')
                .Append(ShaderAbiNames.INSTANCES).Append('.')
                .Append(ShaderAbiNames.INSTANCES_FIELD).Append('[')
                .Append(ShaderAbiNames.INSTANCE_INDEX).Append("]\n");
        }

        foreach (ShaderAbiTexture texture in layout.Textures) {
            text.Append('\n');
            text.Append("#define ").Append(texture.Name).Append(' ')
                .Append(ShaderAbiNames.TEXTURES).Append("[nonuniformEXT(")
                .Append(ShaderAbiNames.INSTANCE_ACCESSOR).Append('.').Append(texture.Name)
                .Append(ShaderAbiNames.TEXTURE_INDEX_FIELD_SUFFIX).Append(")]\n");
        }

        foreach (ShaderAbiUniform uniform in layout.Uniforms) {
            text.Append('\n');
            text.Append("#define ").Append(uniform.Name).Append(' ')
                .Append(ShaderAbiNames.INSTANCE_ACCESSOR).Append('.').Append(uniform.Name).Append('\n');
        }

        string userMain = vertex ? ShaderAbiNames.USER_VERTEX_MAIN : ShaderAbiNames.USER_FRAGMENT_MAIN;
        string entryPoint = vertex ? ShaderAbiNames.VERTEX_ENTRY_POINT : ShaderAbiNames.FRAGMENT_ENTRY_POINT;
        text.Append('\n');
        text.Append("void ").Append(userMain).Append("();\n");
        text.Append('\n');
        text.Append("void ").Append(ShaderAbiNames.GLSL_ENTRY_POINT).Append("() {\n");
        if (vertex) {
            text.Append("    ").Append(ShaderAbiNames.INSTANCE_INDEX_OUT)
                .Append(" = uint(gl_InstanceIndex);\n");
        }

        text.Append("    ").Append(userMain).Append("();\n");
        text.Append("}\n");
        text.Append('\n');
        text.Append("#define ").Append(entryPoint).Append(' ').Append(userMain).Append('\n');
        return text.ToString();
    }

    private static bool TryValidateDeclarations(
        string stripped,
        ShaderAbiLayout layout,
        out string message) {
        var forbidden = new HashSet<string>(_reservedIdentifiers, StringComparer.Ordinal);
        foreach (ShaderAbiUniform uniform in layout.Uniforms) {
            forbidden.Add(uniform.Name);
        }

        foreach (ShaderAbiTexture texture in layout.Textures) {
            forbidden.Add(texture.Name);
        }

        List<GlslToken> tokens = Tokenize(stripped);

        for (int index = 0; index < tokens.Count; index++) {
            GlslToken token = tokens[index];
            if (token.IsIdentifier
                && string.Equals(token.Text, "define", StringComparison.Ordinal)
                && index > 0
                && string.Equals(tokens[index - 1].Text, "#", StringComparison.Ordinal)) {
                for (int next = index + 1; next < tokens.Count; next++) {
                    if (!tokens[next].IsIdentifier) {
                        continue;
                    }

                    if (forbidden.Contains(tokens[next].Text)) {
                        message = CreateDeclarationMessage(
                            stripped,
                            tokens[next],
                            _reservedIdentifiers.Contains(tokens[next].Text));
                        return false;
                    }

                    break;
                }
            }
        }

        bool expectsDeclarator = false;
        foreach (GlslToken token in tokens) {
            if (token.IsIdentifier) {
                if (expectsDeclarator && forbidden.Contains(token.Text)) {
                    message = CreateDeclarationMessage(
                        stripped,
                        token,
                        _reservedIdentifiers.Contains(token.Text));
                    return false;
                }

                if (_declarationKeywords.Contains(token.Text)) {
                    expectsDeclarator = true;
                }

                continue;
            }

            if (token.Text.Length > 0 && char.IsAsciiDigit(token.Text[0])) {
                continue;
            }

            expectsDeclarator = token.Text switch {
                "," => expectsDeclarator,
                "[" => expectsDeclarator,
                "]" => expectsDeclarator,
                _ => false,
            };
        }

        message = string.Empty;
        return true;
    }

    private static string CreateDeclarationMessage(
        string stripped,
        GlslToken token,
        bool reserved) {
        int line = LineOf(stripped, token.Start);
        string kind = reserved ? "reserved engine identifier" : "sidecar uniform or texture";
        return $"line {line.ToString(CultureInfo.InvariantCulture)}: the {kind} '{token.Text}' is "
            + "generated by the M4 shader ABI and must not be declared.";
    }

    private static bool TryValidateLayoutQualifiers(string stripped, out string message) {
        List<GlslToken> tokens = Tokenize(stripped);
        for (int index = 0; index < tokens.Count; index++) {
            if (!tokens[index].IsIdentifier
                || !string.Equals(tokens[index].Text, "layout", StringComparison.Ordinal)) {
                continue;
            }

            if (index + 1 >= tokens.Count
                || !string.Equals(tokens[index + 1].Text, "(", StringComparison.Ordinal)) {
                continue;
            }

            int depth = 0;
            int close = -1;
            for (int scan = index + 1; scan < tokens.Count; scan++) {
                if (string.Equals(tokens[scan].Text, "(", StringComparison.Ordinal)) {
                    depth++;
                } else if (string.Equals(tokens[scan].Text, ")", StringComparison.Ordinal)) {
                    depth--;
                    if (depth == 0) {
                        close = scan;
                        break;
                    }
                }
            }

            if (close < 0) {
                continue;
            }

            string qualifiers = stripped[(tokens[index + 1].Start + 1)..tokens[close].Start];
            int line = LineOf(stripped, tokens[index].Start);
            foreach (string qualifier in qualifiers.Split(',')) {
                string trimmed = qualifier.Trim();
                if (HasQualifier(trimmed, "set")) {
                    message = $"line {line.ToString(CultureInfo.InvariantCulture)}: a "
                        + "'layout(set = ...)' qualifier declares an engine-owned descriptor binding; "
                        + "the M4 shader ABI generates every set 0 binding.";
                    return false;
                }

                if (HasQualifier(trimmed, "location")
                    && TryReadQualifierInt(trimmed, out int location)
                    && location == ShaderAbiNames.RESERVED_VARYING_LOCATION) {
                    message = $"line {line.ToString(CultureInfo.InvariantCulture)}: location "
                        + $"{ShaderAbiNames.RESERVED_VARYING_LOCATION.ToString(CultureInfo.InvariantCulture)} "
                        + "is reserved for the engine instance-index varying.";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    private static bool HasQualifier(string qualifier, string name) {
        if (!qualifier.StartsWith(name, StringComparison.Ordinal)) {
            return false;
        }

        if (qualifier.Length == name.Length) {
            return true;
        }

        char next = qualifier[name.Length];
        return next is '=' or ' ' or '\t' or '\n' or '\r';
    }

    private static bool TryReadQualifierInt(string qualifier, out int value) {
        value = 0;
        int equals = qualifier.IndexOf('=');
        if (equals < 0) {
            return false;
        }

        string text = qualifier[(equals + 1)..].Trim();
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string InsertPrologue(string source, string stripped, string prologue) {
        int versionLineEnd = FindVersionDirectiveEnd(stripped);
        if (versionLineEnd < 0) {
            return GENERATED_VERSION_DIRECTIVE + NEW_LINE + prologue + NEW_LINE + source;
        }

        string before = source[..versionLineEnd];
        string after = source[versionLineEnd..];
        var wrapped = new StringBuilder(before.Length + prologue.Length + after.Length + 2);
        wrapped.Append(before);
        if (before.Length > 0 && before[^1] != '\n') {
            wrapped.Append('\n');
        }

        wrapped.Append(prologue);
        wrapped.Append('\n');
        wrapped.Append(after);
        return wrapped.ToString();
    }

    private static int FindVersionDirectiveEnd(string stripped) {
        int lineStart = 0;
        while (lineStart < stripped.Length) {
            int lineEnd = stripped.IndexOf('\n', lineStart);
            int nextLine = lineEnd < 0 ? stripped.Length : lineEnd + 1;
            int content = lineStart;
            while (content < nextLine && char.IsWhiteSpace(stripped[content])) {
                content++;
            }

            if (HasVersionDirective(stripped, content, nextLine)) {
                return nextLine;
            }

            lineStart = nextLine;
        }

        return -1;
    }

    private static bool HasVersionDirective(string source, int start, int end) {
        if (end - start < VERSION_DIRECTIVE.Length
            || !source.AsSpan(start, VERSION_DIRECTIVE.Length).SequenceEqual(VERSION_DIRECTIVE.AsSpan())) {
            return false;
        }

        int after = start + VERSION_DIRECTIVE.Length;
        return after >= end || char.IsWhiteSpace(source[after]);
    }

    private static string StripComments(string source) {
        var stripped = new StringBuilder(source.Length);
        int index = 0;
        while (index < source.Length) {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (current == '/' && next == '/') {
                while (index < source.Length && source[index] != '\n') {
                    stripped.Append(' ');
                    index++;
                }

                continue;
            }

            if (current == '/' && next == '*') {
                stripped.Append(' ').Append(' ');
                index += 2;
                while (index < source.Length) {
                    if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/') {
                        stripped.Append(' ').Append(' ');
                        index += 2;
                        break;
                    }

                    stripped.Append(source[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                continue;
            }

            stripped.Append(current);
            index++;
        }

        return stripped.ToString();
    }

    private static List<GlslToken> Tokenize(string source) {
        var tokens = new List<GlslToken>();
        int index = 0;
        while (index < source.Length) {
            char current = source[index];
            if (char.IsWhiteSpace(current)) {
                index++;
                continue;
            }

            if (char.IsAsciiLetter(current) || current == '_') {
                int start = index;
                index++;
                while (index < source.Length
                    && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_')) {
                    index++;
                }

                tokens.Add(new GlslToken(source[start..index], IsIdentifier: true, start));
                continue;
            }

            if (char.IsAsciiDigit(current)) {
                int start = index;
                index++;
                while (index < source.Length
                    && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '.')) {
                    index++;
                }

                tokens.Add(new GlslToken(source[start..index], IsIdentifier: false, start));
                continue;
            }

            tokens.Add(new GlslToken(current.ToString(), IsIdentifier: false, index));
            index++;
        }

        return tokens;
    }

    private static int LineOf(string source, int index) {
        int line = 1;
        int limit = Math.Min(index, source.Length);
        for (int position = 0; position < limit; position++) {
            if (source[position] == '\n') {
                line++;
            }
        }

        return line;
    }

    private static string ToGlslType(ShaderUniformType type)
        => type switch {
            ShaderUniformType.Float => "float",
            ShaderUniformType.Int => "int",
            ShaderUniformType.UInt => "uint",
            ShaderUniformType.Bool => "bool",
            ShaderUniformType.Vector2 => "vec2",
            ShaderUniformType.Vector3 => "vec3",
            ShaderUniformType.Vector4 => "vec4",
            ShaderUniformType.Matrix4x4 => "mat4",
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "The uniform type is not supported by the M4 shader ABI."),
        };

    private static void EnsureSingleStage(ShaderStageMask stage) {
        if (stage is not ShaderStageMask.Vertex and not ShaderStageMask.Fragment) {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "A shader prologue is generated for exactly one stage.");
        }
    }

    private readonly record struct GlslToken(string Text, bool IsIdentifier, int Start);
}
